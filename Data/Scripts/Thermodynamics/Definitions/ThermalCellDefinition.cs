using System;
using System.Collections.Generic;
using System.Text;
using System.Xml.Serialization;
using Draygo.BlockExtensionsAPI;
using ProtoBuf;
using VRage.Game;
using VRage.Utils;

namespace Thermodynamics
{
    [ProtoContract]
    public class ThermalCellDefinition
    {
        private static readonly MyStringId GroupId = MyStringId.GetOrCompute("ThermalBlockProperties");
        private static readonly MyStringId ConductivityId = MyStringId.GetOrCompute("Conductivity");
        private static readonly MyStringId SpecificHeatId = MyStringId.GetOrCompute("SpecificHeat");
        private static readonly MyStringId ProducerHeatPerWattId = MyStringId.GetOrCompute("ProducerHeatPerWatt");
        private static readonly MyStringId ConsumerHeatPerWattId = MyStringId.GetOrCompute("ConsumerHeatPerWatt");
        private static readonly MyDefinitionId DefaultCubeBlockDefinitionId = new MyDefinitionId(typeof(MyObjectBuilder_EnvironmentDefinition), Settings.DefaultSubtypeId);


        /// <summary>
        /// This field specifies the conductivity of the block.
        /// </summary>
        [ProtoMember(4)]
        public float Conductivity;

        /// <summary>
        /// This field specifies the heat capacity of the block in joules per kelvin.
        /// </summary>
        [ProtoMember(5)]
        public float SpacificHeat;
        

        /// <summary>
        /// 
        /// </summary>
        [ProtoMember(20)]
        public float ProducerWasteHeatPerWatt;

        /// <summary>
        /// 
        /// </summary>
        [ProtoMember(30)]
        public float ConsumerWasteHeatPerWatt;


        public static ThermalCellDefinition GetDefinition(MyDefinitionId defId)
        {
            ThermalCellDefinition def = new ThermalCellDefinition();
            DefinitionExtensionsAPI lookup = Session.Definitions;

            if (!lookup.DefinitionIdExists(defId)) 
            {
                defId = new MyDefinitionId(defId.TypeId, Settings.DefaultSubtypeId);
                
                if (!lookup.DefinitionIdExists(defId)) 
                {
                    defId = DefaultCubeBlockDefinitionId;
                }
            }

            double dvalue;

            if (lookup.TryGetDouble(defId, GroupId, ConductivityId, out dvalue))
                def.Conductivity = (float)dvalue;

            if (lookup.TryGetDouble(defId, GroupId, SpecificHeatId, out dvalue))
                def.SpacificHeat = (float)dvalue;

            if (lookup.TryGetDouble(defId, GroupId, ProducerHeatPerWattId, out dvalue))
                def.ProducerWasteHeatPerWatt = (float)dvalue;

            if (lookup.TryGetDouble(defId, GroupId, ConsumerHeatPerWattId, out dvalue))
                def.ConsumerWasteHeatPerWatt = (float)dvalue;

            return def;
        }
    }
}
