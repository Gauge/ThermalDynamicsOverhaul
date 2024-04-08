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
using System.IO.Compression;
using VRage.Game.Components.Interfaces;
using System.Drawing;

namespace Thermodynamics
{
    public class ThermalCell
    {
        public int Id;
        public long Frame;

        public float Temperature;
        public float DeltaTemperature;
        public float LastTemprature;

        public float PowerProduced;
        public float PowerConsumed;
        public float HeatGeneration;

        public float C; // c =  Temp / (watt * meter)
        public float Mass; // kg
        public float Area; // m^2
        public float ExposedSurfaceArea; // m^2 of all exposed faces on this block
        public float Radiation;
        public float ThermalMassInv; // 1 / SpecificHeat * Mass


        //public float CubeArea;
        //public float CubeAreaInv;

        public ThermalGrid Grid;
        public IMySlimBlock Block;
        public ThermalCellDefinition Definition;

        public List<Vector3I> Exposed = new List<Vector3I>();
        public List<Vector3I> ExposedSurface = new List<Vector3I>();
        public List<Vector3I> ExposedSurfaceDirection = new List<Vector3I>();
        public List<Vector3I> Inside = new List<Vector3I>();
        public List<Vector3I> InsideSurface = new List<Vector3I>();

        public List<ThermalCell> Neighbors = new List<ThermalCell>();
        public List<int> TouchingSerfacesByNeighbor = new List<int>();

        public ThermalCell(ThermalGrid g, IMySlimBlock b)
        {
            Grid = g;
            Block = b;
            Id = b.Position.Flatten();
            Definition = ThermalCellDefinition.GetDefinition(Block.BlockDefinition.Id);

            //TODO: the listeners need to handle changes at the end
            //of the update cycle instead of whenever.
            SetupListeners();

            Mass = Block.Mass;
            Area = Block.CubeGrid.GridSize * Block.CubeGrid.GridSize;
            C = 1 / (Definition.SpecificHeat * Block.CubeGrid.GridSize);
            ThermalMassInv = 1f / (Definition.SpecificHeat * Mass);

            Vector3I size = (Block.Max - Block.Min) + 1;
            float largestSurface = Math.Max(size.X * size.Y, Math.Max(size.X * size.Z, size.Y * size.Z));
            float kA = Definition.Conductivity * (Area * largestSurface);

            if (kA*C >= 1f) 
            {
                MyLog.Default.Info($"[{Settings.Name}] {Block.BlockDefinition.Id} has a transfer rate of ({kA * C}). Increase the SpecificHeat, Decrease the Conductivity, or increase the update frequency");
            }


            //
            //CubeArea = 2 * (size.X * size.Z + size.Y * size.Z + size.X * size.Y);
            //CubeAreaInv = 1f / CubeArea;

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
                    fat.Components.Get<MyResourceSourceComponent>().OutputChanged += PowerProducedChanged;
                }

                if (fat.Components.Contains(typeof(MyResourceSinkComponent)))
                {
                    fat.Components.Get<MyResourceSinkComponent>().CurrentInputChanged += PowerConsumedChanged;
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
                                    AddNeighbors(c, ncell);
                                }
                            }
                        }
                    }
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

                    RemoveNeighbors(cell, ncell);
                    break;
                }
            }
            else
            {
                ThermalCell ncell = block.Top.CubeGrid.GameLogic.GetAs<ThermalGrid>().Get(block.Top.Position);

                //MyLog.Default.Info($"[{Settings.Name}] Grid: {cell.Block.CubeGrid.EntityId} cell: {cell.Block.Position} adding connection to, nGrid: {ncell.Block.CubeGrid.EntityId} nCell: {ncell.Block.Position}");

                AddNeighbors(cell, ncell);
            }
        }

        private void OnThrustChanged(IMyThrust block, float old, float current)
        {
            MyThrustDefinition def = block.SlimBlock.BlockDefinition as MyThrustDefinition;


            if (block.IsWorking)
            {
                if (def.FuelConverter.FuelId == MyResourceDistributorComponent.ElectricityId)
                {
                    PowerConsumed = (def.MinPowerConsumption + (def.MaxPowerConsumption * (block.CurrentThrust / block.MaxThrust))) * Tools.MWtoWatt;
                }
                else
                {
                    PowerConsumed = def.ForceMagnitude * (block.CurrentThrust / block.MaxThrust);
                }
            }
            else
            {
                PowerConsumed = 0;
            }

            UpdateHeat();
        }

        private void OnComponentAdded(Type compType, IMyEntityComponentBase component)
        {
            if (compType == typeof(MyResourceSourceComponent))
            {
                (component as MyResourceSourceComponent).OutputChanged += PowerProducedChanged;
            }

            if (compType == typeof(MyResourceSinkComponent))
            {
                (component as MyResourceSinkComponent).CurrentInputChanged += PowerConsumedChanged;
            }
        }

        private void OnComponentRemoved(Type compType, IMyEntityComponentBase component)
        {
            if (compType == typeof(MyResourceSourceComponent))
            {
                (component as MyResourceSourceComponent).OutputChanged -= PowerProducedChanged;
            }

            if (compType == typeof(MyResourceSinkComponent))
            {
                (component as MyResourceSinkComponent).CurrentInputChanged -= PowerConsumedChanged;
            }
        }

        /// <summary>
        /// Adjusts heat generation based on consumed power 
        /// </summary>
        private void PowerConsumedChanged(MyDefinitionId resourceTypeId, float oldInput, MyResourceSinkComponent sink)
        {
            if (resourceTypeId == MyResourceDistributorComponent.ElectricityId)
            {
                try
                {
                    // power in watts
                    PowerConsumed = sink.CurrentInputByType(MyResourceDistributorComponent.ElectricityId) * Tools.MWtoWatt;
                    UpdateHeat();
                }
                catch { }
            }
        }

        private void PowerProducedChanged(MyDefinitionId changedResourceId, float oldOutput, MyResourceSourceComponent source)
        {
            if (changedResourceId == MyResourceDistributorComponent.ElectricityId)
            {
                try
                {
                    // power in watts
                    PowerProduced = source.CurrentOutputByType(MyResourceDistributorComponent.ElectricityId) * Tools.MWtoWatt;
                    UpdateHeat();
                }
                catch { }
            }
        }

        private void UpdateHeat()
        {
            // power produced and consumed are in Watts or Joules per second.
            // it gets multiplied by the waste energy percent and timescale ratio.
            // we then have the heat in joules that needs to be converted into temprature.
            // we do that by dividing it by the ThermalMass (SpecificHeat * Mass)
            HeatGeneration = Settings.Instance.TimeScaleRatio * ((PowerProduced * Definition.ProducerWasteEnergy) + (PowerConsumed * Definition.ConsumerWasteEnergy)) * ThermalMassInv;
        }

        public void ResetNeighbors()
        {
            ClearNeighbors();
            AddAllNeighbors();
        }

        public void ClearNeighbors()
        {
            for (int i = 0; i < Neighbors.Count; i++)
            {
                ThermalCell ncell = Neighbors[i];
                int j = ncell.Neighbors.IndexOf(this);
                if (j != -1)
                {
                    ncell.Neighbors.RemoveAt(j);
                    ncell.TouchingSerfacesByNeighbor.RemoveAt(j);
                }
            }

            Neighbors.Clear();
            TouchingSerfacesByNeighbor.Clear();
        }

        public void AddAllNeighbors()
        {
            //get a list of current neighbors from the grid
            List<IMySlimBlock> neighbors = new List<IMySlimBlock>();
            Block.GetNeighbours(neighbors);

            for (int i = 0; i < neighbors.Count; i++)
            {
                IMySlimBlock n = neighbors[i];
                ThermalCell ncell = Grid.Get(n.Position);

                if (!Neighbors.Contains(ncell))
                {
                    AddNeighbors(this, ncell);
                }
            }
        }

        protected static void AddNeighbors(ThermalCell n1, ThermalCell n2)
        {
            n1.Neighbors.Add(n2);
            n2.Neighbors.Add(n1);

            int area = Tools.FindTouchingSurfaceArea(n1.Block.Min, n1.Block.Max + 1, n2.Block.Min, n2.Block.Max + 1);

            n1.TouchingSerfacesByNeighbor.Add(area);
            n2.TouchingSerfacesByNeighbor.Add(area);
        }

        protected static void RemoveNeighbors(ThermalCell n1, ThermalCell n2)
        {
            int i = n1.Neighbors.IndexOf(n2);
            if (i != -1)
            {
                n1.Neighbors.RemoveAt(i);
                n1.TouchingSerfacesByNeighbor.RemoveAt(i);
            }

            int j = n2.Neighbors.IndexOf(n1);
            if (i != -1)
            {
                n2.Neighbors.RemoveAt(j);
                n2.TouchingSerfacesByNeighbor.RemoveAt(j);
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

            ExposedSurfaceArea = Exposed.Count * Area;
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
            LastTemprature = Temperature;

            // calculate delta between all neighboring blocks
            float deltaTemperature = 0;
            for (int i = 0; i < Neighbors.Count; i++)
            {
                ThermalCell ncell = Neighbors[i];
                // area = meter^2
                float area = Area <= ncell.Area ? Area : ncell.Area;

                // conductivity, k = watt / (meter * Temp)
                // kA = watt * meter / Temp
                float kA = Definition.Conductivity * area * TouchingSerfacesByNeighbor[i];

                // deltaTemperature = watt * meter
                if (ncell.Frame != Frame)
                {
                    deltaTemperature += kA * (ncell.Temperature - Temperature);
                }
                else
                {
                    deltaTemperature += kA * (ncell.LastTemprature - Temperature);
                }
            }

            // use Stefan-Boltzmann Law to calculate the energy lossed/gained from the environment
            // we make it nagiative to indicate removal of energy
            //Radiation = -1 * Definition.Emissivity * Tools.BoltzmannConstant * ExposedSurfaceArea * (Temperature * Temperature * Temperature * Temperature - Grid.FrameAmbientTemprature * Grid.FrameAmbientTemprature * Grid.FrameAmbientTemprature * Grid.FrameAmbientTemprature);

            if (!Grid.FrameSolarOccluded)
            {
                //float intensity = DirectionalRadiationIntensity(ref Grid.FrameSolarDirection, ref Grid.SolarRadiationNode);
                //Radiation += Settings.Instance.SolarEnergy * Definition.Emissivity * (intensity * ExposedSurfaceArea);
            }

            // calculate ambiant temprature
            //deltaTemperature += (Grid.FrameAmbientTemprature - Temperature) * (Exposed.Count * CubeAreaInv) * Grid.FrameAmbientStrength * Grid.FrameAmbientConductivity;



            // C = Temp / Watt * meter and deltaTemperature = Watt * Meter.
            // these cancel leaving only temperature behind
            DeltaTemperature = ((C * deltaTemperature) + (Radiation * ThermalMassInv)) * Settings.Instance.TimeScaleRatio;
            Temperature = Math.Max(0, Temperature + DeltaTemperature);

            // generate heat based on power usage
            Temperature += HeatGeneration;

            // apply solar heating
            //float solarIntensity = 0;
            //if (!Grid.FrameSolarOccluded)
            //{
            //    solarIntensity = UpdateRadiation(ref Grid.FrameSolarDirection, ref Grid.SolarRadiationNode);
            //    Temperature += Area * Settings.Instance.SolarEnergy * solarIntensity * Grid.FrameSolarDecay * c * Settings.Instance.TimeScaleRatio;
            //}

            if (Settings.Debug && MyAPIGateway.Session.IsServer)
            {
                Vector3 color = Tools.GetTemperatureColor(Temperature);
                //Vector3 c = Tools.GetTemperatureColor(solarIntensity, 10, 0.5f, 4);
                if (Block.ColorMaskHSV != color)
                {
                    Block.CubeGrid.ColorBlocks(Block.Min, Block.Max, color);
                }
            }
        }

        /// <summary>
        /// Calculates the intensity of directional heating objects (likely only solar)
        /// </summary>
        /// <param name="targetDirection"></param>
        /// <param name="node"></param>
        /// <returns></returns>
        internal float DirectionalRadiationIntensity(ref Vector3 targetDirection, ref ThermalRadiationNode node)
        {
            float intensity = 0;
            MatrixD matrix = Grid.FrameMatrix;
            bool isCube = (Block.Max - Block.Min).Volume() <= 1;

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
                    //debug
                    //Vector3D start = Vector3D.Transform((Vector3D)(Vector3)ExposedSurface[i] * gridSize, matrix);
                    //var white = Color.Red.ToVector4();
                    //MySimpleObjectDraw.DrawLine(start, start + (startDirection * 0.5f), MyStringId.GetOrCompute("Square"), ref white, 0.012f * gridSize);
                }
                else
                {
                    //debug
                    //Vector3D start = Vector3D.Transform((Vector3D)(Vector3)ExposedSurface[i] * gridSize, matrix);
                    //var white = Color.White.ToVector4();
                    //MySimpleObjectDraw.DrawLine(start, start + (startDirection), MyStringId.GetOrCompute("Square"), ref white, 0.0012f * gridSize);
                }

                if (isCube)
                {
                    // records the surface averages for all 1x1x1 blocks
                    node.Sides[directionIndex] += intensity;
                    node.SideSurfaces[directionIndex]++;
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
