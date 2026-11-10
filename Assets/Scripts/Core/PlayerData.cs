using UnityEngine;

namespace Snaploop.Core
{
    /// <summary>Player-facing toggles, persisted in PlayerPrefs.</summary>
    public static class GameSettings
    {
        const string SoundKey = "settings.sound";
        const string HapticsKey = "settings.haptics";
        const string OnboardingKey = "onboarding.seen";
        const string DevFreeDragKey = "dev.freeDrag";

        public static bool SoundOn
        {
            get => PlayerPrefs.GetInt(SoundKey, 1) == 1;
            set { PlayerPrefs.SetInt(SoundKey, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        public static bool HapticsOn
        {
            get => PlayerPrefs.GetInt(HapticsKey, 1) == 1;
            set { PlayerPrefs.SetInt(HapticsKey, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        public static bool OnboardingSeen
        {
            get => PlayerPrefs.GetInt(OnboardingKey, 0) == 1;
            set { PlayerPrefs.SetInt(OnboardingKey, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        /// <summary>Dev menu: puzzles use the old free-drag movement instead of grid swapping (from the next level).</summary>
        public static bool DevFreeDrag
        {
            get => PlayerPrefs.GetInt(DevFreeDragKey, 0) == 1;
            set { PlayerPrefs.SetInt(DevFreeDragKey, value ? 1 : 0); PlayerPrefs.Save(); }
        }
    }

    /// <summary>Per-level stars and unlocks, keyed by level id so reordering levels.json keeps progress.</summary>
    public static class Progress
    {
        const string StarsPrefix = "progress.stars.";
        const string UnlockAllKey = "progress.unlockAll";

        /// <summary>0 = not cleared, 1-3 = best stars.</summary>
        public static int GetStars(string levelId) => PlayerPrefs.GetInt(StarsPrefix + levelId, 0);

        public static bool IsCleared(string levelId) => GetStars(levelId) > 0;

        /// <summary>Records a clear. Returns true if this was the first clear of the level.</summary>
        public static bool RecordClear(string levelId, int stars)
        {
            int prev = GetStars(levelId);
            if (stars > prev)
            {
                PlayerPrefs.SetInt(StarsPrefix + levelId, Mathf.Clamp(stars, 1, 3));
                PlayerPrefs.Save();
            }
            return prev == 0;
        }

        /// <summary>Dev toggle: every level is playable regardless of progress.</summary>
        public static bool UnlockAll
        {
            get => PlayerPrefs.GetInt(UnlockAllKey, 0) == 1;
            set { PlayerPrefs.SetInt(UnlockAllKey, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        public static void ResetAll()
        {
            bool sound = GameSettings.SoundOn, haptics = GameSettings.HapticsOn;
            PlayerPrefs.DeleteAll();
            GameSettings.SoundOn = sound;
            GameSettings.HapticsOn = haptics;
        }

        /// <summary>
        /// Stars for a finished puzzle: start at 3, lose one for using any hint, lose one for
        /// taking longer than par (6 s per piece). Never below 1.
        /// </summary>
        public static int ComputeStars(int pieceCount, float elapsedSec, int hintsUsed)
        {
            int stars = 3;
            if (hintsUsed > 0) stars--;
            if (elapsedSec > pieceCount * 6f) stars--;
            return Mathf.Max(1, stars);
        }
    }
}
