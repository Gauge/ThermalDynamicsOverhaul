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
		public const bool Debug = true;

		public const float SecondsPerFrame = 1f / 60f;

		public static Settings Instance;

		[ProtoMember(1)]
		public int Version;

		/// <summary>
		/// The frequency with which the system updates 
		/// </summary>
		[ProtoMember(10)]
		public int Frequency;

		/// <summary>
		/// Used to adjust values that are are calculated in seconds, to the current time scale 
		/// </summary>
		[XmlIgnore]
		public float TimeScaleRatio;

		[ProtoMember(80)]
		public BlockProperties Vacuum;

		[ProtoMember(81)]
		public BlockProperties Generic;

		[ProtoMember(100)]
		public List<BlockProperties> BlockConfig;

		public static Settings GetDefaults()
		{
			Settings s = new Settings {
				Version = 1,
				Frequency = 60,
				Vacuum = new BlockProperties {
					Type = "Vaccum",
					Conductivity = 0f,
					HeatCapacity = 0f,
					HeatGeneration = 0f,
				},

				Generic = new BlockProperties {
					Type = "Generic",
					Conductivity = 80f,
					HeatCapacity = 100f,
					HeatGeneration = 0f,
				},

				BlockConfig = new List<BlockProperties>() {
					new BlockProperties { 
						Type = "MyObjectBuilder_Reactor",
						Conductivity = 80f,
						HeatCapacity = 100f,
						HeatGeneration = 10000000000f,
					},

					new BlockProperties {
						Type = "MyObjectBuilder_ConveyorConnector",
						Conductivity = 1000f,
						HeatCapacity = 50f,
						HeatGeneration = 0f,
					},
				},
			};

			s.Init();
			return s;
			
		}

		private void Init() {

			if (Frequency < 1)
				Frequency = 1;

			TimeScaleRatio = Frequency / 60f;
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

			settings.Init();
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
