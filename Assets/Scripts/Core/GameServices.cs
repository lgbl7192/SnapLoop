using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Video;

namespace Snaploop.Core
{
    /// <summary>
    /// Decides whether the player gets a hint. The ads worker replaces the default with a
    /// rewarded-ad implementation; gameplay only ever calls <see cref="RequestHint"/>.
    /// </summary>
    public interface IHintGate
    {
        /// <summary>Label for the hint button, e.g. "Hint" or "Hint (watch ad)".</summary>
        string ButtonLabel { get; }

        /// <summary>Ask for a hint. Call <paramref name="onResult"/> exactly once, on the main thread.</summary>
        void RequestHint(Action<bool> onResult);
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

    sealed class FreeHintGate : IHintGate
    {
        public string ButtonLabel => "Hint";
        public void RequestHint(Action<bool> onResult) => onResult(true);
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
        static IHintGate hints = new FreeHintGate();
        static IBetweenPuzzlesGate betweenPuzzles = new PassThroughGate();

        /// <summary>Hint gate. Default grants every hint for free.</summary>
        public static IHintGate Hints
        {
            get => hints;
            set => hints = value ?? new FreeHintGate();
        }

        /// <summary>Between-puzzles gate (interstitial slot). Default proceeds immediately.</summary>
        public static IBetweenPuzzlesGate BetweenPuzzles
        {
            get => betweenPuzzles;
            set => betweenPuzzles = value ?? new PassThroughGate();
        }

        /// <summary>Optional share provider. The win panel shows a Share button only when this is set and available.</summary>
        public static IShareProvider Share { get; set; }

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
    }
}
