using System;

namespace Snaploop.Monetization
{
    /// <summary>
    /// Ad consent (GDPR/US state privacy). The real implementation will wrap Google's User Messaging
    /// Platform (UMP). No ad is requested until <see cref="Gather"/> has finished.
    /// </summary>
    public interface IConsentProvider
    {
        bool IsFinished { get; }

        /// <summary>Whether ads may be requested at all (personalised or not is the SDK's business).</summary>
        bool CanRequestAds { get; }

        /// <summary>The player must be able to reopen the consent form: show a "Privacy choices" row.</summary>
        bool PrivacyOptionsRequired { get; }

        /// <summary>Update consent info and show the form if needed. Called once per cold launch.</summary>
        void Gather(Action onDone);

        void ShowPrivacyOptions(Action onClosed);

        event Action Changed;
    }

    /// <summary>Placeholder until UMP is integrated: finishes immediately and allows ads.</summary>
    public sealed class StubConsentProvider : IConsentProvider
    {
        readonly Action<string> toast;
        bool privacyOptionsRequired;

        public StubConsentProvider(Action<string> toast) => this.toast = toast;

        public bool IsFinished { get; private set; }
        public bool CanRequestAds => IsFinished;

        public bool PrivacyOptionsRequired
        {
            get => privacyOptionsRequired;
            set
            {
                if (privacyOptionsRequired == value) return;
                privacyOptionsRequired = value;
                Changed?.Invoke();
            }
        }

        public event Action Changed;

        public void Gather(Action onDone)
        {
            IsFinished = true;
            Changed?.Invoke();
            onDone?.Invoke();
        }

        public void ShowPrivacyOptions(Action onClosed)
        {
            toast?.Invoke("The privacy choices form (Google UMP) will open here.");
            onClosed?.Invoke();
        }
    }
}
