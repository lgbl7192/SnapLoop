using System;
using System.Globalization;
using System.IO;
using Snaploop.Core;
using UnityEngine;

namespace Snaploop.Monetization
{
    /// <summary>
    /// The only code that asks the ad network to load or show anything. Applies <see cref="AdGate"/> to
    /// every request, keeps the <see cref="AdLedger"/> (persisted to <c>ad-ledger.json</c>), preloads,
    /// retries with backoff, runs the show watchdog, drives the level-select banner, and logs every
    /// decision to <see cref="IAdTelemetry"/>. No ad is requested before consent has finished.
    /// </summary>
    public sealed class AdsService : IAdsService
    {
        const float LoadTimeoutSec = 60f;
        const float WatchdogNotDisplayedSec = 10f;
        const float WatchdogAfterResumeSec = 5f;
        const float WatchdogBackstopSec = 300f;
        const float PollIntervalSec = 0.5f;
        const string LedgerFile = "ad-ledger.json";

        enum SlotState { Idle, Loading, Backoff }

        sealed class Slot
        {
            public SlotState State;
            public int Failures;
            public float Since;
            public float RetryAt;
        }

        readonly IAdNetwork network;
        readonly MonetizationConfig config;
        readonly MonetizationOverlay overlay;
        readonly IConsentProvider consent;
        readonly IRemoteConfigProvider remote;
        readonly IAdTelemetry telemetry;
        readonly AdRules localRules;
        readonly Slot[] slots = { new Slot(), new Slot(), new Slot() };
        readonly System.Random random = new System.Random();
        readonly string ledgerPath;

        AdLedger ledger;
        bool networkStarted, networkReady;
        AdShowHandle current;
        float showStartedAt, displayedAt, resumedAt = -1f;
        bool audioWasPaused;
        bool lastRewardedReady;
        float pollTimer;
        bool bannerShown;
        float bannerRefreshAt;
        AdBlock? lastBannerBlock;

        public AdsService(IAdNetwork network, MonetizationConfig config, MonetizationOverlay overlay,
            IConsentProvider consent, IRemoteConfigProvider remote, IAdTelemetry telemetry)
        {
            this.network = network;
            this.config = config;
            this.overlay = overlay;
            this.consent = consent;
            this.remote = remote;
            this.telemetry = telemetry;
            localRules = AdRuleSpecs.RestrictToDefaults(config.adRules ?? new AdRules(),
                (key, value, why) => telemetry.Log("config_rejected", detail: "source=local key=" + key + " value=" + value + " reason=" + why));
            Rules = localRules;
            ledgerPath = Path.Combine(Application.persistentDataPath, LedgerFile);
            ledger = LoadLedger();
        }

        /// <summary>Active rules: the local config, made stricter by remote config at session start.</summary>
        public AdRules Rules { get; private set; }

        public AdLedger Ledger => ledger;
        public IAdNetwork Network => network;
        public bool ServesAds => network.ServesAds;

        /// <summary>Dev toggle: the next Next-after-win skips grace, gaps, caps and the rage-quit pause (not the kill switch, Remove Ads or consent).</summary>
        public bool ForceNextInterstitial
        {
            get => forceNextInterstitial;
            set
            {
                forceNextInterstitial = value;
                if (value) Load(AdFormat.Interstitial);
            }
        }

        bool forceNextInterstitial;

        public event Action Changed;

        static long Now => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        static int Today => (int)(DateTime.Now.Date.Ticks / TimeSpan.TicksPerDay);
        static float Realtime => Time.realtimeSinceStartup;

        bool CanRequest => networkReady && consent.IsFinished && consent.CanRequestAds;

        // ------------------------------------------------------------ startup and sessions

        public void Start()
        {
            BeginSession("cold_launch");
            GameEvents.PuzzleCompleted += OnPuzzleCompleted;
            GameEvents.LevelSelectVisibilityChanged += _ => UpdateBanner();
            Entitlements.Changed += OnEntitlementsChanged;
            consent.Changed += OnConsentChanged;
            telemetry.Log("consent_requested");
            consent.Gather(() =>
            {
                telemetry.Log("consent_finished", detail: "can_request_ads=" + consent.CanRequestAds +
                    " privacy_options_required=" + consent.PrivacyOptionsRequired);
                OnConsentChanged();
            });
        }

        void BeginSession(string reason)
        {
            ledger.StartSession(Now, Today, localRules.rageQuitSuppressHours);
            remote.ActivateFetched();
            Rules = AdRuleSpecs.Restrict(localRules,
                spec => remote.TryGetNumber(spec.Key, out double v) ? v : (double?)null,
                (key, value, why) => telemetry.Log("config_rejected", detail: "source=remote key=" + key + " value=" + value + " reason=" + why));
            remote.FetchForNextSession();
            SaveLedger();
            telemetry.Log("session_start", detail: "reason=" + reason + " " + LedgerSummary());
            Changed?.Invoke();
        }

        /// <summary>Forwarded from the runtime MonoBehaviour. Pauses caused by a full-screen ad are not sessions.</summary>
        public void OnApplicationPause(bool paused)
        {
            if (current != null)
            {
                if (!paused) resumedAt = Realtime;
                return;
            }
            if (paused)
            {
                ledger.RecordBackground(Now, Rules);
                SaveLedger();
                telemetry.Log("session_background", detail: "rage_quit_streak=" + ledger.rageQuitStreak +
                    (ledger.rageQuitPausedUntil > Now ? " interstitials_paused_until=" + ledger.rageQuitPausedUntil : ""));
                return;
            }
            if (ledger.IsSessionExpired(Now, Rules.sessionTimeoutMinutes))
            {
                BeginSession("resume_after_timeout");
            }
            else
            {
                ledger.lastBackgroundAt = 0;
                SaveLedger();
            }
            Preload();
            UpdateBanner();
        }

        public void OnApplicationQuit()
        {
            if (current != null) return;
            ledger.RecordBackground(Now, Rules);
            SaveLedger();
        }

        void OnConsentChanged()
        {
            if (consent.IsFinished && consent.CanRequestAds && !networkStarted) StartNetwork();
            Changed?.Invoke();
        }

        void StartNetwork()
        {
            networkStarted = true;
            network.LoadFinished += OnLoadFinished;
            network.Initialize(config.LevelPlayIds, ok =>
            {
                networkReady = ok;
                telemetry.Log("network_init", detail: "network=" + network.Name + " ok=" + ok);
                Preload();
                UpdateBanner();
                PollRewarded();
            });
        }

        void OnPuzzleCompleted(PuzzleResult r)
        {
            ledger.RecordLevelEnd(true);
            SaveLedger();
            Preload();
        }

        void OnEntitlementsChanged()
        {
            UpdateBanner();
            PollRewarded();
        }

        // ------------------------------------------------------------ loading

        bool WantsLoaded(AdFormat f)
        {
            switch (f)
            {
                case AdFormat.Rewarded:
                    return Rules.rewardedEnabled;
                case AdFormat.Interstitial:
                    return Rules.interEnabled && !Entitlements.RemoveAds &&
                           (ForceNextInterstitial || AdGate.PastInterstitialGrace(Rules, ledger));
                default:
                    return BannerBlock() == AdBlock.None;
            }
        }

        /// <summary>Keep one rewarded ad loaded always, and one interstitial once past the grace period.</summary>
        void Preload()
        {
            Load(AdFormat.Rewarded);
            Load(AdFormat.Interstitial);
        }

        void Load(AdFormat f)
        {
            if (!CanRequest || !WantsLoaded(f) || network.IsLoaded(f)) return;
            if (slots[(int)f].State != SlotState.Idle) return;
            RequestLoad(f);
        }

        void RequestLoad(AdFormat f)
        {
            var slot = slots[(int)f];
            slot.State = SlotState.Loading;
            slot.Since = Realtime;
            Log("ad_request", f, null, slot.Failures > 0 ? "attempt=" + (slot.Failures + 1) : null);
            network.Load(f);
        }

        void OnLoadFinished(AdFormat f, bool ok, string error)
        {
            var slot = slots[(int)f];
            if (ok)
            {
                string loadTime = slot.State == SlotState.Loading
                    ? "load_ms=" + Mathf.RoundToInt((Realtime - slot.Since) * 1000f).ToString(CultureInfo.InvariantCulture)
                    : null;
                slot.State = SlotState.Idle;
                slot.Failures = 0;
                Log("ad_loaded", f, null, loadTime);
                if (f == AdFormat.Banner) OnBannerLoaded();
                else if (f == AdFormat.Rewarded) PollRewarded();
                return;
            }
            if (slot.State != SlotState.Loading) return;
            Fail(f, error);
        }

        void Fail(AdFormat f, string error)
        {
            var slot = slots[(int)f];
            slot.Failures++;
            double delay = AdBackoff.DelaySeconds(slot.Failures, random.NextDouble());
            slot.State = SlotState.Backoff;
            slot.RetryAt = Realtime + (float)delay;
            Log("ad_load_failed", f, null, "error=" + (error ?? "unknown") + " attempt=" + slot.Failures +
                " retry_in_s=" + delay.ToString("0.0", CultureInfo.InvariantCulture));
        }

        // ------------------------------------------------------------ per frame

        public void Tick(float dt)
        {
            for (int i = 0; i < slots.Length; i++)
            {
                var f = (AdFormat)i;
                var slot = slots[i];
                if (slot.State == SlotState.Loading && Realtime - slot.Since > LoadTimeoutSec)
                {
                    Log("ad_load_timeout", f, null, "after_s=" + LoadTimeoutSec);
                    Fail(f, "timeout");
                }
                else if (slot.State == SlotState.Backoff && Realtime >= slot.RetryAt)
                {
                    slot.State = SlotState.Idle;
                    if (f == AdFormat.Banner) UpdateBanner();
                    else Load(f);
                }
            }

            RunWatchdog();

            pollTimer += dt;
            if (pollTimer < PollIntervalSec) return;
            pollTimer = 0f;
            PollRewarded();
            UpdateBanner();
        }

        void RunWatchdog()
        {
            if (current == null) return;
            float elapsed = Realtime - showStartedAt;
            string why = null;
            if (!current.IsDisplayed && elapsed > WatchdogNotDisplayedSec) why = "never_displayed";
            else if (resumedAt >= 0f && Realtime - resumedAt > WatchdogAfterResumeSec) why = "no_close_after_resume";
            else if (elapsed > WatchdogBackstopSec) why = "backstop";
            if (why == null) return;
            Log("ad_watchdog", current.Format, current.Placement, "reason=" + why);
            current.Finish("watchdog: " + why);
        }

        // ------------------------------------------------------------ rewarded

        AdGateContext Context(AdFormat f) => new AdGateContext
        {
            Rules = Rules,
            Ledger = ledger,
            Now = Now,
            Today = Today,
            RemoveAdsOwned = Entitlements.RemoveAds,
            PuzzleInProgress = GameContext.IsPuzzleInProgress,
            ConsentAllowsAds = consent.IsFinished && consent.CanRequestAds,
            AdLoaded = networkReady && network.IsLoaded(f),
            OnBannerScreen = GameContext.IsLevelSelectVisible,
            Force = f == AdFormat.Interstitial && ForceNextInterstitial,
        };

        AdBlock RewardedBlock() => AdGate.Rewarded(Context(AdFormat.Rewarded));

        public bool IsRewardedReady(string placement) =>
            network.ServesAds && current == null && RewardedBlock() == AdBlock.None;

        bool? lastClaimTripleReady;

        /// <summary>Whether the win card may show "Claim x3" (see <see cref="AdGate.ClaimTriple"/>). Logs offer changes.</summary>
        public bool IsClaimTripleReady(string placement, int tokensEarned, bool claimedThisWin)
        {
            AdBlock? block = !network.ServesAds ? AdBlock.KillSwitch
                : current != null ? (AdBlock?)null
                : AdGate.ClaimTriple(Context(AdFormat.Rewarded), tokensEarned, claimedThisWin);
            bool ready = block == AdBlock.None;
            if (ready != lastClaimTripleReady)
            {
                lastClaimTripleReady = ready;
                Log("rewarded_offer", AdFormat.Rewarded, placement, ready ? "shown" : "hidden reason=" + (block?.ToString() ?? "ShowInProgress"));
            }
            return ready;
        }

        void PollRewarded()
        {
            bool ready = IsRewardedReady(config.hintPlacement);
            if (ready == lastRewardedReady) return;
            lastRewardedReady = ready;
            string reason = ready ? "" : current != null ? " reason=ShowInProgress" : " reason=" + RewardedBlock();
            Log("rewarded_offer", AdFormat.Rewarded, config.hintPlacement, (ready ? "shown" : "hidden") + reason);
            Changed?.Invoke();
        }

        public void ShowRewarded(string placement, Action<bool> onDone)
        {
            var block = current != null ? (AdBlock?)null : RewardedBlock();
            if (block != AdBlock.None)
            {
                Log("ad_gate_blocked", AdFormat.Rewarded, placement, "rule=" + (block?.ToString() ?? "ShowInProgress"));
                overlay.Toast("No ad available right now. Try again in a moment.");
                PollRewarded();
                onDone(false);
                return;
            }

            Log("rewarded_start", AdFormat.Rewarded, placement);
            var show = BeginShow(AdFormat.Rewarded, placement);
            show.OnDisplayed = h =>
            {
                ledger.RecordRewardedShown(Now, Today);
                SaveLedger();
                LogDisplayed(h);
            };
            show.OnFinished = h =>
            {
                EndShow(h);
                if (h.IsEarned && !h.IsDisplayed)
                {
                    ledger.RecordRewardedShown(Now, Today);
                    SaveLedger();
                }
                LogClosed(h);
                if (h.IsEarned) Log("rewarded_earned", h.Format, h.Placement);
                else if (h.IsDisplayed) Log("rewarded_abandoned", h.Format, h.Placement);

                if (!h.IsEarned)
                    overlay.Toast(h.IsDisplayed ? "Watch the whole ad to get the reward." : "The ad couldn't be shown. Try again in a moment.");
                Load(AdFormat.Rewarded);
                PollRewarded();
                onDone(h.IsEarned);
            };
            network.ShowRewarded(show);
        }

        // ------------------------------------------------------------ interstitial

        public void MaybeShowInterstitial(string placement, Action onDone)
        {
            if (current != null)
            {
                Log("ad_gate_blocked", AdFormat.Interstitial, placement, "rule=ShowInProgress");
                onDone();
                return;
            }

            var ctx = Context(AdFormat.Interstitial);
            var block = AdGate.Interstitial(ctx);
            Log("ad_decision", AdFormat.Interstitial, placement,
                "decision=" + (block == AdBlock.None ? "show" : "skip") + " policy=gates_v1" + (ctx.Force ? " forced=true" : "") + " " + LedgerSummary());
            if (block != AdBlock.None)
            {
                Log("ad_gate_blocked", AdFormat.Interstitial, placement, "rule=" + block);
                if (block == AdBlock.NotLoaded) Load(AdFormat.Interstitial);
                onDone();
                return;
            }

            ForceNextInterstitial = false;
            var show = BeginShow(AdFormat.Interstitial, placement);
            show.OnDisplayed = h =>
            {
                ledger.RecordInterstitialShown(Now, Today);
                SaveLedger();
                LogDisplayed(h);
            };
            show.OnFinished = h =>
            {
                EndShow(h);
                if (h.IsDisplayed)
                {
                    ledger.RecordInterstitialClosed(Now);
                    SaveLedger();
                }
                LogClosed(h);
                Load(AdFormat.Interstitial);
                onDone();
            };
            network.ShowInterstitial(show);
        }

        // ------------------------------------------------------------ shared show handling

        AdShowHandle BeginShow(AdFormat f, string placement)
        {
            var handle = new AdShowHandle { Format = f, Placement = placement };
            current = handle;
            showStartedAt = displayedAt = Realtime;
            resumedAt = -1f;
            audioWasPaused = AudioListener.pause;
            AudioListener.pause = true;
            PollRewarded();
            return handle;
        }

        void EndShow(AdShowHandle h)
        {
            if (current != h) return;
            current = null;
            AudioListener.pause = audioWasPaused;
        }

        void LogDisplayed(AdShowHandle h)
        {
            displayedAt = Realtime;
            Log("ad_displayed", h.Format, h.Placement);
        }

        void LogClosed(AdShowHandle h)
        {
            if (h.Error != null)
            {
                Log("ad_show_failed", h.Format, h.Placement, "error=" + h.Error + " displayed=" + h.IsDisplayed);
                return;
            }
            string onScreen = "s_on_screen=" + (Realtime - displayedAt).ToString("0.0", CultureInfo.InvariantCulture);
            Log("ad_closed", h.Format, h.Placement, h.Format == AdFormat.Rewarded ? onScreen + " earned=" + h.IsEarned : onScreen);
        }

        // ------------------------------------------------------------ banner (level select only)

        AdBlock BannerBlock()
        {
            if (!network.ServesAds) return AdBlock.KillSwitch;
            if (current != null) return AdBlock.NotOnBannerScreen;
            return AdGate.Banner(Context(AdFormat.Banner));
        }

        void UpdateBanner()
        {
            var block = BannerBlock();
            if (block != lastBannerBlock)
            {
                lastBannerBlock = block;
                if (block != AdBlock.None) Log("ad_gate_blocked", AdFormat.Banner, config.levelSelectBannerPlacement, "rule=" + block);
            }

            if (block != AdBlock.None || !CanRequest)
            {
                if (bannerShown || network.IsLoaded(AdFormat.Banner) || slots[(int)AdFormat.Banner].State == SlotState.Loading)
                    DestroyBanner();
                return;
            }

            if (!network.IsLoaded(AdFormat.Banner)) Load(AdFormat.Banner);
            else if (!bannerShown) ShowLoadedBanner();
            else if (Realtime >= bannerRefreshAt) RefreshBanner();
        }

        void ShowLoadedBanner()
        {
            network.ShowBanner(config.levelSelectBannerPlacement);
            bannerShown = true;
            bannerRefreshAt = Realtime + Rules.bannerRefreshSeconds;
            GameServices.SetLevelSelectBottomInset(network.BannerTopPixels);
            Log("banner_shown", AdFormat.Banner, config.levelSelectBannerPlacement);
        }

        /// <summary>The app drives refresh (strategy section 5): only while level select is visible and the app is in front.</summary>
        void RefreshBanner()
        {
            bannerRefreshAt = Realtime + Rules.bannerRefreshSeconds;
            if (slots[(int)AdFormat.Banner].State != SlotState.Idle) return;
            RequestLoad(AdFormat.Banner);
        }

        void OnBannerLoaded()
        {
            if (bannerShown) GameServices.SetLevelSelectBottomInset(network.BannerTopPixels);
            else UpdateBanner();
        }

        void DestroyBanner()
        {
            network.DestroyBanner();
            var slot = slots[(int)AdFormat.Banner];
            if (slot.State == SlotState.Loading) slot.State = SlotState.Idle;
            if (bannerShown) Log("banner_hidden", AdFormat.Banner, config.levelSelectBannerPlacement, "rule=" + lastBannerBlock);
            bannerShown = false;
            GameServices.SetLevelSelectBottomInset(0f);
        }

        // ------------------------------------------------------------ ledger persistence

        AdLedger LoadLedger()
        {
            try
            {
                if (File.Exists(ledgerPath))
                {
                    var loaded = JsonUtility.FromJson<AdLedger>(File.ReadAllText(ledgerPath));
                    if (loaded != null) return loaded;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Ads] Could not read " + LedgerFile + ", starting a new ledger: " + e.Message);
            }
            return new AdLedger();
        }

        void SaveLedger()
        {
            try
            {
                File.WriteAllText(ledgerPath, JsonUtility.ToJson(ledger));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Ads] Could not save " + LedgerFile + ": " + e.Message);
            }
        }

        string LedgerSummary()
        {
            long now = Now;
            return "session=" + ledger.sessionCount +
                   " days_since_install=" + ledger.DaysSinceInstall(Today) +
                   " levels_cleared=" + ledger.levelsCleared +
                   " level_ends_since_inter=" + ledger.levelEndsSinceInterstitial +
                   " s_since_inter=" + (ledger.lastInterstitialAt > 0 ? (now - ledger.lastInterstitialAt).ToString(CultureInfo.InvariantCulture) : "never") +
                   " s_since_rewarded=" + (ledger.lastRewardedAt > 0 ? (now - ledger.lastRewardedAt).ToString(CultureInfo.InvariantCulture) : "never") +
                   " session_inters=" + ledger.sessionInterstitials +
                   " daily_inters=" + ledger.DailyInterstitials(Today);
        }

        void Log(string evt, AdFormat f, string placement = null, string detail = null) =>
            telemetry.Log(evt, FormatName(f), placement, detail);

        static string FormatName(AdFormat f) =>
            f == AdFormat.Rewarded ? "rewarded" : f == AdFormat.Interstitial ? "interstitial" : "banner";

        // ------------------------------------------------------------ dev menu

        public string DevLedgerLabel =>
            "Ad ledger: session " + ledger.sessionCount + ", cleared " + ledger.levelsCleared +
            ", day " + (ledger.DaysSinceInstall(Today) + 1) + " (tap: skip grace)";

        /// <summary>Dev: make this install look past every grace period and gap.</summary>
        public void DevSkipGrace()
        {
            ledger.sessionCount = Math.Max(ledger.sessionCount, Math.Max(Rules.interGraceSessions, Rules.bannerGraceSessions) + 1);
            ledger.levelsCleared = Math.Max(ledger.levelsCleared, Rules.interGraceLevels);
            ledger.levelEndsSinceInterstitial = Math.Max(ledger.levelEndsSinceInterstitial, Rules.interMinLevelEnds);
            ledger.installDay = Math.Min(ledger.installDay, Today - Rules.bannerGraceDays);
            ledger.lastInterstitialAt = 0;
            ledger.lastRewardedAt = 0;
            ledger.rageQuitPausedUntil = 0;
            SaveLedger();
            Preload();
            UpdateBanner();
            overlay.Toast("Ad grace periods skipped.");
        }

        /// <summary>Dev: forget all ad history, as on a fresh install (a new session starts).</summary>
        public void DevResetLedger()
        {
            ledger = new AdLedger();
            DestroyBanner();
            BeginSession("dev_reset");
            UpdateBanner();
            overlay.Toast("Ad ledger reset: fresh install.");
        }
    }

    /// <summary>
    /// The hint's ad option, used by <see cref="TokenHintGate"/> when the player is short of tokens: one rewarded ad
    /// per hint, not offered while no ad is ready, a cap is reached, or there is no ad network. Remove Ads keeps it.
    /// </summary>
    public sealed class RewardedHintGate : IHintGate
    {
        readonly AdsService ads;
        readonly string placement;

        public RewardedHintGate(AdsService ads, string placement)
        {
            this.ads = ads;
            this.placement = placement;
        }

        bool AdsAvailable => ads.ServesAds && ads.Rules.rewardedEnabled;

        public string ButtonLabel => "Get a hint";
        public bool IsOffered => AdsAvailable && ads.IsRewardedReady(placement);
        public bool IsRewardedAd => true;
        public int TokenCost => 0;

        public void RequestHint(Action<bool> onResult)
        {
            if (!AdsAvailable) { onResult(false); return; }
            ads.ShowRewarded(placement, onResult);
        }

        public void Refund() { }
    }

    /// <summary>The win card's "Claim x3" rewarded offer: <see cref="AdGate.ClaimTriple"/> plus a loaded ad.</summary>
    public sealed class ClaimTripleOffer : IClaimTripleOffer
    {
        readonly AdsService ads;
        readonly string placement;

        public ClaimTripleOffer(AdsService ads, string placement)
        {
            this.ads = ads;
            this.placement = placement;
        }

        public bool IsOffered(int tokensEarned, bool claimedThisWin) => ads.IsClaimTripleReady(placement, tokensEarned, claimedThisWin);

        public void Request(Action<bool> onResult) => ads.ShowRewarded(placement, onResult);
    }

    /// <summary>Next after a win: the only place an interstitial can appear.</summary>
    public sealed class InterstitialGate : IBetweenPuzzlesGate
    {
        readonly AdsService ads;
        readonly string placement;

        public InterstitialGate(AdsService ads, string placement)
        {
            this.ads = ads;
            this.placement = placement;
        }

        public void BeforeNextPuzzle(PuzzleResult completed, Action proceed) => ads.MaybeShowInterstitial(placement, proceed);
    }
}
