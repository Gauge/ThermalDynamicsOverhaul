using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;
using ProtoBuf;
using Sandbox.ModAPI;
using VRage.Utils;
using VRageMath;

namespace ThermalOverhaul
{
	[ProtoContract]
	public class Settings
	{
		public const string Filename = "ThermalOverhaulConfig.cfg";
		public const string Name = "Thermal Overhaul";

		[ProtoMember(1)]
		public int Version;

		/// <summary>
		/// The temperature differnece at which a block will move from active to idle
		/// </summary>
		[ProtoMember(10)]
		public float TemperatureActivationThreshold;

		/// <summary>
		/// The update interval of active blocks in seconds
		/// </summary>
		[ProtoMember(11)]
		public float ActiveTime;

		/// <summary>
		/// The update interval of idle blocks in seconds
		/// </summary>
		[ProtoMember(12)]
		public float IdleTime;


		[ProtoMember(80)]
		public BlockProperties Vacuum;

		[ProtoMember(81)]
		public BlockProperties Generic;

		[ProtoMember(100)]
		public List<BlockProperties> BlockConfig;

		public static Settings GetDefaults()
		{
			return new Settings {
				Version = 1,
				TemperatureActivationThreshold = 0.01f,
				ActiveTime = 0f,
				IdleTime = 1f,
				Vacuum = new BlockProperties {
					Type = "Vaccum",
					Conductivity = 0f,
					HeatCapacity = 0f,
					HeatGeneration = 0f,
				},

				Generic = new BlockProperties {
					Type = "Generic",
					Conductivity = 80f,
					HeatCapacity = 450f,
					HeatGeneration = 0f,
				},

				BlockConfig = new List<BlockProperties>() {
					new BlockProperties { 
						Type = "MyObjectBuilder_Reactor",
						Conductivity = 80f,
						HeatCapacity = 450f,
						HeatGeneration = 10000f,
					},

					new BlockProperties {
						Type = "MyObjectBuilder_ConveyorConnector",
						Conductivity = 600f,
						HeatCapacity = 100f,
						HeatGeneration = 0f,
					},
				},
			};
		}

		public static Settings Load()
		{
			Settings defaults = GetDefaults();
			Settings settings = defaults;
			try
			{
				if (MyAPIGateway.Utilities.FileExistsInWorldStorage(Filename, typeof(Settings)))
				{
					MyLog.Default.Info($"[{Name}] Loading saved settings");
					TextReader reader = MyAPIGateway.Utilities.ReadFileInWorldStorage(Filename, typeof(Settings));
					string text = reader.ReadToEnd();
					reader.Close();

					settings = MyAPIGateway.Utilities.SerializeFromXML<Settings>(text);

					if (settings.Version != defaults.Version)
					{
						MyLog.Default.Info($"[{Name}] Old version updating config {settings.Version}->{GetDefaults().Version}");
						settings = GetDefaults();
						Save(settings);
					}
				}
				else
				{
					MyLog.Default.Info($"[{Name}] Config file not found. Loading default settings");
					Save(settings);
				}
			}
			catch (Exception e)
			{
				MyLog.Default.Info($"[{Name}] Failed to load saved configuration. Loading defaults\n {e.ToString()}");
				Save(settings);
			}

			return settings;
		}




		public static void Save(Settings settings)
		{
			try
			{
				MyLog.Default.Info($"[{Name}] Saving Settings");
				TextWriter writer = MyAPIGateway.Utilities.WriteFileInWorldStorage(Filename, typeof(Settings));
				writer.Write(MyAPIGateway.Utilities.SerializeToXML(settings));
				writer.Close();
			}
			catch (Exception e)
			{
				MyLog.Default.Info($"[{Name}] Failed to save settings\n{e.ToString()}");
			}
		}
	}
}
