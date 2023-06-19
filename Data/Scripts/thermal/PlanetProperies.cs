using System;
using System.Collections.Generic;
using System.Text;
using System.Xml.Serialization;
using ProtoBuf;
using VRage.Game;

namespace ThermalOverhaul
{
    [ProtoContract]
    public class PlanetProperties
    {
        /// <summary>
        /// The partial planet name
        /// Used by planets to lookup their settings
        /// </summary>
        [ProtoMember(1)]
        public string Name;

        /// <summary>
        /// Temprature at the warmest part of the day.
        /// </summary>
        [ProtoMember(2)]
        public string MaxAmbiantTemperature;

        /// <summary>
        /// Temprature at the coldest part of the day.
        /// </summary>
        [ProtoMember(3)]
        public string MinAmbiantTemperature;




    }
}
