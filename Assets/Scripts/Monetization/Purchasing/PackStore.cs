using System;
using Snaploop.Core;
using Snaploop.Levels;
using UnityEngine;

namespace Snaploop.Monetization
{
    /// <summary>
    /// Theme packs as non-consumable products, one per pack with a <c>productId</c> in packs.json.
    /// Ownership lives in <see cref="Entitlements"/> next to Remove Ads and is restored from the store
    /// on launch and by Restore Purchases. Remove Ads does not unlock packs, and there is no bundle.
    /// </summary>
    public sealed class PackStore : IPackStore
    {
        readonly MonetizationConfig config;
        readonly PurchaseService purchases;

        public PackStore(MonetizationConfig config, PurchaseService purchases)
        {
            this.config = config;
            this.purchases = purchases;
        }

        /// <summary>Registers every buyable pack in packs.json with the store. Runs once the pack catalog is loaded.</summary>
        public void RegisterProducts()
        {
            int n = 0;
            foreach (var pack in PackCatalog.Packs)
            {
                if (!pack.CanBuy) continue;
                if (purchases.IsRemoveAds(pack.productId))
                {
                    Debug.LogWarning("[Monetization] Pack " + pack.id + " uses the Remove Ads product id; it can't be bought.");
                    continue;
                }
                purchases.Register(new StoreProduct { Id = pack.productId, Title = pack.DisplayTitle + " pack", FallbackPrice = config.packFallbackPrice });
                n++;
            }
            purchases.Initialize();
            Debug.Log("[Monetization] Pack products: " + n);
        }

        public bool Owns(string productId) => !purchases.IsRemoveAds(productId) && Entitlements.Owns(productId);

        public string PriceLabel(string productId) => purchases.PriceLabel(productId);

        public void Buy(string productId, string title, Action<bool> onDone)
        {
            if (purchases.IsRemoveAds(productId)) { onDone?.Invoke(false); return; }
            purchases.Buy(productId, "Thank you! " + title + " is unlocked.", onDone);
        }
    }

    /// <summary>The win card's "Double tokens" rewarded offer: <see cref="AdGate.DoubleTokens"/> plus a loaded ad.</summary>
    public sealed class DoubleTokensOffer : IDoubleTokensOffer
    {
        readonly AdsService ads;
        readonly string placement;

        public DoubleTokensOffer(AdsService ads, string placement)
        {
            this.ads = ads;
            this.placement = placement;
        }

        public bool IsOffered(int tokensEarned, bool claimedThisWin) => ads.IsDoubleTokensReady(placement, tokensEarned, claimedThisWin);

        public void Request(Action<bool> onResult) => ads.ShowRewarded(placement, onResult);
    }
}
