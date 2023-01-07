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

            //MyLog.Default.Info($"[{Settings.Name}] Block Type: {bp.Type} {bp.HeatGeneration} {type} {bp.Type == type}");
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


            int index = Thermals.Allocate();
            PositionToIndex.Add(b.Position, index);
            Thermals.list[index] = cell;

            CountPerFrame = GetCountPerFrame();
        }

        private void BlockRemoved(IMySlimBlock b)
        {
            int index = PositionToIndex[b.Position];
            ThermalCell cell = Thermals.list[index];

            for (int i = 0; i < cell.Neighbors.Count; i++)
            {
                cell.Neighbors[i].Neighbors.Remove(cell);
            }

            PositionToIndex.Remove(b.Position);
            Thermals.Free(index);

            CountPerFrame = GetCountPerFrame();
        }

        public int GetCountPerFrame() {
            return 1 + (Thermals.Count / Settings.Instance.Frequency);
        }

        public void AddNeighbors(ThermalCell cell)
        {
            //get a list of current neighbors from the grid
            List<IMySlimBlock> neighbors = new List<IMySlimBlock>();
            cell.Block.GetNeighbours(neighbors);

            for (int i = 0; i < neighbors.Count; i++)
            {
                IMySlimBlock n = neighbors[i];
                ThermalCell ncell = Thermals.list[PositionToIndex[n.Position]];

                cell.Neighbors.Add(ncell);
                ncell.Neighbors.Add(cell);
            }
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
                ThermalCell cell = Thermals.list[(LoopDirection) ? IterationIndex : count-IterationIndex-1];
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

        private void UpdateAttachedGrids() 
        {
            //List<IMyCubeGrid> subgrids = new List<IMyCubeGrid>();
            //MyAPIGateway.GridGroups.GetGroup(hitGrid, GridLinkTypeEnum.Mechanical, subgrids);
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
                // k * A * (dT / dX)
                heat += cell.kA * (cell.Neighbors[i].Temperature - cell.Temperature) * 0.5f;

            }

            cell.Temperature += heat * cell.HeatCapacityRatio;            

			if (Settings.Debug)
			{
                cell.LastHeat = heat * cell.HeatCapacityRatio;
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

            return new Color(red, (LoopDirection ? 0 : 1), blue);
        }




    }

}
