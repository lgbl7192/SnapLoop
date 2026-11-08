using System;
using Snaploop.Levels;
using UnityEngine;

namespace Snaploop.Core
{
    /// <summary>Outcome of a finished puzzle, passed to <see cref="GameEvents.PuzzleCompleted"/>.</summary>
    public struct PuzzleResult
    {
        public LevelInfo Level;
        public int LevelIndex;
        public float ElapsedSec;
        public int HintsUsed;
        public int Stars;
        public bool IsFirstClear;
        /// <summary>True when the win was forced from the dev menu.</summary>
        public bool ForcedByDevMenu;
    }

    /// <summary>
    /// Game-wide notifications. Follow-up systems (ads/IAP, vertical clip capture, analytics)
    /// subscribe here instead of being called directly by gameplay code.
    /// All events are raised on the main thread.
    /// </summary>
    public static class GameEvents
    {
        /// <summary>A puzzle finished loading and its pieces are about to scramble. Args: level, index.</summary>
        public static event Action<LevelInfo, int> PuzzleStarted;

        /// <summary>The player can start dragging (scramble animation finished).</summary>
        public static event Action<LevelInfo, int> PuzzlePlayable;

        /// <summary>A piece locked into its slot. Args: placed count, total count.</summary>
        public static event Action<int, int> PieceSnapped;

        /// <summary>A hint was granted and applied (one piece auto-placed).</summary>
        public static event Action<LevelInfo, int> HintUsed;

        /// <summary>The last piece snapped. Raised before the reveal animation.</summary>
        public static event Action<PuzzleResult> PuzzleCompleted;

        /// <summary>The full uncut loop starts playing (reveal animation begins). Good moment to start a capture tail.</summary>
        public static event Action<LevelInfo, int> RevealStarted;

        /// <summary>Reveal animation finished and the win panel is shown.</summary>
        public static event Action<PuzzleResult> RevealFinished;

        /// <summary>
        /// Player tapped "Next" on the win panel. Args: completed level index, next level index (-1 if none).
        /// The actual transition is gated by <see cref="GameServices.BetweenPuzzles"/> (interstitial slot).
        /// </summary>
        public static event Action<int, int> NextRequested;

        /// <summary>The player left a puzzle without finishing it (back to level select).</summary>
        public static event Action<LevelInfo, int> PuzzleAbandoned;

        internal static void RaisePuzzleStarted(LevelInfo l, int i) => Invoke(PuzzleStarted, l, i);
        internal static void RaisePuzzlePlayable(LevelInfo l, int i) => Invoke(PuzzlePlayable, l, i);
        internal static void RaisePieceSnapped(int placed, int total) => Invoke(PieceSnapped, placed, total);
        internal static void RaiseHintUsed(LevelInfo l, int i) => Invoke(HintUsed, l, i);
        internal static void RaisePuzzleCompleted(PuzzleResult r) => Invoke(PuzzleCompleted, r);
        internal static void RaiseRevealStarted(LevelInfo l, int i) => Invoke(RevealStarted, l, i);
        internal static void RaiseRevealFinished(PuzzleResult r) => Invoke(RevealFinished, r);
        internal static void RaiseNextRequested(int from, int to) => Invoke(NextRequested, from, to);
        internal static void RaisePuzzleAbandoned(LevelInfo l, int i) => Invoke(PuzzleAbandoned, l, i);

        // A throwing subscriber must never break the game loop.
        static void Invoke<T>(Action<T> e, T a)
        {
            if (e == null) return;
            foreach (Action<T> h in e.GetInvocationList())
            {
                try { h(a); } catch (Exception ex) { Debug.LogException(ex); }
            }
        }

        static void Invoke<T1, T2>(Action<T1, T2> e, T1 a, T2 b)
        {
            if (e == null) return;
            foreach (Action<T1, T2> h in e.GetInvocationList())
            {
                try { h(a, b); } catch (Exception ex) { Debug.LogException(ex); }
            }
        }
    }
}
