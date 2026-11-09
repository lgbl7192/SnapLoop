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
    /// Default grid sizes when a level omits cols/rows: 9-16 pieces early, ramping to ~36.
    /// Grids are portrait (rows >= cols) so pieces stay close to square on 9:16 clips.
    /// </summary>
    public static class DifficultyRamp
    {
        static readonly Vector2Int[] Steps =
        {
            new Vector2Int(3, 3),  // 9
            new Vector2Int(3, 4),  // 12
            new Vector2Int(3, 5),  // 15
            new Vector2Int(4, 5),  // 20
            new Vector2Int(4, 6),  // 24
            new Vector2Int(4, 7),  // 28
            new Vector2Int(5, 7),  // 35
            new Vector2Int(4, 9),  // 36
        };

        public const int MinSide = 2;
        public const int MaxSide = 10;

        /// <summary>Grid for the level at <paramref name="index"/> within its pack; ramps one step every 3 levels, then holds.</summary>
        public static Vector2Int ForIndex(int index)
        {
            int step = Mathf.Clamp(index / 3, 0, Steps.Length - 1);
            if (index == 0) step = 0;
            return Steps[step];
        }
    }
}
