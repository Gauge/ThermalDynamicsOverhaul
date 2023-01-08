using System;
using System.Collections.Generic;
using VRage.Game.ModAPI;
using VRageMath;

namespace ThermalOverhaul
{
	public class ThermalCell
	{
		public float Temperature;
		public float Generation;
		public float HeatCapacityRatio;
		public float NeighborCountRatio;
		public float kA;
		public float LastDeltaTemp;


		public IMySlimBlock Block;
		public List<ThermalCell> Neighbors = new List<ThermalCell>();

		public void Init(BlockProperties p)
		{
			float watts = p.HeatGeneration * Settings.Instance.TimeScaleRatio;
			
			float k = p.Conductivity * Settings.Instance.TimeScaleRatio;
			float A = Block.CubeGrid.GridSize * Block.CubeGrid.GridSize;

			Generation = ((p.HeatCapacity != 0) ? watts / p.HeatCapacity : 0);
			HeatCapacityRatio = 1f / (Block.Mass * p.HeatCapacity);
			kA = k*A ;

			// calculate melting point
		}
	}
}
