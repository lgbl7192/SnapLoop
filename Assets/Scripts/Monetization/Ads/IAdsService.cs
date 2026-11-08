using System;

namespace Snaploop.Monetization
{
    public enum AdShowResult
    {
        /// <summary>Rewarded: the reward was earned. Interstitial: the ad was shown and closed.</summary>
        Completed,
        /// <summary>The player closed a rewarded ad before earning the reward.</summary>
        Skipped,
        /// <summary>Nothing was shown (not loaded, no fill, display error).</summary>
        Failed,
    }

    /// <summary>
    /// One ad network or mediation SDK. Implementations only load and show; the rules about
    /// when an ad may appear live in <see cref="AdsController"/>, which is the only caller.
    /// Callbacks must be invoked exactly once, on the main thread.
    /// </summary>
    public interface IAdsService
    {
        string Name { get; }

        /// <summary>False when the adapter can never show anything (no SDK, no ids). Hints are then free.</summary>
        bool ServesAds { get; }

        void Initialize(MonetizationConfig config, Action<bool> onDone);

        bool IsRewardedReady { get; }
        bool IsInterstitialReady { get; }

        /// <summary>Start loading if nothing is loaded or loading. Safe to call repeatedly.</summary>
        void LoadRewarded();
        void LoadInterstitial();

        void ShowRewarded(string placement, Action<AdShowResult> onDone);
        void ShowInterstitial(string placement, Action<AdShowResult> onDone);
    }

    /// <summary>No network: nothing loads, nothing shows.</summary>
    public sealed class NoAdsService : IAdsService
    {
        public string Name => "None";
        public bool ServesAds => false;
        public void Initialize(MonetizationConfig config, Action<bool> onDone) => onDone(true);
        public bool IsRewardedReady => false;
        public bool IsInterstitialReady => false;
        public void LoadRewarded() { }
        public void LoadInterstitial() { }
        public void ShowRewarded(string placement, Action<AdShowResult> onDone) => onDone(AdShowResult.Failed);
        public void ShowInterstitial(string placement, Action<AdShowResult> onDone) => onDone(AdShowResult.Failed);
    }
}
