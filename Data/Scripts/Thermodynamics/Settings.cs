﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;
using ProtoBuf;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Utils;
using VRageMath;

namespace Thermodynamics
{
	[ProtoContract]
	public class Settings
	{
		public const string Filename = "ThermodynamicsConfig.cfg";
		public const string Name = "Thermodynamics";
		public const bool Debug = true;

		public const float SecondsPerFrame = 1f / 60f;

		public static Settings Instance;

        public static readonly MyStringHash DefaultSubtypeId = MyStringHash.GetOrCompute("DefaultThermodynamics");

        [ProtoMember(1)]
		public int Version;

		/// <summary>
		/// The frequency with which the system updates 
		/// </summary>
		[ProtoMember(10)]
		public int Frequency;

		[ProtoMember(20)]
		public float SolarEnergy;

		[ProtoMember(30)]
		public float EnvironmentalRaycastDistance;

        /// <summary>
        /// The drain rate in watts per cubic meter of exposed surface
        /// </summary>
        [ProtoMember(50)]
		public float VaccumDrainRate;

		[ProtoMember(60)]
		public float VaccumeFullStrengthTemperature;

		[ProtoMember(70)]
		public float VaccumeRadiationStrength;


        /// <summary>
        /// Used to adjust values that are calculated in seconds, to the current time scale 
        /// </summary>
        [XmlIgnore]
		public float TimeScaleRatio;

		public static Settings GetDefaults()
		{
			Settings s = new Settings {
				Version = 1,
				Frequency = 1,
				SolarEnergy = 1000000f,
				EnvironmentalRaycastDistance = 5000f,
				VaccumeRadiationStrength = 0.0005f,
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