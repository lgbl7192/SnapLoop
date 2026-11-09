using System;

namespace Snaploop.Monetization
{
    /// <summary>
    /// Every tunable ad number. The field initializers are the shipped defaults and also the ceiling:
    /// local config and remote config may only move a value in the "fewer ads" direction
    /// (see <see cref="AdRuleSpecs.Restrict"/>). Raising ad volume needs a code change here.
    /// Pure C#: no Unity calls, so it can be tested outside the engine.
    /// </summary>
    [Serializable]
    public sealed class AdRules
    {
        public bool interEnabled = true;
        public int interGraceSessions = 2;
        public int interGraceLevels = 5;
        public int interMinLevelEnds = 3;
        public int interMinSeconds = 90;
        public int interAfterRewardedSeconds = 90;
        public int interMaxPerSession = 4;
        public int interMaxPerDay = 10;
        public int rageQuitWindowSeconds = 30;
        public int rageQuitCount = 2;
        public int rageQuitSuppressHours = 24;
        public int sessionTimeoutMinutes = 30;

        public bool rewardedEnabled = true;
        public int rewardedMaxPerSession = 8;
        public int rewardedMaxPerDay = 20;
        /// <summary>The win card's "Double tokens" offer. It shares the rewarded caps above.</summary>
        public bool doubleTokensEnabled = true;

        public bool bannerEnabled = true;
        public int bannerGraceDays = 2;
        public int bannerGraceSessions = 5;
        public int bannerRefreshSeconds = 60;

        public AdRules Clone() => (AdRules)MemberwiseClone();
    }

    /// <summary>Key, range and direction of every <see cref="AdRules"/> field.</summary>
    public static class AdRuleSpecs
    {
        public sealed class Spec
        {
            public string Key;
            public bool IsBool;
            /// <summary>Hard bounds. The usable range is further cut at the ceiling value on the "more ads" side.</summary>
            public double Min, Max;
            /// <summary>True for gaps, grace periods and cooldowns; false for caps.</summary>
            public bool HigherMeansFewerAds;
            public Func<AdRules, double> Get;
            public Action<AdRules, double> Set;
        }

        public static readonly Spec[] All =
        {
            Bool("ads_inter_enabled", r => r.interEnabled, (r, v) => r.interEnabled = v),
            Int("ads_inter_grace_sessions", 0, 1000, true, r => r.interGraceSessions, (r, v) => r.interGraceSessions = v),
            Int("ads_inter_grace_levels", 0, 10000, true, r => r.interGraceLevels, (r, v) => r.interGraceLevels = v),
            Int("ads_inter_min_levels", 1, 1000, true, r => r.interMinLevelEnds, (r, v) => r.interMinLevelEnds = v),
            Int("ads_inter_min_seconds", 0, 86400, true, r => r.interMinSeconds, (r, v) => r.interMinSeconds = v),
            Int("ads_inter_after_rewarded_seconds", 0, 86400, true, r => r.interAfterRewardedSeconds, (r, v) => r.interAfterRewardedSeconds = v),
            Int("ads_inter_max_per_session", 0, 100, false, r => r.interMaxPerSession, (r, v) => r.interMaxPerSession = v),
            Int("ads_inter_max_per_day", 0, 1000, false, r => r.interMaxPerDay, (r, v) => r.interMaxPerDay = v),
            Int("ads_ragequit_window_seconds", 0, 3600, true, r => r.rageQuitWindowSeconds, (r, v) => r.rageQuitWindowSeconds = v),
            Int("ads_ragequit_count", 1, 100, false, r => r.rageQuitCount, (r, v) => r.rageQuitCount = v),
            Int("ads_ragequit_suppress_hours", 0, 24 * 30, true, r => r.rageQuitSuppressHours, (r, v) => r.rageQuitSuppressHours = v),
            Int("ads_session_timeout_min", 1, 24 * 60, true, r => r.sessionTimeoutMinutes, (r, v) => r.sessionTimeoutMinutes = v),
            Bool("ads_rewarded_enabled", r => r.rewardedEnabled, (r, v) => r.rewardedEnabled = v),
            Int("ads_rewarded_max_per_session", 0, 100, false, r => r.rewardedMaxPerSession, (r, v) => r.rewardedMaxPerSession = v),
            Int("ads_rewarded_max_per_day", 0, 1000, false, r => r.rewardedMaxPerDay, (r, v) => r.rewardedMaxPerDay = v),
            Bool("ads_rewarded_double_tokens_enabled", r => r.doubleTokensEnabled, (r, v) => r.doubleTokensEnabled = v),
            Bool("ads_banner_enabled", r => r.bannerEnabled, (r, v) => r.bannerEnabled = v),
            Int("ads_banner_grace_days", 0, 3650, true, r => r.bannerGraceDays, (r, v) => r.bannerGraceDays = v),
            Int("ads_banner_grace_sessions", 0, 10000, true, r => r.bannerGraceSessions, (r, v) => r.bannerGraceSessions = v),
            Int("ads_banner_refresh_seconds", 30, 3600, true, r => r.bannerRefreshSeconds, (r, v) => r.bannerRefreshSeconds = v),
        };

        /// <summary>
        /// Returns <paramref name="ceiling"/> with each value replaced by the candidate's when the candidate is
        /// valid: the right type, inside the hard bounds, and no more permissive than the ceiling. Anything else
        /// keeps the ceiling value and is reported to <paramref name="onRejected"/> (key, value, reason).
        /// Kill switches can only be turned off.
        /// </summary>
        public static AdRules Restrict(AdRules ceiling, Func<Spec, double?> candidate, Action<string, string, string> onRejected = null)
        {
            var result = ceiling.Clone();
            foreach (var spec in All)
            {
                double? c = candidate(spec);
                if (!c.HasValue) continue;
                double v = c.Value;
                double cap = spec.Get(ceiling);
                string reason = null;
                if (double.IsNaN(v) || double.IsInfinity(v)) reason = "not a number";
                else if (spec.IsBool && v != 0.0 && v != 1.0) reason = "not a bool";
                else if (!spec.IsBool && v != Math.Floor(v)) reason = "not an integer";
                else if (v < spec.Min || v > spec.Max) reason = "out of range [" + spec.Min + ", " + spec.Max + "]";
                else if (spec.IsBool && v > cap) reason = "kill switch can only be turned off";
                else if (!spec.IsBool && spec.HigherMeansFewerAds && v < cap) reason = "would allow more ads (minimum " + cap + ")";
                else if (!spec.IsBool && !spec.HigherMeansFewerAds && v > cap) reason = "would allow more ads (maximum " + cap + ")";

                if (reason == null) spec.Set(result, v);
                else if (v != cap) onRejected?.Invoke(spec.Key, v.ToString(System.Globalization.CultureInfo.InvariantCulture), reason);
            }
            return result;
        }

        /// <summary>Validate a whole rules object (e.g. the local config asset) against the code defaults.</summary>
        public static AdRules RestrictToDefaults(AdRules local, Action<string, string, string> onRejected = null)
            => Restrict(new AdRules(), s => s.Get(local), onRejected);

        static Spec Bool(string key, Func<AdRules, bool> get, Action<AdRules, bool> set) => new Spec
        {
            Key = key, IsBool = true, Min = 0, Max = 1, HigherMeansFewerAds = false,
            Get = r => get(r) ? 1.0 : 0.0, Set = (r, v) => set(r, v != 0.0),
        };

        static Spec Int(string key, double min, double max, bool higherMeansFewer, Func<AdRules, int> get, Action<AdRules, int> set) => new Spec
        {
            Key = key, Min = min, Max = max, HigherMeansFewerAds = higherMeansFewer,
            Get = r => get(r), Set = (r, v) => set(r, (int)v),
        };
    }
}
