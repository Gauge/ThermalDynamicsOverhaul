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
using System.Security.AccessControl;
using System.Reflection.Metadata.Ecma335;

namespace Thermodynamics
{
    public class ThermalCell
    {
        public int Id;
        public long Frame;

        public float Temperature;
        public float LastTemprature;
        public float DeltaTemperature;

        public float EnergyProduction;
        public float EnergyConsumption;
        public float ThrustEnergyConsumption;
        public float HeatGeneration;

        public float C; // c =  Temp / (watt * meter)
        public float Mass; // kg
        public float Area; // m^2
        public float ExposedSurfaceArea; // m^2 of all exposed faces on this block
        public float Radiation;
        public float ThermalMassInv; // 1 / SpecificHeat * Mass
        public float boltzmann;

        public ThermalGrid Grid;
        public IMySlimBlock Block;
        public ThermalCellDefinition Definition;

        private List<ThermalCell> Neighbors = new List<ThermalCell>();
        private List<int> TouchingSerfacesByNeighbor = new List<int>();
        
        public int ExposedSurfaces = 0;
        private List<Vector3I> ExposedSurfaceDirections = new List<Vector3I>();

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
            C = 1 / (Definition.SpecificHeat * Mass * Block.CubeGrid.GridSize);
            ThermalMassInv = 1f / (Definition.SpecificHeat * Mass);
            boltzmann = -1 * Definition.Emissivity * Tools.BoltzmannConstant;

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
                (fat as IMyDoor).DoorStateChanged += (state) => Grid.ResetMapper();
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

            ThrustEnergyConsumption = def.ForceMagnitude * (block.CurrentThrust / block.MaxThrust);
            UpdateHeat();
        }

        /// <summary>
        /// Adjusts heat generation based on consumed power 
        /// </summary>
        private void PowerConsumedChanged(MyDefinitionId resourceTypeId, float oldInput, MyResourceSinkComponent sink)
        {
            try
            {
                if (resourceTypeId == MyResourceDistributorComponent.ElectricityId)
                {
                    EnergyConsumption = sink.CurrentInputByType(resourceTypeId) * Tools.MWtoWatt;
                    UpdateHeat();
                }
            }
            catch { }
        }

        private void PowerProducedChanged(MyDefinitionId resourceTypeId, float oldOutput, MyResourceSourceComponent source)
        {
            try
            {
                if (resourceTypeId == MyResourceDistributorComponent.ElectricityId)
                {
                    EnergyProduction = source.CurrentOutputByType(resourceTypeId) * Tools.MWtoWatt;
                    UpdateHeat();
                }
            }
            catch { }
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

            int area = 0;
            if (n1.Grid.Entity.EntityId != n2.Grid.Entity.EntityId)
            {
                area = 1;
            }
            else 
            {
                area = Tools.FindTouchingSurfaceArea(n1.Block.Min, n1.Block.Max + 1, n2.Block.Min, n2.Block.Max + 1);
            }

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


        public float GetTemperature()
        {
            return Temperature;
        }

        /// <summary>
        /// Update the temperature of each cell in the grid
        /// </summary>
        internal void Update()
        {
            // cells are only looked at once per frame. cells must keep track of their unultered temperature (LastTemperature)
            // so that all blocks are working with the same simulation frame data.
            Frame = Grid.SimulationFrame;
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

                // if the neighboring blocks have not been updated use temperature
                // otherwise use LastTemperature
                if (ncell.Frame != Frame)
                {
                    // deltaTemperature = watt * meter
                    deltaTemperature += kA * (ncell.Temperature - Temperature);
                }
                else
                {
                    deltaTemperature += kA * (ncell.LastTemprature - Temperature);
                }

                //if (Block.BlockDefinition.Id.ToString().Contains("LargeRotor")) 
                //{
                //    MyLog.Default.Info($"[{Settings.Name}] {Id}->{ncell.Id} ns: {TouchingSerfacesByNeighbor[i]} T: {Temperature} nT: {ncell.Temperature} dT: {deltaTemperature}");
                //}
            }

            // use Stefan-Boltzmann Law to calculate the energy lossed/gained from the environment
            // we make it nagiative to indicate removal of energy
            Radiation = boltzmann * (Temperature * Temperature * Temperature * Temperature - Grid.FrameAmbientTempratureP4);

            if (Settings.Instance.EnableSolarHeat && !Grid.FrameSolarOccluded)
            {
                float intensity = DirectionalRadiationIntensity(ref Grid.FrameSolarDirection, ref Grid.SolarRadiationNode);
                Radiation += Settings.Instance.SolarEnergy * Definition.Emissivity * (intensity * ExposedSurfaceArea);
            }

            // C = Temp / Watt * meter and deltaTemperature = Watt * Meter.
            // these cancel leaving only temperature behind
            DeltaTemperature = ((C * deltaTemperature) + (Radiation * ThermalMassInv)) * Settings.Instance.TimeScaleRatio;
            Temperature = Math.Max(0, Temperature + DeltaTemperature);

            // generate heat based on power usage
            Temperature += HeatGeneration;

            if (Settings.Instance.EnableDamage = Temperature > Definition.CriticalTemperature) 
            {
                Block.DoDamage((Temperature - Definition.CriticalTemperature) * Definition.CriticalTemperatureScaler, MyStringHash.GetOrCompute("thermal"), false);
            }

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

        private void UpdateHeat()
        {
            // power produced and consumed are in Watts or Joules per second.
            // it gets multiplied by the waste energy percent and timescale ratio.
            // we then have the heat in joules that needs to be converted into temprature.
            // we do that by dividing it by the ThermalMass (SpecificHeat * Mass)


            float produced = EnergyProduction * Definition.ProducerWasteEnergy;
            float consumed = (EnergyConsumption + ThrustEnergyConsumption) * Definition.ConsumerWasteEnergy;
            HeatGeneration = Settings.Instance.TimeScaleRatio * (produced + consumed) * ThermalMassInv;
        }

        public void UpdateSurfaces(ref HashSet<Vector3I> exterior, ref Vector3I[] neighbors)
        {
            ExposedSurfaces = 0;
            ExposedSurfaceDirections.Clear();

            Vector3I min = Block.Min;
            Vector3I max = Block.Max + 1;

            for (int x = min.X; x < max.X; x++)
            {
                for (int y = min.Y; y < max.Y; y++)
                {
                    for (int z = min.Z; z < max.Z; z++)
                    {
                        Vector3I node = new Vector3I(x, y, z);
                        int flag = Grid.BlockNodes[node];

                        for (int i = 0; i < 6; i++) 
                        {
                            // if this node is solid but the neighboring block is not, add an exposed serface
                            int d = (1 << i);
                            int nd = (1 << i + 6);
                            if ((flag&nd) == 0) 
                            {
                                Vector3I neighbor = neighbors[i];
                                Vector3I n = node + neighbor;

                                if (exterior.Contains(n))
                                {
                                    ExposedSurfaces++;
                                    if (!ExposedSurfaceDirections.Contains(neighbor))
                                        ExposedSurfaceDirections.Add(neighbor);
                                }
                            }
                        }
                    }
                }
            }

            ExposedSurfaceArea = ExposedSurfaces * Area;
            boltzmann = -1 * Definition.Emissivity * Tools.BoltzmannConstant * ExposedSurfaceArea;
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

            for (int i = 0; i < ExposedSurfaceDirections.Count; i++)
            {
                // calculate the surface direction
                Vector3I direction = ExposedSurfaceDirections[i];
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
