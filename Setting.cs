using AssetDatabaseContributor.Systems;
using Game.Modding;
using Game.Settings;
using Game.UI;
using StarQ.Shared.Extensions;
using StarQ.Shared.Generators;

namespace AssetDatabaseContributor
{
    [GenerateSettingCommonAttribute]
    public partial class Setting : ModSetting
    {
        public override void SetDefaults()
        {
            ContribEnabled = false;
            PackCount = 50;
            Cooldown = 60;
            TaskDelay = 5;
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
        public int TaskDelay { get; set; } = 5;

        bool InGameOrEditor => WorldHelper.IsGameOrEditor;

        //[SettingsUIButton]
        //[SettingsUISection(GeneralTab, GeneralGroup)]
        //[SettingsUIDisableByCondition(typeof(Setting), nameof(InGameOrEditor))]
        //public bool RunNow
        //{
        //    set { WorldHelper.GetSystem<StartupSystem>().Start(); }
        //}

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
        public bool CleanLocalSource
        {
            set { StartupSystem.CleanLocalSource(); }
        }

#if DEBUG
        [SettingsUIDisplayName(overrideValue: "Extract Game Prefabs")]
        [SettingsUIDescription(overrideValue: "Extract Game Prefabs")]
        [SettingsUISection(GeneralTab, GeneralGroup)]
        [SettingsUIButton]
        public bool ExtractGamePrefabs
        {
            set
            {
                WorldHelper
                    .GetSystem<ExtractionSystem>()
                    .ExtractPrefabs(ExtractionSystem.Limits.Game);
            }
        }

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

        [SettingsUIDisplayName(overrideValue: "Extract Mod Prefabs")]
        [SettingsUIDescription(overrideValue: "Extract Mod Prefabs")]
        [SettingsUISection(GeneralTab, GeneralGroup)]
        [SettingsUIButton]
        public bool ExtractSubscribedPrefabs
        {
            set
            {
                ValidateSubs();

                WorldHelper.RunOnMainThreadAsync(
                    WorldHelper.GetSystem<StartupSystem>().CollectImages
                );

                Task.Run(WorldHelper.GetSystem<StartupSystem>().ZipImages);

                WorldHelper
                    .GetSystem<ExtractionSystem>()
                    .ExtractPrefabs(ExtractionSystem.Limits.Mod);
            }
        }

        //[SettingsUIDisplayName(overrideValue: "Test Prefabs")]
        //[SettingsUIDescription(overrideValue: "Test Prefabs")]
        //[SettingsUISection(GeneralTab, GeneralGroup)]
        //[SettingsUIButton]
        //public bool TestPrefabs
        //{
        //    set { WorldHelper.GetSystem<ExtractionSystem>().TestPrefabs(); }
        //}

#endif
    }
}
