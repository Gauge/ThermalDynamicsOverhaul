using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using SpaceEngineers.Game.ModAPI;
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
		//public Dictionary<Vector3I, ThermalCell> Rooms;

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

			PositionToIndex = new Dictionary<Vector3I, int>(Vector3I.Comparer);
			Thermals = new MyFreeList<ThermalCell>();
			//Rooms = new Dictionary<Vector3I, ThermalCell>();
			Mapper = new GridMapper(Grid);


			Grid.OnBlockAdded += BlockAdded;
			Grid.OnBlockRemoved += BlockRemoved;

			NeedsUpdate = MyEntityUpdateEnum.EACH_FRAME | MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
		}

		public override void UpdateOnceBeforeFrame()
		{
			if (Grid.Physics == null)
				NeedsUpdate = MyEntityUpdateEnum.NONE;
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
					//HandleRooms();
					Mapper.ExternalRoomUpdateComplete = true;
					ThermalCellUpdateComplete = false;
				}

			}
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

			cell.AssignNeighbors();

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
				gear.StateChanged += (state) => {
					IMyEntity entity = gear.GetAttachedEntity();
					ThermalCell c = GetCellThermals(gear.Position);

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

								ThermalCell ncell = gtherms.GetCellThermals(temp);
								//MyLog.Default.Info($"[{Settings.Name}] testing {temp} {ncell != null}");
								if (ncell != null)
								{
									// connect the cells found
									ncell.AddNeighbor(c);
									c.AddNeighbor(ncell);
								}
							}
						}
					}
				};
			}
			else if (fat is IMyDoor)
			{
				(fat as IMyDoor).DoorStateChanged += (state) => Mapper.ExternalBlockReset();
			}

			int index = Thermals.Allocate();
			PositionToIndex.Add(b.Position, index);
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

			int index = PositionToIndex[b.Position];
			ThermalCell cell = Thermals.list[index];
			cell.ClearNeighbors();

			PositionToIndex.Remove(b.Position);
			Thermals.Free(index);

			CountPerFrame = GetCountPerFrame();
			Mapper.ExternalBlockReset();
		}

		private void GridGroupChanged(IMyMechanicalConnectionBlock block)
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

		public int GetCountPerFrame()
		{
			return 1 + (Thermals.Count / Settings.Instance.Frequency);
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
			cell.Temperature = Math.Max(0, cell.Temperature + cell.LastDeltaTemp);

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
			//float max = 6f;
			float max = 100f;
			// Clamp the temperature to the range 0-100
			float t = Math.Max(0, Math.Min(max, temp));

			// Calculate the red and blue values using a linear scale
			float red = (t / max);
			float blue = (1f - (t / max));

			return new Color(red, (!LoopDirection && Settings.Instance.Frequency >= 60 ? 1 : 0), blue);
		}


		//private void HandleRooms() {
		//	List<IMyOxygenRoom> rooms = new List<IMyOxygenRoom>();
		//	(Grid as IMyCubeGrid).GasSystem.GetRooms(rooms);


		//	Dictionary<Vector3I, ThermalCell> newRooms = new Dictionary<Vector3I, ThermalCell>();
		//	for (int i = 0; i < rooms.Count; i++)
		//	{
		//		IMyOxygenRoom r = rooms[i];

		//		if (Rooms.ContainsKey(r.StartingPosition))
		//		{
		//			ThermalCell c = Rooms[r.StartingPosition];

		//		}

		//		MyLog.Default.Info($"[{Settings.Name}] Room: {r.StartingPosition} {r.BlockCount}");
		//		MyLog.Default.Flush();
		//	}
		//}
	}
}
