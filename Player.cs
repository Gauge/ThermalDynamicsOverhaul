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

				MyAPIGateway.Utilities.ShowNotification($"[Grid] All {tGrid.All.Count} Active {tGrid.Active.Count} Idle {tGrid.Idle.Count}", 1, "White");
				MyAPIGateway.Utilities.ShowNotification($"[Cell] {cell.Properties.Type} Mass: {cell.Mass} Temp: {cell.Temperature.ToString("n3")} Rate: {cell.LastTransferRate.ToString("n6")}", 1, "White");
				//MyAPIGateway.Utilities.ShowNotification($"[Cell] {(cell.neighbors[0] != null ? $"Left ({cell.neighbors[0].Id})" : "")} {(cell.neighbors[1] != null ? $"Right ({cell.neighbors[1].Id})" : "")} {(cell.neighbors[2] != null ? $"Up ({cell.neighbors[2].Id})" : "")} {(cell.neighbors[3] != null ? $"Down ({cell.neighbors[3].Id})" : "")} {(cell.neighbors[4] != null ? $"Front ({cell.neighbors[4].Id})" : "")} {(cell.neighbors[5] != null ? $"Back ({cell.neighbors[5].Id})" : "")}", 1, "White");
			}
		}
	}
}
