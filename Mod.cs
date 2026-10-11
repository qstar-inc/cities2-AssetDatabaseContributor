using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AssetDatabaseContributor.Systems;
using Colossal.IO.AssetDatabase;
using Game;
using Game.Modding;
using StarQ.Shared.Extensions;
using StarQ.Shared.Generators;

namespace AssetDatabaseContributor
{
    [GenerateModInfo]
    public partial class Mod : IMod
    {
        public void OnLoad(UpdateSystem updateSystem)
        {
            Instance = this;
            LogHelper.Init(Id, log);
            LocaleHelper.Init(Id, Name, GetReplacements);

            m_Setting = new Setting(this);
            m_Setting.RegisterInOptionsUI();
            AssetDatabase.global.LoadSettings(
                nameof(AssetDatabaseContributor),
                m_Setting,
                new Setting(this)
            );

            Directory.CreateDirectory(DataDir);

            RemoveOutdatedVersions();

            WorldHelper.GetSystem<StartupSystem>();
            //LogHelper.SendLog(
            //    "Mod temporarily disabled while shenenigans are being solved. You don't have to do anything."
            //);
        }

        public void OnDispose()
        {
            LocaleHelper.Dispose();
            m_Setting?.UnregisterInOptionsUI();
            m_Setting = null;
        }

        public static Dictionary<string, string> GetReplacements()
        {
            return new() { };
        }

        public static void RemoveOutdatedVersions()
        {
            string[] validFilenames = new[]
            {
                $"ComponentData_v{StartupSystem.CurrentComponentDataVersion}.ADC_json",
                $"SourceData_v{StartupSystem.CurrentSourceDataVersion}.ADC_json",
                $"SourceData_v{StartupSystem.CurrentSourceDataVersion}_debug.ADC_json",
            };

            if (!Directory.Exists(DataDir))
                return;

            HashSet<string> validFiles = new(validFilenames, StringComparer.OrdinalIgnoreCase);

            IEnumerable<string> files = Directory
                .EnumerateFiles(DataDir)
                .Where(file =>
                {
                    var name = Path.GetFileName(file);

                    return (
                            name.StartsWith("SourceData_v", StringComparison.OrdinalIgnoreCase)
                            || name.StartsWith(
                                "ComponentData_v",
                                StringComparison.OrdinalIgnoreCase
                            )
                        ) && name.EndsWith(".ADC_json", StringComparison.OrdinalIgnoreCase);
                });

            foreach (string file in files)
            {
                string filename = Path.GetFileName(file);

                if (validFiles.Contains(filename))
                    continue;

                try
                {
                    LogHelper.SendLog($"Deleting outdated file '{file}'");
                    File.Delete(file);
                }
                catch (Exception ex)
                {
                    LogHelper.SendLog($"Failed to delete outdated SourceData file '{file}': {ex}");
                }
            }
        }
    }
}
