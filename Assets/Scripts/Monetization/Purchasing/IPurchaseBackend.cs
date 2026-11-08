using System;

namespace Snaploop.Monetization
{
    public enum PurchaseOutcome
    {
        Purchased,
        AlreadyOwned,
        Cancelled,
        Failed,
    }

    /// <summary>A store that sells the single Remove Ads non-consumable. All callbacks on the main thread.</summary>
    public interface IPurchaseBackend
    {
        string Name { get; }
        bool IsReady { get; }

        /// <summary>Localized price from the store, or null until products are fetched.</summary>
        string LocalizedPrice { get; }

        /// <summary>Raised whenever the store reports that the player owns the product (launch, restore, late purchase).</summary>
        event Action OwnershipFound;

        /// <summary>Connect and fetch the product. Safe to call again after a failure.</summary>
        void Initialize(string productId);

        void Purchase(Action<PurchaseOutcome, string> onDone);

        /// <summary>Ask the store for past purchases. <c>found</c> is true if the product is owned.</summary>
        void Restore(Action<bool, string> onDone);
    }
}
