using System;

namespace Snaploop.Monetization
{
    /// <summary>
    /// What the game asks of ads. Every rule (caps, grace, cooldowns, consent, Remove Ads) is applied
    /// behind this interface, so callers never need to know them.
    /// </summary>
    public interface IAdsService
    {
        /// <summary>False when this build has no ad network (hints are then free).</summary>
        bool ServesAds { get; }

        /// <summary>True when a rewarded offer may be shown right now: allowed by the rules and an ad is loaded.</summary>
        bool IsRewardedReady(string placement);

        /// <summary>Show a rewarded ad. <paramref name="onDone"/> gets true only if the player watched to the end.</summary>
        void ShowRewarded(string placement, Action<bool> onDone);

        /// <summary>Show an interstitial if every rule allows it and one is already loaded; never waits for a load.</summary>
        void MaybeShowInterstitial(string placement, Action onDone);

        /// <summary>Raised when rewarded readiness may have changed (an ad loaded, a cap was reached, Remove Ads bought).</summary>
        event Action Changed;
    }
}
