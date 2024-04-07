using Draygo.BlockExtensionsAPI;
using ProtoBuf;
using System;
using System.Collections.Generic;
using System.Text;
using VRage.Game;
using VRage.Utils;

namespace Thermodynamics
{
    [ProtoContract]
    public class PlanetDefinition
    {

        private static readonly MyStringId GroupId = MyStringId.GetOrCompute("ThermalPlanetProperties");
        private static readonly MyStringId NightTemperatureId = MyStringId.GetOrCompute("NightTemperature");
        private static readonly MyStringId DayTemperatureId = MyStringId.GetOrCompute("DayTemperature");
        private static readonly MyStringId UndergroundTemperatureId = MyStringId.GetOrCompute("UndergroundTemperature");
        private static readonly MyStringId CoreTemperatureId = MyStringId.GetOrCompute("CoreTemperature");
        private static readonly MyStringId SealevelDeadzoneId = MyStringId.GetOrCompute("SealevelDeadzone");
        private static readonly MyStringId SolarDecayId = MyStringId.GetOrCompute("SolarDecay");
        private static readonly MyStringId AtmoConductivityId = MyStringId.GetOrCompute("AtmoConductivity");
        private static readonly MyStringId AtmoDensityId = MyStringId.GetOrCompute("AtmoDensity");

        /// <summary>
        /// The ambiant temperature when the sun is on the opposite side of the planet
        /// </summary>
        [ProtoMember(10)]
        public float NightTemperature;

        /// <summary>
        /// The ambiant temperature when the sun is directly overhead
        /// </summary>
        [ProtoMember(15)]
        public float DayTemperature;

        /// <summary>
        /// The ambiant temperature when underground
        /// </summary>
        [ProtoMember(17)]
        public float UndergroundTemperature;

        /// <summary>
        /// The temperature at the center of the planet
        /// </summary>
        [ProtoMember(20)]
        public float CoreTemperature;

        /// <summary>
        /// the distance below sealevel that remains underground temperatures
        /// </summary>
        [ProtoMember(25)]
        public float SealevelDeadzone;

        /// <summary>
        /// A value between 0 and 1
        /// Represents the percentage of solar engery that will be lost in full atomosphere 
        /// </summary>
        [ProtoMember(30)]
        public float SolarDecay;

        /// <summary>
        /// The conductiveness of the atmospheric gas
        /// </summary>
        [ProtoMember(40)]
        public float AtmoConductivity;

        /// <summary>
        /// The weight of the atmospheric gas
        /// </summary>
        [ProtoMember(50)]
        public float AtmoDensity;

        public static PlanetDefinition GetDefinition(MyDefinitionId defId) 
        {
            MyLog.Default.Info($"[{Settings.Name}] Planet Definition: {defId}");

            PlanetDefinition def = new PlanetDefinition();
            DefinitionExtensionsAPI lookup = Session.Definitions;

            if (!lookup.DefinitionIdExists(defId))
            {
                defId = new MyDefinitionId(typeof(MyObjectBuilder_PlanetGeneratorDefinition), Settings.DefaultSubtypeId);
            }

            double dvalue;

            if (lookup.TryGetDouble(defId, GroupId, NightTemperatureId, out dvalue))
                def.NightTemperature = (float)dvalue;

            if (lookup.TryGetDouble(defId, GroupId, DayTemperatureId, out dvalue))
                def.DayTemperature = (float)dvalue;

            if (lookup.TryGetDouble(defId, GroupId, UndergroundTemperatureId, out dvalue))
                def.UndergroundTemperature = (float)dvalue;

            if (lookup.TryGetDouble(defId, GroupId, CoreTemperatureId, out dvalue))
                def.CoreTemperature = (float)dvalue;

            if (lookup.TryGetDouble(defId, GroupId, SealevelDeadzoneId, out dvalue))
                def.SealevelDeadzone = (float)dvalue;

            if (lookup.TryGetDouble(defId, GroupId, SolarDecayId, out dvalue))
                def.SolarDecay = (float)dvalue;

            if (lookup.TryGetDouble(defId, GroupId, AtmoConductivityId, out dvalue))
                def.AtmoConductivity = (float)dvalue;

            if (lookup.TryGetDouble(defId, GroupId, AtmoDensityId, out dvalue))
                def.AtmoDensity = (float)dvalue;


            return def;

        }
    }
}
