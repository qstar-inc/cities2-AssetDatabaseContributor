using System.Collections.Generic;
using System.IO;
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

            WorldHelper.GetSystem<StartupSystem>();
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
    }
}
