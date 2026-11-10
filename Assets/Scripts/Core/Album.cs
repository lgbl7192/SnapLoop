using System;
using System.Collections.Generic;
using UnityEngine;

namespace Snaploop.Core
{
    /// <summary>What a clear did to the album, for the win card.</summary>
    public struct AlbumFill
    {
        /// <summary>False when the clear filled nothing (a replay, or the level was already in the album).</summary>
        public bool Filled;
        public int Page;
        /// <summary>0-based slot on <see cref="Page"/>.</summary>
        public int Slot;
        public bool PageCompleted;
        /// <summary>Tokens the full page paid (already in the wallet).</summary>
        public int Bonus;
    }

    /// <summary>
    /// The album (see <see cref="AlbumRules"/>), saved in PlayerPrefs as level ids in fill order, so the dev menu's
    /// "Reset progress" empties it along with the stars and tokens.
    /// </summary>
    public static class Album
    {
        const string SlotsKey = "album.slots";
        const string PagesPaidKey = "album.pagesPaid";
        const char Separator = '|';

        public static event Action Changed;

        /// <summary>Level ids in the order they filled their slots.</summary>
        public static List<string> Slots()
        {
            var list = new List<string>();
            string raw = PlayerPrefs.GetString(SlotsKey, "");
            if (raw.Length == 0) return list;
            foreach (var id in raw.Split(Separator))
                if (id.Length > 0) list.Add(id);
            return list;
        }

        public static int Filled => Slots().Count;

        public static int CurrentPage => AlbumRules.CurrentPage(Filled);

        public static bool Contains(string levelId) => Slots().Contains(levelId);

        /// <summary>Level id in a slot, or null while it's empty.</summary>
        public static string At(int page, int slot)
        {
            var slots = Slots();
            int i = page * AlbumRules.SlotsPerPage + slot;
            return i >= 0 && i < slots.Count ? slots[i] : null;
        }

        /// <summary>Fills the next slot with <paramref name="levelId"/> (once per level) and pays a completed page's bonus.</summary>
        public static AlbumFill RecordClear(string levelId)
        {
            if (string.IsNullOrEmpty(levelId) || levelId.IndexOf(Separator) >= 0) return default;
            var slots = Slots();
            if (slots.Contains(levelId)) return default;
            slots.Add(levelId);
            int index = slots.Count - 1;
            var fill = new AlbumFill
            {
                Filled = true,
                Page = index / AlbumRules.SlotsPerPage,
                Slot = index % AlbumRules.SlotsPerPage,
            };
            PlayerPrefs.SetString(SlotsKey, string.Join(Separator.ToString(), slots));

            int paid = PlayerPrefs.GetInt(PagesPaidKey, 0);
            int owed = AlbumRules.BonusesOwed(slots.Count, paid);
            if (owed > 0)
            {
                fill.PageCompleted = true;
                fill.Bonus = owed * AlbumRules.PageBonus;
                PlayerPrefs.SetInt(PagesPaidKey, paid + owed);
            }
            PlayerPrefs.Save();
            if (fill.Bonus > 0) Tokens.Add(fill.Bonus);
            Changed?.Invoke();
            return fill;
        }

        public static void NotifyChanged() => Changed?.Invoke();
    }
}
