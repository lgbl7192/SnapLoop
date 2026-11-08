using System;
using System.Collections;
using UnityEngine;
#if SNAPLOOP_LEVELPLAY
using Unity.Services.LevelPlay;
#endif

namespace Snaploop.Monetization
{
    /// <summary>
    /// Adapter seam for Unity LevelPlay mediation (com.unity.services.levelplay, 9.x API).
    /// The SDK is not part of the project. To turn it on: install the package, add the
    /// <c>SNAPLOOP_LEVELPLAY</c> scripting define for Android, and fill in the app key and
    /// ad unit ids in MonetizationConfig. Without the define this class compiles to a stub
    /// that serves nothing, and release builds run without ads (hints are free).
    /// </summary>
    public sealed class LevelPlayAdsService : IAdsService
    {
#if SNAPLOOP_LEVELPLAY
        public const bool IsCompiledIn = true;
#else
        public const bool IsCompiledIn = false;
#endif

        MonetizationConfig config;

#if SNAPLOOP_LEVELPLAY
        readonly MonoBehaviour host;
        public LevelPlayAdsService(MonoBehaviour host) => this.host = host;
#else
        public LevelPlayAdsService(MonoBehaviour host) { }
#endif

        public string Name => "LevelPlay";

        public static bool IsConfigured(MonetizationConfig c) =>
            IsCompiledIn && !string.IsNullOrEmpty(c.levelPlayAppKeyAndroid) &&
            (!string.IsNullOrEmpty(c.rewardedAdUnitIdAndroid) || !string.IsNullOrEmpty(c.interstitialAdUnitIdAndroid));

        public bool ServesAds => config != null && IsConfigured(config);

#if SNAPLOOP_LEVELPLAY
        /// <summary>LevelPlay can report the reward just after the close event; wait this long before calling it a skip.</summary>
        const float RewardGraceSec = 0.75f;

        LevelPlayRewardedAd rewarded;
        LevelPlayInterstitialAd interstitial;
        bool rewardedLoading, interstitialLoading, rewardEarned;
        int rewardedFailures, interstitialFailures;
        Action<AdShowResult> rewardedDone, interstitialDone;

        public void Initialize(MonetizationConfig config, Action<bool> onDone)
        {
            this.config = config;
            if (!IsConfigured(config)) { onDone(false); return; }
            bool reported = false;
            LevelPlay.OnInitSuccess += _ => MonetizationRuntime.Post(() =>
            {
                CreateAds();
                if (!reported) { reported = true; onDone(true); }
            });
            LevelPlay.OnInitFailed += e => MonetizationRuntime.Post(() =>
            {
                Debug.LogWarning("[Monetization] LevelPlay init failed: " + e);
                if (!reported) { reported = true; onDone(false); }
            });
            if (Debug.isDebugBuild) LevelPlay.ValidateIntegration();
            LevelPlay.Init(config.levelPlayAppKeyAndroid);
        }

        void CreateAds()
        {
            if (rewarded == null && !string.IsNullOrEmpty(config.rewardedAdUnitIdAndroid))
            {
                rewarded = new LevelPlayRewardedAd(config.rewardedAdUnitIdAndroid);
                rewarded.OnAdLoaded += _ => MonetizationRuntime.Post(() => { rewardedLoading = false; rewardedFailures = 0; });
                rewarded.OnAdLoadFailed += e => MonetizationRuntime.Post(() =>
                {
                    rewardedLoading = false;
                    Debug.LogWarning("[Monetization] Rewarded load failed: " + e);
                    host.StartCoroutine(Retry(++rewardedFailures, LoadRewarded));
                });
                rewarded.OnAdRewarded += (_, __) => MonetizationRuntime.Post(() => rewardEarned = true);
                rewarded.OnAdClosed += _ => MonetizationRuntime.Post(() => host.StartCoroutine(FinishRewardedAfterGrace()));
                rewarded.OnAdDisplayFailed += (_, e) => MonetizationRuntime.Post(() =>
                {
                    Debug.LogWarning("[Monetization] Rewarded display failed: " + e);
                    FinishRewarded(AdShowResult.Failed);
                });
            }
            if (interstitial == null && !string.IsNullOrEmpty(config.interstitialAdUnitIdAndroid))
            {
                interstitial = new LevelPlayInterstitialAd(config.interstitialAdUnitIdAndroid);
                interstitial.OnAdLoaded += _ => MonetizationRuntime.Post(() => { interstitialLoading = false; interstitialFailures = 0; });
                interstitial.OnAdLoadFailed += e => MonetizationRuntime.Post(() =>
                {
                    interstitialLoading = false;
                    Debug.LogWarning("[Monetization] Interstitial load failed: " + e);
                    host.StartCoroutine(Retry(++interstitialFailures, LoadInterstitial));
                });
                interstitial.OnAdClosed += _ => MonetizationRuntime.Post(() => FinishInterstitial(AdShowResult.Completed));
                interstitial.OnAdDisplayFailed += (_, e) => MonetizationRuntime.Post(() =>
                {
                    Debug.LogWarning("[Monetization] Interstitial display failed: " + e);
                    FinishInterstitial(AdShowResult.Failed);
                });
            }
        }

        public bool IsRewardedReady => rewarded != null && rewarded.IsAdReady();
        public bool IsInterstitialReady => interstitial != null && interstitial.IsAdReady();

        public void LoadRewarded()
        {
            if (rewarded == null || rewardedLoading || rewarded.IsAdReady()) return;
            rewardedLoading = true;
            rewarded.LoadAd();
        }

        public void LoadInterstitial()
        {
            if (interstitial == null || interstitialLoading || interstitial.IsAdReady()) return;
            interstitialLoading = true;
            interstitial.LoadAd();
        }

        public void ShowRewarded(string placement, Action<AdShowResult> onDone)
        {
            if (!IsRewardedReady || rewardedDone != null) { onDone(AdShowResult.Failed); return; }
            rewardEarned = false;
            rewardedDone = onDone;
            rewarded.ShowAd(string.IsNullOrEmpty(placement) ? null : placement);
        }

        public void ShowInterstitial(string placement, Action<AdShowResult> onDone)
        {
            if (!IsInterstitialReady || interstitialDone != null) { onDone(AdShowResult.Failed); return; }
            interstitialDone = onDone;
            interstitial.ShowAd(string.IsNullOrEmpty(placement) ? null : placement);
        }

        IEnumerator FinishRewardedAfterGrace()
        {
            float t = 0f;
            while (!rewardEarned && t < RewardGraceSec)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            FinishRewarded(rewardEarned ? AdShowResult.Completed : AdShowResult.Skipped);
        }

        void FinishRewarded(AdShowResult result)
        {
            var cb = rewardedDone;
            rewardedDone = null;
            cb?.Invoke(result);
        }

        void FinishInterstitial(AdShowResult result)
        {
            var cb = interstitialDone;
            interstitialDone = null;
            cb?.Invoke(result);
        }

        static IEnumerator Retry(int failures, Action load)
        {
            yield return new WaitForSecondsRealtime(Mathf.Min(60f, Mathf.Pow(2f, Mathf.Min(failures, 6))));
            load();
        }
#else
        public void Initialize(MonetizationConfig config, Action<bool> onDone)
        {
            this.config = config;
            Debug.LogWarning("[Monetization] LevelPlay selected but the SDK is not compiled in (missing SNAPLOOP_LEVELPLAY define). No ads will show.");
            onDone(false);
        }

        public bool IsRewardedReady => false;
        public bool IsInterstitialReady => false;
        public void LoadRewarded() { }
        public void LoadInterstitial() { }
        public void ShowRewarded(string placement, Action<AdShowResult> onDone) => onDone(AdShowResult.Failed);
        public void ShowInterstitial(string placement, Action<AdShowResult> onDone) => onDone(AdShowResult.Failed);
#endif
    }
}
