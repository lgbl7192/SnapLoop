using System;

namespace Snaploop.Monetization
{
    /// <summary>
    /// The persisted facts the ad gates read. Times are UTC Unix seconds (0 = never); days are local
    /// calendar day numbers, so daily counters reset at local midnight. Pure C#: the caller passes the
    /// clock in and saves the object (JSON) after every change.
    /// A device clock moved backwards is clamped: stored times later than "now" become "now", and the
    /// day never rolls back. Changing the clock can therefore restart cooldowns but never adds ads.
    /// </summary>
    [Serializable]
    public sealed class AdLedger
    {
        public long installTime;
        public int installDay;
        public int sessionCount;

        /// <summary>Puzzles won since install (grace period).</summary>
        public int levelsCleared;
        /// <summary>Level ends (Snaploop: wins) since the last interstitial. Starts at 0 on install.</summary>
        public int levelEndsSinceInterstitial;

        public long lastInterstitialAt;
        public long lastInterstitialClosedAt;
        public long lastRewardedAt;

        public int sessionInterstitials;
        public int sessionRewarded;

        public int day;
        public int dailyInterstitials;
        public int dailyRewarded;

        public int rageQuitStreak;
        /// <summary>
        /// False from an interstitial closing until the player either leaves the app (rage-quit if within the
        /// window) or finishes a level (resets the streak). Each interstitial is judged once, so the streak
        /// counts consecutive interstitials that each ended with a quick exit.
        /// </summary>
        public bool lastCloseJudged = true;
        public long rageQuitPausedUntil;

        public long lastBackgroundAt;
        public long lastSeen;

        public bool IsNew => sessionCount == 0;

        public int DaysSinceInstall(int today) => Math.Max(0, today - installDay);

        public int DailyInterstitials(int today) => today > day ? 0 : dailyInterstitials;
        public int DailyRewarded(int today) => today > day ? 0 : dailyRewarded;

        public void ClampClock(long now, int today, int maxSuppressHours)
        {
            if (installTime == 0 || installTime > now) installTime = now;
            if (sessionCount == 0 || installDay > today) installDay = today;
            if (lastInterstitialAt > now) lastInterstitialAt = now;
            if (lastInterstitialClosedAt > now) lastInterstitialClosedAt = now;
            if (lastRewardedAt > now) lastRewardedAt = now;
            if (lastBackgroundAt > now) lastBackgroundAt = now;
            long maxPause = now + (long)maxSuppressHours * 3600;
            if (rageQuitPausedUntil > maxPause) rageQuitPausedUntil = maxPause;
            lastSeen = Math.Max(lastSeen, now);
        }

        /// <summary>Daily counters reset only when the local day moves forward.</summary>
        public void RollDay(int today)
        {
            if (today <= day) return;
            day = today;
            dailyInterstitials = 0;
            dailyRewarded = 0;
        }

        /// <summary>Cold launch, or return from a long background (<see cref="IsSessionExpired"/>).</summary>
        public void StartSession(long now, int today, int maxSuppressHours)
        {
            ClampClock(now, today, maxSuppressHours);
            RollDay(today);
            sessionCount++;
            sessionInterstitials = 0;
            sessionRewarded = 0;
            lastBackgroundAt = 0;
        }

        public bool IsSessionExpired(long now, int timeoutMinutes) =>
            lastBackgroundAt > 0 && now - lastBackgroundAt >= (long)timeoutMinutes * 60;

        /// <summary>The app went to the background (or is quitting). Also the rage-quit check of section 4.1.</summary>
        public void RecordBackground(long now, AdRules rules)
        {
            lastBackgroundAt = now;
            if (lastCloseJudged || lastInterstitialClosedAt == 0) return;
            lastCloseJudged = true;
            if (now - lastInterstitialClosedAt > rules.rageQuitWindowSeconds)
            {
                rageQuitStreak = 0;
                return;
            }
            rageQuitStreak++;
            if (rageQuitStreak >= rules.rageQuitCount)
            {
                rageQuitPausedUntil = now + (long)rules.rageQuitSuppressHours * 3600;
                rageQuitStreak = 0;
            }
        }

        public void RecordLevelEnd(bool won)
        {
            levelEndsSinceInterstitial++;
            if (won) levelsCleared++;
            if (!lastCloseJudged)
            {
                lastCloseJudged = true;
                rageQuitStreak = 0;
            }
        }

        public void RecordInterstitialShown(long now, int today)
        {
            RollDay(today);
            lastInterstitialAt = now;
            sessionInterstitials++;
            dailyInterstitials++;
            levelEndsSinceInterstitial = 0;
        }

        public void RecordInterstitialClosed(long now)
        {
            lastInterstitialClosedAt = now;
            lastCloseJudged = false;
        }

        /// <summary>Counted when the ad is displayed, whether or not the player watches to the end.</summary>
        public void RecordRewardedShown(long now, int today)
        {
            RollDay(today);
            lastRewardedAt = now;
            sessionRewarded++;
            dailyRewarded++;
        }
    }
}
