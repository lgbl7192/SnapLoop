using System;
using System.Collections.Generic;

namespace Snaploop.Levels
{
    /// <summary>One entry of StreamingAssets/Levels/packs.json. Field names match the JSON schema exactly.</summary>
    [Serializable]
    public sealed class PackInfo
    {
        /// <summary>The free pack every level without a "pack" field belongs to. Always unlocked.</summary>
        public const string StarterId = "starter";

        public string id;
        public string title;
        /// <summary>Tokens to unlock. 0 with no <see cref="productId"/> means free.</summary>
        public int tokenCost;
        /// <summary>Non-consumable in-app product id, or null/empty when the pack can't be bought.</summary>
        public string productId;
        /// <summary>Months (1-12, device clock) in which the pack gets a "Seasonal" badge and sorts first.</summary>
        public int[] featuredMonths;
        /// <summary>Level id whose thumbnail is the cover. Missing or unknown: the pack's first level, or a drawn stub.</summary>
        public string cover;

        /// <summary>Position in packs.json.</summary>
        [NonSerialized] public int Index;
        /// <summary>The pack's levels in play order (levels.json order). Empty until footage is added.</summary>
        [NonSerialized] public List<LevelInfo> Levels = new List<LevelInfo>();

        public string DisplayTitle => string.IsNullOrEmpty(title) ? id : title;
        public bool IsStarter => id == StarterId;
        public bool IsFree => IsStarter || (tokenCost <= 0 && !CanBuy);
        public bool CanUnlockWithTokens => !IsFree && tokenCost > 0;
        public bool CanBuy => !string.IsNullOrEmpty(productId);
        public bool IsEmpty => Levels == null || Levels.Count == 0;

        public LevelInfo CoverLevel
        {
            get
            {
                if (IsEmpty) return null;
                foreach (var l in Levels)
                    if (l.id == cover) return l;
                return Levels[0];
            }
        }
    }

    [Serializable]
    sealed class PackManifest
    {
        public PackInfo[] packs = null;
    }
}
