using System;
using System.Collections.Generic;
using System.Linq;
using Snaploop.Core;
using Snaploop.Monetization;

namespace Snaploop.Monetization
{
    // IAdNetwork.cs references this Unity-serialized config type; the tests only need the name.
    public sealed class AdUnitIds { }
}

static class AdPolicyTests
{
    static int failures, passes;

    static void Check(bool ok, string what)
    {
        if (ok) passes++;
        else failures++;
        Console.WriteLine((ok ? "PASS " : "FAIL ") + what);
    }

    static void Eq<T>(T expected, T actual, string what) =>
        Check(EqualityComparer<T>.Default.Equals(expected, actual), what + " (expected " + expected + ", got " + actual + ")");

    const long T0 = 1_800_000_000; // a fixed UTC "now"
    const int Day0 = 20000;

    /// <summary>A ledger past every interstitial gate except what the test changes.</summary>
    static AdLedger Veteran()
    {
        var l = new AdLedger();
        l.StartSession(T0 - 10 * 86400, Day0 - 10, 24);
        l.sessionCount = 10;
        l.levelsCleared = 50;
        l.levelEndsSinceInterstitial = 3;
        l.day = Day0;
        return l;
    }

    static AdGateContext Ctx(AdLedger l, AdRules r = null, long now = T0, int today = Day0) => new AdGateContext
    {
        Rules = r ?? new AdRules(),
        Ledger = l,
        Now = now,
        Today = today,
        ConsentAllowsAds = true,
        AdLoaded = true,
        OnBannerScreen = true,
    };

    static int Main()
    {
        RulesTests();
        LedgerTests();
        RageQuitTests();
        InterstitialGateTests();
        RewardedGateTests();
        DoubleTokensGateTests();
        BannerGateTests();
        SimulationTests();
        BackoffTests();
        ShowHandleTests();
        TokenRulesTests();
        Console.WriteLine();
        Console.WriteLine(passes + " passed, " + failures + " failed");
        return failures == 0 ? 0 : 1;
    }

    // ------------------------------------------------------------ config

    static void RulesTests()
    {
        var rejected = new List<string>();
        Action<string, string, string> log = (k, v, why) => rejected.Add(k);

        var d = AdRuleSpecs.RestrictToDefaults(new AdRules(), log);
        Check(rejected.Count == 0, "defaults validate with no rejections");
        Check(AdRuleSpecs.All.All(s => s.Get(d) == s.Get(new AdRules())), "defaults unchanged by validation");
        Eq(20, AdRuleSpecs.All.Length, "every AdRules field has a spec");
        Eq(AdRuleSpecs.All.Length, AdRuleSpecs.All.Select(s => s.Key).Distinct().Count(), "spec keys unique");
        Check(AdRuleSpecs.All.All(s => s.Get(new AdRules()) >= s.Min && s.Get(new AdRules()) <= s.Max), "defaults inside hard bounds");

        var roundTrip = true;
        foreach (var s in AdRuleSpecs.All)
        {
            var r = new AdRules();
            double v = s.IsBool ? 0 : s.Get(r) + 1;
            s.Set(r, v);
            roundTrip &= s.Get(r) == v;
        }
        Check(roundTrip, "spec Get/Set address the right field");

        rejected.Clear();
        var stricter = new AdRules { interMaxPerDay = 5, interMinSeconds = 120, interGraceLevels = 8, bannerEnabled = false, rageQuitCount = 1 };
        var s1 = AdRuleSpecs.RestrictToDefaults(stricter, log);
        Check(s1.interMaxPerDay == 5 && s1.interMinSeconds == 120 && s1.interGraceLevels == 8 && !s1.bannerEnabled && s1.rageQuitCount == 1 && rejected.Count == 0,
            "stricter local values are accepted");

        rejected.Clear();
        var looser = new AdRules { interMaxPerDay = 50, interMinSeconds = 30, interGraceLevels = 0, rewardedMaxPerSession = 9, bannerRefreshSeconds = 30, rageQuitCount = 5 };
        var s2 = AdRuleSpecs.RestrictToDefaults(looser, log);
        Check(s2.interMaxPerDay == 10 && s2.interMinSeconds == 90 && s2.interGraceLevels == 5 && s2.rewardedMaxPerSession == 8 &&
              s2.bannerRefreshSeconds == 60 && s2.rageQuitCount == 2, "looser local values fall back to the defaults");
        Eq(6, rejected.Count, "each looser value is reported");

        rejected.Clear();
        var bad = new AdRules { interMinSeconds = -5, interMaxPerSession = -1, bannerRefreshSeconds = 10 };
        var s3 = AdRuleSpecs.RestrictToDefaults(bad, log);
        Check(s3.interMinSeconds == 90 && s3.interMaxPerSession == 4 && s3.bannerRefreshSeconds == 60, "out-of-range values fall back to the defaults");
        Eq(3, rejected.Count, "out-of-range values are reported");

        var remote = new Dictionary<string, double>
        {
            ["ads_inter_max_per_day"] = 8,       // stricter than local 10: ok
            ["ads_inter_min_seconds"] = 60,      // looser: rejected
            ["ads_inter_enabled"] = 1,           // already on: no-op
            ["ads_banner_enabled"] = 0,          // turn off: ok
            ["ads_rewarded_max_per_day"] = 2.5,  // not an integer
            ["ads_inter_max_per_session"] = double.NaN,
            ["ads_rewarded_enabled"] = 7,        // not a bool
        };
        rejected.Clear();
        var r1 = AdRuleSpecs.Restrict(new AdRules(), s => remote.TryGetValue(s.Key, out var v) ? v : (double?)null, log);
        Check(r1.interMaxPerDay == 8 && r1.interMinSeconds == 90 && r1.interEnabled && !r1.bannerEnabled &&
              r1.rewardedMaxPerDay == 20 && r1.interMaxPerSession == 4 && r1.rewardedEnabled, "remote can only reduce ads");
        Check(rejected.Contains("ads_inter_min_seconds") && rejected.Contains("ads_rewarded_max_per_day") &&
              rejected.Contains("ads_inter_max_per_session") && rejected.Contains("ads_rewarded_enabled"), "bad remote values are reported");

        var localOff = new AdRules { interEnabled = false, interMaxPerDay = 5 };
        var r2 = AdRuleSpecs.Restrict(localOff, s => s.Key == "ads_inter_enabled" ? 1 : s.Key == "ads_inter_max_per_day" ? 8 : (double?)null);
        Check(!r2.interEnabled, "remote cannot turn on a kill switch the local config turned off");
        Eq(5, r2.interMaxPerDay, "remote cannot loosen a stricter local value");
    }

    // ------------------------------------------------------------ ledger

    static void LedgerTests()
    {
        var l = new AdLedger();
        l.StartSession(T0, Day0, 24);
        Check(l.sessionCount == 1 && l.installDay == Day0 && l.installTime == T0 && l.day == Day0, "first session records install time and day");

        l.RecordInterstitialShown(T0 + 10, Day0);
        l.RecordRewardedShown(T0 + 20, Day0);
        Check(l.sessionInterstitials == 1 && l.dailyInterstitials == 1 && l.sessionRewarded == 1 && l.dailyRewarded == 1, "shows are counted per session and day");

        Eq(0, l.DailyInterstitials(Day0 + 1), "daily interstitial count reads 0 after local midnight");
        Eq(0, l.DailyRewarded(Day0 + 1), "daily rewarded count reads 0 after local midnight");
        l.RollDay(Day0 - 1);
        Check(l.day == Day0 && l.dailyInterstitials == 1, "clock moved back a day: no daily reset");
        l.RollDay(Day0 + 1);
        Check(l.day == Day0 + 1 && l.dailyInterstitials == 0 && l.dailyRewarded == 0, "next day: daily counters reset");

        l.StartSession(T0 + 100, Day0 + 1, 24);
        Check(l.sessionCount == 2 && l.sessionInterstitials == 0 && l.sessionRewarded == 0, "new session resets session counters");
        Eq(Day0, l.installDay, "install day kept across sessions");

        var e = new AdLedger();
        e.StartSession(T0, Day0, 24);
        Check(!e.IsSessionExpired(T0 + 3600, 30), "no background recorded: not a new session");
        e.RecordBackground(T0, new AdRules());
        Check(!e.IsSessionExpired(T0 + 29 * 60, 30), "29 min in background: same session");
        Check(e.IsSessionExpired(T0 + 30 * 60, 30), "30 min in background: new session");

        l.RecordLevelEnd(true);
        l.RecordLevelEnd(true);
        Check(l.levelsCleared == 2 && l.levelEndsSinceInterstitial == 2, "wins count as cleared levels and level ends");
        l.RecordInterstitialShown(T0 + 200, Day0 + 1);
        Eq(0, l.levelEndsSinceInterstitial, "an interstitial resets level ends");

        // Clock moved backwards: stored times in the future clamp to now, so cooldowns restart, never shorten.
        var c = Veteran();
        c.lastInterstitialAt = T0 + 5000;
        c.lastRewardedAt = T0 + 5000;
        c.rageQuitPausedUntil = T0 + 30L * 86400;
        c.ClampClock(T0, Day0, 24);
        Check(c.lastInterstitialAt == T0 && c.lastRewardedAt == T0, "future timestamps clamp to now");
        Eq(T0 + 24L * 3600, c.rageQuitPausedUntil, "rage-quit pause capped at its maximum length");
        Eq(AdBlock.TimeGap, AdGate.Interstitial(Ctx(c)), "after a clock rollback the time gap still blocks");

        var j = new AdLedger();
        j.StartSession(T0, Day0, 24);
        j.StartSession(T0 - 86400 * 3, Day0 - 3, 24);
        Check(j.installDay == Day0 - 3 && j.installTime == T0 - 86400 * 3, "install day/time clamp back when the clock is earlier than install");
    }

    static void RageQuitTests()
    {
        var r = new AdRules();

        var l = Veteran();
        l.RecordInterstitialShown(T0, Day0);
        l.RecordInterstitialClosed(T0 + 5);
        l.RecordBackground(T0 + 20, r);
        Check(l.rageQuitStreak == 1 && l.rageQuitPausedUntil == 0, "one quick exit after an interstitial: streak 1, no pause");
        l.RecordBackground(T0 + 25, r);
        Eq(1, l.rageQuitStreak, "the same interstitial is only judged once");

        l.RecordInterstitialShown(T0 + 1000, Day0);
        l.RecordInterstitialClosed(T0 + 1005);
        l.RecordBackground(T0 + 1035, r);
        Check(l.rageQuitPausedUntil == T0 + 1035 + 24 * 3600, "second quick exit in a row: interstitials paused 24 h");
        l.levelEndsSinceInterstitial = 3;
        Eq(AdBlock.RageQuitPause, AdGate.Interstitial(Ctx(l, r, T0 + 5000)), "gate blocks during the pause");
        Eq(AdBlock.None, AdGate.Interstitial(Ctx(l, r, T0 + 1035 + 24 * 3600)), "pause ends after 24 h");

        var k = Veteran();
        k.RecordInterstitialShown(T0, Day0);
        k.RecordInterstitialClosed(T0 + 5);
        k.RecordBackground(T0 + 10, r);
        k.RecordInterstitialShown(T0 + 1000, Day0);
        k.RecordInterstitialClosed(T0 + 1005);
        k.RecordLevelEnd(true);
        Eq(0, k.rageQuitStreak, "finishing a level after an interstitial resets the count");
        k.RecordBackground(T0 + 1010, r);
        Check(k.rageQuitStreak == 0 && k.rageQuitPausedUntil == 0, "leaving after that level is not a rage-quit");

        var m = Veteran();
        m.RecordInterstitialShown(T0, Day0);
        m.RecordInterstitialClosed(T0 + 5);
        m.RecordBackground(T0 + 10, r);
        m.RecordInterstitialShown(T0 + 1000, Day0);
        m.RecordInterstitialClosed(T0 + 1005);
        m.RecordBackground(T0 + 1036, r);
        Check(m.rageQuitStreak == 0 && m.rageQuitPausedUntil == 0, "leaving after 31 s breaks the streak");
    }

    // ------------------------------------------------------------ gates

    static void InterstitialGateTests()
    {
        var r = new AdRules();
        Eq(AdBlock.None, AdGate.Interstitial(Ctx(Veteran())), "veteran with a loaded ad: show");

        var c = Ctx(Veteran(), new AdRules { interEnabled = false });
        c.RemoveAdsOwned = true;
        Eq(AdBlock.KillSwitch, AdGate.Interstitial(c), "kill switch is checked first");
        c = Ctx(Veteran());
        c.RemoveAdsOwned = true;
        Eq(AdBlock.RemoveAds, AdGate.Interstitial(c), "Remove Ads blocks interstitials");
        c = Ctx(Veteran());
        c.PuzzleInProgress = true;
        Eq(AdBlock.PuzzleInProgress, AdGate.Interstitial(c), "never while a puzzle is in progress");

        var g = Veteran(); g.sessionCount = 2;
        Eq(AdBlock.Grace, AdGate.Interstitial(Ctx(g)), "grace: second session blocks");
        g = Veteran(); g.sessionCount = 3; g.levelsCleared = 4;
        Eq(AdBlock.Grace, AdGate.Interstitial(Ctx(g)), "grace: 4 cleared levels blocks");
        g.levelsCleared = 5;
        Eq(AdBlock.None, AdGate.Interstitial(Ctx(g)), "grace: third session and 5 cleared levels passes");

        var lg = Veteran(); lg.levelEndsSinceInterstitial = 2;
        Eq(AdBlock.LevelGap, AdGate.Interstitial(Ctx(lg)), "level gap: 2 level ends blocks");

        var tg = Veteran(); tg.lastInterstitialAt = T0 - 89;
        Eq(AdBlock.TimeGap, AdGate.Interstitial(Ctx(tg)), "time gap: 89 s blocks");
        tg.lastInterstitialAt = T0 - 90;
        Eq(AdBlock.None, AdGate.Interstitial(Ctx(tg)), "time gap: 90 s passes");

        var rc = Veteran(); rc.lastRewardedAt = T0 - 89;
        Eq(AdBlock.RewardedCooldown, AdGate.Interstitial(Ctx(rc)), "rewarded cooldown: 89 s blocks");
        rc.lastRewardedAt = T0 - 90;
        Eq(AdBlock.None, AdGate.Interstitial(Ctx(rc)), "rewarded cooldown: 90 s passes");

        var sc = Veteran(); sc.sessionInterstitials = 4;
        Eq(AdBlock.SessionCap, AdGate.Interstitial(Ctx(sc)), "session cap 4");
        var dc = Veteran(); dc.dailyInterstitials = 10;
        Eq(AdBlock.DailyCap, AdGate.Interstitial(Ctx(dc)), "daily cap 10");
        Eq(AdBlock.None, AdGate.Interstitial(Ctx(dc, r, T0 + 86400, Day0 + 1)), "daily cap clears after local midnight");

        c = Ctx(Veteran());
        c.ConsentAllowsAds = false;
        Eq(AdBlock.Consent, AdGate.Interstitial(c), "no ad before consent");
        c = Ctx(Veteran());
        c.AdLoaded = false;
        Eq(AdBlock.NotLoaded, AdGate.Interstitial(c), "never waits for a load");

        var f = new AdLedger();
        f.StartSession(T0, Day0, 24);
        c = Ctx(f);
        c.Force = true;
        Eq(AdBlock.None, AdGate.Interstitial(c), "dev force skips grace and gaps");
        c.RemoveAdsOwned = true;
        Eq(AdBlock.RemoveAds, AdGate.Interstitial(c), "dev force still respects Remove Ads");
        c.RemoveAdsOwned = false;
        c.AdLoaded = false;
        Eq(AdBlock.NotLoaded, AdGate.Interstitial(c), "dev force still needs a loaded ad");
    }

    static void RewardedGateTests()
    {
        var c = Ctx(Veteran());
        c.RemoveAdsOwned = true;
        Eq(AdBlock.None, AdGate.Rewarded(c), "rewarded stays available after Remove Ads");

        var f = new AdLedger();
        f.StartSession(T0, Day0, 24);
        Eq(AdBlock.None, AdGate.Rewarded(Ctx(f)), "rewarded available from the first session");

        var s = Veteran(); s.sessionRewarded = 8;
        Eq(AdBlock.SessionCap, AdGate.Rewarded(Ctx(s)), "rewarded session cap 8");
        var d = Veteran(); d.dailyRewarded = 20;
        Eq(AdBlock.DailyCap, AdGate.Rewarded(Ctx(d)), "rewarded daily cap 20");
        Eq(AdBlock.None, AdGate.Rewarded(Ctx(d, null, T0 + 86400, Day0 + 1)), "rewarded daily cap clears after midnight");

        Eq(AdBlock.KillSwitch, AdGate.Rewarded(Ctx(Veteran(), new AdRules { rewardedEnabled = false })), "rewarded kill switch");
        c = Ctx(Veteran());
        c.ConsentAllowsAds = false;
        Eq(AdBlock.Consent, AdGate.Rewarded(c), "rewarded waits for consent");
        c = Ctx(Veteran());
        c.AdLoaded = false;
        Eq(AdBlock.NotLoaded, AdGate.Rewarded(c), "rewarded offer hidden while nothing is loaded");
    }

    static void DoubleTokensGateTests()
    {
        Eq(AdBlock.None, AdGate.DoubleTokens(Ctx(Veteran()), 8, false), "double tokens: offered after a win that paid tokens");

        var f = new AdLedger();
        f.StartSession(T0, Day0, 24);
        Eq(AdBlock.None, AdGate.DoubleTokens(Ctx(f), 5, false), "double tokens: available from the first session");

        var c = Ctx(Veteran());
        c.RemoveAdsOwned = true;
        Eq(AdBlock.None, AdGate.DoubleTokens(c, 8, false), "double tokens: still offered after Remove Ads");

        Eq(AdBlock.NothingToDouble, AdGate.DoubleTokens(Ctx(Veteran()), 0, false), "double tokens: hidden when the win paid nothing");
        Eq(AdBlock.AlreadyClaimed, AdGate.DoubleTokens(Ctx(Veteran()), 8, true), "double tokens: once per win");

        Eq(AdBlock.KillSwitch, AdGate.DoubleTokens(Ctx(Veteran(), new AdRules { doubleTokensEnabled = false }), 8, false), "double tokens: own kill switch");
        Eq(AdBlock.KillSwitch, AdGate.DoubleTokens(Ctx(Veteran(), new AdRules { rewardedEnabled = false }), 8, false), "double tokens: rewarded kill switch also hides it");
        c = Ctx(Veteran());
        c.ConsentAllowsAds = false;
        Eq(AdBlock.Consent, AdGate.DoubleTokens(c, 8, false), "double tokens: waits for consent");
        c = Ctx(Veteran());
        c.AdLoaded = false;
        Eq(AdBlock.NotLoaded, AdGate.DoubleTokens(c, 8, false), "double tokens: hidden while nothing is loaded");

        // One ledger for every rewarded placement: hints and double tokens draw on the same caps.
        var shared = Veteran();
        for (int i = 0; i < 7; i++) shared.RecordRewardedShown(T0 - 1000 + i, Day0);
        Eq(AdBlock.None, AdGate.DoubleTokens(Ctx(shared), 8, false), "double tokens: 7 hint ads this session leave one slot");
        shared.RecordRewardedShown(T0 - 500, Day0);
        Eq(AdBlock.SessionCap, AdGate.DoubleTokens(Ctx(shared), 8, false), "double tokens: hint ads count toward the session cap of 8");
        Eq(AdBlock.SessionCap, AdGate.Rewarded(Ctx(shared)), "a double-tokens ad counts toward the hint cap the same way");

        var daily = Veteran();
        daily.dailyRewarded = 19;
        daily.sessionRewarded = 0;
        Eq(AdBlock.None, AdGate.DoubleTokens(Ctx(daily), 8, false), "double tokens: 19 rewarded today leaves one");
        daily.RecordRewardedShown(T0 - 10, Day0);
        Eq(AdBlock.DailyCap, AdGate.DoubleTokens(Ctx(daily), 8, false), "double tokens: shares the daily cap of 20");
        Eq(AdBlock.DailyCap, AdGate.Rewarded(Ctx(daily)), "hints see the double-tokens ad in the daily cap");

        // The rewarded/interstitial spacing: watching double tokens on the win card holds back the interstitial on Next.
        var spacing = Veteran();
        spacing.RecordRewardedShown(T0, Day0);
        Eq(AdBlock.RewardedCooldown, AdGate.Interstitial(Ctx(spacing, null, T0 + 5)), "Next right after double tokens: no interstitial");
        Eq(AdBlock.RewardedCooldown, AdGate.Interstitial(Ctx(spacing, null, T0 + 89)), "89 s after double tokens: still no interstitial");
        Eq(AdBlock.None, AdGate.Interstitial(Ctx(spacing, null, T0 + 90)), "90 s after double tokens: interstitials allowed again");

        var stricter = AdRuleSpecs.Restrict(new AdRules(), s => s.Key == "ads_rewarded_double_tokens_enabled" ? 0 : (double?)null);
        Check(!stricter.doubleTokensEnabled && stricter.rewardedEnabled, "remote can switch off double tokens alone");
        var rejected = new List<string>();
        var looser = AdRuleSpecs.Restrict(new AdRules { doubleTokensEnabled = false },
            s => s.Key == "ads_rewarded_double_tokens_enabled" ? 1 : (double?)null, (k, v, why) => rejected.Add(k));
        Check(!looser.doubleTokensEnabled && rejected.Contains("ads_rewarded_double_tokens_enabled"), "remote cannot turn double tokens back on");

        // A player who chains wins and always takes the offer: never more than the shared caps, never an interstitial within 90 s of one.
        var r = new AdRules();
        var l = Veteran();
        l.sessionRewarded = 0;
        long now = T0;
        int doubles = 0, inters = 0;
        bool spacingViolated = false;
        for (int win = 0; win < 30; win++)
        {
            now += 45;
            l.RecordLevelEnd(true);
            if (AdGate.DoubleTokens(Ctx(l, r, now), 8, false) == AdBlock.None)
            {
                l.RecordRewardedShown(now, Day0);
                doubles++;
                now += 30;
            }
            if (AdGate.Interstitial(Ctx(l, r, now)) == AdBlock.None)
            {
                if (l.lastRewardedAt > 0 && now - l.lastRewardedAt < r.interAfterRewardedSeconds) spacingViolated = true;
                l.RecordInterstitialShown(now, Day0);
                now += 15;
                l.RecordInterstitialClosed(now);
                inters++;
            }
        }
        Eq(8, doubles, "simulation: a session of wins gets at most 8 double-tokens ads");
        Check(!spacingViolated, "simulation: no interstitial within 90 s of a double-tokens ad");
        Console.WriteLine("     (double-tokens simulation: " + doubles + " rewarded, " + inters + " interstitials over 30 wins)");
    }

    static void BannerGateTests()
    {
        var l = Veteran();
        l.installDay = Day0 - 1;
        l.sessionCount = 10;
        Eq(AdBlock.BannerGraceDays, AdGate.Banner(Ctx(l)), "banner: day 2 blocks");
        l.installDay = Day0 - 2;
        l.sessionCount = 5;
        Eq(AdBlock.BannerGraceSessions, AdGate.Banner(Ctx(l)), "banner: day 3 but 5 sessions blocks");
        l.sessionCount = 6;
        Eq(AdBlock.None, AdGate.Banner(Ctx(l)), "banner: day 3 and past 5 sessions shows");

        var c = Ctx(l);
        c.OnBannerScreen = false;
        Eq(AdBlock.NotOnBannerScreen, AdGate.Banner(c), "banner only on level select");
        c = Ctx(l);
        c.RemoveAdsOwned = true;
        Eq(AdBlock.RemoveAds, AdGate.Banner(c), "banner off after Remove Ads");
        Eq(AdBlock.KillSwitch, AdGate.Banner(Ctx(l, new AdRules { bannerEnabled = false })), "banner kill switch");
        c = Ctx(l);
        c.ConsentAllowsAds = false;
        Eq(AdBlock.Consent, AdGate.Banner(c), "banner waits for consent");
    }

    // ------------------------------------------------------------ whole-player simulation

    static void SimulationTests()
    {
        var r = new AdRules();
        var l = new AdLedger();
        var shownAt = new List<(long t, int session, int level)>();
        long now = T0;
        int today = Day0;
        int level = 0;
        bool graceViolated = false;

        for (int session = 1; session <= 12; session++)
        {
            if (session == 7) { today++; now += 12 * 3600; }
            l.StartSession(now, today, r.rageQuitSuppressHours);
            for (int i = 0; i < 25; i++)
            {
                now += 40; // a 40 s puzzle
                l.RecordLevelEnd(true);
                level++;
                var ctx = Ctx(l, r, now, today);
                if (AdGate.Interstitial(ctx) == AdBlock.None)
                {
                    if (session <= 2 || l.levelsCleared < 5) graceViolated = true;
                    l.RecordInterstitialShown(now, today);
                    now += 15;
                    l.RecordInterstitialClosed(now);
                    shownAt.Add((now, session, level));
                }
            }
            l.RecordBackground(now, r);
            now += 3600;
        }

        Check(!graceViolated && shownAt.Count > 0 && shownAt[0].session == 3, "simulation: first interstitial in session 3");
        Check(shownAt.GroupBy(s => s.session).All(g => g.Count() <= 4), "simulation: at most 4 per session");
        bool gaps = true;
        for (int i = 1; i < shownAt.Count; i++)
            gaps &= shownAt[i].level - shownAt[i - 1].level >= 3 && shownAt[i].t - shownAt[i - 1].t >= 90;
        Check(gaps, "simulation: at least 3 levels and 90 s between interstitials");
        Check(shownAt.Count(s => s.session <= 6) <= 10 && shownAt.Count(s => s.session > 6) <= 10, "simulation: at most 10 per day");
        Console.WriteLine("     (simulation showed " + shownAt.Count + " interstitials over 12 sessions / 300 levels)");
    }

    // ------------------------------------------------------------ backoff

    static void BackoffTests()
    {
        bool Near(double a, double b) => Math.Abs(a - b) < 1e-9;
        Check(Near(AdBackoff.DelaySeconds(1, 0.5), 4), "backoff: first retry 4 s");
        Check(Near(AdBackoff.DelaySeconds(2, 0.5), 8), "backoff: doubles");
        Check(Near(AdBackoff.DelaySeconds(6, 0.5), 128), "backoff: 6th failure 128 s");
        Check(Near(AdBackoff.DelaySeconds(7, 0.5), 180), "backoff: capped at 3 min");
        Check(Near(AdBackoff.DelaySeconds(1000, 0.5), 180), "backoff: huge failure counts stay capped");
        Check(Near(AdBackoff.DelaySeconds(1, 0.0), 3.2) && Near(AdBackoff.DelaySeconds(1, 1.0), 4.8), "backoff: jitter is ±20%");
        Check(Near(AdBackoff.DelaySeconds(0, 0.5), 4), "backoff: 0 failures treated as the first");
    }

    // ------------------------------------------------------------ show handle

    static void ShowHandleTests()
    {
        int displayed = 0, finished = 0;
        var h = new AdShowHandle { OnDisplayed = _ => displayed++, OnFinished = _ => finished++ };
        h.Displayed();
        h.Displayed();
        h.Earned();
        h.Closed();
        h.Closed();
        h.Failed("late");
        Check(displayed == 1 && finished == 1 && h.IsEarned && h.Error == null, "show handle: callbacks fire once, late calls ignored");

        var w = new AdShowHandle { OnFinished = _ => finished++ };
        w.Finish("watchdog: never_displayed");
        w.Earned();
        w.Closed();
        Check(!w.IsEarned && w.Error == "watchdog: never_displayed", "show handle: no reward after the watchdog ended it");

        var f = new AdShowHandle();
        f.Failed(null);
        Eq("show failed", f.Error, "show handle: failure without a message");
    }

    // ------------------------------------------------------------ tokens and packs

    static void TokenRulesTests()
    {
        Check(TokenRules.ForStars(1) == 5 && TokenRules.ForStars(2) == 8 && TokenRules.ForStars(3) == 10, "tokens: 1/2/3 stars pay 5/8/10");
        Eq(0, TokenRules.ForStars(0), "tokens: not cleared pays nothing");
        Eq(10, TokenRules.ForStars(4), "tokens: stars above 3 pay like 3");
        Eq(8, TokenRules.ForClear(0, 2), "tokens: first clear with 2 stars pays 8");
        Eq(2, TokenRules.ForClear(2, 3), "tokens: improving 2 to 3 stars pays the difference");
        Eq(5, TokenRules.ForClear(1, 3), "tokens: improving 1 to 3 stars pays the difference");
        Eq(0, TokenRules.ForClear(3, 3), "tokens: replay at the same stars pays nothing");
        Eq(0, TokenRules.ForClear(3, 1), "tokens: replay at fewer stars pays nothing");
        int total = TokenRules.ForClear(0, 1) + TokenRules.ForClear(1, 2) + TokenRules.ForClear(2, 3);
        Eq(10, total, "tokens: 1 then 2 then 3 stars adds up to the 3-star payout");
        Eq(16, 8 + TokenRules.DoubledBonus(8), "tokens: doubling an 8-token clear gives 16");
        Eq(0, TokenRules.DoubledBonus(0), "tokens: doubling nothing gives nothing");

        Check(Math.Abs(TokenRules.UnlockProgress(75, 150) - 0.5f) < 1e-6, "unlock progress: halfway");
        Eq(1f, TokenRules.UnlockProgress(400, 150), "unlock progress: capped at 1");
        Eq(0f, TokenRules.UnlockProgress(-5, 150), "unlock progress: never negative");
        Eq(1f, TokenRules.UnlockProgress(0, 0), "unlock progress: a free pack is complete");

        Check(PackRules.IsFeatured(new[] { 11, 12 }, 12) && PackRules.IsFeatured(new[] { 11, 12 }, 11), "seasonal: featured in its months");
        Check(!PackRules.IsFeatured(new[] { 11, 12 }, 1), "seasonal: not featured outside them");
        Check(!PackRules.IsFeatured(null, 12) && !PackRules.IsFeatured(new int[0], 12), "seasonal: no months means never featured");
    }
}
