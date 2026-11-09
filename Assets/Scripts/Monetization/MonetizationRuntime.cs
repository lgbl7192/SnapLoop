using System;
using System.Collections.Generic;
using Snaploop.Core;
using Snaploop.Levels;
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
        public PurchaseService Purchases { get; private set; }
        public RemoveAdsStore RemoveAds { get; private set; }
        public PackStore Packs { get; private set; }

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
            Purchases = new PurchaseService(Config, CreatePurchaseBackend(Config), Overlay);
            RemoveAds = new RemoveAdsStore(Config, Purchases);
            Packs = new PackStore(Config, Purchases);
            Debug.Log("[Monetization] Ads: " + network.Name + ", store: " + Purchases.Backend.Name + ", Remove Ads owned: " + Entitlements.RemoveAds +
                      ", pack products owned: " + Entitlements.OwnedProductCount);

            GameServices.Hints = new RewardedHintGate(Ads, Config.hintPlacement);
            GameServices.BetweenPuzzles = new InterstitialGate(Ads, Config.nextLevelPlacement);
            GameServices.DoubleTokens = new DoubleTokensOffer(Ads, Config.doubleTokensPlacement);
            GameServices.PackStore = Packs;
            Ads.Changed += GameServices.NotifyHintOfferChanged;
            Ads.Changed += GameServices.NotifyRewardedOffersChanged;
            Entitlements.Changed += GameServices.NotifySettingsChanged;
            Entitlements.Changed += GameServices.NotifyPacksChanged;
            Consent.Changed += GameServices.NotifySettingsChanged;

            RemoveAds.AddSettingsRows();
            Purchases.Initialize();
            // Pack product ids come from packs.json, which the app reads after the first scene loads.
            if (PackCatalog.IsLoaded) Packs.RegisterProducts();
            else PackCatalog.Loaded += Packs.RegisterProducts;
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
                return new FakeStoreBackend(Overlay, c.removeAdsProductId);
            return new UnityIapBackend();
        }

        void RegisterDevMenu()
        {
            var fake = Purchases.Backend as FakeStoreBackend;
            GameServices.AddDevMenuEntry(
                () => "Remove Ads: " + (Entitlements.RemoveAds ? "owned" : "not owned") + " (tap to toggle)",
                () =>
                {
                    bool own = !Entitlements.RemoveAds;
                    Entitlements.RemoveAds = own;
                    fake?.SetAccountOwns(Config.removeAdsProductId, own);
                });
            GameServices.AddDevMenuEntry(
                () => "Forget local Remove Ads (test Restore)",
                () => Entitlements.RemoveAds = false);
            GameServices.AddDevMenuEntry(
                () => "Bought packs: " + Entitlements.OwnedProductCount + " on this device (tap: forget, test Restore)",
                Entitlements.ForgetProducts);
            if (fake != null)
            {
                GameServices.AddDevMenuEntry(
                    () => "Test store account: owns " + fake.AccountOwnedCount(id => !Purchases.IsRemoveAds(id)) + " pack(s) (tap: clear)",
                    () =>
                    {
                        foreach (var p in fake.Products)
                            if (!Purchases.IsRemoveAds(p.Id)) fake.SetAccountOwns(p.Id, false);
                    });
            }
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
