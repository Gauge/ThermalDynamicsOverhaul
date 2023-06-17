using Sandbox.Game.Entities;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using System.Text;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRageMath;

namespace ThermalOverhaul
{
	[MySessionComponentDescriptor(MyUpdateOrder.Simulation, 100)]
	public class Player : MySessionComponentBase
	{
		public override void Simulate()
		{

			if (Settings.Debug && !MyAPIGateway.Utilities.IsDedicated)
			{
				//MyAPIGateway.Utilities.ShowNotification($"[Grid] Frequency: {Settings.Instance.Frequency}", 1, "White");
				MatrixD matrix = MyAPIGateway.Session.Camera.WorldMatrix;
				Vector3D start = matrix.Translation;
				Vector3D end = start + (matrix.Forward * 100);

				IHitInfo hit;
				MyAPIGateway.Physics.CastRay(start, end, out hit);
				MyCubeGrid grid = hit?.HitEntity as MyCubeGrid;

				if (grid == null) return;

				ThermalGrid tGrid = grid.GameLogic.GetAs<ThermalGrid>();
				Vector3I position = grid.WorldToGridInteger(hit.Position + (matrix.Forward * 0.05f));

				ThermalCell cell = tGrid.Get(position);

				if (cell == null)
					return;

				MyAPIGateway.Utilities.ShowNotification($"[Grid] Cell Count {tGrid.Thermals.Count}", 1, "White");
				MyAPIGateway.Utilities.ShowNotification($"[Cell] {cell.Block.Position} T: {cell.Temperature.ToString("n4")} dT: {cell.LastDeltaTemp.ToString("n6")} Gen: {cell.HeatGeneration} Neighbors: {cell.Neighbors.Count} ratio: {cell.SpacificHeatInverted}", 1, "White");


				MyAPIGateway.Utilities.ShowNotification($"[Cell] Input: {cell.PowerInput} heat: {cell.PowerInput * cell.ConsumerGeneration} heatPerWatt: {cell.ConsumerGeneration}", 1, "White");
				MyAPIGateway.Utilities.ShowNotification($"[Cell] Output: {cell.PowerOutput} heat: {cell.PowerOutput * cell.ProducerGeneration} heatPerWatt: {cell.ProducerGeneration}", 1, "White");

				//Output: {cell.PowerOutput}


				MyAPIGateway.Utilities.ShowNotification($"[Cell] Exposed: {cell.Exposed.Count}  Inside: {cell.Inside.Count} SurfaceArea: {cell.ExposedSurfaceArea}", 1, "White");
				MyAPIGateway.Utilities.ShowNotification($"[External] {tGrid.Mapper.Blocks.Count} EComplete: {tGrid.Mapper.ExternalRoomUpdateComplete} BComplete: {tGrid.ThermalCellUpdateComplete}", 1, "White");
			}
		}
	}
}
