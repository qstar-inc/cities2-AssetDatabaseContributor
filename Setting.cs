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
            PackCount = 10;
            Cooldown = 60;
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
        public int PackCount { get; set; } = 10;

        [SettingsUISlider(
            max = Constants.CooldownMax,
            min = Constants.CooldownMin,
            step = 10,
            unit = Unit.kInteger
        )]
        [SettingsUISection(GeneralTab, GeneralGroup)]
        public int Cooldown { get; set; } = 60;

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

#if !DEBUG
        [SettingsUIHidden]
#endif
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

#if !DEBUG
        [SettingsUIHidden]
#endif
        [SettingsUISection(GeneralTab, GeneralGroup)]
        [SettingsUIButton]
        public bool ExtractSubscribedPrefabs
        {
            set
            {
                WorldHelper
                    .GetSystem<ExtractionSystem>()
                    .ExtractPrefabs(ExtractionSystem.Limits.Mod);
            }
        }
    }
}
