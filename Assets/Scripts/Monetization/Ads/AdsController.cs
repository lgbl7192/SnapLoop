using System;
using System.Collections;
using Snaploop.Core;
using UnityEngine;

namespace Snaploop.Monetization
{
    /// <summary>
    /// The only code that asks the ad network to show anything, and the place the ad rules live:
    /// rewarded ads only through the hint button; interstitials only from the between-puzzles slot,
    /// never while a puzzle is in progress, frequency-capped, and never once Remove Ads is owned.
    /// </summary>
    public sealed class AdsController
    {
        const string WinsKey = "monetization.winsSinceInterstitial";

        readonly IAdsService ads;
        readonly MonetizationConfig config;
        readonly MonetizationOverlay overlay;
        readonly MonoBehaviour host;
        readonly InterstitialPacing pacing;
        bool showing;
        bool audioWasPaused;

        /// <summary>Dev toggle: the next between-puzzles slot ignores the level/win/cooldown caps (not Remove Ads).</summary>
        public bool ForceNextInterstitial { get; set; }

        public IAdsService Network => ads;

        public AdsController(IAdsService ads, MonetizationConfig config, MonetizationOverlay overlay, MonoBehaviour host)
        {
            this.ads = ads;
            this.config = config;
            this.overlay = overlay;
            this.host = host;
            pacing = new InterstitialPacing
            {
                FreeLevels = config.interstitialFreeLevels,
                EveryNthWin = config.interstitialEveryNthWin,
                MinIntervalSec = config.interstitialMinIntervalSec,
                WinsSinceLast = PlayerPrefs.GetInt(WinsKey, 0),
            };
        }

        public void Initialize()
        {
            GameEvents.PuzzleCompleted += OnPuzzleCompleted;
            ads.Initialize(config, ok =>
            {
                Debug.Log("[Monetization] Ads network '" + ads.Name + "' initialized: " + ok);
                if (!ok) return;
                ads.LoadRewarded();
                if (!Entitlements.RemoveAds) ads.LoadInterstitial();
            });
        }

        /// <summary>True when a hint costs a rewarded ad.</summary>
        public bool HintsNeedAd => ads.ServesAds && !(Entitlements.RemoveAds && config.freeHintsWithRemoveAds);

        static double Now => Time.realtimeSinceStartupAsDouble;

        void OnPuzzleCompleted(PuzzleResult r)
        {
            pacing.RecordWin(r.LevelIndex);
            SaveWins();
        }

        void SaveWins()
        {
            PlayerPrefs.SetInt(WinsKey, pacing.WinsSinceLast);
            PlayerPrefs.Save();
        }

        // ------------------------------------------------------------ rewarded (hints only)

        public void RequestRewardedHint(Action<bool> onResult)
        {
            if (!HintsNeedAd) { onResult(true); return; }
            if (showing) { onResult(false); return; }
            if (ads.IsRewardedReady) { ShowRewarded(onResult); return; }
            ads.LoadRewarded();
            host.StartCoroutine(WaitForRewarded(onResult));
        }

        IEnumerator WaitForRewarded(Action<bool> onResult)
        {
            overlay.ShowBusy("Loading ad…");
            float waited = 0f;
            while (waited < config.rewardedLoadWaitSec && !ads.IsRewardedReady)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
            overlay.HideBusy();
            if (ads.IsRewardedReady && !showing) ShowRewarded(onResult);
            else NoRewardedAd(onResult);
        }

        void ShowRewarded(Action<bool> onResult)
        {
            BeginAd();
            ads.ShowRewarded(config.hintPlacement, result =>
            {
                EndAd();
                ads.LoadRewarded();
                switch (result)
                {
                    case AdShowResult.Completed:
                        onResult(true);
                        break;
                    case AdShowResult.Skipped:
                        overlay.Toast("Watch the whole ad to get a hint.");
                        onResult(false);
                        break;
                    default:
                        NoRewardedAd(onResult);
                        break;
                }
            });
        }

        void NoRewardedAd(Action<bool> onResult)
        {
            if (config.grantHintWhenNoAd)
            {
                onResult(true);
                return;
            }
            overlay.Toast("No ad available right now. Try again in a moment.");
            onResult(false);
        }

        // ------------------------------------------------------------ interstitial (between puzzles only)

        public void BeforeNextPuzzle(PuzzleResult completed, Action proceed)
        {
            var decision = pacing.Evaluate(Entitlements.RemoveAds, GameContext.IsPuzzleInProgress, completed.LevelIndex, Now, ForceNextInterstitial);
            if (decision != InterstitialDecision.Show || showing)
            {
                LogDecision(showing ? "skip (another ad is showing)" : decision.ToString());
                proceed();
                return;
            }
            if (!ads.IsInterstitialReady)
            {
                LogDecision("skip (no interstitial loaded)");
                ads.LoadInterstitial();
                proceed();
                return;
            }

            LogDecision("show");
            ForceNextInterstitial = false;
            BeginAd();
            ads.ShowInterstitial(config.betweenPuzzlesPlacement, result =>
            {
                EndAd();
                if (result != AdShowResult.Failed)
                {
                    pacing.RecordShown(Now);
                    SaveWins();
                }
                if (!Entitlements.RemoveAds) ads.LoadInterstitial();
                proceed();
            });
        }

        void LogDecision(string what)
        {
            if (Debug.isDebugBuild)
                Debug.Log("[Monetization] Interstitial: " + what + " (wins since last: " + pacing.WinsSinceLast + ")");
        }

        void BeginAd()
        {
            showing = true;
            audioWasPaused = AudioListener.pause;
            AudioListener.pause = true;
        }

        void EndAd()
        {
            showing = false;
            AudioListener.pause = audioWasPaused;
        }
    }

    /// <summary>Hint button: one rewarded ad per hint.</summary>
    public sealed class RewardedHintGate : IHintGate
    {
        readonly AdsController ads;

        public RewardedHintGate(AdsController ads) => this.ads = ads;

        public string ButtonLabel => ads.HintsNeedAd ? "Hint (watch ad)" : "Hint";

        public void RequestHint(Action<bool> onResult) => ads.RequestRewardedHint(onResult);
    }

    /// <summary>The between-puzzles slot: the only place an interstitial can appear.</summary>
    public sealed class InterstitialGate : IBetweenPuzzlesGate
    {
        readonly AdsController ads;

        public InterstitialGate(AdsController ads) => this.ads = ads;

        public void BeforeNextPuzzle(PuzzleResult completed, Action proceed) => ads.BeforeNextPuzzle(completed, proceed);
    }
}
