using System;
using UnityEngine;

namespace Snaploop.Levels
{
    /// <summary>One entry of StreamingAssets/Levels/levels.json. Field names match the JSON schema exactly.</summary>
    [Serializable]
    public sealed class LevelInfo
    {
        public string id;
        public string title;
        public string file;
        public int cols;
        public int rows;
        public float durationSec;
        public string category;
        public string credit;
        public string sourceUrl;
        public string license;
        /// <summary>Optional theme pack id from packs.json. Missing or empty means <see cref="PackInfo.StarterId"/>.</summary>
        public string pack;

        /// <summary>Columns after validation/fallback (see <see cref="DifficultyRamp"/>).</summary>
        [NonSerialized] public int Cols;
        /// <summary>Rows after validation/fallback.</summary>
        [NonSerialized] public int Rows;
        /// <summary>Position in levels.json across all packs.</summary>
        [NonSerialized] public int Index;
        /// <summary><see cref="pack"/> after defaulting to the starter pack.</summary>
        [NonSerialized] public string PackId;
        /// <summary>Position among the levels of its pack (play order inside the pack).</summary>
        [NonSerialized] public int PackIndex;

        public int PieceCount => Cols * Rows;

        public string DisplayTitle => string.IsNullOrEmpty(title) ? id : title;
    }

    [Serializable]
    sealed class LevelManifest
    {
        public LevelInfo[] levels = null;
    }

    /// <summary>
    /// Default grid sizes when a level omits cols/rows, by position in the pack: a sawtooth that climbs for four
    /// levels and eases on every 5th, and follows a big grid with a smaller one, topping out at 6x8.
    /// Grids are portrait (rows >= cols) so pieces stay close to square on 9:16 clips.
    /// </summary>
    public static class DifficultyRamp
    {
        static readonly Vector2Int[] Steps =
        {
            new Vector2Int(3, 3),  // 9
            new Vector2Int(3, 4),  // 12
            new Vector2Int(4, 4),  // 16
            new Vector2Int(4, 5),  // 20
            new Vector2Int(3, 4),  // 12, breather
            new Vector2Int(4, 6),  // 24
            new Vector2Int(5, 6),  // 30
            new Vector2Int(6, 6),  // 36
            new Vector2Int(5, 5),  // 25
            new Vector2Int(4, 5),  // 20, breather
            new Vector2Int(5, 6),  // 30
            new Vector2Int(6, 7),  // 42
            new Vector2Int(5, 5),  // 25
            new Vector2Int(6, 8),  // 48
            new Vector2Int(4, 5),  // 20, breather
        };

        /// <summary>The last levels repeat from here, so long packs keep the late sawtooth.</summary>
        const int LoopFrom = 10;

        public const int MinSide = 2;
        public const int MaxSide = 10;

        /// <summary>Grid for the level at <paramref name="index"/> within its pack.</summary>
        public static Vector2Int ForIndex(int index)
        {
            if (index < Steps.Length) return Steps[Mathf.Max(0, index)];
            return Steps[LoopFrom + (index - Steps.Length) % (Steps.Length - LoopFrom)];
        }
    }
}
