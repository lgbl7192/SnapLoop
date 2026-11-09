using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Snaploop.Levels
{
    /// <summary>
    /// Loads StreamingAssets/Levels/packs.json and groups the loaded levels by their "pack" field.
    /// Load it after <see cref="LevelCatalog"/>. A missing or broken packs.json leaves just the starter pack,
    /// so the game still plays every level without a pack field.
    /// </summary>
    public static class PackCatalog
    {
        public const string ManifestName = "packs.json";
        public const int MaxTokenCost = 100000;

        static readonly List<PackInfo> packs = new List<PackInfo>();

        /// <summary>Packs in packs.json order. The starter pack is always present.</summary>
        public static IReadOnlyList<PackInfo> Packs => packs;
        public static bool IsLoaded { get; private set; }
        public static string LoadError { get; private set; }

        /// <summary>Raised once packs.json has been read (or failed) and the levels are grouped.</summary>
        public static event Action Loaded;

        public static PackInfo Starter => Get(PackInfo.StarterId);

        public static PackInfo Get(string id)
        {
            foreach (var p in packs)
                if (p.id == id) return p;
            return null;
        }

        /// <summary>The pack a level belongs to, or null if its "pack" names no pack in packs.json.</summary>
        public static PackInfo For(LevelInfo level) => level == null ? null : Get(level.PackId);

        public static IEnumerator Load()
        {
            packs.Clear();
            LoadError = null;
            string json = null;
            yield return LevelCatalog.ReadText(ManifestName, (text, error) => { json = text; LoadError = error; });

            var warnings = new List<string>();
            if (json != null && !TryParse(json, packs, warnings, out string parseError)) LoadError = parseError;
            Link(packs, LevelCatalog.Levels, warnings);
            foreach (var w in warnings) Debug.LogWarning("[Snaploop] " + ManifestName + ": " + w);
            if (LoadError != null) Debug.LogError("[Snaploop] " + LoadError + " Only the starter pack is available.");
            IsLoaded = true;
            Loaded?.Invoke();
        }

        /// <summary>Parses and validates packs.json into <paramref name="target"/>. Shared with the editor validator.</summary>
        public static bool TryParse(string json, List<PackInfo> target, List<string> warnings, out string error)
        {
            error = null;
            PackManifest manifest;
            try { manifest = JsonUtility.FromJson<PackManifest>(json); }
            catch (Exception e) { error = ManifestName + " is not valid JSON: " + e.Message; return false; }
            if (manifest == null || manifest.packs == null)
            {
                error = ManifestName + " has no \"packs\" array.";
                return false;
            }

            var ids = new HashSet<string>();
            var products = new HashSet<string>();
            foreach (var p in manifest.packs)
            {
                if (p == null || string.IsNullOrWhiteSpace(p.id))
                {
                    warnings.Add("A pack has no \"id\"; skipped.");
                    continue;
                }
                p.id = p.id.Trim();
                if (!ids.Add(p.id))
                {
                    warnings.Add("Duplicate pack id \"" + p.id + "\"; skipped.");
                    continue;
                }
                if (p.IsStarter && (p.tokenCost != 0 || !string.IsNullOrEmpty(p.productId)))
                {
                    warnings.Add("The starter pack is always free; its tokenCost and productId are ignored.");
                    p.tokenCost = 0;
                    p.productId = null;
                }
                if (p.tokenCost < 0 || p.tokenCost > MaxTokenCost)
                {
                    warnings.Add(p.id + ": tokenCost " + p.tokenCost + " is out of range; the pack can only be bought.");
                    p.tokenCost = 0;
                }
                if (!string.IsNullOrEmpty(p.productId))
                {
                    p.productId = p.productId.Trim();
                    if (!products.Add(p.productId))
                    {
                        warnings.Add(p.id + ": productId \"" + p.productId + "\" is used by another pack; this pack can't be bought.");
                        p.productId = null;
                    }
                }
                if (!p.IsStarter && p.tokenCost == 0 && string.IsNullOrEmpty(p.productId))
                    warnings.Add(p.id + ": no tokenCost and no productId, so the pack is free.");
                if (p.featuredMonths != null)
                    foreach (int m in p.featuredMonths)
                        if (m < 1 || m > 12) warnings.Add(p.id + ": featuredMonths has " + m + "; months are 1-12.");
                target.Add(p);
            }
            return true;
        }

        /// <summary>
        /// Adds the starter pack if packs.json left it out, then gives each pack its levels in levels.json order.
        /// Levels whose "pack" isn't in packs.json are left out of every pack (and so can't be played from the UI).
        /// </summary>
        public static void Link(List<PackInfo> target, IReadOnlyList<LevelInfo> levels, List<string> warnings)
        {
            if (!target.Exists(p => p.IsStarter))
                target.Insert(0, new PackInfo { id = PackInfo.StarterId, title = "Starter" });
            for (int i = 0; i < target.Count; i++)
            {
                target[i].Index = i;
                target[i].Levels = new List<LevelInfo>();
            }

            var unknown = new Dictionary<string, int>();
            foreach (var l in levels)
            {
                var pack = target.Find(p => p.id == l.PackId);
                if (pack != null) pack.Levels.Add(l);
                else unknown[l.PackId] = unknown.TryGetValue(l.PackId, out int n) ? n + 1 : 1;
            }
            foreach (var kv in unknown)
                warnings.Add(kv.Value + " level(s) use pack \"" + kv.Key + "\", which packs.json doesn't list; they are hidden until it does.");
            foreach (var p in target)
            {
                if (!string.IsNullOrEmpty(p.cover) && !p.IsEmpty && !p.Levels.Exists(l => l.id == p.cover))
                    warnings.Add(p.id + ": cover \"" + p.cover + "\" is not one of its levels; using its first level.");
            }
        }
    }
}
