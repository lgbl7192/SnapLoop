using System;
using System.Collections.Generic;

namespace Snaploop.Monetization
{
    public enum PurchaseOutcome
    {
        Purchased,
        AlreadyOwned,
        Cancelled,
        Failed,
    }

    /// <summary>One non-consumable the game sells.</summary>
    public sealed class StoreProduct
    {
        public string Id;
        /// <summary>Player-facing name, e.g. "Remove Ads" or "Sea Life pack".</summary>
        public string Title;
        /// <summary>Shown until the store returns the localized price.</summary>
        public string FallbackPrice;
    }

    /// <summary>A store that sells the game's non-consumables. All callbacks on the main thread.</summary>
    public interface IPurchaseBackend
    {
        string Name { get; }

        /// <summary>True when <paramref name="productId"/> was fetched and can be bought now.</summary>
        bool IsReady(string productId);

        /// <summary>Localized price from the store, or null until products are fetched.</summary>
        string LocalizedPrice(string productId);

        /// <summary>Raised with a product id whenever the store reports that the player owns it (launch, restore, late purchase).</summary>
        event Action<string> OwnershipFound;

        /// <summary>Connect and fetch every product. Call once; later calls only retry a failed connection.</summary>
        void Initialize(IReadOnlyList<StoreProduct> products);

        void Purchase(string productId, Action<PurchaseOutcome, string> onDone);

        /// <summary>Ask the store for past purchases. <c>owned</c> lists the product ids found; <c>error</c> is null on success.</summary>
        void Restore(Action<IReadOnlyList<string>, string> onDone);
    }
}
