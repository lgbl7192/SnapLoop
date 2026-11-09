using System;
using System.Collections.Generic;
using Snaploop.Levels;
using UnityEngine;

namespace Snaploop.Core
{
    /// <summary>
    /// The local token wallet (PlayerPrefs, so the dev menu's "Reset progress" empties it along with the stars
    /// that earned it). Shown on level select and the pack screen only, never over a puzzle.
    /// </summary>
    public static class Tokens
    {
        const string BalanceKey = "tokens.balance";
        public const int MaxBalance = 999999;

        public static event Action Changed;

        public static int Balance => Mathf.Clamp(PlayerPrefs.GetInt(BalanceKey, 0), 0, MaxBalance);

        public static void Add(int amount)
        {
            if (amount > 0) Set(Balance + amount);
        }

        public static bool TrySpend(int amount)
        {
            if (amount < 0 || Balance < amount) return false;
            Set(Balance - amount);
            return true;
        }

        /// <summary>Pays for a clear (see <see cref="TokenRules.ForClear"/>) and returns what was added.</summary>
        public static int AwardClear(int previousBest, int stars)
        {
            int tokens = TokenRules.ForClear(previousBest, stars);
            Add(tokens);
            return tokens;
        }

        public static void DevReset() => Set(0);

        static void Set(int value)
        {
            PlayerPrefs.SetInt(BalanceKey, Mathf.Clamp(value, 0, MaxBalance));
            PlayerPrefs.Save();
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// Who can play what. A pack is unlocked when it is free, was unlocked with tokens (PlayerPrefs), or is
    /// owned through <see cref="GameServices.PackStore"/> (purchases survive "Reset progress"). Inside an
    /// unlocked pack, levels unlock in order.
    /// </summary>
    public static class PackAccess
    {
        const string TokenUnlockPrefix = "packs.unlocked.";
        const string DevMonthKey = "packs.devMonth";

        public static bool IsUnlocked(PackInfo pack) =>
            pack != null && (pack.IsFree || IsUnlockedWithTokens(pack) || IsBought(pack));

        public static bool IsUnlockedWithTokens(PackInfo pack) =>
            pack != null && PlayerPrefs.GetInt(TokenUnlockPrefix + pack.id, 0) == 1;

        public static bool IsBought(PackInfo pack) =>
            pack != null && pack.CanBuy && GameServices.PackStore != null && GameServices.PackStore.Owns(pack.productId);

        public static bool CanAfford(PackInfo pack) => pack != null && pack.CanUnlockWithTokens && Tokens.Balance >= pack.tokenCost;

        /// <summary>Spends the pack's token cost and unlocks it. False if it can't be unlocked with tokens or the balance is short.</summary>
        public static bool TryUnlockWithTokens(PackInfo pack)
        {
            if (pack == null || !pack.CanUnlockWithTokens || pack.IsEmpty || IsUnlocked(pack)) return false;
            if (!Tokens.TrySpend(pack.tokenCost)) return false;
            SetTokenUnlock(pack, true);
            return true;
        }

        public static bool IsLevelUnlocked(LevelInfo level)
        {
            var pack = PackCatalog.For(level);
            if (!IsUnlocked(pack)) return false;
            if (level.PackIndex <= 0 || Progress.UnlockAll || Progress.IsCleared(level.id)) return true;
            return level.PackIndex - 1 < pack.Levels.Count && Progress.IsCleared(pack.Levels[level.PackIndex - 1].id);
        }

        /// <summary>The level after <paramref name="level"/> in its pack, or null at the end of the pack.</summary>
        public static LevelInfo NextInPack(LevelInfo level)
        {
            var pack = PackCatalog.For(level);
            if (pack == null) return null;
            int i = level.PackIndex + 1;
            return i < pack.Levels.Count ? pack.Levels[i] : null;
        }

        public static int StarsIn(PackInfo pack, out int max)
        {
            int total = 0;
            max = 0;
            if (pack == null) return 0;
            foreach (var l in pack.Levels) total += Progress.GetStars(l.id);
            max = pack.Levels.Count * 3;
            return total;
        }

        public static int ClearedIn(PackInfo pack)
        {
            int n = 0;
            if (pack != null)
                foreach (var l in pack.Levels)
                    if (Progress.IsCleared(l.id)) n++;
            return n;
        }

        /// <summary>Month 1-12 used for seasonal badges: the device clock, unless the dev menu overrides it.</summary>
        public static int CurrentMonth => DevMonthOverride > 0 ? DevMonthOverride : DateTime.Now.Month;

        /// <summary>Dev menu only: 0 follows the device clock.</summary>
        public static int DevMonthOverride
        {
            get => Debug.isDebugBuild ? Mathf.Clamp(PlayerPrefs.GetInt(DevMonthKey, 0), 0, 12) : 0;
            set { PlayerPrefs.SetInt(DevMonthKey, Mathf.Clamp(value, 0, 12)); PlayerPrefs.Save(); }
        }

        public static bool IsFeatured(PackInfo pack) => pack != null && PackRules.IsFeatured(pack.featuredMonths, CurrentMonth);

        /// <summary>Packs as the pack screen lists them: featured (seasonal) packs first, then packs.json order.</summary>
        public static List<PackInfo> InDisplayOrder()
        {
            var list = new List<PackInfo>(PackCatalog.Packs);
            list.Sort((a, b) =>
            {
                int fa = IsFeatured(a) ? 0 : 1, fb = IsFeatured(b) ? 0 : 1;
                return fa != fb ? fa.CompareTo(fb) : a.Index.CompareTo(b.Index);
            });
            return list;
        }

        public static int UnlockedCount()
        {
            int n = 0;
            foreach (var p in PackCatalog.Packs)
                if (IsUnlocked(p)) n++;
            return n;
        }

        /// <summary>Dev menu: token-unlock every pack, or take every token unlock back (purchases are untouched).</summary>
        public static void DevSetAllUnlocked(bool unlocked)
        {
            foreach (var p in PackCatalog.Packs)
                if (!p.IsFree) PlayerPrefs.SetInt(TokenUnlockPrefix + p.id, unlocked ? 1 : 0);
            PlayerPrefs.Save();
            GameServices.NotifyPacksChanged();
        }

        static void SetTokenUnlock(PackInfo pack, bool unlocked)
        {
            PlayerPrefs.SetInt(TokenUnlockPrefix + pack.id, unlocked ? 1 : 0);
            PlayerPrefs.Save();
            GameServices.NotifyPacksChanged();
        }
    }
}
