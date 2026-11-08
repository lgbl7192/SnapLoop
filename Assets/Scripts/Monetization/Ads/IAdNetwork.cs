using System;

namespace Snaploop.Monetization
{
    public enum AdFormat
    {
        Rewarded,
        Interstitial,
        Banner,
    }

    /// <summary>
    /// One full-screen ad show. The network reports into it; <see cref="AdsService"/> finishes it, and a
    /// watchdog can finish it first if a callback never arrives. Calls after it finished are ignored.
    /// </summary>
    public sealed class AdShowHandle
    {
        internal Action<AdShowHandle> OnDisplayed;
        internal Action<AdShowHandle> OnFinished;

        public AdFormat Format { get; internal set; }
        public string Placement { get; internal set; }
        public bool IsDisplayed { get; private set; }
        public bool IsEarned { get; private set; }
        public bool IsFinished { get; private set; }
        public string Error { get; private set; }

        public void Displayed()
        {
            if (IsFinished || IsDisplayed) return;
            IsDisplayed = true;
            OnDisplayed?.Invoke(this);
        }

        /// <summary>Rewarded only: the player watched to the end.</summary>
        public void Earned()
        {
            if (!IsFinished) IsEarned = true;
        }

        public void Closed() => Finish(null);

        public void Failed(string error) => Finish(string.IsNullOrEmpty(error) ? "show failed" : error);

        internal void Finish(string error)
        {
            if (IsFinished) return;
            IsFinished = true;
            Error = error;
            OnFinished?.Invoke(this);
        }
    }

    /// <summary>
    /// One ad SDK or mediation layer. It only loads and shows; it knows nothing about the rules,
    /// which live in <see cref="AdsService"/> and <see cref="AdGate"/>. All callbacks on the main thread
    /// (use <see cref="MonetizationRuntime.Post"/> from SDK threads).
    /// </summary>
    public interface IAdNetwork
    {
        string Name { get; }

        /// <summary>False when the adapter can never show anything (no SDK, no ids). Hints are then free.</summary>
        bool ServesAds { get; }

        void Initialize(AdUnitIds ids, Action<bool> onDone);

        bool IsLoaded(AdFormat format);

        /// <summary>Request one ad. The outcome arrives through <see cref="LoadFinished"/>.</summary>
        void Load(AdFormat format);

        /// <summary>format, success, error message.</summary>
        event Action<AdFormat, bool, string> LoadFinished;

        void ShowRewarded(AdShowHandle handle);
        void ShowInterstitial(AdShowHandle handle);

        /// <summary>Show the loaded banner at the bottom of the screen.</summary>
        void ShowBanner(string placement);
        /// <summary>Remove and release the banner; the next <see cref="Load"/> creates a fresh one.</summary>
        void DestroyBanner();
        /// <summary>Screen pixels from the bottom edge of the screen to the top of the loaded banner; 0 when none is loaded.</summary>
        float BannerTopPixels { get; }
    }

    /// <summary>No network: nothing loads, nothing shows.</summary>
    public sealed class NoAdNetwork : IAdNetwork
    {
        public string Name => "None";
        public bool ServesAds => false;
        public void Initialize(AdUnitIds ids, Action<bool> onDone) => onDone(false);
        public bool IsLoaded(AdFormat format) => false;
        public void Load(AdFormat format) { }
        public event Action<AdFormat, bool, string> LoadFinished { add { } remove { } }
        public void ShowRewarded(AdShowHandle handle) => handle.Failed("no network");
        public void ShowInterstitial(AdShowHandle handle) => handle.Failed("no network");
        public void ShowBanner(string placement) { }
        public void DestroyBanner() { }
        public float BannerTopPixels => 0f;
    }
}
