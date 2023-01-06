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

        private Queue<ThermalCell> activeQueue;
        private Queue<ThermalCell> idleQueue;

        private ThermalCell Vacuum;

        private float ActiveTime = 0;
        private float IdleTime = 0;

        public static float SecondsPerFrame = 1f / 60f;

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);

            Grid = Entity as MyCubeGrid;
            cfg = Settings.GetDefaults();

            // set the system to do one full update
            ActiveTime = cfg.ActiveTimeStep;
            IdleTime = cfg.IdleTimeStep;

            All = new Dictionary<int, ThermalCell>();
            Active = new Dictionary<int, ThermalCell>();
            Idle = new Dictionary<int, ThermalCell>();

            activeQueue = new Queue<ThermalCell>();
            idleQueue = new Queue<ThermalCell>();

            Grid.OnBlockAdded += BlockAdded;
            Grid.OnBlockRemoved += BlockRemoved;

            NeedsUpdate = MyEntityUpdateEnum.EACH_FRAME | MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
        }

		public override void UpdateOnceBeforeFrame()
		{
            if (Grid.Physics == null)
                NeedsUpdate = MyEntityUpdateEnum.NONE;   
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
                        AddNeighbors(cell.Id, position);

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
                        }
                        else
                        {
                            Idle.Add(cell.Id, cell);
                        }

                        MyLog.Default.Info($"[{Settings.Name}] Cell Added: {cell.Id} [{x}, {y}, {z}] {cell.Temperature}, {cell.LastTransferRate}");
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
                        RemoveNeighbors(id);
                        
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
            //MyLog.Default.Info($"[{Settings.Name}] State Transfer: {cell.Id} last: {cell.LastHeatTransfer.ToString("n5")} {All.ContainsKey(cell.Id)} {Active.ContainsKey(cell.Id)} {Idle.ContainsKey(cell.Id)}");

            while (activeQueue.Count > 0)
            {
                ThermalCell cell = activeQueue.Dequeue();
                Active.Add(cell.Id, cell);
                Idle.Remove(cell.Id);
            }

            while (idleQueue.Count > 0)
            {
                ThermalCell cell = idleQueue.Dequeue();
                Idle.Add(cell.Id, cell);
                Active.Remove(cell.Id);
            }
        }

		public override void UpdateBeforeSimulation()
		{
            ActiveTime += SecondsPerFrame;
            IdleTime += SecondsPerFrame;

            //MyAPIGateway.Utilities.ShowNotification($"[Sim] {ActiveTime >= cfg.ActiveTimeStep} {(int)Math.Floor(ActiveTime * cfg.IterationTimeStep)}", 1, "White");

            UpdateActivity();


            if (ActiveTime >= cfg.ActiveTimeStep)
            {
                float dt = Math.Min(1f, ActiveTime);

                UpdateTemperatures(ref Active, dt, true);
                ActiveTime -= cfg.ActiveTimeStep;
			}

			if (IdleTime >= cfg.IdleTimeStep)
			{
                float dt = Math.Min(1f, IdleTime);

                UpdateTemperatures(ref Idle, dt, false);
                IdleTime -= cfg.IdleTimeStep;
			}
        }

		/// <summary>
		/// Update the temperature of each cell in the grid
		/// </summary>
		private void UpdateTemperatures(ref Dictionary<int, ThermalCell> thermals, float deltaTime, bool isActive)
        {
            foreach (ThermalCell cell in thermals.Values)
            {
                cell.Temperature += cell.TemperatureGeneration * deltaTime;

                float kA = cell.Properties.Conductivity * cell.CrossSectionalArea;
                float t = cell.Temperature;

                // Calculate the total heat gained or lost by the cell
                float heat = 0;
                float rates = cell.LastTransferRate;
                foreach (ThermalCell neighbor in cell.neighbors)
                {
                    if (neighbor == null)
                    {
                        // calculate ambiant drain
                        continue;
                    }

                    // the full formula is: k * A * (dT/dX)
                    // since this calculation only spreads in cardinal direction the value of dX will always be 1
                    heat += kA * (neighbor.Temperature - t) * 0.5f;
                    rates += neighbor.LastTransferRate;
                }
                cell.ActivationRate = rates;

                float rate = heat * cell.HeatCapacityRatio;

                // Update the temperature of the cell based on the total heat gained or lost
                cell.Temperature += rate * deltaTime;
                cell.LastTransferRate = Math.Abs(rate);

                
                if (isActive && cell.Properties.HeatGeneration == 0 && cell.ActivationRate <= cfg.IdleThreshold)
                {
                    idleQueue.Enqueue(cell);
                }
                else if (!isActive && cell.ActivationRate >= cfg.ActiveThreshold)
                {
                    activeQueue.Enqueue(cell);
                }

                if (Settings.Debug)
                {
                    Vector3 c = GetTemperatureColor(cell.Temperature, !isActive).ColorToHSV();
                    if (cell.Block.ColorMaskHSV != c)
                    {
                        cell.Block.CubeGrid.ColorBlocks(cell.Block.Min, cell.Block.Max, c);
                    }
                }
            }
        }

        /// <summary>
        /// Removes this cell from neighboring cells
        /// </summary>
        /// <param name="id"></param>
        private void RemoveNeighbors(int id) {
            ThermalCell cell = All[id];

            for (int i = 0; i < 6; i++)
            {
                ThermalCell n = cell.neighbors[i];
                if (n == null)
                    continue;

                if (i < 5 && n.neighbors[i + 1] == cell)
                {
                    n.neighbors[i + 1] = null;
                }
                else if (i > 0 && n.neighbors[i - 1] == cell)
                {
                    n.neighbors[i - 1] = null;
                }
            }
        }

        /// <summary>
        /// Adds neighbors to this cell and to the neighbor cells
        /// </summary>
        /// <param name="cell"></param>
        /// <param name="p"></param>
        private void AddNeighbors(int id, Vector3I p)
        {
            if (!All.ContainsKey(id))
                return;

            ThermalCell cell = All[id];

			cell.neighbors = new ThermalCell[] { null, null, null, null, null, null };

			ThermalCell c;
            if (All.TryGetValue(ThermalCell.GetId(p.X - 1, p.Y, p.Z), out c))
            {
                cell.neighbors[0] = c;
                c.neighbors[1] = cell;
            }

            if (All.TryGetValue(ThermalCell.GetId(p.X + 1, p.Y, p.Z), out c))
            {
                cell.neighbors[1] = c;
                c.neighbors[0] = cell;
            }

            if (All.TryGetValue(ThermalCell.GetId(p.X, p.Y - 1, p.Z), out c))
            {
                cell.neighbors[2] = c;
                c.neighbors[3] = cell;
            }

            if (All.TryGetValue(ThermalCell.GetId(p.X, p.Y + 1, p.Z), out c))
            {
                cell.neighbors[3] = c;
                c.neighbors[2] = cell;
            }

            if (All.TryGetValue(ThermalCell.GetId(p.X, p.Y, p.Z - 1), out c))
            {
                cell.neighbors[4] = c;
                c.neighbors[5] = cell;
            }

            if (All.TryGetValue(ThermalCell.GetId(p.X, p.Y, p.Z + 1), out c))
            {
                cell.neighbors[5] = c;
                c.neighbors[4] = cell;
            }
        }


        public Color GetTemperatureColor(float temp, bool isIdle)
        {
            float max = 100f;
            // Clamp the temperature to the range 0-100
            float t = Math.Max(0, Math.Min(max, temp));

            // Calculate the red and blue values using a linear scale
            float red = (t / max);
            float blue =  (1f - (t / max));

            return new Color(red, (isIdle? 1 : 0), blue);
        }
    }

}
