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
        private static readonly MyStringId EmissivityId = MyStringId.GetOrCompute("Emissivity");
        private static readonly MyStringId ProducerWasteEnergyId = MyStringId.GetOrCompute("ProducerWasteEnergy");
        private static readonly MyStringId ConsumerWasteEnergyId = MyStringId.GetOrCompute("ConsumerWasteEnergy");
        private static readonly MyStringId CriticalTemperatureId = MyStringId.GetOrCompute("CriticalTemperature");
        private static readonly MyStringId CriticalTemperatureScalerId = MyStringId.GetOrCompute("CriticalTemperatureScaler");
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
        /// This is a value between 0 and 1 that represents how much energy will radiate away
        /// see for examples https://www.engineeringtoolbox.com/emissivity-coefficients-d_447.html
        /// </summary>
        [ProtoMember(15)]
        public float Emissivity;

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

        [ProtoMember(40)]
        public float CriticalTemperature;

        [ProtoMember(45)]
        public float CriticalTemperatureScaler;


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

            if (lookup.TryGetDouble(defId, GroupId, EmissivityId, out dvalue))
                def.Emissivity = (float)dvalue;

            if (lookup.TryGetDouble(defId, GroupId, ProducerWasteEnergyId, out dvalue))
                def.ProducerWasteEnergy = (float)dvalue;

            if (lookup.TryGetDouble(defId, GroupId, ConsumerWasteEnergyId, out dvalue))
                def.ConsumerWasteEnergy = (float)dvalue;

            if (lookup.TryGetDouble(defId, GroupId, CriticalTemperatureId, out dvalue))
                def.CriticalTemperature = (float)dvalue;

            if (lookup.TryGetDouble(defId, GroupId, CriticalTemperatureScalerId, out dvalue))
                def.CriticalTemperatureScaler = (float)dvalue;

            def.Conductivity = Math.Max(0, def.Conductivity);

            def.SpecificHeat = Math.Max(0, def.SpecificHeat);

            def.Emissivity = Math.Max(0, def.Emissivity);

            def.ProducerWasteEnergy = Math.Max(0, def.ProducerWasteEnergy);

            def.ConsumerWasteEnergy = Math.Max(0, def.ConsumerWasteEnergy);

            def.CriticalTemperature = Math.Max(0, def.CriticalTemperature);

            def.CriticalTemperatureScaler = Math.Max(0, Math.Min(1, def.CriticalTemperatureScaler));

            return def;
        }
    }
}
