using System;
using System.Collections;
using UnityEngine;

namespace Snaploop.Monetization
{
    /// <summary>
    /// Dummy network for the editor and development builds: "loads" after a short delay and shows
    /// overlays labelled TEST AD / TEST BANNER. Never selected in release builds.
    /// </summary>
    public sealed class TestAdNetwork : IAdNetwork
    {
        enum Slot { Empty, Loading, Ready }

        /// <summary>Dev toggle: loads fail, as if the network had no fill.</summary>
        public static bool SimulateNoFill;

        const float LoadSeconds = 0.6f;

        readonly MonoBehaviour host;
        readonly MonetizationOverlay overlay;
        readonly float rewardedSeconds, interstitialCloseDelay;
        readonly Slot[] slots = new Slot[3];

        public TestAdNetwork(MonoBehaviour host, MonetizationOverlay overlay, float rewardedSeconds, float interstitialCloseDelay)
        {
            this.host = host;
            this.overlay = overlay;
            this.rewardedSeconds = rewardedSeconds;
            this.interstitialCloseDelay = interstitialCloseDelay;
        }

        public string Name => "Test ads";
        public bool ServesAds => true;

        public event Action<AdFormat, bool, string> LoadFinished;

        public void Initialize(AdUnitIds ids, Action<bool> onDone) => onDone(true);

        public bool IsLoaded(AdFormat format) => slots[(int)format] == Slot.Ready;

        public void Load(AdFormat format)
        {
            // Loading over a shown banner refreshes it in place; a failed refresh keeps the old one.
            bool refresh = format == AdFormat.Banner && slots[(int)format] == Slot.Ready;
            if (!refresh)
            {
                if (slots[(int)format] == Slot.Loading) return;
                slots[(int)format] = Slot.Loading;
            }
            host.StartCoroutine(FakeLoad(format, refresh));
        }

        IEnumerator FakeLoad(AdFormat format, bool refresh)
        {
            yield return new WaitForSecondsRealtime(LoadSeconds);
            bool ok = !SimulateNoFill;
            if (!refresh || ok) slots[(int)format] = ok ? Slot.Ready : Slot.Empty;
            if (format == AdFormat.Banner && ok) overlay.RefreshTestBanner();
            LoadFinished?.Invoke(format, ok, ok ? null : "no fill (simulated)");
        }

        public void ShowRewarded(AdShowHandle handle) => ShowFullScreen(AdFormat.Rewarded, handle, rewardedSeconds);

        public void ShowInterstitial(AdShowHandle handle) => ShowFullScreen(AdFormat.Interstitial, handle, interstitialCloseDelay);

        void ShowFullScreen(AdFormat format, AdShowHandle handle, float seconds)
        {
            if (slots[(int)format] != Slot.Ready) { handle.Failed("not loaded"); return; }
            slots[(int)format] = Slot.Empty;
            overlay.ShowTestAd(handle, seconds);
        }

        public void ShowBanner(string placement)
        {
            if (slots[(int)AdFormat.Banner] == Slot.Ready) overlay.ShowTestBanner(placement);
        }

        public void DestroyBanner()
        {
            slots[(int)AdFormat.Banner] = Slot.Empty;
            overlay.HideTestBanner();
        }

        public float BannerTopPixels => IsLoaded(AdFormat.Banner) ? overlay.TestBannerHeightPixels : 0f;
    }
}
