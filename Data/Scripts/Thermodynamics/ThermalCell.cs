using Sandbox.Definitions;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRageMath;
using VRage.GameServices;
using VRage.Utils;
using SpaceEngineers.Game.ModAPI;
using VRage.ModAPI;
using Sandbox.Game.Entities;
using Sandbox.Game;
using VRage.ObjectBuilders;

namespace Thermodynamics
{
    public class ThermalCell
    {
        public int Id;
        public long Frame;

        public float LastTemperature;
        public float Temperature;

        public float HeatGeneration;
        public float SpecificHeatInverted;

        public float PowerOutput;
        public float PowerInput;
        public float ConsumerGeneration;
        public float ProducerGeneration;

        public float k;
        public float A;
        public float kA;
        public float dxInverted;
        public float LastDeltaTemp;
        public float ExposedSurfaceArea;

        public float CubeArea;
        public float CubeAreaInv;

        public ThermalGrid Grid;
        public IMySlimBlock Block;

        public List<Vector3I> Exposed = new List<Vector3I>();
        public List<Vector3I> ExposedSurface = new List<Vector3I>();
        public List<Vector3I> ExposedSurfaceDirection = new List<Vector3I>();
        public List<Vector3I> Inside = new List<Vector3I>();
        public List<Vector3I> InsideSurface = new List<Vector3I>();



        public List<ThermalCell> Neighbors = new List<ThermalCell>();



        public ThermalCell(ThermalGrid g, IMySlimBlock b)
        {
            Grid = g;
            Block = b;
            Id = b.Position.Flatten();
            ThermalCellDefinition p = ThermalCellDefinition.GetDefinition(Block.BlockDefinition.Id);

            //TODO: the listeners need to handle changes at the end
            //of the update cycle instead of whenever.
            SetupListeners();

            // k = Watts / (meters - kelven)
            k = p.Conductivity;

            // A = surface area
            A = Block.CubeGrid.GridSize * Block.CubeGrid.GridSize;
            kA = k * A * Settings.Instance.TimeScaleRatio; // added the time scale ratio to save on compute power
            dxInverted = 1f / Block.CubeGrid.GridSize;

            Vector3I size = (Block.Max - Block.Min) + 1;

            CubeArea = 2 * (size.X * size.Z + size.Y * size.Z + size.X * size.Y);
            CubeAreaInv = 1f/CubeArea;

            if (p.SpacificHeat > 0)
            {
                SpecificHeatInverted = 1f / (Block.Mass * p.SpacificHeat);
            }

            ProducerGeneration = p.ProducerWasteHeatPerWatt * Settings.Instance.TimeScaleRatio * SpecificHeatInverted;
            ConsumerGeneration = p.ConsumerWasteHeatPerWatt * Settings.Instance.TimeScaleRatio * SpecificHeatInverted;
            UpdateHeat();
        }



        //public void Init(ThermalCellDefinition p, IMySlimBlock b, ThermalGrid g)
        //{
        //    Grid = g;
        //    Block = b;
        //    Id = b.Position.Flatten();
        //    SetupListeners();

        //    // k = Watts / (meters - kelven)
        //    k = p.Conductivity;

        //    // A = surface area
        //    A = Block.CubeGrid.GridSize * Block.CubeGrid.GridSize;
        //    kA = k * A * Settings.Instance.TimeScaleRatio; // added the time scale ratio to save on compute power
        //    dxInverted = 1f / Block.CubeGrid.GridSize;

        //    if (p.SpacificHeat > 0)
        //    {
        //        SpecificHeatInverted = 1f / (Block.Mass * p.SpacificHeat);
        //    }

        //    ProducerGeneration = p.ProducerWasteHeatPerWatt * Settings.Instance.TimeScaleRatio * SpecificHeatInverted;
        //    ConsumerGeneration = p.ConsumerWasteHeatPerWatt * Settings.Instance.TimeScaleRatio * SpecificHeatInverted;
        //    UpdateHeat();
        //}

        private void SetupListeners()
        {
            if (Block.FatBlock == null) return;

            IMyCubeBlock fat = Block.FatBlock;
            if (fat is IMyThrust)
            {
                IMyThrust thrust = (fat as IMyThrust);
                thrust.ThrustChanged += OnThrustChanged;
                OnThrustChanged(thrust, 0, thrust.CurrentThrust);

            }
            else
            {
                fat.Components.ComponentAdded += OnComponentAdded;
                fat.Components.ComponentRemoved += OnComponentRemoved;

                if (fat.Components.Contains(typeof(MyResourceSourceComponent)))
                {
                    fat.Components.Get<MyResourceSourceComponent>().OutputChanged += PowerOutputChanged;
                }

                if (fat.Components.Contains(typeof(MyResourceSinkComponent)))
                {
                    fat.Components.Get<MyResourceSinkComponent>().CurrentInputChanged += PowerInputChanged;
                }
            }

            if (fat is IMyPistonBase)
            {
                (fat as IMyPistonBase).AttachedEntityChanged += GridGroupChanged;
            }
            else if (fat is IMyMotorBase)
            {
                (fat as IMyMotorBase).AttachedEntityChanged += GridGroupChanged;
            }
            else if (fat is IMyDoor)
            {
                (fat as IMyDoor).DoorStateChanged += (state) => Grid.ExternalBlockReset();
            }
            else if (fat is IMyLandingGear)
            {
                // had to use this crappy method because the better method is broken
                // KEEN!!! fix your code please!
                IMyLandingGear gear = (fat as IMyLandingGear);
                gear.StateChanged += (state) =>
                {
                    IMyEntity entity = gear.GetAttachedEntity();
                    ThermalCell c = Grid.Get(gear.Position);

                    // if the entity is not MyCubeGrid reset landing gear neighbors because we probably detached
                    if (!(entity is MyCubeGrid))
                    {
                        c.ResetNeighbors();
                        return;
                    }

                    // get the search area
                    MyCubeGrid grid = entity as MyCubeGrid;
                    ThermalGrid gtherms = grid.GameLogic.GetAs<ThermalGrid>();

                    Vector3D oldMin = gear.CubeGrid.GridIntegerToWorld(new Vector3I(gear.Min.X, gear.Min.Y, gear.Min.Z));
                    Vector3D oldMax = gear.CubeGrid.GridIntegerToWorld(new Vector3I(gear.Max.X, gear.Max.Y, gear.Min.Z));

                    oldMax += gear.WorldMatrix.Down * (grid.GridSize + 0.2f);

                    Vector3I min = grid.WorldToGridInteger(oldMin);
                    Vector3I max = grid.WorldToGridInteger(oldMax);

                    //MyLog.Default.Info($"[{Settings.Name}] min {min} max {max}");

                    // look for active cells on the other grid that are inside the search area
                    Vector3I temp = Vector3I.Zero;
                    for (int x = min.X; x <= max.X; x++)
                    {
                        temp.X = x;
                        for (int y = min.Y; y <= max.Y; y++)
                        {
                            temp.Y = y;
                            for (int z = min.Z; z <= max.Z; z++)
                            {
                                temp.Z = z;

                                ThermalCell ncell = gtherms.Get(temp);
                                //MyLog.Default.Info($"[{Settings.Name}] testing {temp} {ncell != null}");
                                if (ncell != null)
                                {
                                    // connect the cells found
                                    ncell.Neighbors.Add(c);
                                    //ncell.CalculateSurface();
                                    c.Neighbors.Add(ncell);
                                }
                            }
                        }
                    }

                    //c.CalculateSurface();
                };
            }
        }

        private void GridGroupChanged(IMyMechanicalConnectionBlock block)
        {
            ThermalGrid g = block.CubeGrid.GameLogic.GetAs<ThermalGrid>();
            ThermalCell cell = g.Get(block.Position);

            if (cell == null) return;

            //MyLog.Default.Info($"[{Settings.Name}] Grid: {cell.Block.CubeGrid.EntityId} cell {block.Position} IsAttached: {block.IsAttached} DoubleCheck: {block.Top != null}");

            if (block.Top == null)
            {
                for (int i = 0; i < cell.Neighbors.Count; i++)
                {
                    ThermalCell ncell = cell.Neighbors[i];

                    if (ncell.Block.CubeGrid == cell.Block.CubeGrid) continue;

                    //MyLog.Default.Info($"[{Settings.Name}] Grid: {cell.Block.CubeGrid.EntityId} cell: {cell.Block.Position} removed connection to, Grid: {ncell.Block.CubeGrid.EntityId} Cell: {ncell.Block.Position}");

                    ncell.Neighbors.Remove(cell);
                    //ncell.CalculateSurface();
                    cell.Neighbors.RemoveAt(i);
                    break;
                }
                //cell.CalculateSurface();
            }
            else
            {
                ThermalCell ncell = block.Top.CubeGrid.GameLogic.GetAs<ThermalGrid>().Get(block.Top.Position);

                //MyLog.Default.Info($"[{Settings.Name}] Grid: {cell.Block.CubeGrid.EntityId} cell: {cell.Block.Position} adding connection to, nGrid: {ncell.Block.CubeGrid.EntityId} nCell: {ncell.Block.Position}");

                cell.Neighbors.Add(ncell);
                //cell.CalculateSurface();

                ncell.Neighbors.Add(cell);
                //ncell.CalculateSurface();
            }
        }

        private void OnThrustChanged(IMyThrust block, float old, float current)
        {
            MyThrustDefinition def = block.SlimBlock.BlockDefinition as MyThrustDefinition;
            if (block.IsWorking)
            {
                PowerInput = def.MinPowerConsumption + (def.MaxPowerConsumption * (block.CurrentThrust / block.MaxThrust));
                PowerInput *= 1000000;
            }
            else
            {
                PowerInput = 0;
            }

            UpdateHeat();
        }

        private void OnComponentAdded(Type compType, MyEntityComponentBase component)
        {
            if (compType == typeof(MyResourceSourceComponent))
            {
                (component as MyResourceSourceComponent).OutputChanged += PowerOutputChanged;
            }

            if (compType == typeof(MyResourceSinkComponent))
            {
                (component as MyResourceSinkComponent).CurrentInputChanged += PowerInputChanged;
            }
        }

        private void OnComponentRemoved(Type compType, MyEntityComponentBase component)
        {
            if (compType == typeof(MyResourceSourceComponent))
            {
                (component as MyResourceSourceComponent).OutputChanged -= PowerOutputChanged;
            }

            if (compType == typeof(MyResourceSinkComponent))
            {
                (component as MyResourceSinkComponent).CurrentInputChanged -= PowerInputChanged;
            }


        }

        private void PowerInputChanged(MyDefinitionId resourceTypeId, float oldInput, MyResourceSinkComponent sink)
        {
            try
            {
                // power in watts
                PowerInput = sink.CurrentInputByType(MyResourceDistributorComponent.ElectricityId) * 1000000;
                UpdateHeat();
            }
            catch { }
        }

        private void PowerOutputChanged(MyDefinitionId changedResourceId, float oldOutput, MyResourceSourceComponent source)
        {
            try
            {
                // power in watts
                PowerOutput = source.CurrentOutputByType(MyResourceDistributorComponent.ElectricityId) * 1000000;
                UpdateHeat();
            }
            catch { }
        }

        private void UpdateHeat()
        {
            HeatGeneration = (PowerOutput * ProducerGeneration) + (PowerInput * ConsumerGeneration);
        }

        public void ResetNeighbors()
        {
            ClearNeighbors();
            AssignNeighbors();
        }

        public void ClearNeighbors()
        {
            for (int i = 0; i < Neighbors.Count; i++)
            {
                ThermalCell ncell = Neighbors[i];
                ncell.Neighbors.Remove(this);
            }

            Neighbors.Clear();
        }

        public void AssignNeighbors()
        {
            //get a list of current neighbors from the grid
            List<IMySlimBlock> neighbors = new List<IMySlimBlock>();
            Block.GetNeighbours(neighbors);

            for (int i = 0; i < neighbors.Count; i++)
            {
                IMySlimBlock n = neighbors[i];
                ThermalCell ncell = Grid.Get(n.Position);

                Neighbors.Add(ncell);
                ncell.Neighbors.Add(this);
            }
        }

        public void UpdateSurfaces(ref HashSet<Vector3I> exposed, ref HashSet<Vector3I> exposedSurface, ref HashSet<Vector3I> inside, ref HashSet<Vector3I> insideSurface)
        {
            Inside.Clear();
            InsideSurface.Clear();

            Exposed.Clear();
            ExposedSurface.Clear();
            ExposedSurfaceDirection.Clear();

            // define the cells area
            Vector3I min = Block.Min;
            Vector3I max = Block.Max + 1;

            // define the connected cell area
            Vector3I emin = Block.Min - 1;
            Vector3I emax = Block.Max + 2;

            for (int x = emin.X; x < emax.X; x++)
            {
                bool xIn = x >= min.X && x < max.X;

                for (int y = emin.Y; y < emax.Y; y++)
                {
                    bool yIn = y >= min.Y && y < max.Y;

                    for (int z = emin.Z; z < emax.Z; z++)
                    {
                        bool zIn = z >= min.Z && z < max.Z;

                        if ((!xIn && yIn && zIn ||
                            xIn && !yIn && zIn ||
                            xIn && yIn && !zIn) == false
                            ) continue;

                        Vector3I p = new Vector3I(x, y, z);

                        if (exposed.Contains(p))
                        {
                            Exposed.Add(p);

                            if (exposedSurface.Contains(p))
                            {
                                ExposedSurface.Add(p);
                                Vector3I dir = Vector3I.Zero; 
                                if (!xIn)
                                {
                                    dir.X = x == max.X ? 1 : -1;
                                }
                                else if (!yIn)
                                {
                                    dir.Y = y == max.Y ? 1 : -1;
                                }
                                else
                                {
                                    dir.Z = z == max.Z ? 1 : -1;
                                }

                                ExposedSurfaceDirection.Add(dir);


                            }
                        }
                        else if (inside.Contains(p))
                        {
                            Inside.Add(p);

                            if (insideSurface.Contains(p))
                            {
                                InsideSurface.Add(p);
                            }
                        }
                    }
                }
            }

            //
            ExposedSurfaceArea = Exposed.Count * A;
        }

        public float GetTemperature()
        {
            return Temperature;
        }


        /// <summary>
        /// Update the temperature of each cell in the grid
        /// </summary>
        internal void Update()
        {
            // update to the new frame
            Frame++;
            LastTemperature = Temperature;

            // calculate delta between all blocks
            float dt = 0;
            for (int i = 0; i < Neighbors.Count; i++)
            {
                ThermalCell ncell = Neighbors[i];
                if (ncell.Frame != Frame)
                {
                    dt += ncell.Temperature - Temperature;
                }
                else
                {
                    dt += ncell.LastTemperature - Temperature;
                }
            }
            dt = dt * (CubeArea - Exposed.Count) * CubeAreaInv;

            // calculate ambiant temprature
            dt += (Grid.FrameAmbiantTemprature - Temperature) * (Exposed.Count * CubeAreaInv) * Grid.FrameAirDensity;

            // k * A * (dT / dX)
            LastDeltaTemp = kA * dt * dxInverted * SpecificHeatInverted;
            Temperature = Math.Max(0, Temperature + LastDeltaTemp);

            // calculate heat loss
            //float strength = (cell.Temperature / Settings.Instance.VaccumeFullStrengthTemperature);
            //float cool = Settings.Instance.VaccumDrainRate * cell.ExposedSurfaceArea * strength * cell.SpacificHeatRatio;

            // generate heat based on power usage
            //Temperature += HeatGeneration;

            // apply solar heating
            float solarIntensity = UpdateRadiation(ref Grid.FrameSolarDirection, ref Grid.SolarRadiationNode);
            Temperature += A * Settings.Instance.SolarEnergy * solarIntensity * Grid.FrameSolarDecay * Settings.Instance.TimeScaleRatio * SpecificHeatInverted;



            if (Settings.Debug && MyAPIGateway.Session.IsServer)
            {
                Vector3 c = Tools.GetTemperatureColor(Temperature);
                //Vector3 c = Tools.GetTemperatureColor(intensity, 10, 0.5f, 4);
                if (Block.ColorMaskHSV != c)
                {
                    Block.CubeGrid.ColorBlocks(Block.Min, Block.Max, c);
                }
            }
        }
        internal float UpdateRadiation(ref Vector3 targetDirection, ref ThermalRadiationNode node) 
        {
            float intensity = 0;
            MatrixD matrix = Grid.FrameMatrix;
            bool isArmorBlock = Block.FatBlock == null;
            //float gridSize = Grid.Grid.GridSize;

            for (int i = 0; i < ExposedSurfaceDirection.Count; i++)
            {
                // calculate the surface direction
                Vector3I direction = ExposedSurfaceDirection[i];
                int directionIndex = Tools.DirectionToIndex(direction);

                Vector3D startDirection = Vector3D.Rotate(direction, matrix);
                float dot = Vector3.Dot(startDirection, targetDirection);

                
                if (dot < 0)
                {
                    dot = 0;
                    //Vector3D start = Vector3D.Transform((Vector3D)(Vector3)ExposedSurface[i] * gridSize, matrix);
                    //var white = Color.Red.ToVector4();
                    //MySimpleObjectDraw.DrawLine(start, start + (startDirection * 0.5f), MyStringId.GetOrCompute("Square"), ref white, 0.012f * gridSize);
                }
                else
                {
                    //Vector3D start = Vector3D.Transform((Vector3D)(Vector3)ExposedSurface[i] * gridSize, matrix);
                    //var white = Color.White.ToVector4();
                    //MySimpleObjectDraw.DrawLine(start, start + (startDirection), MyStringId.GetOrCompute("Square"), ref white, 0.0012f * gridSize);
                }

                if (isArmorBlock)
                {
                    // records the surface averages
                    node.Sides[directionIndex] += intensity;
                    node.SideSurfaces[directionIndex]++; // should try to calculate this when UpdateSurfaces
                    intensity += dot;
                }
                else
                {
                    intensity += (dot < node.SideAverages[directionIndex]) ? dot : node.SideAverages[directionIndex];
                }
            }

            return intensity;
        }
    }
}
