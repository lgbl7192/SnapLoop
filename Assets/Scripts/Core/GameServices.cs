using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Video;

namespace Snaploop.Core
{
    /// <summary>
    /// Decides whether the player gets a hint and what it costs. The default is <see cref="TokenHintGate"/>;
    /// gameplay only ever calls <see cref="RequestHint"/>.
    /// </summary>
    public interface IHintGate
    {
        /// <summary>Label for the hint button, e.g. "Hint".</summary>
        string ButtonLabel { get; }

        /// <summary>
        /// False hides the hint button (e.g. too few tokens and no rewarded ad loaded). Call
        /// <see cref="GameServices.NotifyHintOfferChanged"/> when this or the label changes.
        /// </summary>
        bool IsOffered { get; }

        /// <summary>True when a hint costs a rewarded ad: the button gets the shared rewarded look with an AD badge.</summary>
        bool IsRewardedAd { get; }

        /// <summary>Tokens the next hint costs; 0 when it is free or paid with an ad. The button shows it as a price tag.</summary>
        int TokenCost { get; }

        /// <summary>Ask for a hint. Call <paramref name="onResult"/> exactly once, on the main thread.</summary>
        void RequestHint(Action<bool> onResult);

        /// <summary>A granted hint could not be applied (nothing left to place): give back what it cost, if anything can be.</summary>
        void Refund();
    }

    /// <summary>
    /// Hints paid with tokens (<see cref="HintRules"/>). When the player is short, <paramref name="adFallback"/>
    /// (the rewarded-ad gate, null without ads) takes over; with neither, the hint is not offered.
    /// </summary>
    public sealed class TokenHintGate : IHintGate
    {
        readonly IHintGate adFallback;
        int lastPaid;

        public TokenHintGate(IHintGate adFallback)
        {
            this.adFallback = adFallback;
        }

        HintPayment Payment => HintRules.Choose(Tokens.Balance, HintRules.TokenCost, adFallback != null && adFallback.IsOffered);

        public string ButtonLabel => Payment == HintPayment.RewardedAd ? adFallback.ButtonLabel : "Hint";
        public bool IsOffered => Payment != HintPayment.None;
        public bool IsRewardedAd => Payment == HintPayment.RewardedAd;
        public int TokenCost => Payment == HintPayment.Tokens ? HintRules.TokenCost : 0;

        public void RequestHint(Action<bool> onResult)
        {
            lastPaid = 0;
            switch (Payment)
            {
                case HintPayment.Tokens:
                    if (!Tokens.TrySpend(HintRules.TokenCost)) { onResult(false); return; }
                    lastPaid = HintRules.TokenCost;
                    onResult(true);
                    return;
                case HintPayment.RewardedAd:
                    adFallback.RequestHint(onResult);
                    return;
                default:
                    onResult(false);
                    return;
            }
        }

        public void Refund()
        {
            if (lastPaid > 0) Tokens.Add(lastPaid);
            lastPaid = 0;
        }
    }

    /// <summary>
    /// Runs between finishing one puzzle and loading the next. This is the only place an
    /// interstitial may be shown; it is never invoked mid-puzzle.
    /// </summary>
    public interface IBetweenPuzzlesGate
    {
        /// <summary>Do any between-puzzle work, then call <paramref name="proceed"/> exactly once.</summary>
        void BeforeNextPuzzle(PuzzleResult completed, Action proceed);
    }

    /// <summary>Share/export action shown on the win panel (vertical clip capture worker).</summary>
    public interface IShareProvider
    {
        bool IsAvailable { get; }
        string ButtonLabel { get; }
        void Share(PuzzleResult result);
    }

    /// <summary>
    /// Sells theme packs as non-consumable in-app products (monetization worker). Without one, packs that
    /// have a product id show no Buy button and unlock with tokens only.
    /// </summary>
    public interface IPackStore
    {
        /// <summary>True when the player owns the product (granted locally, restored from the store on launch).</summary>
        bool Owns(string productId);

        /// <summary>Store price, or a placeholder until the store returns it.</summary>
        string PriceLabel(string productId);

        /// <summary>Runs the purchase flow. <paramref name="onDone"/> gets true when the product is owned afterwards.</summary>
        void Buy(string productId, string title, Action<bool> onDone);
    }

    /// <summary>
    /// The win card's optional rewarded "Claim x3" button, next to the plain Claim. Without one, or while
    /// <see cref="IsOffered"/> is false, the button is not shown at all and only the plain Claim remains.
    /// </summary>
    public interface IClaimTripleOffer
    {
        /// <summary>
        /// Whether to show the offer for a win that paid <paramref name="tokensEarned"/>. Call
        /// <see cref="GameServices.NotifyRewardedOffersChanged"/> when this may have changed (an ad loaded, a cap was reached).
        /// </summary>
        bool IsOffered(int tokensEarned, bool claimedThisWin);

        /// <summary>Plays the rewarded ad. <paramref name="onResult"/> gets true only if it was watched to the end.</summary>
        void Request(Action<bool> onResult);
    }

    /// <summary>An extra row in the Settings screen (e.g. "Remove Ads", "Restore Purchases").</summary>
    public sealed class SettingsEntry
    {
        public string Label;
        public Action OnClick;
        /// <summary>Optional; when it returns false the row is hidden.</summary>
        public Func<bool> IsVisible;
    }

    /// <summary>An extra row in the dev menu. <see cref="Label"/> is re-read after every click.</summary>
    public sealed class DevMenuEntry
    {
        public Func<string> Label;
        public Action OnClick;
    }

    sealed class PassThroughGate : IBetweenPuzzlesGate
    {
        public void BeforeNextPuzzle(PuzzleResult completed, Action proceed) => proceed();
    }

    /// <summary>
    /// Service locator for follow-up systems. Assign from a
    /// <c>[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]</c> method
    /// so the replacements are in place before the first screen is built.
    /// </summary>
    public static class GameServices
    {
        static IHintGate hints = new TokenHintGate(null);
        static IBetweenPuzzlesGate betweenPuzzles = new PassThroughGate();

        /// <summary>Hint gate. Default: hints cost tokens, with no ad fallback.</summary>
        public static IHintGate Hints
        {
            get => hints;
            set => hints = value ?? new TokenHintGate(null);
        }

        /// <summary>Between-puzzles gate (interstitial slot). Default proceeds immediately.</summary>
        public static IBetweenPuzzlesGate BetweenPuzzles
        {
            get => betweenPuzzles;
            set => betweenPuzzles = value ?? new PassThroughGate();
        }

        /// <summary>Raised by <see cref="NotifyHintOfferChanged"/>; the HUD re-reads the hint gate.</summary>
        public static event Action HintOfferChanged;

        public static void NotifyHintOfferChanged() => HintOfferChanged?.Invoke();

        /// <summary>Screen pixels at the bottom of the level select reserved for a banner (0 = none).</summary>
        public static float LevelSelectBottomInset { get; private set; }

        public static event Action LevelSelectBottomInsetChanged;

        /// <summary>
        /// Reserve space at the bottom of the level select, measured in screen pixels from the bottom edge
        /// of the screen. Only set it once a banner has loaded, and back to 0 when it is gone.
        /// </summary>
        public static void SetLevelSelectBottomInset(float screenPixels)
        {
            screenPixels = Mathf.Max(0f, screenPixels);
            if (Mathf.Approximately(screenPixels, LevelSelectBottomInset)) return;
            LevelSelectBottomInset = screenPixels;
            LevelSelectBottomInsetChanged?.Invoke();
        }

        /// <summary>Optional share provider. The win panel shows a Share button only when this is set and available.</summary>
        public static IShareProvider Share { get; set; }

        /// <summary>Optional pack store. Packs are bought through it; null means packs unlock with tokens only.</summary>
        public static IPackStore PackStore { get; set; }

        /// <summary>Raised by <see cref="NotifyPacksChanged"/>: a pack was unlocked, bought, restored or locked again.</summary>
        public static event Action PacksChanged;

        public static void NotifyPacksChanged() => PacksChanged?.Invoke();

        /// <summary>Optional rewarded "Claim x3" offer on the win card. Null hides it.</summary>
        public static IClaimTripleOffer ClaimTriple { get; set; }

        /// <summary>Raised by <see cref="NotifyRewardedOffersChanged"/>; the win card re-reads <see cref="ClaimTriple"/>.</summary>
        public static event Action RewardedOffersChanged;

        public static void NotifyRewardedOffersChanged() => RewardedOffersChanged?.Invoke();

        public static readonly List<SettingsEntry> SettingsEntries = new List<SettingsEntry>();

        public static void AddSettingsEntry(string label, Action onClick, Func<bool> isVisible = null)
        {
            SettingsEntries.Add(new SettingsEntry { Label = label, OnClick = onClick, IsVisible = isVisible });
        }

        /// <summary>Raised by <see cref="NotifySettingsChanged"/>; an open Settings screen rebuilds its rows.</summary>
        public static event Action SettingsChanged;

        /// <summary>Call when a settings row's label or visibility changed outside a click (e.g. a purchase finished).</summary>
        public static void NotifySettingsChanged() => SettingsChanged?.Invoke();

        public static readonly List<DevMenuEntry> DevMenuEntries = new List<DevMenuEntry>();

        /// <summary>
        /// Adds a row to the dev menu. The dev menu also opens in release builds (wordmark taps),
        /// so only register entries that are safe there, or guard with <c>Debug.isDebugBuild</c>.
        /// </summary>
        public static void AddDevMenuEntry(Func<string> label, Action onClick)
        {
            DevMenuEntries.Add(new DevMenuEntry { Label = label, OnClick = onClick });
        }
    }

    /// <summary>
    /// Read-only access to the live rendering objects, for capture/recording code.
    /// Populated by the app at startup; valid while a puzzle is on screen.
    /// </summary>
    public static class GameContext
    {
        /// <summary>The orthographic camera that renders the board and pieces (UI is a separate overlay canvas).</summary>
        public static Camera BoardCamera { get; internal set; }

        /// <summary>The single decoder. Its output is <see cref="VideoTexture"/>.</summary>
        public static VideoPlayer Video { get; internal set; }

        /// <summary>Shared RenderTexture every piece samples. Null until the first clip is prepared.</summary>
        public static Texture VideoTexture { get; internal set; }

        /// <summary>Board (puzzle frame) rectangle in screen pixels, bottom-left origin.</summary>
        public static Rect BoardScreenRect { get; internal set; }

        /// <summary>True from <see cref="GameEvents.PuzzleStarted"/> until the puzzle is completed or abandoned.</summary>
        public static bool IsPuzzleInProgress { get; internal set; }

        /// <summary>True while the level select screen is shown. Changes raise <see cref="GameEvents.LevelSelectVisibilityChanged"/>.</summary>
        public static bool IsLevelSelectVisible { get; internal set; }
    }
}
