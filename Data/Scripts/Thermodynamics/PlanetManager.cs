using Draygo.BlockExtensionsAPI;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using System.Drawing;
using VRage.Game;
using VRage.Game.Components;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;

namespace Thermodynamics
{
    [MySessionComponentDescriptor(MyUpdateOrder.NoUpdate)]
    public class PlanetManager : MySessionComponentBase
    {
        public static readonly double SunSize = 0.045f;
        public static readonly double Denominator = 1 - SunSize;
        public static readonly PlanetDefinition NullDef = new PlanetDefinition();

        public class Planet
        {
            public MyPlanet Entity;
            public Vector3D Position;
            public MyGravityProviderComponent GravityComponent;
            private PlanetDefinition definition = NullDef;
            public PlanetDefinition Definition() 
            {
                if (definition == NullDef && Entity.DefinitionId.HasValue) 
                {
                    definition = PlanetDefinition.GetDefinition(Entity.DefinitionId.Value);

                    MyLog.Default.Info($"[{Settings.Name}] updated planet definition: {Entity.DisplayName}");
                }

                return definition;
            }
        }

        public class ExternalForceData
        {
            public Vector3D Gravity = Vector3D.Zero;
            public Vector3D WindDirection = Vector3D.Zero;
            public float WindSpeed;
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

                MyLog.Default.Info($"[{Settings.Name}] Added Planet: {entity.DisplayName} - {entity.DefinitionId.HasValue}");

                Planet p = new Planet()
                {
                    Entity = entity,
                    Position = entity.PositionComp.WorldMatrixRef.Translation,
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
        public static ExternalForceData GetExternalForces(Vector3D worldPosition)
        {
            ExternalForceData data = new ExternalForceData();

            Planet planet = null;
            double distance = double.MaxValue;
            foreach (Planet p in Planets)
            {
                data.Gravity += p.GravityComponent.GetWorldGravity(worldPosition);

                double d = (p.Position - worldPosition).LengthSquared();
                if (d < distance)
                {
                    planet = p;
                    distance = d;
                }
            }

            if (planet?.Entity.HasAtmosphere == true)
            {
                data.AtmosphericPressure = planet.Entity.GetAirDensity(worldPosition);
                data.WindSpeed = planet.Entity.GetWindSpeed(worldPosition);
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

        public static bool IsSolarOccluded(Vector3D observer, Vector3 solarDirection, MyPlanet planet)
        {
            Vector3D local = observer - planet.PositionComp.WorldMatrixRef.Translation;
            double dot = Vector3.Dot(Vector3D.Normalize(local), solarDirection);
            return dot < GetLargestOcclusionDotProduct(GetVisualSize(local, planet.AverageRadius));
        }


        /// <summary>
        /// a number between 0 and 1 representing the side object based on distance
        /// </summary>
        /// <param name="observer">the local vector between the observer and the target</param>
        /// <param name="radius">the size of the target</param>
        public static double GetVisualSize(Vector3D observer, double radius)
        {
            return 2 * Math.Atan(radius / (2 * observer.Length()));
        }

        /// <summary>
        /// a number between 0 and 1 representing the side object based on distance
        /// </summary>
        /// <param name="distance">the distance between the observer and the target</param>
        /// <param name="radius">the size of the target</param>
        public static double GetVisualSize(double distance, double radius)
        {
            return 2 * Math.Atan(radius / (2 * distance));
        }

        /// <summary>
        /// an equation made by plotting the edge most angle of the occluded sun
        /// takes in the current visual size of the planet and produces a number between 0 and -1
        /// if the dot product of the planet and sun directions is less than this number it is occluded
        /// </summary>
        /// <param name="visualSize"></param>
        /// <returns></returns>
        public static double GetLargestOcclusionDotProduct(double visualSize)
        {
            return -1 + (0.85 * visualSize * visualSize * visualSize);
        }

    }


}
