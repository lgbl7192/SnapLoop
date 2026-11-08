namespace Snaploop.Monetization
{
    public enum InterstitialDecision
    {
        Show,
        SkipRemoveAdsOwned,
        SkipPuzzleInProgress,
        SkipFreeLevel,
        SkipNotEnoughWins,
        SkipCooldown,
    }

    /// <summary>
    /// Interstitial frequency cap. Pure logic (no Unity calls) so it can be tested outside the engine.
    /// Wins are counted from <c>GameEvents.PuzzleCompleted</c>; the decision is taken in the between-puzzles slot.
    /// </summary>
    public sealed class InterstitialPacing
    {
        public int FreeLevels = 3;
        public int EveryNthWin = 2;
        public double MinIntervalSec = 60.0;

        /// <summary>Wins (outside the free levels) since the last interstitial. Persisted by the caller.</summary>
        public int WinsSinceLast;

        /// <summary>Clock value of the last interstitial, or null if none was shown this session.</summary>
        public double? LastShownAt;

        public void RecordWin(int levelIndex)
        {
            if (levelIndex >= FreeLevels) WinsSinceLast++;
        }

        /// <param name="force">Dev override: skips the level/win/cooldown caps, never the Remove Ads or mid-puzzle rules.</param>
        public InterstitialDecision Evaluate(bool removeAdsOwned, bool puzzleInProgress, int completedLevelIndex, double now, bool force = false)
        {
            if (removeAdsOwned) return InterstitialDecision.SkipRemoveAdsOwned;
            if (puzzleInProgress) return InterstitialDecision.SkipPuzzleInProgress;
            if (force) return InterstitialDecision.Show;
            if (completedLevelIndex < FreeLevels) return InterstitialDecision.SkipFreeLevel;
            if (WinsSinceLast < (EveryNthWin < 1 ? 1 : EveryNthWin)) return InterstitialDecision.SkipNotEnoughWins;
            if (LastShownAt.HasValue && now - LastShownAt.Value < MinIntervalSec) return InterstitialDecision.SkipCooldown;
            return InterstitialDecision.Show;
        }

        public void RecordShown(double now)
        {
            WinsSinceLast = 0;
            LastShownAt = now;
        }
    }
}
