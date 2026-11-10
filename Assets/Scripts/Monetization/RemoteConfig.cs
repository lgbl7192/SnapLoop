namespace Snaploop.Monetization
{
    /// <summary>
    /// Remote overrides for the ad rules (keys in <see cref="AdRuleSpecs"/>, e.g. "ads_inter_max_per_day").
    /// Values fetched during a session are activated at the next session start, so rules never change
    /// mid-session. Overrides can only make ads rarer than the local config; anything else is rejected.
    /// </summary>
    public interface IRemoteConfigProvider
    {
        /// <summary>Make the values fetched earlier the active ones. Called at each session start.</summary>
        void ActivateFetched();

        /// <summary>Numbers and booleans (1/0) for the active values; false when the key is not set.</summary>
        bool TryGetNumber(string key, out double value);

        /// <summary>Start a background fetch for the next session.</summary>
        void FetchForNextSession();
    }

    /// <summary>No remote config yet: nothing is ever set.</summary>
    public sealed class StubRemoteConfigProvider : IRemoteConfigProvider
    {
        public void ActivateFetched() { }

        public bool TryGetNumber(string key, out double value)
        {
            value = 0;
            return false;
        }

        public void FetchForNextSession() { }
    }
}
