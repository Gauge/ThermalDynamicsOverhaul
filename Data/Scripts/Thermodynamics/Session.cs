using Draygo.BlockExtensionsAPI;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.EntityComponents;
using Sandbox.Game.SessionComponents;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using System.Text;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRageMath;
using static VRageRender.MyBillboard;

namespace Thermodynamics
{
	[MySessionComponentDescriptor(MyUpdateOrder.Simulation)]
	public class Session : MySessionComponentBase
	{

        public static DefinitionExtensionsAPI Definitions;
        public Session()
        {
            MyLog.Default.Info($"[{Settings.Name}] Setup Definition Extention API");
            Definitions = new DefinitionExtensionsAPI(Done);
        }

        private void Done()
        {
            MyLog.Default.Info($"[{Settings.Name}] Definition Extention API - Done");
        }

        protected override void UnloadData()
        {
            Definitions?.UnloadData();

            base.UnloadData();
        }

        public Color GetTemperatureColor(float temp)
        {
            float max = 500f;
            // Clamp the temperature to the range 0-100
            float t = Math.Max(0, Math.Min(max, temp));



            // Calculate the red and blue values using a linear scale
            float red = (t / max);

            float blue = (1f - (t / max));

            return new Color(red, 0, blue, 255);
        }

        public override void Simulate()
		{
            if (Settings.Debug && !MyAPIGateway.Utilities.IsDedicated)
            {
                //MyAPIGateway.Utilities.ShowNotification($"[Grid] Frequency: {Settings.Instance.Frequency}", 1, "White");
                MatrixD matrix = MyAPIGateway.Session.Camera.WorldMatrix;
                Vector3D start = matrix.Translation;
                Vector3D end = start + (matrix.Forward * 15);

                IHitInfo hit;
                MyAPIGateway.Physics.CastRay(start, end, out hit);
                MyCubeGrid grid = hit?.HitEntity as MyCubeGrid;

                if (grid == null) return;

                ThermalGrid g = grid.GameLogic.GetAs<ThermalGrid>();
                Vector3I position = grid.WorldToGridInteger(hit.Position + (matrix.Forward * 0.005f));
                IMySlimBlock block = grid.GetCubeBlock(position);

                if (block == null) return;

                ThermalCell c = g.Get(block.Position);

                if (c == null)
                    return;

                Vector3D blockPosition;
                Matrix blockRotation;

                block.ComputeWorldCenter(out blockPosition);
                block.Orientation.GetMatrix(out blockRotation);

                MatrixD gridRotationMatrix = block.CubeGrid.WorldMatrix;
                gridRotationMatrix.Translation = Vector3D.Zero;
                blockRotation *= gridRotationMatrix;
                MatrixD blockWorldMatrix = MatrixD.CreateWorld(blockPosition, blockRotation.Forward, blockRotation.Up);

                float unit = block.CubeGrid.GridSize * 0.5f;
                Vector3 halfExtents = new Vector3((float)unit, (float)unit, (float)unit);
                BoundingBoxD box = new BoundingBoxD(-halfExtents, halfExtents);

                //GetTemperatureColor(c.Temperature);

                Color color = ColorExtensions.HSVtoColor(Tools.GetTemperatureColor(c.Temperature));


                MySimpleObjectDraw.DrawTransparentBox(ref blockWorldMatrix, ref box, ref color, MySimpleObjectRasterizer.Solid, 1, 0.01f, null, null, true, -1, BlendTypeEnum.AdditiveTop, 1000f);


                //MyAPIGateway.Utilities.ShowNotification($"[Grid] {tGrid.Entity.EntityId} Count: {tGrid.Thermals.Count}", 1, "White");


                MyAPIGateway.Utilities.ShowNotification($"[Env] " +
                    $"sim: {Settings.Instance.SimulationSpeed.ToString("n2")} " +
                    $"freq: {Settings.Instance.Frequency.ToString("n2")} " +
                    $"tstep: {Settings.Instance.TimeScaleRatio.ToString("n2")} " +
                    $"ambT: {(g.FrameAmbientTemprature).ToString("n4")} " +
                    $"decay: {g.FrameSolarDecay.ToString("n4")} " +
                    $"wind: {g.FrameWindDirection.Length().ToString("n4")} " +
                    $"isOcc: {g.FrameSolarOccluded}", 1, "White");

                MyAPIGateway.Utilities.ShowNotification($"[Cell] {c.Block.Position} " +
                    $"T: {c.Temperature.ToString("n4")} " +
                    $"dT: {c.DeltaTemperature.ToString("n6")} " +
                    $"Gen: {c.HeatGeneration.ToString("n4")} " +
                    $"ext: {c.ExposedSurfaces.ToString("n4")} ", 1, "White");

                MyAPIGateway.Utilities.ShowNotification(
                    $"[Calc] m: {c.Mass.ToString("n0")} " +
                    $"k: {c.Definition.Conductivity} " +
                    $"sh {c.Definition.SpecificHeat} " +
                    $"em {c.Definition.Emissivity} " +
                    $"pwe: {c.Definition.ProducerWasteEnergy} " +
                    $"cwe: {c.Definition.ConsumerWasteEnergy} " +
                    $"kA: {(c.Definition.Conductivity * c.Area).ToString("n0")} " +
                    $"tm: {(c.Definition.SpecificHeat * c.Mass).ToString("n0")} " +
                    $"c: {c.C.ToString("n4")} " +
                    $"r: {c.Radiation.ToString("n2")} " +
                    $"rdt: {(c.Radiation * c.ThermalMassInv).ToString("n4")} " +
                    $"prod: {c.EnergyProduction} " +
                    $"cons: {(c.EnergyConsumption + c.ThrustEnergyConsumption)} ", 1, "White");

                //MyAPIGateway.Utilities.ShowNotification($"[Solar] {c.SolarIntensity.ToString("n3")} Average: {g.AverageSolarHeat[0].ToString("n3")}, {tGrid.AverageSolarHeat[1].ToString("n3")}, {tGrid.AverageSolarHeat[2].ToString("n3")}, {tGrid.AverageSolarHeat[3].ToString("n3")}, {tGrid.AverageSolarHeat[4].ToString("n3")}, {tGrid.AverageSolarHeat[5].ToString("n3")}", 1, "White");

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
