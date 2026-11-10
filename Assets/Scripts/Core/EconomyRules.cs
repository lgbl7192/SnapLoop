using System;

namespace Snaploop.Core
{
    /// <summary>
    /// Token payouts. Tokens are paid once per star earned on a level: a first clear pays for its stars,
    /// and a replay that beats the best pays only the difference, so replaying for 3 stars tops up to 10.
    /// Pure C#: no Unity calls, unit-tested in Tests/AdPolicy.
    /// </summary>
    public static class TokenRules
    {
        public const int OneStar = 5;
        public const int TwoStars = 8;
        public const int ThreeStars = 10;

        /// <summary>Total tokens a level pays out up to <paramref name="stars"/> best stars (0 = not cleared).</summary>
        public static int ForStars(int stars)
        {
            if (stars >= 3) return ThreeStars;
            if (stars == 2) return TwoStars;
            if (stars == 1) return OneStar;
            return 0;
        }

        /// <summary>Tokens for a clear with <paramref name="stars"/> when the best so far was <paramref name="previousBest"/>.</summary>
        public static int ForClear(int previousBest, int stars) => Math.Max(0, ForStars(stars) - ForStars(previousBest));

        /// <summary>The win card's rewarded offer doubles what the clear paid.</summary>
        public static int DoubledBonus(int tokensEarned) => Math.Max(0, tokensEarned);

        /// <summary>Progress toward a token unlock, 0..1 (1 when affordable or free).</summary>
        public static float UnlockProgress(int balance, int cost) =>
            cost <= 0 ? 1f : Math.Min(1f, Math.Max(0f, balance / (float)cost));
    }

    /// <summary>Seasonal packs are always on sale; their featured months only add a badge and sort them first.</summary>
    public static class PackRules
    {
        public static bool IsFeatured(int[] featuredMonths, int month)
        {
            if (featuredMonths == null) return false;
            foreach (int m in featuredMonths)
                if (m == month) return true;
            return false;
        }
    }
}
