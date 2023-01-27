using System;
using System.Collections.Generic;
using System.Text;
using System.Xml.Serialization;
using ProtoBuf;
using VRage.Game;

namespace ThermalOverhaul
{
    [ProtoContract]
    public class BlockProperties
    {
        /// <summary>
        /// This field specifies the type of block.
        /// </summary>
        [ProtoMember(1)]
        public string Type;

        /// <summary>
        /// This field specifies the subtype of the block.
        /// </summary>
        [ProtoMember(2)]
        public string Subtype;

        /// <summary>
        /// This field specifies the mass of the block in kilograms.
        /// </summary>
        //[ProtoMember(3)]
        //public float Mass;

        /// <summary>
        /// This field specifies the conductivity of the block in siemens per meter.
        /// </summary>
        [ProtoMember(4)]
        public float Conductivity;

        /// <summary>
        /// This field specifies the heat capacity of the block in joules per kelvin.
        /// </summary>
        [ProtoMember(5)]
        public float HeatCapacity;

        /// <summary>
        /// This field specifies the heat generation of the block in joules per second.
        /// </summary>
        [ProtoMember(10)]
        public float HeatGeneration;
    }
}
