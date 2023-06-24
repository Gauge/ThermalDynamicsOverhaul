using Draygo.BlockExtensionsAPI;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using System.Collections.Generic;
using VRage.Game;
using VRage.Game.Components;
using VRage.ModAPI;
using VRageMath;

namespace Thermodynamics
{
    [MySessionComponentDescriptor(MyUpdateOrder.NoUpdate)]
    public class PlanetManager : MySessionComponentBase
    {
        public class Planet
        {
            public MyPlanet Entity;
            public Vector3D Position;
            public PlanetDefinition Definition;
            public MyGravityProviderComponent GravityComponent;
        }

        public class ExternalForceData
        {
            public Vector3D Gravity = Vector3D.Zero;
            public float AtmosphericPressure;
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
                MyPlanet entity = ent as MyPlanet;

                Planet p = new Planet()
                {
                    Entity = entity,
                    Position = entity.PositionComp.WorldMatrixRef.Translation,
                    Definition = PlanetDefinition.GetDefinition(entity.DefinitionId.Value),
                    GravityComponent = entity.Components.Get<MyGravityProviderComponent>(),
                };

                Planets.Add(p);
            }
        }

        private void RemovePlanet(IMyEntity ent)
        {
            Planet p = Planets.Find(x => x.Entity.EntityId == ent.EntityId);

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
                data.Gravity += p.GravityComponent.GetWorldGravity(WorldPosition);

                if (p.Entity.HasAtmosphere)
                {
                    data.AtmosphericPressure += p.Entity.GetAirDensity(WorldPosition);
                }
            }

            return data;
        }

        /// <summary>
        /// Finds the closest planet to the current position
        /// </summary>
        /// <param name="position"></param>
        /// <returns></returns>
        public static Planet GetClosestPlanet(Vector3D position) 
        {
            Planet current = null;
            double distance = double.MaxValue;
            for (int i = 0; i < Planets.Count; i++) 
            {
                Planet p = Planets[i];
                double d = (p.Position - position).LengthSquared();
                if (d < distance) 
                {
                    current = p;
                    distance = d;
                }
            }

            return current;
        }


    }


}
