using System;
using System.Collections.Generic;
using Snaploop.Core;
using UnityEngine;

namespace Snaploop.Monetization
{
    /// <summary>
    /// Installs ads and the Remove Ads purchase before the first scene loads, and hosts their
    /// coroutines, main-thread dispatch and overlay UI. Gameplay never references this; it only
    /// sees the gates registered on <see cref="GameServices"/>.
    /// </summary>
    public sealed class MonetizationRuntime : MonoBehaviour
    {
        static readonly Queue<Action> mainThreadQueue = new Queue<Action>();

        public static MonetizationRuntime Instance { get; private set; }

        public MonetizationConfig Config { get; private set; }
        public MonetizationOverlay Overlay { get; private set; }
        public AdsController Ads { get; private set; }
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

            IAdsService network = CreateAdsService(Config);
            Ads = new AdsController(network, Config, Overlay, this);
            RemoveAds = new RemoveAdsStore(Config, CreatePurchaseBackend(Config), Overlay);
            Debug.Log("[Monetization] Ads: " + network.Name + ", store: " + RemoveAds.Backend.Name + ", Remove Ads owned: " + Entitlements.RemoveAds);

            GameServices.Hints = new RewardedHintGate(Ads);
            GameServices.BetweenPuzzles = new InterstitialGate(Ads);
            Entitlements.Changed += GameServices.NotifySettingsChanged;

            Ads.Initialize();
            RemoveAds.Initialize();
            if (Debug.isDebugBuild) RegisterDevMenu();
        }

        IAdsService CreateAdsService(MonetizationConfig c)
        {
            bool dev = Debug.isDebugBuild;
            switch (c.adapter)
            {
                case AdsAdapterMode.Disabled:
                    return new NoAdsService();
                case AdsAdapterMode.TestAds:
                    if (dev) return new TestAdsService(this, Overlay);
                    Debug.LogWarning("[Monetization] Test ads are never used in release builds. Running without ads.");
                    return new NoAdsService();
                case AdsAdapterMode.LevelPlay:
                    return new LevelPlayAdsService(this);
                default:
                    if (dev) return new TestAdsService(this, Overlay);
                    if (LevelPlayAdsService.IsConfigured(c)) return new LevelPlayAdsService(this);
                    Debug.LogWarning("[Monetization] No ad network configured for this release build. Running without ads; hints are free.");
                    return new NoAdsService();
            }
        }

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
                () => "Test ads: " + (TestAdsService.SimulateNoFill ? "no fill" : "fill"),
                () => TestAdsService.SimulateNoFill = !TestAdsService.SimulateNoFill);
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
            Overlay.Tick(Time.unscaledDeltaTime);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}
