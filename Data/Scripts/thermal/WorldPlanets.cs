using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using System.Collections.Generic;
using VRage.Game;
using VRage.Game.Components;
using VRage.ModAPI;
using VRageMath;

namespace ThermalOverhaul
{
    [MySessionComponentDescriptor(MyUpdateOrder.NoUpdate)]
    public class WorldPlanets : MySessionComponentBase
    {
        private class Planet
        {
            public Vector3D Position;
            public MyGravityProviderComponent Gravity;
            public MyPlanet GamePlanet;
        }

        private static List<Planet> Planets = new List<Planet>();

        public override void Init(MyObjectBuilder_SessionComponent sessionComponent)
        {
            MyAPIGateway.Entities.OnEntityAdd += AddPlanet;
            MyAPIGateway.Entities.OnEntityRemove += RemovePlanet;
        }



        private void AddPlanet(IMyEntity ent)
        {
            if (ent is MyPlanet)
            {
                MyPlanet gamePlanet = ent as MyPlanet;

                Planet p = new Planet()
                {
                    Position = gamePlanet.PositionComp.GetPosition(),
                    Gravity = gamePlanet.Components.Get<MyGravityProviderComponent>(),
                    GamePlanet = gamePlanet
                };

                Planets.Add(p);
            }
        }

        private void RemovePlanet(IMyEntity ent)
        {
            Planet p = Planets.Find(x => x.GamePlanet.EntityId == ent.EntityId);

            if (p != null)
            {
                Planets.Remove(p);
            }
        }


        /// <summary>
        /// returns the gravity force vetor being applied at a location
        /// also returns total air pressure at that location
        /// </summary>
        public static ExternalForceData GetExternalForces(Vector3D WorldPosition)
        {
            ExternalForceData data = new ExternalForceData();

            foreach (Planet p in Planets)
            {
                data.Gravity += p.Gravity.GetWorldGravity(WorldPosition);

                if (p.GamePlanet.HasAtmosphere)
                {
                    data.AtmosphericPressure += p.GamePlanet.GetAirDensity(WorldPosition);
                }
            }

            return data;
        }
    }

    public class ExternalForceData
    {
        public Vector3D Gravity = Vector3D.Zero;
        public float AtmosphericPressure = 0;
    }
}
