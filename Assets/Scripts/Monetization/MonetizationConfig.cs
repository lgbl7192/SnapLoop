using System;
using UnityEngine;

namespace Snaploop.Monetization
{
    public enum AdsAdapterMode
    {
        /// <summary>Editor and development builds: test ads. Release builds: LevelPlay if compiled in and configured, otherwise no ads.</summary>
        Auto = 0,
        /// <summary>Fake, clearly labelled test ads. Ignored in release builds (falls back to no ads).</summary>
        TestAds = 1,
        /// <summary>Unity LevelPlay mediation. Needs the SDK package and the SNAPLOOP_LEVELPLAY scripting define.</summary>
        LevelPlay = 2,
        /// <summary>No ads at all. Hints are free.</summary>
        Disabled = 3,
    }

    /// <summary>One LevelPlay app and its ad units.</summary>
    [Serializable]
    public sealed class AdUnitIds
    {
        [Tooltip("LevelPlay app key (LevelPlay dashboard > Apps).")]
        public string appKey = "";
        [Tooltip("Rewarded ad unit id (hints).")]
        public string rewarded = "";
        [Tooltip("Interstitial ad unit id (Next after a win).")]
        public string interstitial = "";
        [Tooltip("Banner ad unit id (level select). Turn auto refresh off for this unit in the dashboard; the game refreshes it.")]
        public string banner = "";

        /// <summary>An app key plus at least one ad unit.</summary>
        public bool IsComplete =>
            !string.IsNullOrEmpty(appKey) &&
            (!string.IsNullOrEmpty(rewarded) || !string.IsNullOrEmpty(interstitial) || !string.IsNullOrEmpty(banner));
    }

    /// <summary>
    /// Every ad/IAP id and ad rule in one asset: <c>Assets/Resources/Monetization/MonetizationConfig.asset</c>.
    /// If the asset is missing, the defaults below are used. The ad rules are checked against the code
    /// defaults in <see cref="AdRules"/>: a value that is out of range or would allow more ads is ignored.
    /// </summary>
    [CreateAssetMenu(menuName = "Snaploop/Monetization Config", fileName = "MonetizationConfig")]
    public sealed class MonetizationConfig : ScriptableObject
    {
        public const string ResourcePath = "Monetization/MonetizationConfig";

        [Header("Ads network")]
        public AdsAdapterMode adapter = AdsAdapterMode.Auto;

        [Tooltip("Release ids for the Android app. Used by release builds only. Empty = LevelPlay is not used.")]
        public AdUnitIds levelPlayAndroid = new AdUnitIds();

        [Tooltip("Ids used by every development build, whatever is set above. Defaults are LevelPlay's public demo ids.")]
        public AdUnitIds levelPlayTestAndroid = new AdUnitIds
        {
            appKey = "25b63cf85",
            rewarded = "syz3d8ekts22q0or",
            interstitial = "h3xw38h9214adgxo",
            banner = "4fpetq4lhe5lsw3e",
        };

        [Header("Placements (reported to the network)")]
        public string hintPlacement = "hint";
        public string nextLevelPlacement = "next_level";
        public string levelSelectBannerPlacement = "level_select";

        [Header("Ad rules (can only be made stricter than the code defaults)")]
        public AdRules adRules = new AdRules();

        [Header("Test ads (editor and development builds only)")]
        [Min(1)] public float testRewardedSeconds = 5f;
        [Min(0)] public float testInterstitialCloseDelaySec = 2f;

        [Header("Remove Ads in-app purchase")]
        [Tooltip("Non-consumable product id. Must match the Google Play Console product id exactly.")]
        public string removeAdsProductId = "remove_ads";

        [Tooltip("Shown until the store returns the localized price.")]
        public string removeAdsFallbackPrice = "$4.99";

        [Tooltip("Editor and development builds use the built-in fake store (no money, no Play account). Turn off to test real Google Play billing from a development build.")]
        public bool fakeStoreInDevelopmentBuilds = true;

        /// <summary>Development builds always use the test ids so a debug build can never serve live ads.</summary>
        public AdUnitIds LevelPlayIds => Debug.isDebugBuild ? levelPlayTestAndroid : levelPlayAndroid;

        public static MonetizationConfig Load()
        {
            var config = Resources.Load<MonetizationConfig>(ResourcePath);
            if (config != null) return config;
            Debug.LogWarning("[Monetization] " + ResourcePath + " not found in Resources; using defaults.");
            return CreateInstance<MonetizationConfig>();
        }
    }
}
