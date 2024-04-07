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
        /// Conductivity equation: watt / ( meter * Temp)
        /// For examples see https://www.engineeringtoolbox.com/thermal-conductivity-metals-d_858.html
        /// </summary>
        [ProtoMember(1)]
        public float Conductivity;

        /// <summary>
        /// SpecificHeat equation: watt / (mass_kg * temp_kelven)
        /// For examples see https://en.wikipedia.org/wiki/Table_of_specific_heat_capacities
        /// </summary>
        [ProtoMember(10)]
        public float SpecificHeat;
        
        /// <summary>
        /// the percent of produced energy converted to heat
        /// </summary>
        [ProtoMember(20)]
        public float ProducerWasteEnergy;

        /// <summary>
        /// the percent of consumed energy converted to heat
        /// </summary>
        [ProtoMember(30)]
        public float ConsumerWasteEnergy;


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
                def.SpecificHeat = (float)dvalue;

            if (lookup.TryGetDouble(defId, GroupId, ProducerHeatPerWattId, out dvalue))
                def.ProducerWasteEnergy = (float)dvalue;

            if (lookup.TryGetDouble(defId, GroupId, ConsumerHeatPerWattId, out dvalue))
                def.ConsumerWasteEnergy = (float)dvalue;


            def.Conductivity = Math.Max(0, def.Conductivity);

            def.SpecificHeat = Math.Max(0, def.SpecificHeat);

            def.ProducerWasteEnergy = Math.Max(0, def.ProducerWasteEnergy);

            def.ConsumerWasteEnergy = Math.Max(0, def.ConsumerWasteEnergy);

            return def;
        }
    }
}
