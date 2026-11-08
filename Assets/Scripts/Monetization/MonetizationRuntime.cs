using System;
using System.Collections.Generic;
using Snaploop.Core;
using UnityEngine;

namespace Snaploop.Monetization
{
    /// <summary>
    /// Installs ads and the Remove Ads purchase before the first scene loads, and hosts their
    /// coroutines, main-thread dispatch, app lifecycle callbacks and overlay UI. Gameplay never
    /// references this; it only sees the gates registered on <see cref="GameServices"/>.
    /// </summary>
    public sealed class MonetizationRuntime : MonoBehaviour
    {
        static readonly Queue<Action> mainThreadQueue = new Queue<Action>();

        public static MonetizationRuntime Instance { get; private set; }

        public MonetizationConfig Config { get; private set; }
        public MonetizationOverlay Overlay { get; private set; }
        public IConsentProvider Consent { get; private set; }
        public AdsService Ads { get; private set; }
        public RemoveAdsStore RemoveAds { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
            if (Instance != null) return;
            var go = new GameObject("Monetization");
            DontDestroyOnLoad(go);
            go.AddComponent<MonetizationRuntime>().Setup();
        }

        /// <summary>Run <paramref name="action"/> on the main thread during the next Update. Safe from any thread.</summary>
        public static void Post(Action action)
        {
            lock (mainThreadQueue) mainThreadQueue.Enqueue(action);
        }

        void Setup()
        {
            Instance = this;
            Config = MonetizationConfig.Load();
            Overlay = new MonetizationOverlay(transform);
            Consent = new StubConsentProvider(msg => Overlay.Toast(msg));

            IAdNetwork network = CreateAdNetwork(Config);
            Ads = new AdsService(network, Config, Overlay, Consent, new StubRemoteConfigProvider(), new DebugLogAdTelemetry());
            RemoveAds = new RemoveAdsStore(Config, CreatePurchaseBackend(Config), Overlay);
            Debug.Log("[Monetization] Ads: " + network.Name + ", store: " + RemoveAds.Backend.Name + ", Remove Ads owned: " + Entitlements.RemoveAds);

            GameServices.Hints = new RewardedHintGate(Ads, Config.hintPlacement);
            GameServices.BetweenPuzzles = new InterstitialGate(Ads, Config.nextLevelPlacement);
            Ads.Changed += GameServices.NotifyHintOfferChanged;
            Entitlements.Changed += GameServices.NotifySettingsChanged;
            Consent.Changed += GameServices.NotifySettingsChanged;

            RemoveAds.Initialize();
            GameServices.AddSettingsEntry("Privacy choices",
                () => Consent.ShowPrivacyOptions(null),
                () => Consent.PrivacyOptionsRequired);
            Ads.Start();
            if (Debug.isDebugBuild) RegisterDevMenu();
        }

        IAdNetwork CreateAdNetwork(MonetizationConfig c)
        {
            bool dev = Debug.isDebugBuild;
            switch (c.adapter)
            {
                case AdsAdapterMode.Disabled:
                    return new NoAdNetwork();
                case AdsAdapterMode.TestAds:
                    if (dev) return CreateTestNetwork(c);
                    Debug.LogWarning("[Monetization] Test ads are never used in release builds. Running without ads.");
                    return new NoAdNetwork();
                case AdsAdapterMode.LevelPlay:
                    return new LevelPlayAdNetwork(this, c.levelSelectBannerPlacement);
                default:
                    if (dev) return CreateTestNetwork(c);
                    if (LevelPlayAdNetwork.IsConfigured(c.LevelPlayIds)) return new LevelPlayAdNetwork(this, c.levelSelectBannerPlacement);
                    Debug.LogWarning("[Monetization] No ad network configured for this release build. Running without ads; hints are free.");
                    return new NoAdNetwork();
            }
        }

        TestAdNetwork CreateTestNetwork(MonetizationConfig c) =>
            new TestAdNetwork(this, Overlay, c.testRewardedSeconds, c.testInterstitialCloseDelaySec);

        IPurchaseBackend CreatePurchaseBackend(MonetizationConfig c)
        {
            if (Application.isEditor || (Debug.isDebugBuild && c.fakeStoreInDevelopmentBuilds))
                return new FakeStoreBackend(Overlay, c.removeAdsFallbackPrice);
            return new UnityIapBackend();
        }

        void RegisterDevMenu()
        {
            GameServices.AddDevMenuEntry(
                () => "Remove Ads: " + (Entitlements.RemoveAds ? "owned" : "not owned") + " (tap to toggle)",
                () =>
                {
                    bool own = !Entitlements.RemoveAds;
                    Entitlements.RemoveAds = own;
                    FakeStoreBackend.AccountOwns = own;
                });
            GameServices.AddDevMenuEntry(
                () => "Forget local Remove Ads (test Restore)",
                () => Entitlements.RemoveAds = false);
            GameServices.AddDevMenuEntry(
                () => "Force interstitial on next Next: " + (Ads.ForceNextInterstitial ? "on" : "off"),
                () => Ads.ForceNextInterstitial = !Ads.ForceNextInterstitial);
            GameServices.AddDevMenuEntry(
                () => "Test ads: " + (TestAdNetwork.SimulateNoFill ? "no fill" : "fill"),
                () => TestAdNetwork.SimulateNoFill = !TestAdNetwork.SimulateNoFill);
            GameServices.AddDevMenuEntry(() => Ads.DevLedgerLabel, Ads.DevSkipGrace);
            GameServices.AddDevMenuEntry(() => "Reset ad ledger (fresh install)", Ads.DevResetLedger);
            if (Consent is StubConsentProvider stub)
            {
                GameServices.AddDevMenuEntry(
                    () => "Privacy choices row: " + (stub.PrivacyOptionsRequired ? "shown" : "hidden") + " (consent stub)",
                    () => stub.PrivacyOptionsRequired = !stub.PrivacyOptionsRequired);
            }
        }

        void Update()
        {
            while (true)
            {
                Action next;
                lock (mainThreadQueue)
                {
                    if (mainThreadQueue.Count == 0) break;
                    next = mainThreadQueue.Dequeue();
                }
                try { next(); } catch (Exception e) { Debug.LogException(e); }
            }
            float dt = Time.unscaledDeltaTime;
            Ads.Tick(dt);
            Overlay.Tick(dt);
        }

        void OnApplicationPause(bool paused) => Ads?.OnApplicationPause(paused);

        void OnApplicationQuit() => Ads?.OnApplicationQuit();

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}
