using AssetDatabaseContributor.Systems;
using Game.Modding;
using Game.Settings;
using Game.UI;
using StarQ.Shared.Extensions;
using StarQ.Shared.Generators;
#if DEBUG
using System.Collections.Generic;
using System.Text.RegularExpressions;
#endif

namespace AssetDatabaseContributor
{
    [GenerateSettingCommonAttribute]
    public partial class Setting : ModSetting
    {
        public override void SetDefaults()
        {
            ContribEnabled = false;
            PackCount = Constants.PackCountMax;
            Cooldown = Constants.CooldownMin;
            TaskDelay = Constants.DelayMin;
            AskedForConsent = false;
            ConsentForContribution = false;
            ConsentForUsernameShare = false;
        }

        [SettingsUIHidden]
        public bool AskedForConsent { get; set; } = false;

        [SettingsUIHidden]
        public bool ConsentForContribution { get; set; } = false;

        [SettingsUIHidden]
        public bool ConsentForUsernameShare { get; set; } = false;

        [SettingsUISection(GeneralTab, GeneralGroup)]
        public bool ContribEnabled { get; set; } = false;

        [SettingsUISlider(
            max = Constants.PackCountMax,
            min = Constants.PackCountMin,
            unit = Unit.kInteger
        )]
        [SettingsUISection(GeneralTab, GeneralGroup)]
        public int PackCount { get; set; } = 25;

        [SettingsUISlider(
            max = Constants.CooldownMax,
            min = Constants.CooldownMin,
            step = 10,
            unit = Unit.kInteger
        )]
        [SettingsUISection(GeneralTab, GeneralGroup)]
        public int Cooldown { get; set; } = 60;

        [SettingsUISlider(
            max = Constants.DelayMax,
            min = Constants.DelayMin,
            step = 10,
            unit = Unit.kInteger
        )]
        [SettingsUISection(GeneralTab, GeneralGroup)]
        public int TaskDelay { get; set; } = 30;

        bool InGameOrEditor => WorldHelper.IsGameOrEditor;

        [SettingsUIButton]
        [SettingsUISection(GeneralTab, GeneralGroup)]
        public bool ResetConsent
        {
            set { SetDefaults(); }
        }

        bool DisabledPrivacyPolicy => true;

        [SettingsUIButton]
        [SettingsUISection(GeneralTab, GeneralGroup)]
        [SettingsUIDisableByCondition(typeof(Setting), nameof(DisabledPrivacyPolicy))]
        public bool PrivacyPolicy
        {
            set => VariableHelper.OpenURL("https://cities2.starq.fyi/privacy-policy");
        }

        [SettingsUIButton]
        [SettingsUISection(GeneralTab, GeneralGroup)]
        public bool CleanupLocalData
        {
            set { StartupSystem.CleanupLocalData(); }
        }

#if DEBUG

        [SettingsUITextInput]
        [SettingsUISection(GeneralTab, GeneralGroup)]
        [SettingsUIDisplayName(overrideValue: "Mod To Check")]
        [SettingsUIDescription(overrideValue: "Comma seperated list")]
        [SettingsUISetter(typeof(Setting), nameof(ValidateSubs))]
        public string ModToCheck { get; set; } = "";

        public void ValidateSubs()
        {
            var ids = ModToCheck.Split(',');
            HashSet<string> list = new();
            foreach (var id in ids)
            {
                var trimmed = id.Trim();
                if (Regex.IsMatch(trimmed, @"^\d+_\d+$"))
                    list.Add(trimmed);
            }

            if (list.Count <= 0)
            {
                LogHelper.SendLog("Nothing found");
                return;
            }
            LogHelper.SendLog($"Adding {list.Count}: {string.Join(",", list)}");
            ExtractionSystem.ModsToCheck = list;
        }

        [SettingsUIDisplayName(overrideValue: "Extract Data")]
        [SettingsUIDescription(overrideValue: "")]
        [SettingsUISection(GeneralTab, GeneralGroup)]
        [SettingsUIButton]
        public bool ExtractData
        {
            set
            {
                ValidateSubs();

                WorldHelper.GetSystem<ExtractionSystem>().ExtractData();
            }
        }

        [SettingsUIDisplayName(overrideValue: "Startup System")]
        [SettingsUIDescription(overrideValue: "Startup System")]
        [SettingsUISection(GeneralTab, GeneralGroup)]
        [SettingsUIButton]
        public bool StartStartupSystem
        {
            set
            {
                StartupSystem.TaskRunning = false;
                WorldHelper.GetSystem<StartupSystem>().AfterGameLoadingComplete();
            }
        }

        [SettingsUIDisplayName(overrideValue: "MapTest")]
        [SettingsUIDescription(overrideValue: "MapTest")]
        [SettingsUISection(GeneralTab, GeneralGroup)]
        [SettingsUIButton]
        public bool MapTest
        {
            set { WorldHelper.GetSystem<TestSystem>().TestStuff(); }
        }

#endif
    }
}
