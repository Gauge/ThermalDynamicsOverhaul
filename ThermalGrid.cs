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
        public Dictionary<Vector3I, int> PositionToIndex;
        public MyFreeList<ThermalCell> Thermals;

        private int IterationFrames = 0;
        private int IterationIndex = 0;
        private int CountPerFrame = 0;
        private bool LoopDirection = true;


        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);

            if (Settings.Instance == null)
            {
                Settings.Instance = Settings.GetDefaults();
            }

            Grid = Entity as MyCubeGrid;

            PositionToIndex = new Dictionary<Vector3I, int>(Vector3I.Comparer);
            Thermals = new MyFreeList<ThermalCell>();

            Grid.OnBlockAdded += BlockAdded;
            Grid.OnBlockRemoved += BlockRemoved;

NeedsUpdate = MyEntityUpdateEnum.EACH_FRAME | MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
        }

		public override void UpdateOnceBeforeFrame()
		{
            if (Grid.Physics == null)
                NeedsUpdate = MyEntityUpdateEnum.NONE;   
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
            cell.Block = b;

            AddNeighbors(cell);

            if (block != null)
            {
                cell.Init(block);
            }
            else if (group != null)
            {
                cell.Init(group);
            }
            else
            {
                cell.Init(Settings.Instance.Generic);
            }

            IMyCubeBlock fat = b.FatBlock;
            if (fat is IMyPistonBase)
            {
                (fat as IMyPistonBase).AttachedEntityChanged += gridGroupChanged;
            }
            else if (fat is IMyMotorBase)
            {
                (fat as IMyMotorBase).AttachedEntityChanged += gridGroupChanged;
            }

            int index = Thermals.Allocate();
            PositionToIndex.Add(b.Position, index);
            Thermals.list[index] = cell;

            MyLog.Default.Info($"[{Settings.Name}] Added {b.Position} Index: {index} {type}/{subtype}");

            CountPerFrame = GetCountPerFrame();
        }

        private void BlockRemoved(IMySlimBlock b)
        {
            IMyCubeBlock fat = b.FatBlock;
            if (fat is IMyPistonBase)
            {
                (fat as IMyPistonBase).AttachedEntityChanged -= gridGroupChanged;
            }
            else if (fat is IMyMotorBase)
            {
                (fat as IMyMotorBase).AttachedEntityChanged -= gridGroupChanged;
            }

            int index = PositionToIndex[b.Position];
            ThermalCell cell = Thermals.list[index];

            for (int i = 0; i < cell.Neighbors.Count; i++)
            {
                cell.Neighbors[i].RemoveNeighbor(cell);

                //cell.Neighbors[i].Neighbors.Remove(cell);
            }

            PositionToIndex.Remove(b.Position);
            Thermals.Free(index);

            CountPerFrame = GetCountPerFrame();


        }

        private void gridGroupChanged(IMyMechanicalConnectionBlock block)
        {
            int index = PositionToIndex[block.Position];
            ThermalCell cell = Thermals.list[index];

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
                        cell.Neighbors.RemoveAt(i);
                        break;
                    }
                }
            }
            else
            {

                ThermalGrid g = block.Top.CubeGrid.GameLogic.GetAs<ThermalGrid>();
                ThermalCell ncell = g.GetCellThermals(block.Top.Position);

                MyLog.Default.Info($"[{Settings.Name}] Grid: {cell.Block.CubeGrid.EntityId} cell: {cell.Block.Position} adding connection to, Grid: {ncell.Block.CubeGrid.EntityId} Cell: {ncell.Block.Position}");

                cell.Neighbors.Add(ncell);
                ncell.Neighbors.Add(cell);
            }
        }

        public int GetCountPerFrame() {
            return 1 + (Thermals.Count / Settings.Instance.Frequency);
        }

        public void AddNeighbors(ThermalCell cell)
        {
            //get a list of current neighbors from the grid
            List<IMySlimBlock> neighbors = new List<IMySlimBlock>();
            cell.Block.GetNeighbours(neighbors);


            //MyLog.Default.Info($"[{Settings.Name}] cell: {cell.Block.Position}, neighbors new: {neighbors.Count}  existing: {cell.Neighbors.Count}");
            for (int i = 0; i < neighbors.Count; i++)
            {
                IMySlimBlock n = neighbors[i];
                ThermalCell ncell = Thermals.list[PositionToIndex[n.Position]];

                cell.AddNeighbor(ncell);
                ncell.AddNeighbor(cell);

                //cell.Neighbors.Add(ncell);
                //ncell.Neighbors.Add(cell);
                //ncell.NeighborCountRatio = (ncell.Neighbors.Count == 0) ? 0 : 1f / ncell.Neighbors.Count;
            }

            //cell.NeighborCountRatio = (cell.Neighbors.Count == 0) ? 0 : 1f / cell.Neighbors.Count;
        }



		public override void UpdateBeforeSimulation()
		{
            IterationFrames++;

            int count = Thermals.Count;
            int target = Thermals.Count - IterationIndex;
            if (CountPerFrame < target)
            {
                target = CountPerFrame;
            }

            target += IterationIndex;

            //MyAPIGateway.Utilities.ShowNotification($"[Loop] Nodes: {count}, Frames/Cycle {Settings.Instance.Frequency} Nodes/Cycle: {CountPerFrame} Target: {target}, Index: {IterationIndex}", 1, "White");
            while (IterationIndex < target)
            {
                ThermalCell cell = Thermals.list[(LoopDirection) ? IterationIndex : count-1-IterationIndex];
                if (cell != null)
                {
                    UpdateTemperatures(ref cell);
                }

                IterationIndex++;
            }

            if (IterationIndex >= count && IterationFrames >= Settings.Instance.Frequency)
            {
                IterationIndex = 0;
                IterationFrames = 0;
                LoopDirection = !LoopDirection;
            }
        }

 


		/// <summary>
		/// Update the temperature of each cell in the grid
		/// </summary>
		private void UpdateTemperatures(ref ThermalCell cell)
        {
            cell.Temperature += cell.Generation * cell.HeatCapacityRatio;

            // Calculate the total heat gained or lost by the cell
            float heat = 0;
            for (int i = 0; i < cell.Neighbors.Count; i++)
            {
                ThermalCell ncell = cell.Neighbors[i];
                heat += ncell.Temperature - cell.Temperature;
            }

            // k * A * (dT / dX)
            //cell.LastDeltaTemp = cell.kA * (heat * cell.NeighborCountRatio) * cell.HeatCapacityRatio;

            float cool = Settings.Instance.VaccumDrainRate * cell.ExposedSurfaceArea * cell.HeatCapacityRatio;
            cell.LastDeltaTemp = cell.kA * (heat - cool) * cell.HeatCapacityRatio;
            cell.Temperature = Math.Max(0, cell.Temperature+cell.LastDeltaTemp);

            if (Settings.Debug)
			{
                Vector3 c = GetTemperatureColor(cell.Temperature).ColorToHSV();
				if (cell.Block.ColorMaskHSV != c)
				{
					cell.Block.CubeGrid.ColorBlocks(cell.Block.Min, cell.Block.Max, c);
				}
			}
        }

        public ThermalCell GetCellThermals(Vector3I position)
        {
            if (PositionToIndex.ContainsKey(position))
            {
                return Thermals.list[PositionToIndex[position]];
            }

            return null;
        }


        public Color GetTemperatureColor(float temp)
        {
            float max = 100f;
            // Clamp the temperature to the range 0-100
            float t = Math.Max(0, Math.Min(max, temp));

            // Calculate the red and blue values using a linear scale
            float red = (t / max);
            float blue =  (1f - (t / max));

            return new Color(red, (!LoopDirection && Settings.Instance.Frequency >= 60 ? 1 : 0), blue);
        }
    }

}
