using Draygo.BlockExtensionsAPI;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.EntityComponents;
using Sandbox.Game.SessionComponents;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using System.Text;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRageMath;

namespace Thermodynamics
{
	[MySessionComponentDescriptor(MyUpdateOrder.Simulation, 100)]
	public class Session : MySessionComponentBase
	{

        public static DefinitionExtensionsAPI Definitions;
        public Session()
        {
            Definitions = new DefinitionExtensionsAPI(Done);
        }

        private void Done()
        {

        }

        protected override void UnloadData()
        {
            Definitions?.UnloadData();

            base.UnloadData();
        }

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

				ThermalGrid g = grid.GameLogic.GetAs<ThermalGrid>();
				Vector3I position = grid.WorldToGridInteger(hit.Position + (matrix.Forward * 0.05f));
				IMySlimBlock block = grid.GetCubeBlock(position);

				if (block == null) return;

				ThermalCell c = g.Get(block.Position);

				if (c == null)
					return;


                //MyAPIGateway.Utilities.ShowNotification($"[Grid] {tGrid.Entity.EntityId} Count: {tGrid.Thermals.Count}", 1, "White");


                MyAPIGateway.Utilities.ShowNotification($"[Env] " +
                    $"ambT: {g.FrameAmbientTemprature.ToString("n4")} " +
                    $"decay: {g.FrameSolarDecay.ToString("n4")} " +
                    $"atmo: {g.FrameAmbientStrength.ToString("n4")} " +
                    $"isOcc: {g.FrameSolarOccluded}", 1, "White");

                MyAPIGateway.Utilities.ShowNotification($"[Cell] {c.Block.Position} " +
                    $"T: {c.Temperature.ToString("n4")} " +
                    $"dT: {c.LastDeltaTemp.ToString("n6")} " +
                    $"Gen: {c.HeatGeneration.ToString("n4")} " +
                    $"Neigh: {c.Neighbors.Count} ", 1, "White");

                //MyAPIGateway.Utilities.ShowNotification($"[Solar] {cell.SolarIntensity.ToString("n3")} Average: {tGrid.AverageSolarHeat[0].ToString("n3")}, {tGrid.AverageSolarHeat[1].ToString("n3")}, {tGrid.AverageSolarHeat[2].ToString("n3")}, {tGrid.AverageSolarHeat[3].ToString("n3")}, {tGrid.AverageSolarHeat[4].ToString("n3")}, {tGrid.AverageSolarHeat[5].ToString("n3")}", 1, "White");

                //Grid.AverageSolarHeat[directionIndex])

                //MyAPIGateway.Utilities.ShowNotification($"[Grid] Exposed: {tGrid.ExposedNodes.Count} {tGrid.ExposedSurface.Count} inside: {tGrid.InsideNodes.Count} {tGrid.InsideSurface.Count} Rooms: {tGrid.Rooms.Count}", 1, "White");
                //MyAPIGateway.Utilities.ShowNotification($"[Cell] Exposed: {cell.Exposed.Count} {cell.ExposedSurface.Count}  Inside: {cell.Inside.Count} {cell.InsideSurface.Count} SurfaceArea: {cell.ExposedSurfaceArea}", 1, "White");


                //MyAPIGateway.Utilities.ShowNotification($"[Cell] Input: {cell.PowerInput} heat: {cell.PowerInput * cell.ConsumerGeneration} heatPerWatt: {cell.ConsumerGeneration}", 1, "White");
                //MyAPIGateway.Utilities.ShowNotification($"[Cell] Output: {cell.PowerOutput} heat: {cell.PowerOutput * cell.ProducerGeneration} heatPerWatt: {cell.ProducerGeneration}", 1, "White");


                //MyAPIGateway.Utilities.ShowNotification($"[Cell] Exposed: {cell.Exposed.Count}  Inside: {cell.Inside.Count} SurfaceArea: {cell.ExposedSurfaceArea}", 1, "White");
                //MyAPIGateway.Utilities.ShowNotification($"[External] {tGrid.Mapper.Blocks.Count} EComplete: {tGrid.Mapper.ExternalRoomUpdateComplete} BComplete: {tGrid.ThermalCellUpdateComplete}", 1, "White");
            }
        }
	}
}
