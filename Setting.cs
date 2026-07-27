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

        [SettingsUISlider(max = 20, min = 3, unit = Unit.kInteger)]
        [SettingsUISection(GeneralTab, GeneralGroup)]
        public int PackCount { get; set; } = 10;

        [SettingsUISlider(max = 6 * 60, min = 10, step = 10, unit = Unit.kInteger)]
        [SettingsUISection(GeneralTab, GeneralGroup)]
        public int Cooldown { get; set; } = 60;

        [SettingsUIButton]
        [SettingsUISection(GeneralTab, GeneralGroup)]
        public bool ResetConsent
        {
            set { SetDefaults(); }
        }

        [SettingsUIHidden]
        public bool Enabled = false;

        [SettingsUIButton]
        [SettingsUISection(GeneralTab, GeneralGroup)]
        [SettingsUIDisableByCondition(typeof(Setting), nameof(Enabled))]
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
