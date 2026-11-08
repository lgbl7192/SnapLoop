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

    /// <summary>
    /// Every ad/IAP id and pacing rule in one asset: <c>Assets/Resources/Monetization/MonetizationConfig.asset</c>.
    /// If the asset is missing, the defaults below are used.
    /// </summary>
    [CreateAssetMenu(menuName = "Snaploop/Monetization Config", fileName = "MonetizationConfig")]
    public sealed class MonetizationConfig : ScriptableObject
    {
        public const string ResourcePath = "Monetization/MonetizationConfig";

        [Header("Ads network")]
        public AdsAdapterMode adapter = AdsAdapterMode.Auto;

        [Tooltip("LevelPlay app key for the Android app (LevelPlay dashboard > Apps). Empty = LevelPlay is not used.")]
        public string levelPlayAppKeyAndroid = "";

        [Tooltip("LevelPlay ad unit id of the rewarded ad unit used for hints.")]
        public string rewardedAdUnitIdAndroid = "";

        [Tooltip("LevelPlay ad unit id of the interstitial ad unit shown between puzzles.")]
        public string interstitialAdUnitIdAndroid = "";

        [Tooltip("Placement names reported to the network (optional, for reporting/capping on the dashboard).")]
        public string hintPlacement = "hint";
        public string betweenPuzzlesPlacement = "between_puzzles";

        [Header("Interstitial pacing (between puzzles only)")]
        [Tooltip("No interstitial after finishing any of the first N levels in play order.")]
        [Min(0)] public int interstitialFreeLevels = 3;

        [Tooltip("At most one interstitial every N wins (counted after the free levels).")]
        [Min(1)] public int interstitialEveryNthWin = 2;

        [Tooltip("Minimum seconds between two interstitials.")]
        [Min(0)] public float interstitialMinIntervalSec = 60f;

        [Header("Rewarded hints")]
        [Tooltip("How long the hint button waits for a rewarded ad to load before giving up.")]
        [Min(0)] public float rewardedLoadWaitSec = 4f;

        [Tooltip("Grant the hint anyway when no rewarded ad can be shown (no fill, offline).")]
        public bool grantHintWhenNoAd = false;

        [Tooltip("Players who bought Remove Ads get hints without watching an ad.")]
        public bool freeHintsWithRemoveAds = true;

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

        public static MonetizationConfig Load()
        {
            var config = Resources.Load<MonetizationConfig>(ResourcePath);
            if (config != null) return config;
            Debug.LogWarning("[Monetization] " + ResourcePath + " not found in Resources; using defaults.");
            return CreateInstance<MonetizationConfig>();
        }
    }
}
