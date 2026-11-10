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

        /// <summary>The win card's rewarded "Claim x3" pays this many times what the clear paid.</summary>
        public const int ClaimMultiplier = 3;

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

        /// <summary>What "Claim x3" adds on top of the clear's own tokens (which the plain claim already pays).</summary>
        public static int TripleBonus(int tokensEarned) => Math.Max(0, tokensEarned) * (ClaimMultiplier - 1);

        /// <summary>Progress toward a token unlock, 0..1 (1 when affordable or free).</summary>
        public static float UnlockProgress(int balance, int cost) =>
            cost <= 0 ? 1f : Math.Min(1f, Math.Max(0f, balance / (float)cost));
    }

    /// <summary>How a hint gets paid for.</summary>
    public enum HintPayment
    {
        /// <summary>Not on offer: too few tokens and no rewarded ad ready. The button is hidden.</summary>
        None,
        Tokens,
        RewardedAd,
    }

    /// <summary>
    /// Hints cost tokens. A player who is short can watch a rewarded ad instead; tokens always come first so
    /// nobody watches an ad for something they can already afford. Remove Ads owners keep the ad option.
    /// </summary>
    public static class HintRules
    {
        /// <summary>About two first clears' worth (5/8/10 per clear), so tokens stay mostly for packs.</summary>
        public const int TokenCost = 15;

        public static HintPayment Choose(int balance, int cost, bool rewardedAdReady)
        {
            if (cost <= 0 || balance >= cost) return HintPayment.Tokens;
            return rewardedAdReady ? HintPayment.RewardedAd : HintPayment.None;
        }
    }

    /// <summary>
    /// The album: every first clear fills the next slot of a page, in clear order, with that clip's thumbnail.
    /// A full page pays <see cref="PageBonus"/> tokens once.
    /// </summary>
    public static class AlbumRules
    {
        public const int SlotsPerPage = 12;
        public const int PageBonus = 30;

        /// <summary>The page shown as "current": the one the next slot goes on (0-based).</summary>
        public static int CurrentPage(int filled) => Math.Max(0, filled) / SlotsPerPage;

        public static int PagesCompleted(int filled) => Math.Max(0, filled) / SlotsPerPage;

        /// <summary>Slots filled on <paramref name="page"/>, 0..<see cref="SlotsPerPage"/>.</summary>
        public static int FilledOnPage(int filled, int page) =>
            Math.Min(SlotsPerPage, Math.Max(0, filled - page * SlotsPerPage));

        /// <summary>Page bonuses owed: completed pages that have not been paid yet.</summary>
        public static int BonusesOwed(int filled, int pagesPaid) => Math.Max(0, PagesCompleted(filled) - Math.Max(0, pagesPaid));
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
