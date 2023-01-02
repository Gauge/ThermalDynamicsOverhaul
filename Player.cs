using Sandbox.Game.Entities;
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
			if (!MyAPIGateway.Utilities.IsDedicated)
			{
				MatrixD matrix = MyAPIGateway.Session.Camera.WorldMatrix;
				Vector3D start = matrix.Translation;
				Vector3D end = start + (matrix.Forward * 100);

				IHitInfo hit;
				MyAPIGateway.Physics.CastRay(start, end, out hit);
				MyCubeGrid grid = hit?.HitEntity as MyCubeGrid;

				if (grid == null) return;

				ThermalGrid tGrid = grid.GameLogic.GetAs<ThermalGrid>();
				Vector3I position = grid.WorldToGridInteger(hit.Position + (matrix.Forward * 0.05f));

				ThermalCell cell = tGrid.GetCellThermals(position);

				MyAPIGateway.Utilities.ShowNotification($"[Grid] Cycles: {tGrid.TestCycles} All {tGrid.All.Count} Active {tGrid.Active.Count} Idle {tGrid.Idle.Count}", 1, "White");
				MyAPIGateway.Utilities.ShowNotification($"[Cell] {cell.Properties.Type} [{cell.State}] Temp: {cell.Temperature.ToString("n5")} Rate: {cell.LastHeatTransfer.ToString("n5")}", 1, "White");

			}
		}
	}
}
