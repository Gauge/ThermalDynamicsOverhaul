using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
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

        private MyCubeGrid Grid;
        private Settings cfg;

        public Dictionary<int, ThermalCell> All;
        public Dictionary<int, ThermalCell> Active;
        public Dictionary<int, ThermalCell> Idle;

        private Queue<ThermalCell> queue;

        private ThermalCell Vacuum;

        private float DeltaTime = 0;
        private float ActiveTime = 0;
        private float IdleTime = 0;


        public int TestCycles = 0;

        private long LastRunTimestamp = 0;

        private static float ToSeconds = 1f / TimeSpan.TicksPerSecond;

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);

            Grid = Entity as MyCubeGrid;
            cfg = Settings.GetDefaults();

            // set the system to do one full update
            ActiveTime = cfg.ActiveTime;
            IdleTime = cfg.IdleTime;

            All = new Dictionary<int, ThermalCell>();
            Active = new Dictionary<int, ThermalCell>();
            Idle = new Dictionary<int, ThermalCell>();

            queue = new Queue<ThermalCell>();

            Grid.OnBlockAdded += BlockAdded;
            Grid.OnBlockRemoved += BlockRemoved;

            

            LastRunTimestamp = DateTime.UtcNow.Ticks;

            NeedsUpdate = MyEntityUpdateEnum.EACH_FRAME;
        }

        public ThermalCell GetCellThermals(Vector3I position) {
            int id = ThermalCell.GetId(position);
            if (All.ContainsKey(id))
            {
                return All[id];
            }

            ThermalCell cell = new ThermalCell(cfg.Vacuum, position);
            cell.neighbors = new ThermalCell[] { null, null, null, null, null, null };

            return cell;
        }


        private void BlockAdded(IMySlimBlock b)
        {
            MyCubeBlockDefinition def = b.BlockDefinition as MyCubeBlockDefinition;

            // get block properties
            string type = def.Id.TypeId.ToString();
            string subtype = def.Id.SubtypeId.ToString();

            BlockProperties group = null;
            BlockProperties block = null;

            foreach (BlockProperties bp in cfg.BlockConfig)
            {
                //MyLog.Default.Info($"[{Settings.Name}] Block Type: {bp.Type} {bp.HeatGeneration} {type} {bp.Type == type}");

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

            // create cells for each position this block fills in the grid
            MyLog.Default.Info($"[{Settings.Name}] Block Added: {type}/{subtype} {group!=null} {block!=null} {b.Min} {b.Max}");

            for (int x = b.Min.X; x <= b.Max.X; x++)
            {
                for (int y = b.Min.Y; y <= b.Max.Y; y++)
                {
                    for (int z = b.Min.Z; z <= b.Max.Z; z++)
                    {
                        Vector3I position = new Vector3I(x, y, z);

                        ThermalCell cell = null;
                        if (block != null)
                        {
                            cell = new ThermalCell(block, position, b);
                        }
                        else if (group != null)
                        {
                            cell = new ThermalCell(group, position, b);
                        }
                        else
                        {
                            cell = new ThermalCell(cfg.Generic, position, b);
                        }

                        All.Add(cell.Id, cell);
                        Updateneighbors(cell.Id);

                        // calculate temperature
                        float temp = 0;
                        float count = 0;
                        foreach (ThermalCell neighbor in cell.neighbors)
                        {
                            if (neighbor == null) continue;

                            temp += neighbor.Temperature;
                            count++;
                        }

                        float average = (count > 0) ? (float)(temp / count) : 0;
                        cell.Temperature = average;

                        if (cell.Properties.HeatGeneration != 0)
                        {
                            Active.Add(cell.Id, cell);
                            cell.State = CellState.Active;
                        }
                        else
                        {
                            Idle.Add(cell.Id, cell);
                            cell.State = CellState.Idle;
                        }

                        MyLog.Default.Info($"[{Settings.Name}] Cell Added: {cell.Id} [{x}, {y}, {z}] {cell.State} {cell.Temperature}, {cell.LastHeatTransfer}");
                    }
                }
            }
        }

        private void BlockRemoved(IMySlimBlock b)
        {
            // remove each position this block makes up in the grid
            for (int x = b.Min.X; x <= b.Max.X; x++)
            {
                for (int y = b.Min.Y; y <= b.Max.Y; y++)
                {
                    for (int z = b.Min.Z; z <= b.Max.Z; z++)
                    {
                        int id = ThermalCell.GetId(x,y,z);
                        Updateneighbors(id, true);
                        
                        All.Remove(id);

                        if (Idle.ContainsKey(id))
                        {
                            Idle.Remove(id);
                        } 
                        else if (Active.ContainsKey(id))
                        {
                            Active.Remove(id);
                        }
                    }
                }
            }

        }

        public void UpdateActivity() {
            ThermalCell cell;
            while (queue.TryDequeue(out cell))
            {
                //MyLog.Default.Info($"[{Settings.Name}] State Transfer: {cell.Id} last: {cell.LastHeatTransfer.ToString("n5")} {All.ContainsKey(cell.Id)} {Active.ContainsKey(cell.Id)} {Idle.ContainsKey(cell.Id)}");

                if (cell.LastHeatTransfer > cfg.TemperatureActivationThreshold)
                {
                    if (cell.State == CellState.Idle)
                    {
                        cell.State = CellState.Active;
                        Active.Add(cell.Id, cell);
                        Idle.Remove(cell.Id);
                    }
                }
                else
                {
                    if (cell.State == CellState.Active)
                    {
                        cell.State = CellState.Idle;
                        Idle.Add(cell.Id, cell);
                        Active.Remove(cell.Id);
                    }
                }
            }
        }

		public override void UpdateBeforeSimulation()
		{
            // calculate delta time
            long now = DateTime.UtcNow.Ticks;
            DeltaTime = (now - LastRunTimestamp) * ToSeconds;
            ActiveTime += DeltaTime;
            IdleTime += DeltaTime;
            LastRunTimestamp = now;

            UpdateActivity();

            if (ActiveTime >= cfg.ActiveTime)
            {
                UpdateTemperatures(ref Active, ActiveTime);
                ActiveTime -= (cfg.ActiveTime < DeltaTime) ? DeltaTime : cfg.ActiveTime;
            }

            if (IdleTime >= cfg.IdleTime)
            {
                UpdateTemperatures(ref Idle, IdleTime);
                IdleTime -= (cfg.IdleTime < DeltaTime) ? DeltaTime : cfg.IdleTime;
            }

		}

		/// <summary>
		/// Update the temperature of each cell in the grid
		/// </summary>
		private void UpdateTemperatures(ref Dictionary<int, ThermalCell> thermals, float deltaTime)
        {


            if (TestCycles < 1000)
            {
                TestCycles++;
            }

			foreach (ThermalCell cell in thermals.Values)
            {
                if (TestCycles < 1000)
                {
                    cell.Temperature += cell.TemperatureGeneration * deltaTime;
                }   


                // Calculate the total heat gained or lost by the cell
                float heat = 0;
                foreach (ThermalCell neighbor in cell.neighbors)
                {
                    if (neighbor == null)
                    {
                        // calculate ambiant drain
                        continue;
                    }

                    heat += CalculateHeatTransfer(neighbor, cell);
                }


                cell.LastHeatTransfer = heat * cell.HeatCapacityRatio;
                // Update the temperature of the cell based on the total heat gained or lost
                cell.Temperature += cell.LastHeatTransfer * deltaTime;

                if (StateChanged(cell))
                {
                    queue.Enqueue(cell);
                }

                Color c = GetTemperatureColor(cell.Temperature);
                cell.Block.CubeGrid.ColorBlocks(cell.Block.Min, cell.Block.Max, c.ColorToHSV());
            }
        }

        public bool StateChanged(ThermalCell cell) {
            return cell.Properties.HeatGeneration == 0 && // state should remain active if the block generates heat
                (cell.LastHeatTransfer >= cfg.TemperatureActivationThreshold && cell.State == CellState.Idle ||
                cell.LastHeatTransfer < cfg.TemperatureActivationThreshold && cell.State == CellState.Active);
        }

        /// <summary>
        /// Calculate the heat transfer between two cells
        /// </summary>
        /// <param name="cell1"></param>
        /// <param name="cell2"></param>
        /// <returns></returns>
        private float CalculateHeatTransfer(ThermalCell cell1, ThermalCell cell2)
        {
            float k = cell1.Properties.Conductivity;
            float A = cell1.CrossSectionalArea;
            float dT = cell1.Temperature - cell2.Temperature;

            // the full formula is: k * A * (dT/dX)
            // since this calculation only spreads in cardinal direction the value of dX will always be 1
            return k * A * dT;
        }

        /// <summary>
        /// Update the current cell and all neibouring cells
        /// </summary>
        /// <param name="cell"></param>
        /// <param name="p"></param>
        /// <param name="remove"></param>
        private void Updateneighbors(int id, bool remove = false)
        {
            if (!All.ContainsKey(id))
                return;

            ThermalCell cell = All[id];
            Vector3I p = cell.Block.Position;
			cell.neighbors = new ThermalCell[] { null, null, null, null, null, null };

			ThermalCell c = null;
            if (All.TryGetValue(ThermalCell.GetId(p.X - 1, p.Y, p.Z), out c))
            {
                if (!remove)
                {
                    cell.neighbors[0] = c;
                    c.neighbors[1] = cell;
                }
                else
                {
                    c.neighbors[1] = null;
                }
            }

            if (All.TryGetValue(ThermalCell.GetId(p.X + 1, p.Y, p.Z), out c))
            {
                if (!remove)
                {
                    cell.neighbors[1] = c;
                    c.neighbors[0] = cell;
                }
                else
                {
                    c.neighbors[0] = null;
                }
            }

            if (All.TryGetValue(ThermalCell.GetId(p.X, p.Y - 1, p.Z), out c))
            {
                if (!remove)
                {
                    cell.neighbors[2] = c;
                    c.neighbors[3] = cell;
                }
                else
                {
                    c.neighbors[3] = null;
                }

            }

            if (All.TryGetValue(ThermalCell.GetId(p.X, p.Y + 1, p.Z), out c))
            {
                if (!remove)
                {
                    cell.neighbors[3] = c;
                    c.neighbors[2] = cell;
                }
                else
                {
                    c.neighbors[2] = null;
                }

            }

            if (All.TryGetValue(ThermalCell.GetId(p.X, p.Y, p.Z - 1), out c))
            {
                if (!remove)
                {
                    cell.neighbors[4] = c;
                    c.neighbors[5] = cell;
                }
                else
                {
                    c.neighbors[5] = null;
                }

            }

            if (All.TryGetValue(ThermalCell.GetId(p.X, p.Y, p.Z + 1), out c))
            {
                if (!remove)
                {
                    cell.neighbors[5] = c;
                    c.neighbors[4] = cell;
                }
                else
                {
                    c.neighbors[4] = null;
                }
            }
        }



        public Color GetTemperatureColor(float temp)
        {
            float max = 100f;
            // Clamp the temperature to the range 0-100
            float t = Math.Max(0, Math.Min(max, temp));



            // Calculate the red and blue values using a linear scale
            float red = (t / max);

            float blue =  (1f - (t / max));

            return new Color(red, 0, blue, 0);
        }
    }

}
