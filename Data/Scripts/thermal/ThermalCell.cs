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

namespace ThermalOverhaul
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

        public ThermalGrid Grid;
        public IMySlimBlock Block;

        public List<Vector3I> Exposed = new List<Vector3I>();
        public List<Vector3> ExposedDirection = new List<Vector3>();
        public List<Vector3I> Inside = new List<Vector3I>();
        public List<Vector3I> InsideSurface = new List<Vector3I>();
        public List<Vector3I> ExposedSurface = new List<Vector3I>();
        public List<ThermalCell> Neighbors = new List<ThermalCell>();

        public void Init(BlockProperties p, IMySlimBlock b, ThermalGrid g)
        {
            Grid = g;
            Block = b;
            Id = b.Position.Flatten();
            SetupListeners();

            // k = Watts / (meters - kelven)
            k = p.Conductivity;

            // A = surface area
            A = Block.CubeGrid.GridSize * Block.CubeGrid.GridSize;
            kA = k * A * Settings.Instance.TimeScaleRatio; // added the time scale ratio to save on compute power
            dxInverted = 1f / Block.CubeGrid.GridSize;

            if (p.SpacificHeat > 0)
            {
                SpecificHeatInverted = 1f / (Block.Mass * p.SpacificHeat);
            }

            ProducerGeneration = p.ProducerWasteHeatPerWatt * Settings.Instance.TimeScaleRatio * SpecificHeatInverted;
            ConsumerGeneration = p.ConsumerWasteHeatPerWatt * Settings.Instance.TimeScaleRatio * SpecificHeatInverted;
            UpdateHeat();
        }

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

        public void UpdateInsideBlocks(ref HashSet<Vector3I> exposed, ref HashSet<Vector3I> exposedSurface, ref HashSet<Vector3I> inside, ref HashSet<Vector3I> insideSurface)
        {
            Inside.Clear();
            InsideSurface.Clear();

            Exposed.Clear();
            ExposedSurface.Clear();

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

                                if (!xIn)
                                {
                                    ExposedDirection.Add(new Vector3(x == max.X ? 1 : -1, 0, 0));
                                }
                                else if (!yIn)
                                {
                                    ExposedDirection.Add(new Vector3(0, y == max.Y ? 1 : -1, 0));
                                }
                                else
                                {
                                    ExposedDirection.Add(new Vector3(0, 0, z == max.Z ? 1 : -1));
                                }
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
            // 1. update last temperature
            // 2. Calculate and apply heat exchange with neighbors
            // 3. calculate and apply heat loss
            // 4. Apply heat gain

            Frame++;
            LastTemperature = Temperature;

            // Calculate the total heat gained or lost by the cell

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

            // k * A * (dT / dX)
            LastDeltaTemp = kA * dt * dxInverted * SpecificHeatInverted;
            Temperature = Math.Max(0, Temperature + LastDeltaTemp);

            // calculate heat loss
            //float strength = (cell.Temperature / Settings.Instance.VaccumeFullStrengthTemperature);
            //float cool = Settings.Instance.VaccumDrainRate * cell.ExposedSurfaceArea * strength * cell.SpacificHeatRatio;

            // generate heat based on power usage
            //Temperature += HeatGeneration;

            float intensity = UpdateRadiationNodes();


            if (Settings.Debug && MyAPIGateway.Session.IsServer)
            {
                //Vector3 c = GetTemperatureColor(Temperature);
                Vector3 c = GetTemperatureColor(intensity, 10, 0.5f, 4);
                if (Block.ColorMaskHSV != c)
                {
                    Block.CubeGrid.ColorBlocks(Block.Min, Block.Max, c);
                }
            }
        }

        internal float UpdateRadiationNodes() 
        {
            float intensity = 0;
            MatrixD matrix = Grid.FrameMatrix;
            Vector3 sunDirection = Grid.FrameSolarDirection;
            ThermalRadiationNode node = Grid.SolarRadiationNode;

            float gridSize = Grid.Grid.GridSize;
            //Vector3D gridCenter = Grid.FrameGridCenter;
            //Vector3D blockPosition = (Vector3D)(Vector3)Block.Position;

            for (int i = 0; i < ExposedSurface.Count; i++)
            {
                Vector3I s = ExposedSurface[i];
                int directionIndex = (s.X > 0) ? 0 : (s.X < 0) ? 1 : (s.Y > 0) ? 2 : (s.Y < 0) ? 3 : (s.Z > 0) ? 4 : 5;

                Vector3D startDirection = Vector3D.Rotate(ExposedDirection[i], matrix);
                float dot = Vector3.Dot(startDirection, sunDirection);

                if (dot < 0)
                {
                    dot = 0;
                    //Vector3D start = Vector3D.Transform((Vector3D)(Vector3)ExposedSurface[i] * gridSize, matrix);
                    //var white = Color.Red.ToVector4();
                    //MySimpleObjectDraw.DrawLine(start, start + (startDirection * 0.5f), MyStringId.GetOrCompute("Square"), ref white, 0.008f);
                }
                //else
                //{
                //    Vector3D start = Vector3D.Transform((Vector3D)(Vector3)ExposedSurface[i] * gridSize, matrix);
                //    var white = Color.White.ToVector4();
                //    MySimpleObjectDraw.DrawLine(start, start + (startDirection), MyStringId.GetOrCompute("Square"), ref white, 0.008f);
                //}        

                if (Block.FatBlock == null)
                {
                    node.Sides[directionIndex] += intensity;
                    node.SideSurfaces[directionIndex]++;
                    intensity += dot;
                }
                else
                {
                    intensity += (dot < node.SideAverages[directionIndex]) ? dot : node.SideAverages[directionIndex];
                }
            }

            Temperature += A * Settings.Instance.SolarEnergy * intensity * Settings.Instance.TimeScaleRatio * SpecificHeatInverted;

            return intensity;
        }

        /// <summary>
        /// Generates a heat map
        /// </summary>
        /// <param name="temp">current temperature</param>
        /// <param name="max">maximum possible temprature</param>
        /// <param name="low">0 is black this value is blue</param>
        /// <param name="high">this value is red max value is white</param>
        /// <returns>HSV Vector3</returns>
        public Vector3 GetTemperatureColor(float temp, float max = 2000, float low = 265f, float high = 600f)
        {
            // Clamp the temperature to the range 0-max
            float t = Math.Max(0, Math.Min(max, temp));

            float h = 240f / 360f;
            float s = 1;
            float v = 0.5f;

            if (t < low)
            {
                v = (1.5f * (t / low)) - 1;
            }
            else if (t < high)
            {
                h = (240f - ((t - low) / (high - low) * 240f)) / 360f;
            }
            else
            {
                h = 0;
                s = 1 - (2 * ((t - high) / (max - high)));
            }

            return new Vector3(h, s, v);
        }

        //private void CalculateSurface() 
        //{
        //	//MyLog.Default.Info($"[{Settings.Name}] {Block.Position} Begin Calculate Surface");
        //	Vector3I blockArea = (Block.Max+1) - Block.Min;

        //	int volume = 0;
        //	Vector3I minx = new Vector3I(Block.Min.X - 1, Block.Min.Y, Block.Min.Z);
        //	Vector3I maxx = new Vector3I(Block.Max.X + 2, Block.Max.Y+1, Block.Max.Z+1);
        //	BoundingBoxI SearchAreaX = new BoundingBoxI(minx, maxx);

        //	volume += SearchAreaX.Size.Volume() - blockArea.Volume();

        //	Vector3I miny = new Vector3I(Block.Min.X, Block.Min.Y - 1, Block.Min.Z);
        //	Vector3I maxy = new Vector3I(Block.Max.X+1, Block.Max.Y + 2, Block.Max.Z+1);
        //	BoundingBoxI SearchAreaY = new BoundingBoxI(miny, maxy);

        //	volume += SearchAreaY.Size.Volume() - blockArea.Volume();

        //	Vector3I minz = new Vector3I(Block.Min.X, Block.Min.Y, Block.Min.Z - 1);
        //	Vector3I maxz = new Vector3I(Block.Max.X+1, Block.Max.Y+1, Block.Max.Z + 2);
        //	BoundingBoxI SearchAreaZ = new BoundingBoxI(minz, maxz);

        //	volume += SearchAreaZ.Size.Volume() - blockArea.Volume();

        //	//MyLog.Default.Info($"[{Settings.Name}] {Block.Position} {volume}");

        //	for (int i = 0; i < Neighbors.Count; i++)
        //	{
        //		ThermalCell ncell = Neighbors[i];

        //		var def = (ncell.Block.BlockDefinition as MyCubeBlockDefinition);
        //		if (def != null && def.IsAirTight.HasValue && def.IsAirTight.Value)
        //		{
        //			BoundingBoxI ncellBox = new BoundingBoxI(ncell.Block.Min, ncell.Block.Max + 1);

        //			volume -= ncellBox.Intersect(SearchAreaX).Size.Volume();
        //			volume -= ncellBox.Intersect(SearchAreaY).Size.Volume();
        //			volume -= ncellBox.Intersect(SearchAreaZ).Size.Volume();
        //		}

        //		//MyLog.Default.Info($"[{Settings.Name}] {Block.Position} {volume}");
        //	}

        //	ExposedSurfaceArea = volume * Block.CubeGrid.GridSize * Block.CubeGrid.GridSize;

        //}

    }
}
