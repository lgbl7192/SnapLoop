using System;
using System.Collections.Generic;
using Snaploop.Core;
using UnityEngine;

namespace Snaploop.Monetization
{
    /// <summary>
    /// Every non-consumable the game sells (Remove Ads and theme packs): the product list handed to the store,
    /// the purchase and Restore Purchases flows with their busy and toast UI, and the grant that turns a
    /// store-reported purchase into a local entitlement.
    /// </summary>
    public sealed class PurchaseService
    {
        readonly MonetizationConfig config;
        readonly MonetizationOverlay overlay;
        readonly List<StoreProduct> products = new List<StoreProduct>();
        bool busy;

        public IPurchaseBackend Backend { get; }
        public IReadOnlyList<StoreProduct> Products => products;

        public PurchaseService(MonetizationConfig config, IPurchaseBackend backend, MonetizationOverlay overlay)
        {
            this.config = config;
            this.overlay = overlay;
            Backend = backend;
            Backend.OwnershipFound += Grant;
        }

        /// <summary>Adds a product. Call <see cref="Initialize"/> afterwards so the store fetches it.</summary>
        public void Register(StoreProduct product)
        {
            if (product == null || string.IsNullOrEmpty(product.Id) || Find(product.Id) != null) return;
            products.Add(product);
        }

        /// <summary>Hands every registered product to the store. Safe to call again after registering more.</summary>
        public void Initialize() => Backend.Initialize(products);

        public StoreProduct Find(string productId) => products.Find(p => p.Id == productId);

        public bool IsRemoveAds(string productId) => productId == config.removeAdsProductId;

        public bool Owns(string productId) => IsRemoveAds(productId) ? Entitlements.RemoveAds : Entitlements.Owns(productId);

        public string PriceLabel(string productId) => Backend.LocalizedPrice(productId) ?? Find(productId)?.FallbackPrice ?? "";

        /// <summary>The Settings row. Shown while anything the game sells is not owned on this device.</summary>
        public void AddRestoreRow() =>
            GameServices.AddSettingsEntry("Restore Purchases", Restore, () => products.Exists(p => !Owns(p.Id)));

        void Grant(string productId)
        {
            if (Find(productId) == null || Owns(productId)) return;
            if (IsRemoveAds(productId)) Entitlements.RemoveAds = true;
            else Entitlements.Set(productId, true);
            Debug.Log("[Monetization] Granted " + productId + ".");
        }

        /// <param name="thanks">Toast after a successful purchase.</param>
        /// <param name="onDone">Gets true when the product is owned afterwards.</param>
        public void Buy(string productId, string thanks, Action<bool> onDone)
        {
            var product = Find(productId);
            if (product == null)
            {
                Debug.LogWarning("[Monetization] Buy: unknown product " + productId);
                overlay.Toast("This isn't for sale right now.");
                onDone?.Invoke(false);
                return;
            }
            if (Owns(productId)) { onDone?.Invoke(true); return; }
            if (busy) { onDone?.Invoke(false); return; }

            busy = true;
            overlay.ShowBusy("Contacting the store…");
            Backend.Purchase(productId, (outcome, message) =>
            {
                busy = false;
                overlay.HideBusy();
                switch (outcome)
                {
                    case PurchaseOutcome.Purchased:
                        overlay.Toast(thanks, 4f);
                        break;
                    case PurchaseOutcome.AlreadyOwned:
                        overlay.Toast(Owns(productId) ? "You already own " + product.Title + "." : "You already own this. Restoring…");
                        break;
                    case PurchaseOutcome.Cancelled:
                        break;
                    default:
                        overlay.Toast(message ?? "The purchase didn't go through.");
                        break;
                }
                GameServices.NotifySettingsChanged();
                onDone?.Invoke(Owns(productId));
            });
        }

        void Restore()
        {
            if (busy) return;
            busy = true;
            overlay.ShowBusy("Restoring purchases…");
            Backend.Restore((owned, error) =>
            {
                busy = false;
                overlay.HideBusy();
                var names = new List<string>();
                foreach (var id in owned)
                {
                    var p = Find(id);
                    if (p != null) names.Add(p.Title);
                }
                if (names.Count > 0) overlay.Toast("Restored: " + string.Join(", ", names) + ".", 4f);
                else overlay.Toast(error ?? "No purchases to restore on this account.");
                GameServices.NotifySettingsChanged();
            });
        }
    }
}
