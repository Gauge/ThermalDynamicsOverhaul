using ProtoBuf.Meta;
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
        public Dictionary<int, int> PositionToIndex;
        public MyFreeList<ThermalCell> Thermals;
        public Dictionary<int, float> RecentlyRemoved = new Dictionary<int, float>();

        public GridMapper Mapper;

        private int IterationFrames = 0;
        private int IterationIndex = 0;
        private int CountPerFrame = 0;

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

            PositionToIndex = new Dictionary<int, int>();
            Thermals = new MyFreeList<ThermalCell>();
            Mapper = new GridMapper(Grid);

            Grid.OnGridSplit += GridSplit;
            Grid.OnGridMerge += GridMerge;

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

        private string Pack()
        {
            byte[] bytes = new byte[Thermals.Count * 8];

            int bi = 0;
            for (int i = 0; i < Thermals.UsedLength; i++)
            {
                ThermalCell c = Thermals.list[i];
                if (c == null) continue;

                int id = c.Id;
                bytes[bi] = (byte)id;
                bytes[bi + 1] = (byte)(id >> 8);
                bytes[bi + 2] = (byte)(id >> 16);
                bytes[bi + 3] = (byte)(id >> 24);

                int t = (int)(c.Temperature * 10000);
                bytes[bi + 4] = (byte)t;
                bytes[bi + 5] = (byte)(t >> 8);
                bytes[bi + 6] = (byte)(t >> 16);
                bytes[bi + 7] = (byte)(t >> 24);

                bi += 8;
            }

            return Convert.ToBase64String(bytes);
        }

        private void Unpack(string data) 
        {
            byte[] bytes = Convert.FromBase64String(data);

            for (int i = 0; i < bytes.Length; i += 8)
            {
                int id = bytes[i];
                id |= bytes[i + 1] << 8;
                id |= bytes[i + 2] << 16;
                id |= bytes[i + 3] << 24;

                int f = bytes[i + 4];
                f |= bytes[i + 5] << 8;
                f |= bytes[i + 6] << 16;
                f |= bytes[i + 7] << 24;

                Thermals.list[PositionToIndex[id]].Temperature = f * 0.0001f;    
            }
        } 

        private void Save()
        {
            Stopwatch sw = Stopwatch.StartNew();

            string data = Pack();

            //string data = Convert.ToBase64String(MyAPIGateway.Utilities.SerializeToBinary(PackGridInfo()));

            MyModStorageComponentBase storage = Entity.Storage;
            if (storage.ContainsKey(StorageGuid))
            {
                storage[StorageGuid] = data;
            }
            else
            {
                storage.Add(StorageGuid, data);
            }
            sw.Stop();
            MyLog.Default.Info($"[{Settings.Name}] [SAVE] {Grid.DisplayName} ({Grid.EntityId}) t-{((float)sw.ElapsedTicks / TimeSpan.TicksPerMillisecond).ToString("n8")}ms, size: {data.Length}");
        }

        private void Load()
        {
            Stopwatch sw = Stopwatch.StartNew();
            try
            {
                if (Entity.Storage.ContainsKey(StorageGuid))
                {
                    Unpack(Entity.Storage[StorageGuid]);
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
            if (Grid.EntityId != b.CubeGrid.EntityId)
            {
                MyLog.Default.Info($"[{Settings.Name}] Adding Skipped - Grid: {Grid.EntityId} BlockGrid: {b.CubeGrid.EntityId} {b.Position}");
                return;
            }

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

            MyLog.Default.Info($"[{Settings.Name}] Added {Grid.EntityId} ({b.Position.Flatten()}) {b.Position} --- {type}/{subtype}");

            int index = Thermals.Allocate();
            PositionToIndex.Add(cell.Id, index);
            Thermals.list[index] = cell;

            CountPerFrame = GetCountPerFrame();
            Mapper.ExternalBlockReset();
        }

        private void BlockRemoved(IMySlimBlock b)
        {
            if (Grid.EntityId != b.CubeGrid.EntityId)
            {
                MyLog.Default.Info($"[{Settings.Name}] Removing Skipped - Grid: {Grid.EntityId} BlockGrid: {b.CubeGrid.EntityId} {b.Position}");
                return;
            }

            MyLog.Default.Info($"[{Settings.Name}] Removed {Grid.EntityId} ({b.Position.Flatten()}) {b.Position}");

            int flat = b.Position.Flatten();
            int index = PositionToIndex[flat];
            ThermalCell cell = Thermals.list[index];

            if (RecentlyRemoved.ContainsKey(cell.Id))
            {
                RecentlyRemoved[cell.Id] = cell.Temperature;
            }
            else 
            {
                RecentlyRemoved.Add(cell.Id, cell.Temperature);
            }

            cell.ClearNeighbors();
            PositionToIndex.Remove(flat);
            Thermals.Free(index);

            CountPerFrame = GetCountPerFrame();
            Mapper.ExternalBlockReset();
        }

        private void GridSplit(MyCubeGrid g1, MyCubeGrid g2)
        {
            MyLog.Default.Info($"[{Settings.Name}] Grid Split - G1: {g1.EntityId} G2: {g2.EntityId}");

            ThermalGrid tg1 = g1.GameLogic.GetAs<ThermalGrid>();
            ThermalGrid tg2 = g2.GameLogic.GetAs<ThermalGrid>();

            for (int i = 0; i < tg2.Thermals.UsedLength; i++)
            {
                ThermalCell c = tg2.Thermals.list[i];
                if (c == null) continue;

                if (tg1.RecentlyRemoved.ContainsKey(c.Id)) 
                {
                    c.Temperature = tg1.RecentlyRemoved[c.Id];
                    tg1.RecentlyRemoved.Remove(c.Id);
                }
            }

        }

        private void GridMerge(MyCubeGrid g1, MyCubeGrid g2) 
        {
            ThermalGrid tg1 = g1.GameLogic.GetAs<ThermalGrid>();
            ThermalGrid tg2 = g2.GameLogic.GetAs<ThermalGrid>();

            for (int i = 0; i < tg2.Thermals.UsedLength; i++)
            {
                ThermalCell c = tg2.Thermals.list[i];
                if (c == null) continue;

                int id = c.Block.Position.Flatten();
                if (tg1.PositionToIndex.ContainsKey(id))
                {
                    tg1.Thermals.list[tg1.PositionToIndex[id]].Temperature = c.Temperature;
                }

            }
        }

        public override void UpdateOnceBeforeFrame()
        {
            if (Grid.Physics == null)
                NeedsUpdate = MyEntityUpdateEnum.NONE;

            //Load();
        }

        public override void UpdateBeforeSimulation()
        {
            IterationFrames++;

            int count = Thermals.UsedLength;
            int target = count - IterationIndex;
            if (CountPerFrame < target)
            {
                target = CountPerFrame;
            }

            target += IterationIndex;

            ////MyAPIGateway.Utilities.ShowNotification($"[Loop] Nodes: {count}, Frames/Cycle {Settings.Instance.Frequency} Nodes/Cycle: {CountPerFrame} Target: {target}, Index: {IterationIndex}", 1, "White");
            while (IterationIndex < target)
            {
                ThermalCell cell = Thermals.list[IterationIndex];
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
            cell.Frame++;
            cell.LastTemperature = cell.Temperature;

            // generate heat based on power usage
            cell.Temperature += cell.HeatGeneration;

            // Calculate the total heat gained or lost by the cell
            float dt = 0;
            for (int i = 0; i < cell.Neighbors.Count; i++)
            {
                ThermalCell ncell = cell.Neighbors[i];
                if (ncell.Frame != cell.Frame)
                {
                    dt += ncell.Temperature - cell.Temperature;
                }
                else 
                {
                    dt += ncell.LastTemperature - cell.Temperature;
                }

            }

            //float strength = (cell.Temperature / Settings.Instance.VaccumeFullStrengthTemperature);
            //float cool = Settings.Instance.VaccumDrainRate * cell.ExposedSurfaceArea * strength * cell.SpacificHeatRatio;

            // k * A * (dT / dX)
            cell.LastDeltaTemp = cell.kA * dt * cell.dxInverted * cell.SpacificHeatInverted;
            cell.Temperature = Math.Max(0, cell.Temperature + cell.LastDeltaTemp);

            
            if (Settings.Debug && MyAPIGateway.Session.IsServer)
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
            int flat = position.Flatten();
            if (PositionToIndex.ContainsKey(flat))
            {
                return Thermals.list[PositionToIndex[flat]];
            }

            return null;
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

            return new Color(red, (Settings.Instance.Frequency >= 60 ? 1 : 0), blue);
        }
    }
}