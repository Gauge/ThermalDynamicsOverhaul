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
		public float kA;
		public float LastHeat;


		public IMySlimBlock Block;
		public List<ThermalCell> Neighbors = new List<ThermalCell>();

		public void Init(BlockProperties p)
		{
			float gen = p.HeatGeneration * Settings.Instance.TimeScaleRatio;
			float con = p.Conductivity * Settings.Instance.TimeScaleRatio;

			Generation = ((p.HeatCapacity != 0) ? gen / p.HeatCapacity : 0);
			HeatCapacityRatio = 1f / (Block.Mass * p.HeatCapacity);
			kA = con * (Block.CubeGrid.GridSize * 2) * (Block.CubeGrid.GridSize * 2);

			// calculate melting point
		}
	}
}
