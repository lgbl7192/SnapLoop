using System;
using System.Collections.Generic;
using UnityEngine;

namespace Snaploop.Monetization
{
    /// <summary>
    /// Editor/development stand-in for Google Play. Shows a dialog labelled TEST STORE; no money moves.
    /// It remembers its own "account" purchases separately from the local entitlements, so
    /// Restore Purchases can be tested after the dev menu forgets them locally.
    /// </summary>
    public sealed class FakeStoreBackend : IPurchaseBackend
    {
        const string AccountOwnsKey = "monetization.fakeStore.owns";

        readonly MonetizationOverlay overlay;
        readonly string removeAdsProductId;
        readonly List<StoreProduct> products = new List<StoreProduct>();

        public FakeStoreBackend(MonetizationOverlay overlay, string removeAdsProductId)
        {
            this.overlay = overlay;
            this.removeAdsProductId = removeAdsProductId;
        }

        public string Name => "Fake store";
        public bool IsReady(string productId) => Find(productId) != null;
        public string LocalizedPrice(string productId) => Find(productId)?.FallbackPrice;
        public event Action<string> OwnershipFound;

        /// <summary>Whether the simulated store account owns <paramref name="productId"/>.</summary>
        public bool AccountOwns(string productId) => PlayerPrefs.GetInt(Key(productId), 0) == 1;

        public void SetAccountOwns(string productId, bool owns)
        {
            PlayerPrefs.SetInt(Key(productId), owns ? 1 : 0);
            PlayerPrefs.Save();
        }

        public int AccountOwnedCount(Func<string, bool> filter)
        {
            int n = 0;
            foreach (var p in products)
                if (filter(p.Id) && AccountOwns(p.Id)) n++;
            return n;
        }

        // Remove Ads keeps the key it had before packs existed, so test installs keep their fake purchase.
        string Key(string productId) => productId == removeAdsProductId ? AccountOwnsKey : AccountOwnsKey + "." + productId;

        public IReadOnlyList<StoreProduct> Products => products;

        public void Initialize(IReadOnlyList<StoreProduct> list)
        {
            if (list != null)
                foreach (var p in list)
                    if (p != null && Find(p.Id) == null) products.Add(p);
            foreach (var p in products)
                if (AccountOwns(p.Id)) OwnershipFound?.Invoke(p.Id);
        }

        public void Purchase(string productId, Action<PurchaseOutcome, string> onDone)
        {
            var product = Find(productId);
            if (product == null)
            {
                onDone(PurchaseOutcome.Failed, "Test store: unknown product " + productId + ".");
                return;
            }
            if (AccountOwns(productId))
            {
                OwnershipFound?.Invoke(productId);
                onDone(PurchaseOutcome.AlreadyOwned, null);
                return;
            }
            overlay.ShowFakeStore(product.Title, product.FallbackPrice, outcome =>
            {
                if (outcome == PurchaseOutcome.Purchased)
                {
                    SetAccountOwns(productId, true);
                    OwnershipFound?.Invoke(productId);
                }
                onDone(outcome, outcome == PurchaseOutcome.Failed ? "Test store: simulated failure." : null);
            });
        }

        public void Restore(Action<IReadOnlyList<string>, string> onDone)
        {
            var owned = new List<string>();
            foreach (var p in products)
                if (AccountOwns(p.Id)) owned.Add(p.Id);
            foreach (var id in owned) OwnershipFound?.Invoke(id);
            onDone(owned, null);
        }

        StoreProduct Find(string productId)
        {
            foreach (var p in products)
                if (p.Id == productId) return p;
            return null;
        }
    }
}
