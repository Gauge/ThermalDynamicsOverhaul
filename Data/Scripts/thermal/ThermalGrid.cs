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

		private int IterationFrames = 0;
		private int IterationIndex = 0;
		private int CountPerFrame = 0;
		private bool LoopDirection = true;

		private int ExternalCountPerFrame = 0;
		public bool ExternalRoomUpdateComplete = false;
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
						cell.UpdateInsideBlocks(ref ExternalBlocks);
					}

					UpdateTemperatures(ref cell);
				}

				IterationIndex++;
			}

			int loopCount = 0;
			while (blockQueue.Count > 0 && loopCount < ExternalCountPerFrame)
			{
				ExternalBlockCheck(blockQueue.Dequeue());
				loopCount++;
			}

			if (IterationIndex >= count && IterationFrames >= Settings.Instance.Frequency)
			{
				IterationIndex = 0;
				IterationFrames = 0;
				LoopDirection = !LoopDirection;

				if (!ThermalCellUpdateComplete)
					ThermalCellUpdateComplete = true;

				if (!ExternalRoomUpdateComplete && blockQueue.Count == 0)
				{
					ExternalRoomUpdateComplete = true;
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

					MyLog.Default.Info($"[{Settings.Name}] min {min} max {max}");

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
								MyLog.Default.Info($"[{Settings.Name}] testing {temp} {ncell != null}");
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
				(fat as IMyDoor).DoorStateChanged += (state) => ExternalBlockReset();
			}

			int index = Thermals.Allocate();
			PositionToIndex.Add(b.Position, index);
			Thermals.list[index] = cell;

			MyLog.Default.Info($"[{Settings.Name}] Added {b.Position} Index: {index} {type}/{subtype}");

			CountPerFrame = GetCountPerFrame();
			ExternalBlockReset();
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
				(fat as IMyDoor).DoorStateChanged -= (state) => ExternalBlockReset();
			}

			int index = PositionToIndex[b.Position];
			ThermalCell cell = Thermals.list[index];
			cell.ClearNeighbors();

			PositionToIndex.Remove(b.Position);
			Thermals.Free(index);

			CountPerFrame = GetCountPerFrame();
			ExternalBlockReset();
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


		private readonly Vector3I[] neighbors = new Vector3I[6]
		{
			new Vector3I(1, 0, 0),
			new Vector3I(-1, 0, 0),
			new Vector3I(0, 1, 0),
			new Vector3I(0, -1, 0),
			new Vector3I(0, 0, 1),
			new Vector3I(0, 0, -1)
		};

		public HashSet<Vector3I> ExternalBlocks = new HashSet<Vector3I>();
		private Queue<Vector3I> blockQueue = new Queue<Vector3I>();

		private Vector3I min;
		private Vector3I max;

		private void ExternalBlockReset()
		{
			min = Grid.Min - 1;
			max = Grid.Max + 1;

			blockQueue.Clear();
			blockQueue.Enqueue(min);

			ExternalBlocks.Clear();
			ExternalBlocks.Add(min);

			ExternalCountPerFrame = Math.Max((int)((max - min).Size / 60f), 1);

			ExternalRoomUpdateComplete = false;
		}

		private void ExternalBlockCheck(Vector3I block)
		{
			//Vector3I block = blockQueue.Dequeue();
			for (int i = 0; i < neighbors.Length; i++)
			{
				Vector3I n = block + neighbors[i];

				//MyLog.Default.Info($"[{Settings.Name}] Neighbor {n} OoB: {Vector3I.Min(n, min) != min || Vector3I.Max(n, max) != max} visited: {visitedBlocks.Contains(n)} airtight: {IsAirtightBetweenPositions(block, n)}");
				if (Vector3I.Min(n, min) != min || Vector3I.Max(n, max) != max || ExternalBlocks.Contains(n) || IsAirtightBetweenPositions(block, n))
					continue;

				ExternalBlocks.Add(n);
				blockQueue.Enqueue(n);

				//MyLog.Default.Info($"[{Settings.Name}] Queued: {n}");
			}
		}

		private bool IsAirtightBetweenPositions(Vector3I start, Vector3I end)
		{
			// the the point being moved to is empty it is not air tight
			IMySlimBlock target = Grid.GetCubeBlock(end);
			if (target == null)
				return false;

			IMySlimBlock current = Grid.GetCubeBlock(start);
			// verify that the block is fully built and airtight
			if (current == target)
			{
				MyCubeBlockDefinition def = current.BlockDefinition as MyCubeBlockDefinition;

				if (def == null)
					return false;

				if (def.BuildProgressModels != null && def.BuildProgressModels.Length != 0)
				{
					MyCubeBlockDefinition.BuildProgressModel buildProgressModel = def.BuildProgressModels[def.BuildProgressModels.Length - 1];
					if (current.BuildLevelRatio < buildProgressModel.BuildRatioUpperBound)
					{
						return false;
					}
				}

				return def.IsAirTight == true;

			}

			if (current != null && IsAirtightBlock(current, start, end - start))
			{
				return true;
			}

			return IsAirtightBlock(target, end, start - end);
		}

		private bool? IsAirtightFromDefinition(IMySlimBlock slim)
		{
			if (slim == null)
				return false;

			MyCubeBlockDefinition def = slim.BlockDefinition as MyCubeBlockDefinition;
			if (def.BuildProgressModels != null && def.BuildProgressModels.Length != 0)
			{
				MyCubeBlockDefinition.BuildProgressModel buildProgressModel = def.BuildProgressModels[def.BuildProgressModels.Length - 1];
				if (slim.BuildLevelRatio < buildProgressModel.BuildRatioUpperBound)
				{
					return false;
				}
			}

			return def.IsAirTight;
		}

		private bool IsAirtightBlock(IMySlimBlock block, Vector3I pos, Vector3 normal)
		{
			MyCubeBlockDefinition myCubeBlockDefinition = block.BlockDefinition as MyCubeBlockDefinition;
			if (myCubeBlockDefinition == null)
			{
				return false;
			}

			bool? flag = IsAirtightFromDefinition(block);
			if (flag.HasValue)
			{
				return flag.Value;
			}

			Matrix result;
			block.Orientation.GetMatrix(out result);
			result.TransposeRotationInPlace();
			Vector3I transformedNormal = Vector3I.Round(Vector3.Transform(normal, result));
			Vector3 position = Vector3.Zero;

			if (block.FatBlock != null)
			{
				position = pos - block.FatBlock.Position;
			}

			Vector3 value = Vector3.Transform(position, result) + myCubeBlockDefinition.Center;
			switch (myCubeBlockDefinition.IsCubePressurized[Vector3I.Round(value)][transformedNormal])
			{
				case MyCubeBlockDefinition.MyCubePressurizationMark.PressurizedAlways:
					return true;
				case MyCubeBlockDefinition.MyCubePressurizationMark.PressurizedClosed:
				{
					IMyDoor myDoor;
					if ((myDoor = (block.FatBlock as IMyDoor)) != null && (myDoor.Status == Sandbox.ModAPI.Ingame.DoorStatus.Closed || myDoor.Status == Sandbox.ModAPI.Ingame.DoorStatus.Closing))
					{
						return true;
					}
					break;
				}
			}
			IMyDoor myDoor2 = block.FatBlock as IMyDoor;
			if (myDoor2 != null && (myDoor2.Status == Sandbox.ModAPI.Ingame.DoorStatus.Closed || myDoor2.Status == Sandbox.ModAPI.Ingame.DoorStatus.Closing))
			{
				return IsDoorAirtight(myDoor2, ref transformedNormal, myCubeBlockDefinition);
			}
			return false;
		}

		private bool IsDoorAirtight(IMyDoor doorBlock, ref Vector3I transformedNormal, MyCubeBlockDefinition blockDefinition)
		{
			if (doorBlock is MyAdvancedDoor)
			{
				if (doorBlock.IsFullyClosed)
				{
					MyCubeBlockDefinition.MountPoint[] mountPoints = blockDefinition.MountPoints;
					for (int i = 0; i < mountPoints.Length; i++)
					{
						MyCubeBlockDefinition.MountPoint mountPoint = mountPoints[i];
						if (transformedNormal == mountPoint.Normal)
						{
							return false;
						}
					}
					return true;
				}
			}
			else if (doorBlock is MyAirtightSlideDoor)
			{
				if (doorBlock.IsFullyClosed && transformedNormal == Vector3I.Forward)
				{
					return true;
				}
			}
			else if (doorBlock is MyAirtightDoorGeneric)
			{
				if (doorBlock.IsFullyClosed && (transformedNormal == Vector3I.Forward || transformedNormal == Vector3I.Backward))
				{
					return true;
				}
			}
			else if (doorBlock.IsFullyClosed)
			{
				MyCubeBlockDefinition.MountPoint[] mountPoints = blockDefinition.MountPoints;
				for (int i = 0; i < mountPoints.Length; i++)
				{
					MyCubeBlockDefinition.MountPoint mountPoint2 = mountPoints[i];
					if (transformedNormal == mountPoint2.Normal)
					{
						return false;
					}
				}
				return true;
			}
			return false;
		}

	}
}
