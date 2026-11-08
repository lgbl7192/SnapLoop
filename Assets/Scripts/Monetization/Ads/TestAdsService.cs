using System;
using System.Collections;
using UnityEngine;

namespace Snaploop.Monetization
{
    /// <summary>
    /// Dummy network for the editor and development builds: "loads" after a short delay and shows a
    /// full-screen overlay labelled TEST AD. Never selected in release builds.
    /// </summary>
    public sealed class TestAdsService : IAdsService
    {
        enum Slot { Empty, Loading, Ready }

        /// <summary>Dev toggle: loads fail, as if the network had no fill.</summary>
        public static bool SimulateNoFill;

        const float LoadSeconds = 0.6f;

        readonly MonoBehaviour host;
        readonly MonetizationOverlay overlay;
        MonetizationConfig config;
        Slot rewarded, interstitial;

        public TestAdsService(MonoBehaviour host, MonetizationOverlay overlay)
        {
            this.host = host;
            this.overlay = overlay;
        }

        public string Name => "Test ads";
        public bool ServesAds => true;

        public void Initialize(MonetizationConfig config, Action<bool> onDone)
        {
            this.config = config;
            onDone(true);
        }

        public bool IsRewardedReady => rewarded == Slot.Ready;
        public bool IsInterstitialReady => interstitial == Slot.Ready;

        public void LoadRewarded()
        {
            if (rewarded != Slot.Empty) return;
            rewarded = Slot.Loading;
            host.StartCoroutine(FakeLoad(s => rewarded = s));
        }

        public void LoadInterstitial()
        {
            if (interstitial != Slot.Empty) return;
            interstitial = Slot.Loading;
            host.StartCoroutine(FakeLoad(s => interstitial = s));
        }

        static IEnumerator FakeLoad(Action<Slot> set)
        {
            yield return new WaitForSecondsRealtime(LoadSeconds);
            set(SimulateNoFill ? Slot.Empty : Slot.Ready);
        }

        public void ShowRewarded(string placement, Action<AdShowResult> onDone)
        {
            if (rewarded != Slot.Ready) { onDone(AdShowResult.Failed); return; }
            rewarded = Slot.Empty;
            overlay.ShowTestAd(true, placement, config.testRewardedSeconds, onDone);
        }

        public void ShowInterstitial(string placement, Action<AdShowResult> onDone)
        {
            if (interstitial != Slot.Ready) { onDone(AdShowResult.Failed); return; }
            interstitial = Slot.Empty;
            overlay.ShowTestAd(false, placement, config.testInterstitialCloseDelaySec, onDone);
        }
    }
}
