using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using System.Text;
using Thermodynamics;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRageMath;

namespace ThermalOverhaulExtention
{
    [MySessionComponentDescriptor(MyUpdateOrder.Simulation, 100)]
    public class PlayerExtention : MySessionComponentBase
    {
        public override void Simulate()
        {

            if (!MyAPIGateway.Utilities.IsDedicated)
            {
                MatrixD matrix = MyAPIGateway.Session.Camera.WorldMatrix;
                Vector3D start = matrix.Translation;
                Vector3D end = start + matrix.Forward * 100;

                IHitInfo hit;
                MyAPIGateway.Physics.CastRay(start, end, out hit);

                if (hit == null) return;

                MyCubeGrid grid = hit?.HitEntity as MyCubeGrid;

                if (grid == null) return;

                Vector3I position = grid.WorldToGridInteger(hit.Position + matrix.Forward * 0.05f);

                MyAPIGateway.Utilities.ShowNotification($"[Extention] GameLogic Type: {grid.GameLogic.GetType()}", 1, "White");
                MyAPIGateway.Utilities.ShowNotification($"[Extention] GameLogic As ThermalGrid: {grid.GameLogic as Thermodynamics.ThermalGrid}", 1, "White");
                MyAPIGateway.Utilities.ShowNotification($"[Extention] GameLogic As IThermalGrid: {grid.GameLogic as Thermodynamics.IThermalGrid}", 1, "White");

                //MyLog.Default.Info($"GameLogic is Composite: {grid.GameLogic is MyCompositeGameLogicComponent}");
                //MyAPIGateway.Utilities.ShowNotification($"[Extention] GameLogic Type: {grid.GameLogic.GetType()}", 1, "White");
                //MyAPIGateway.Utilities.ShowNotification($"[Extention] GameLogic is composit: {grid.GameLogic as MyCompositeGameLogicComponent != null}", 1, "White");

                //MyCompositeGameLogicComponent composite = grid.GameLogic as MyCompositeGameLogicComponent;

                //if (composite == null) return;

                //foreach (var comp in composite.GetComponents())
                //{
                //    MyAPIGateway.Utilities.ShowNotification($"[Extention] {comp.GetType()} - {comp as IThermalGrid != null}", 1, "White");
                //}



                //var coms = (grid.GameLogic as MyCompositeGameLogicComponent).GetComponents();

                //foreach (MyGameLogicComponent com in coms) 
                //{
                //                MyAPIGateway.Utilities.ShowNotification($"[Extention] Type: {com.GetType().ToString()}", 1, "White");

                //}

                //IThermalGrid tGrid = grid.GameLogic.GetAs<IThermalGrid>();

                //if (tGrid == null)
                //{
                //                MyAPIGateway.Utilities.ShowNotification($"[Extention] Grid is null", 1, "White");
                //	return;
                //            }				

                //IThermalCell cell = tGrid.GetCell(position);

                //if (cell == null) {
                //                MyAPIGateway.Utilities.ShowNotification($"[Extention] Cell is null", 1, "White");
                //	return;
                //            }

                //MyAPIGateway.Utilities.ShowNotification($"[Extention] Temp {tGrid.Temperature()} -- {cell.Temperature()}", 1, "White");


            }
        }
    }
}
