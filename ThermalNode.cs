using System;
using VRage.Game.ModAPI;
using VRageMath;

namespace ThermalOverhaul
{
	public enum CellState { Idle, Active }

	public class ThermalCell
	{
		public int Id;
		public IMySlimBlock Block;
		public BlockProperties Properties;
		public float LastHeatTransfer = 0;
		public CellState State = CellState.Idle;

		public float Mass;

		public float Temperature;
		public float TemperatureGeneration; // degrees per second
		public float CrossSectionalArea;
		public float HeatCapacityRatio;

		public ThermalCell[] neighbors;


		public ThermalCell(BlockProperties properties, Vector3I position, IMySlimBlock block = null)
		{
			Id = GetId(position);
			Properties = properties;
			
			Temperature = 0;
			TemperatureGeneration = (properties.HeatCapacity != 0) ? properties.HeatGeneration / properties.HeatCapacity : 0;


			if (block != null)
			{
				Block = block;

				Vector3I size = (block.Max + 1) - block.Min;

				Mass = block.Mass / size.Size;


				float sideLength = Block.CubeGrid.GridSize*2;
				CrossSectionalArea = sideLength * sideLength;
				
				
				HeatCapacityRatio = 1f / (Mass * properties.HeatCapacity);
			}
		}

		public static int GetId(Vector3I position) {
			return GetId(position.X, position.Y, position.Z);
		}

		public static int GetId(int x, int y, int z) { 
            return (x * 1000) + y + (z * 1000000);
		}

	}
}
