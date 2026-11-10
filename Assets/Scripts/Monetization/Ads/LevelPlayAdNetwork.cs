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
    /// <c>SNAPLOOP_LEVELPLAY</c> scripting define for Android, and fill in the release ids in
    /// MonetizationConfig. Without the define this class compiles to a stub that serves nothing,
    /// and release builds run without ads (hints are free). Retries, caps and timing live in
    /// <see cref="AdsService"/>; this class only loads and shows.
    /// </summary>
    public sealed class LevelPlayAdNetwork : IAdNetwork
    {
#if SNAPLOOP_LEVELPLAY
        public const bool IsCompiledIn = true;
#else
        public const bool IsCompiledIn = false;
#endif

        public static bool IsConfigured(AdUnitIds ids) => IsCompiledIn && ids != null && ids.IsComplete;

        AdUnitIds ids;

        public string Name => "LevelPlay";
        public bool ServesAds => IsConfigured(ids);

        public event Action<AdFormat, bool, string> LoadFinished;

#if SNAPLOOP_LEVELPLAY
        /// <summary>LevelPlay can report the reward just after the close event; wait this long before closing the show.</summary>
        const float RewardGraceSec = 0.75f;

        readonly MonoBehaviour host;
        readonly string bannerPlacement;
        LevelPlayRewardedAd rewarded;
        LevelPlayInterstitialAd interstitial;
        LevelPlayBannerAd banner;
        bool bannerLoaded;
        int bannerHeightDp;
        AdShowHandle rewardedShow, interstitialShow;

        public LevelPlayAdNetwork(MonoBehaviour host, string bannerPlacement)
        {
            this.host = host;
            this.bannerPlacement = bannerPlacement;
        }

        public void Initialize(AdUnitIds ids, Action<bool> onDone)
        {
            this.ids = ids;
            if (!IsConfigured(ids)) { onDone(false); return; }
            bool reported = false;
            LevelPlay.OnInitSuccess += _ => MonetizationRuntime.Post(() =>
            {
                CreateFullScreenAds();
                if (!reported) { reported = true; onDone(true); }
            });
            LevelPlay.OnInitFailed += e => MonetizationRuntime.Post(() =>
            {
                Debug.LogWarning("[Ads] LevelPlay init failed: " + e);
                if (!reported) { reported = true; onDone(false); }
            });
            if (Debug.isDebugBuild) LevelPlay.ValidateIntegration();
            LevelPlay.Init(ids.appKey);
        }

        void CreateFullScreenAds()
        {
            if (rewarded == null && !string.IsNullOrEmpty(ids.rewarded))
            {
                rewarded = new LevelPlayRewardedAd(ids.rewarded);
                rewarded.OnAdLoaded += _ => Post(() => LoadFinished?.Invoke(AdFormat.Rewarded, true, null));
                rewarded.OnAdLoadFailed += e => Post(() => LoadFinished?.Invoke(AdFormat.Rewarded, false, e?.ToString()));
                rewarded.OnAdDisplayed += _ => Post(() => rewardedShow?.Displayed());
                rewarded.OnAdRewarded += (_, __) => Post(() => rewardedShow?.Earned());
                rewarded.OnAdClosed += _ => Post(() => host.StartCoroutine(CloseRewardedAfterGrace(rewardedShow)));
                rewarded.OnAdDisplayFailed += (_, e) => Post(() => rewardedShow?.Failed(e?.ToString()));
            }
            if (interstitial == null && !string.IsNullOrEmpty(ids.interstitial))
            {
                interstitial = new LevelPlayInterstitialAd(ids.interstitial);
                interstitial.OnAdLoaded += _ => Post(() => LoadFinished?.Invoke(AdFormat.Interstitial, true, null));
                interstitial.OnAdLoadFailed += e => Post(() => LoadFinished?.Invoke(AdFormat.Interstitial, false, e?.ToString()));
                interstitial.OnAdDisplayed += _ => Post(() => interstitialShow?.Displayed());
                interstitial.OnAdClosed += _ => Post(() => interstitialShow?.Closed());
                interstitial.OnAdDisplayFailed += (_, e) => Post(() => interstitialShow?.Failed(e?.ToString()));
            }
        }

        static void Post(Action a) => MonetizationRuntime.Post(a);

        public bool IsLoaded(AdFormat format)
        {
            switch (format)
            {
                case AdFormat.Rewarded: return rewarded != null && rewarded.IsAdReady();
                case AdFormat.Interstitial: return interstitial != null && interstitial.IsAdReady();
                default: return banner != null && bannerLoaded;
            }
        }

        public void Load(AdFormat format)
        {
            switch (format)
            {
                case AdFormat.Rewarded:
                    if (rewarded == null) LoadFinished?.Invoke(format, false, "no rewarded ad unit");
                    else rewarded.LoadAd();
                    break;
                case AdFormat.Interstitial:
                    if (interstitial == null) LoadFinished?.Invoke(format, false, "no interstitial ad unit");
                    else interstitial.LoadAd();
                    break;
                default:
                    LoadBanner();
                    break;
            }
        }

        public void ShowRewarded(AdShowHandle handle)
        {
            if (!IsLoaded(AdFormat.Rewarded)) { handle.Failed("not loaded"); return; }
            rewardedShow = handle;
            rewarded.ShowAd(string.IsNullOrEmpty(handle.Placement) ? null : handle.Placement);
        }

        public void ShowInterstitial(AdShowHandle handle)
        {
            if (!IsLoaded(AdFormat.Interstitial)) { handle.Failed("not loaded"); return; }
            interstitialShow = handle;
            interstitial.ShowAd(string.IsNullOrEmpty(handle.Placement) ? null : handle.Placement);
        }

        IEnumerator CloseRewardedAfterGrace(AdShowHandle handle)
        {
            float t = 0f;
            while (handle != null && !handle.IsEarned && t < RewardGraceSec)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            handle?.Closed();
        }

        // ------------------------------------------------------------ banner

        void LoadBanner()
        {
            if (string.IsNullOrEmpty(ids.banner)) { LoadFinished?.Invoke(AdFormat.Banner, false, "no banner ad unit"); return; }
            if (banner == null)
            {
                var config = new LevelPlayBannerAd.Config.Builder()
                    .SetSize(LevelPlayAdSize.CreateAdaptiveAdSize())
                    .SetPosition(LevelPlayBannerPosition.BottomCenter)
                    .SetDisplayOnLoad(false)
                    .SetRespectSafeArea(true)
                    .SetPlacementName(string.IsNullOrEmpty(bannerPlacement) ? null : bannerPlacement)
                    .Build();
                banner = new LevelPlayBannerAd(ids.banner, config);
                var b = banner;
                banner.OnAdLoaded += info => Post(() =>
                {
                    if (banner != b) return;
                    bannerLoaded = true;
                    // Refresh is driven by AdsService; the ad unit's own auto refresh should also be off in the dashboard.
                    b.PauseAutoRefresh();
                    var size = info?.AdSize ?? b.GetAdSize();
                    bannerHeightDp = size != null ? size.Height : 50;
                    LoadFinished?.Invoke(AdFormat.Banner, true, null);
                });
                banner.OnAdLoadFailed += e => Post(() =>
                {
                    if (banner == b) LoadFinished?.Invoke(AdFormat.Banner, false, e?.ToString());
                });
            }
            banner.LoadAd();
        }

        public void ShowBanner(string placement)
        {
            if (banner != null && bannerLoaded) banner.ShowAd();
        }

        public void DestroyBanner()
        {
            banner?.DestroyAd();
            banner = null;
            bannerLoaded = false;
            bannerHeightDp = 0;
        }

        public float BannerTopPixels => banner != null && bannerLoaded
            ? bannerHeightDp * DisplayDensity() + Screen.safeArea.yMin
            : 0f;

        static float density;

        static float DisplayDensity()
        {
            if (density > 0f) return density;
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var resources = activity.Call<AndroidJavaObject>("getResources"))
                using (var metrics = resources.Call<AndroidJavaObject>("getDisplayMetrics"))
                    density = metrics.Get<float>("density");
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Ads] Display density lookup failed: " + e.Message);
            }
#endif
            if (density <= 0f) density = Screen.dpi > 0f ? Screen.dpi / 160f : 1f;
            return density;
        }
#else
        public LevelPlayAdNetwork(MonoBehaviour host, string bannerPlacement) { }

        public void Initialize(AdUnitIds ids, Action<bool> onDone)
        {
            this.ids = ids;
            Debug.LogWarning("[Ads] LevelPlay selected but the SDK is not compiled in (missing SNAPLOOP_LEVELPLAY define). No ads will show.");
            onDone(false);
        }

        public bool IsLoaded(AdFormat format) => false;
        public void Load(AdFormat format) => LoadFinished?.Invoke(format, false, "LevelPlay not compiled in");
        public void ShowRewarded(AdShowHandle handle) => handle.Failed("LevelPlay not compiled in");
        public void ShowInterstitial(AdShowHandle handle) => handle.Failed("LevelPlay not compiled in");
        public void ShowBanner(string placement) { }
        public void DestroyBanner() { }
        public float BannerTopPixels => 0f;
#endif
    }
}
