using EmptyKeys.UserInterface.Generated.StoreBlockView_Bindings;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using SpaceEngineers.Game.ModAPI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;

namespace ThermalOverhaul
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_CubeGrid), true)]
    public class ThermalGrid : MyGameLogicComponent
    {

        private static readonly Guid StorageGuid = new Guid("f7cd64ae-9cd8-41f3-8e5d-3db992619343");

        private MyCubeGrid Grid;
        public Dictionary<long, int> PositionToIndex;
        public MyFreeList<ThermalCell> Thermals;

        public GridMapper Mapper;

        private int IterationFrames = 0;
        private int IterationIndex = 0;
        private int CountPerFrame = 0;
        private bool LoopDirection = true;

        //private int ExternalCountPerFrame = 0;
        //public bool ExternalRoomUpdateComplete = false;
        public bool ThermalCellUpdateComplete = true;

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);

            if (Settings.Instance == null)
            {
                Settings.Instance = Settings.GetDefaults();
            }

            Grid = Entity as MyCubeGrid;

            if (Entity.Storage == null)
                Entity.Storage = new MyModStorageComponent();

            PositionToIndex = new Dictionary<long, int>();
            Thermals = new MyFreeList<ThermalCell>();
            Mapper = new GridMapper(Grid);

            Grid.OnBlockAdded += BlockAdded;
            Grid.OnBlockRemoved += BlockRemoved;

            //MyLog.Default.Info($"[{Settings.Name}] {Entity.DisplayName} ({Entity.EntityId}) Storage is empty: {Entity.Storage == null}");

            NeedsUpdate = MyEntityUpdateEnum.EACH_FRAME | MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
        }

        public override bool IsSerialized()
        {
            Save();
            return base.IsSerialized();
        }

        private GridData PackGridInfo()
        {
            GridData gridData = new GridData();

            int count = Thermals.Count;

            gridData.position = new long[count];
            gridData.temperature = new float[count];

            int realCount = 0;
            float temp = 0;
            for (int i = 0; i < Thermals.UsedLength; i++)
            {
                ThermalCell c = Thermals.list[i];
                if (c == null || (temp = c.Temperature) == 0) continue;

                gridData.position[realCount] = c.Id;
                gridData.temperature[realCount] = temp;
                realCount++;
            }

            Array.Resize(ref gridData.position, realCount);
            Array.Resize(ref gridData.temperature, realCount);

            return gridData;
        }

        private void Save()
        {
            //Stopwatch sw = Stopwatch.StartNew();

            string data = Convert.ToBase64String(MyAPIGateway.Utilities.SerializeToBinary(PackGridInfo()));

            MyModStorageComponentBase storage = Entity.Storage;
            if (storage.ContainsKey(StorageGuid))
            {
                storage[StorageGuid] = data;
            }
            else
            {
                storage.Add(StorageGuid, data);
            }
            //sw.Stop();
            //MyLog.Default.Info($"[{Settings.Name}] [SAVE] {Grid.DisplayName} ({Grid.EntityId}) t-{((float)sw.ElapsedTicks / TimeSpan.TicksPerMillisecond).ToString("n8")}ms, size: {data.Length}, count: {s.temperature.Length}");
        }

        private void Load()
        {
            Stopwatch sw = Stopwatch.StartNew();
            try
            {
                if (Entity.Storage.ContainsKey(StorageGuid))
                {
                    GridData data = MyAPIGateway.Utilities.SerializeFromBinary<GridData>(Convert.FromBase64String(Entity.Storage[StorageGuid]));

                    for (int i = 0; i < data.temperature.Length; i++)
                    {
                        Thermals.list[PositionToIndex[data.position[i]]].Temperature = data.temperature[i];
                    }
                }
            }
            catch
            {

            }

            sw.Stop();
            MyLog.Default.Info($"[{Settings.Name}] [LOAD] {Grid.DisplayName} ({Grid.EntityId}) t-{((float)sw.ElapsedTicks / TimeSpan.TicksPerMillisecond).ToString("n8")}ms");
        }

        private void BlockAdded(IMySlimBlock b)
        {
            MyCubeBlockDefinition def = b.BlockDefinition as MyCubeBlockDefinition;

            // get block properties
            string type = def.Id.TypeId.ToString();
            string subtype = def.Id.SubtypeId.ToString();

            BlockProperties group = null;
            BlockProperties block = null;


            foreach (BlockProperties bp in Settings.Instance.BlockConfig)
            {
                if (bp.Type != type)
                    continue;

                if (string.IsNullOrWhiteSpace(bp.Subtype))
                {
                    group = bp;
                }
                else if (bp.Subtype == subtype)
                {
                    block = bp;
                    break;
                }
            }
            ThermalCell cell = new ThermalCell();

            if (block != null)
            {
                cell.Init(block, b);
            }
            else if (group != null)
            {
                cell.Init(group, b);
            }
            else
            {
                cell.Init(Settings.Instance.Generic, b);
            }

            cell.AssignNeighbors();

            IMyCubeBlock fat = b.FatBlock;
            if (fat is IMyPistonBase)
            {
                (fat as IMyPistonBase).AttachedEntityChanged += GridGroupChanged;
            }
            else if (fat is IMyMotorBase)
            {
                (fat as IMyMotorBase).AttachedEntityChanged += GridGroupChanged;
            }
            else if (fat is IMyLandingGear)
            {
                // had to use this crappy method because the better method is broken
                // KEEN!!! fix your code please!
                IMyLandingGear gear = (fat as IMyLandingGear);
                gear.StateChanged += (state) =>
                {
                    IMyEntity entity = gear.GetAttachedEntity();
                    ThermalCell c = Get(gear.Position);

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
                                    ncell.CalculateSurface();
                                    c.Neighbors.Add(ncell);
                                }
                            }
                        }
                    }

                    c.CalculateSurface();
                };
            }
            else if (fat is IMyDoor)
            {
                (fat as IMyDoor).DoorStateChanged += (state) => Mapper.ExternalBlockReset();
            }

            //MyLog.Default.Info($"[{Settings.Name}] Added ({b.Position.Flatten()}) {b.Position} --- {type}/{subtype}");

            int index = Thermals.Allocate();
            PositionToIndex.Add(b.Position.Flatten(), index);
            Thermals.list[index] = cell;

            //MyLog.Default.Info($"[{Settings.Name}] Added {b.Position} Index: {index} {type}/{subtype}");

            CountPerFrame = GetCountPerFrame();
            Mapper.ExternalBlockReset();
        }

        private void BlockRemoved(IMySlimBlock b)
        {
            IMyCubeBlock fat = b.FatBlock;
            if (fat is IMyPistonBase)
            {
                (fat as IMyPistonBase).AttachedEntityChanged -= GridGroupChanged;
            }
            else if (fat is IMyMotorBase)
            {
                (fat as IMyMotorBase).AttachedEntityChanged -= GridGroupChanged;
            }
            else if (fat is IMyDoor)
            {
                (fat as IMyDoor).DoorStateChanged -= (state) => Mapper.ExternalBlockReset();
            }


            
            long flat = b.Position.Flatten();
            int index = PositionToIndex[flat];
            ThermalCell cell = Thermals.list[index];
            cell.ClearNeighbors();

            PositionToIndex.Remove(flat);
            Thermals.Free(index);

            CountPerFrame = GetCountPerFrame();
            Mapper.ExternalBlockReset();
        }

        private void GridGroupChanged(IMyMechanicalConnectionBlock block)
        {
            ThermalCell cell = Get(block.Position);

            MyLog.Default.Info($"[{Settings.Name}] {block.Position} IsAttached: {block.IsAttached} DoubleCheck: {block.Top != null}");

            if (block.Top == null)
            {
                for (int i = 0; i < cell.Neighbors.Count; i++)
                {
                    ThermalCell ncell = cell.Neighbors[i];

                    if (ncell.Block.CubeGrid != cell.Block.CubeGrid)
                    {
                        MyLog.Default.Info($"[{Settings.Name}] Grid: {cell.Block.CubeGrid.EntityId} cell: {cell.Block.Position} removed connection to, Grid: {ncell.Block.CubeGrid.EntityId} Cell: {ncell.Block.Position}");

                        ncell.Neighbors.Remove(cell);
                        ncell.CalculateSurface();
                        cell.Neighbors.RemoveAt(i);
                        break;
                    }
                }
                cell.CalculateSurface();
            }
            else
            {
                ThermalGrid g = block.Top.CubeGrid.GameLogic.GetAs<ThermalGrid>();
                ThermalCell ncell = g.Get(block.Top.Position);

                MyLog.Default.Info($"[{Settings.Name}] Grid: {cell.Block.CubeGrid.EntityId} cell: {cell.Block.Position} adding connection to, Grid: {ncell.Block.CubeGrid.EntityId} Cell: {ncell.Block.Position}");

                cell.Neighbors.Add(ncell);
                cell.CalculateSurface();

                ncell.Neighbors.Add(cell);
                ncell.CalculateSurface();
            }
        }

        public override void UpdateOnceBeforeFrame()
        {
            if (Grid.Physics == null)
                NeedsUpdate = MyEntityUpdateEnum.NONE;

            Load();
        }

        public override void UpdateBeforeSimulation()
        {
            IterationFrames++;

            int count = Thermals.Count;
            int target = count - IterationIndex;
            if (CountPerFrame < target)
            {
                target = CountPerFrame;
            }

            target += IterationIndex;

            //MyAPIGateway.Utilities.ShowNotification($"[Loop] Nodes: {count}, Frames/Cycle {Settings.Instance.Frequency} Nodes/Cycle: {CountPerFrame} Target: {target}, Index: {IterationIndex}", 1, "White");
            while (IterationIndex < target)
            {
                ThermalCell cell = Thermals.list[(LoopDirection) ? IterationIndex : count - 1 - IterationIndex];
                if (cell != null)
                {
                    if (!ThermalCellUpdateComplete)
                    {
                        cell.UpdateInsideBlocks(ref Mapper.Blocks);
                    }

                    UpdateTemperatures(ref cell);
                }

                IterationIndex++;
            }

            int loopCount = 0;
            while (Mapper.BlockQueue.Count > 0 && loopCount < Mapper.ExternalCountPerFrame)
            {
                Mapper.ExternalBlockCheck(Mapper.BlockQueue.Dequeue());
                loopCount++;
            }

            if (IterationIndex >= count && IterationFrames >= Settings.Instance.Frequency)
            {
                IterationIndex = 0;
                IterationFrames = 0;
                LoopDirection = !LoopDirection;

                if (!ThermalCellUpdateComplete)
                    ThermalCellUpdateComplete = true;

                if (!Mapper.ExternalRoomUpdateComplete && Mapper.BlockQueue.Count == 0)
                {
                    Mapper.ExternalRoomUpdateComplete = true;
                    ThermalCellUpdateComplete = false;
                }

            }
        }

        public int GetCountPerFrame()
        {
            return 1 + (Thermals.Count / Settings.Instance.Frequency);
        }


        /// <summary>
        /// Update the temperature of each cell in the grid
        /// </summary>
        private void UpdateTemperatures(ref ThermalCell cell)
        {
            cell.CurrentFrame++;
            cell.LastTemperature = cell.Temperature;

            // generate heat based on power usage
            cell.Temperature += cell.HeatGeneration;

            // Calculate the total heat gained or lost by the cell
            float dt = 0;
            for (int i = 0; i < cell.Neighbors.Count; i++)
            {
                ThermalCell ncell = cell.Neighbors[i];
                if (ncell.CurrentFrame != cell.CurrentFrame)
                {
                    dt += ncell.Temperature - cell.Temperature;
                }
                else 
                {
                    dt += ncell.LastTemperature - cell.Temperature;
                }

            }

            // k * A * (dT / dX)
            
            cell.LastDeltaTemp = cell.kA * dt * cell.dxInverted * cell.SpacificHeatInverted;
            cell.Temperature = Math.Max(0, cell.Temperature + cell.LastDeltaTemp);

            //float strength = (cell.Temperature / Settings.Instance.VaccumeFullStrengthTemperature);
            //float cool = Settings.Instance.VaccumDrainRate * cell.ExposedSurfaceArea * strength * cell.SpacificHeatRatio;


            if (Settings.Debug)
            {
                //Vector3 c = GetTemperatureColor(cell.ExposedSurfaceArea / cell.Block.CubeGrid.GridSize / cell.Block.CubeGrid.GridSize).ColorToHSV();
                Vector3 c = GetTemperatureColor(cell.Temperature).ColorToHSV();
                if (cell.Block.ColorMaskHSV != c)
                {
                    cell.Block.CubeGrid.ColorBlocks(cell.Block.Min, cell.Block.Max, c);
                }
            }
        }

        /// <summary>
        /// gets a the thermal cell at a specific location
        /// </summary>
        /// <param name="position"></param>
        /// <returns></returns>
        public ThermalCell Get(Vector3I position)
        {
            long flat = position.Flatten();
            if (PositionToIndex.ContainsKey(flat))
            {
                return Thermals.list[PositionToIndex[flat]];
            }

            return null;
        }

        public float GetTemperature()
        {
            return 10000f;
        }

        public Color GetTemperatureColor(float temp)
        {
            //float max = 6f;
            float max = 100f;
            // Clamp the temperature to the range 0-100
            float t = Math.Max(0, Math.Min(max, temp));

            // Calculate the red and blue values using a linear scale
            float red = (t / max);
            float blue = (1f - (t / max));

            return new Color(red, (!LoopDirection && Settings.Instance.Frequency >= 60 ? 1 : 0), blue);
        }
    }
}
