using System;
using System.Collections.Generic;
using VRage.Game.ModAPI;
using VRage.Utils;
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
		public float ExposedSurfaceArea;


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

		public void AddNeighbor(ThermalCell n) 
		{
			Neighbors.Add(n);
			CalculateSurface();
			NeighborCountRatio = (Neighbors.Count == 0) ? 0 : 1f / Neighbors.Count;
		}

		public void RemoveNeighbor(ThermalCell n) 
		{
			Neighbors.Remove(n);
			CalculateSurface();
			//NeighborCountRatio = (Neighbors.Count == 0) ? 0 : 1f / Neighbors.Count;
		}

		private void CalculateSurface() 
		{
			MyLog.Default.Info($"[{Settings.Name}] {Block.Position} Begin Calculate Surface");
			Vector3I blockArea = (Block.Max+1) - Block.Min;

			int volume = 0;
			Vector3I minx = new Vector3I(Block.Min.X - 1, Block.Min.Y, Block.Min.Z);
			Vector3I maxx = new Vector3I(Block.Max.X + 2, Block.Max.Y+1, Block.Max.Z+1);
			BoundingBoxI SearchAreaX = new BoundingBoxI(minx, maxx);

			volume += SearchAreaX.Size.Volume() - blockArea.Volume();

			Vector3I miny = new Vector3I(Block.Min.X, Block.Min.Y - 1, Block.Min.Z);
			Vector3I maxy = new Vector3I(Block.Max.X+1, Block.Max.Y + 2, Block.Max.Z+1);
			BoundingBoxI SearchAreaY = new BoundingBoxI(miny, maxy);

			volume += SearchAreaY.Size.Volume() - blockArea.Volume();

			Vector3I minz = new Vector3I(Block.Min.X, Block.Min.Y, Block.Min.Z - 1);
			Vector3I maxz = new Vector3I(Block.Max.X+1, Block.Max.Y+1, Block.Max.Z + 2);
			BoundingBoxI SearchAreaZ = new BoundingBoxI(minz, maxz);

			volume += SearchAreaZ.Size.Volume() - blockArea.Volume();

			MyLog.Default.Info($"[{Settings.Name}] {Block.Position} {volume}");

			for (int i = 0; i < Neighbors.Count; i++)
			{
				ThermalCell ncell = Neighbors[i];
				BoundingBoxI ncellBox = new BoundingBoxI(ncell.Block.Min, ncell.Block.Max+1);
				
				volume -= ncellBox.Intersect(SearchAreaX).Size.Volume();
				volume -= ncellBox.Intersect(SearchAreaY).Size.Volume();
				volume -= ncellBox.Intersect(SearchAreaZ).Size.Volume();

				MyLog.Default.Info($"[{Settings.Name}] {Block.Position} {volume}");
			}

			ExposedSurfaceArea = volume * Block.CubeGrid.GridSize * Block.CubeGrid.GridSize;

		}
	}
}
