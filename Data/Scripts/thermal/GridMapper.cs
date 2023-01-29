using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using System.Text;
using VRage.Game.ModAPI;
using VRageMath;

namespace ThermalOverhaul
{
	public class GridMapper
	{
		private readonly Vector3I[] neighbors = new Vector3I[6]
		{
			new Vector3I(1, 0, 0),
			new Vector3I(-1, 0, 0),
			new Vector3I(0, 1, 0),
			new Vector3I(0, -1, 0),
			new Vector3I(0, 0, 1),
			new Vector3I(0, 0, -1)
		};

		public HashSet<Vector3I> Blocks = new HashSet<Vector3I>();
		public Queue<Vector3I> BlockQueue = new Queue<Vector3I>();

		private Vector3I min;
		private Vector3I max;

		public IMyCubeGrid Grid;

		public int ExternalCountPerFrame = 1;
		public bool ExternalRoomUpdateComplete = false;

		public GridMapper(IMyCubeGrid grid)
		{
			Grid = grid;
		}

		public void ExternalBlockReset()
		{
			min = Grid.Min - 1;
			max = Grid.Max + 1;

			BlockQueue.Clear();
			BlockQueue.Enqueue(min);

			Blocks.Clear();
			Blocks.Add(min);

			ExternalCountPerFrame = Math.Max((int)((max - min).Size / 60f), 1);
			ExternalRoomUpdateComplete = false;
		}

		public void ExternalBlockCheck(Vector3I block)
		{
			//Vector3I block = blockQueue.Dequeue();
			for (int i = 0; i < neighbors.Length; i++)
			{
				Vector3I n = block + neighbors[i];

				//MyLog.Default.Info($"[{Settings.Name}] Neighbor {n} OoB: {Vector3I.Min(n, min) != min || Vector3I.Max(n, max) != max} visited: {visitedBlocks.Contains(n)} airtight: {IsAirtightBetweenPositions(block, n)}");
				if (Vector3I.Min(n, min) != min || Vector3I.Max(n, max) != max || Blocks.Contains(n) || IsAirtightBetweenPositions(block, n))
					continue;

				Blocks.Add(n);
				BlockQueue.Enqueue(n);

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
				return (current.BlockDefinition as MyCubeBlockDefinition)?.IsAirTight == true;
			}

			if (current != null && IsAirtightBlock(current, start, end - start))
			{
				return true;
			}

			return IsAirtightBlock(target, end, start - end);
		}

		private bool IsAirtightBlock(IMySlimBlock block, Vector3I pos, Vector3 normal)
		{
			MyCubeBlockDefinition def = block.BlockDefinition as MyCubeBlockDefinition;
			if (def == null)
			{
				return false;
			}

			if (def.IsAirTight.HasValue)
			{
				return def.IsAirTight.Value;
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

			IMyDoor door = block.FatBlock as IMyDoor;
			bool isDoorClosed = door != null && (door.Status == Sandbox.ModAPI.Ingame.DoorStatus.Closed || door.Status == Sandbox.ModAPI.Ingame.DoorStatus.Closing);

			Vector3 value = Vector3.Transform(position, result) + def.Center;
			switch (def.IsCubePressurized[Vector3I.Round(value)][transformedNormal])
			{
				case MyCubeBlockDefinition.MyCubePressurizationMark.PressurizedAlways:
					return true;
				case MyCubeBlockDefinition.MyCubePressurizationMark.PressurizedClosed:
				{
					if (isDoorClosed)
					{
						return true;
					}
					break;
				}
			}

			return isDoorClosed && IsDoorAirtight(door, ref transformedNormal, def);
		}

		private bool IsDoorAirtight(IMyDoor door, ref Vector3I normal, MyCubeBlockDefinition def)
		{
			if (!door.IsFullyClosed)
				return false;

			if (door is MyAirtightSlideDoor)
			{
				if (normal == Vector3I.Forward)
				{
					return true;
				}
			}
			else if (door is MyAirtightDoorGeneric)
			{
				if (normal == Vector3I.Forward || normal == Vector3I.Backward)
				{
					return true;
				}
			}

			// standard and advanced doors
			MyCubeBlockDefinition.MountPoint[] mountPoints = def.MountPoints;
			for (int i = 0; i < mountPoints.Length; i++)
			{
				MyCubeBlockDefinition.MountPoint mountPoint2 = mountPoints[i];
				if (normal == mountPoint2.Normal)
				{
					return false;
				}
			}

			return true;
		}

	}
}
