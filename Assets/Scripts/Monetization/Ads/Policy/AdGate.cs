using System;

namespace Snaploop.Monetization
{
    /// <summary>Why an ad was not allowed. <see cref="None"/> means allowed. Logged as the rule name.</summary>
    public enum AdBlock
    {
        None,
        KillSwitch,
        RemoveAds,
        PuzzleInProgress,
        Grace,
        LevelGap,
        TimeGap,
        RewardedCooldown,
        SessionCap,
        DailyCap,
        RageQuitPause,
        Consent,
        NotLoaded,
        NotOnBannerScreen,
        BannerGraceDays,
        BannerGraceSessions,
        NothingToClaim,
        AlreadyClaimed,
    }

    /// <summary>Every input the gates read, passed in so the rules stay pure and testable.</summary>
    public struct AdGateContext
    {
        public AdRules Rules;
        public AdLedger Ledger;
        public long Now;
        public int Today;
        public bool RemoveAdsOwned;
        public bool PuzzleInProgress;
        public bool ConsentAllowsAds;
        public bool AdLoaded;
        public bool OnBannerScreen;
        /// <summary>Dev override for interstitials: skips grace, gaps, caps and the rage-quit pause only.</summary>
        public bool Force;
    }

    /// <summary>
    /// The ad gating rules (Bricksplode strategy, sections 3.3, 4 and 5), checked in order; the first
    /// failing rule blocks. Not implemented yet: the frustration block (Snaploop has no fail state)
    /// and the exploration skip (no analytics yet).
    /// </summary>
    public static class AdGate
    {
        public static bool PastInterstitialGrace(AdRules r, AdLedger l) =>
            l.sessionCount > r.interGraceSessions && l.levelsCleared >= r.interGraceLevels;

        public static AdBlock Interstitial(in AdGateContext c)
        {
            var r = c.Rules;
            var l = c.Ledger;
            if (!r.interEnabled) return AdBlock.KillSwitch;
            if (c.RemoveAdsOwned) return AdBlock.RemoveAds;
            if (c.PuzzleInProgress) return AdBlock.PuzzleInProgress;
            if (!c.Force)
            {
                if (!PastInterstitialGrace(r, l)) return AdBlock.Grace;
                if (l.levelEndsSinceInterstitial < r.interMinLevelEnds) return AdBlock.LevelGap;
                if (l.lastInterstitialAt > 0 && c.Now - l.lastInterstitialAt < r.interMinSeconds) return AdBlock.TimeGap;
                if (l.lastRewardedAt > 0 && c.Now - l.lastRewardedAt < r.interAfterRewardedSeconds) return AdBlock.RewardedCooldown;
                if (l.sessionInterstitials >= r.interMaxPerSession) return AdBlock.SessionCap;
                if (l.DailyInterstitials(c.Today) >= r.interMaxPerDay) return AdBlock.DailyCap;
                if (c.Now < l.rageQuitPausedUntil) return AdBlock.RageQuitPause;
            }
            if (!c.ConsentAllowsAds) return AdBlock.Consent;
            if (!c.AdLoaded) return AdBlock.NotLoaded;
            return AdBlock.None;
        }

        /// <summary>Rewarded ads stay available after Remove Ads (the player opts in).</summary>
        public static AdBlock Rewarded(in AdGateContext c)
        {
            var r = c.Rules;
            var l = c.Ledger;
            if (!r.rewardedEnabled) return AdBlock.KillSwitch;
            if (l.sessionRewarded >= r.rewardedMaxPerSession) return AdBlock.SessionCap;
            if (l.DailyRewarded(c.Today) >= r.rewardedMaxPerDay) return AdBlock.DailyCap;
            if (!c.ConsentAllowsAds) return AdBlock.Consent;
            if (!c.AdLoaded) return AdBlock.NotLoaded;
            return AdBlock.None;
        }

        /// <summary>
        /// The win card's "Claim x3" offer: one rewarded slot per win, only when the win paid tokens and they
        /// haven't been claimed yet. It is an ordinary rewarded ad underneath, so the session and daily caps are
        /// shared with hints, and the interstitial cooldown after a rewarded ad
        /// (<see cref="AdRules.interAfterRewardedSeconds"/>) applies to the Next tap that follows it.
        /// </summary>
        public static AdBlock ClaimTriple(in AdGateContext c, int tokensEarned, bool claimedThisWin)
        {
            var r = c.Rules;
            if (!r.rewardedEnabled || !r.claimTripleEnabled) return AdBlock.KillSwitch;
            if (tokensEarned <= 0) return AdBlock.NothingToClaim;
            if (claimedThisWin) return AdBlock.AlreadyClaimed;
            return Rewarded(c);
        }

        /// <summary>Whether a banner may be requested and shown. "Loaded" is not a gate: an unloaded banner just takes no space.</summary>
        public static AdBlock Banner(in AdGateContext c)
        {
            var r = c.Rules;
            var l = c.Ledger;
            if (!r.bannerEnabled) return AdBlock.KillSwitch;
            if (c.RemoveAdsOwned) return AdBlock.RemoveAds;
            if (!c.OnBannerScreen) return AdBlock.NotOnBannerScreen;
            if (l.DaysSinceInstall(c.Today) < r.bannerGraceDays) return AdBlock.BannerGraceDays;
            if (l.sessionCount <= r.bannerGraceSessions) return AdBlock.BannerGraceSessions;
            if (!c.ConsentAllowsAds) return AdBlock.Consent;
            return AdBlock.None;
        }
    }

    /// <summary>Retry delay after failed loads: 4 s doubling to 3 min, with ±20% jitter.</summary>
    public static class AdBackoff
    {
        public const double BaseSeconds = 4.0;
        public const double MaxSeconds = 180.0;
        public const double Jitter = 0.2;

        /// <param name="failures">Consecutive failures, 1 or more.</param>
        /// <param name="random01">Uniform random number in [0, 1).</param>
        public static double DelaySeconds(int failures, double random01)
        {
            int n = Math.Max(1, Math.Min(failures, 16));
            double d = Math.Min(MaxSeconds, BaseSeconds * Math.Pow(2, n - 1));
            return d * (1.0 - Jitter + 2.0 * Jitter * random01);
        }
    }
}
