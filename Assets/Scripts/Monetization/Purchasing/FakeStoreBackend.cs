using System;
using UnityEngine;

namespace Snaploop.Monetization
{
    /// <summary>
    /// Editor/development stand-in for Google Play. Shows a dialog labelled TEST STORE; no money moves.
    /// It remembers its own "account" purchase separately from the local entitlement, so
    /// Restore Purchases can be tested after the dev menu marks Remove Ads as not owned.
    /// </summary>
    public sealed class FakeStoreBackend : IPurchaseBackend
    {
        const string AccountOwnsKey = "monetization.fakeStore.owns";

        readonly MonetizationOverlay overlay;
        readonly string price;

        public FakeStoreBackend(MonetizationOverlay overlay, string price)
        {
            this.overlay = overlay;
            this.price = price;
        }

        public string Name => "Fake store";
        public bool IsReady => true;
        public string LocalizedPrice => price;
        public event Action OwnershipFound;

        /// <summary>Whether the simulated store account owns Remove Ads.</summary>
        public static bool AccountOwns
        {
            get => PlayerPrefs.GetInt(AccountOwnsKey, 0) == 1;
            set { PlayerPrefs.SetInt(AccountOwnsKey, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        public void Initialize(string productId)
        {
            if (AccountOwns) OwnershipFound?.Invoke();
        }

        public void Purchase(Action<PurchaseOutcome, string> onDone)
        {
            if (AccountOwns)
            {
                OwnershipFound?.Invoke();
                onDone(PurchaseOutcome.AlreadyOwned, null);
                return;
            }
            overlay.ShowFakeStore("Remove Ads", price, outcome =>
            {
                if (outcome == PurchaseOutcome.Purchased)
                {
                    AccountOwns = true;
                    OwnershipFound?.Invoke();
                }
                onDone(outcome, outcome == PurchaseOutcome.Failed ? "Test store: simulated failure." : null);
            });
        }

        public void Restore(Action<bool, string> onDone)
        {
            bool owns = AccountOwns;
            if (owns) OwnershipFound?.Invoke();
            onDone(owns, null);
        }
    }
}
