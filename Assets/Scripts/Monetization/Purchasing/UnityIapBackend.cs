using System;
using System.Collections.Generic;
using Unity.Services.Core;
using UnityEngine;
using UnityEngine.Purchasing;

namespace Snaploop.Monetization
{
    /// <summary>
    /// Google Play billing through Unity IAP 5 (com.unity.purchasing). Sells non-consumables only (Remove Ads,
    /// theme packs). An entitlement is granted on <c>OnPurchasePending</c>, before the purchase is confirmed,
    /// so an interrupted confirmation is re-delivered on the next launch instead of being lost.
    /// </summary>
    public sealed class UnityIapBackend : IPurchaseBackend
    {
        StoreController store;
        readonly List<string> productIds = new List<string>();
        readonly Dictionary<string, Product> products = new Dictionary<string, Product>();
        bool connecting, connected;
        string pendingProductId;
        Action<PurchaseOutcome, string> pendingPurchase;
        Action<IReadOnlyList<string>, string> pendingRestore;

        public string Name => "Unity IAP";

        public bool IsReady(string productId) =>
            connected && products.TryGetValue(productId ?? "", out var p) && p != null && p.availableToPurchase;

        public string LocalizedPrice(string productId)
        {
            products.TryGetValue(productId ?? "", out var p);
            string price = p?.metadata?.localizedPriceString;
            return string.IsNullOrEmpty(price) ? null : price;
        }

        public event Action<string> OwnershipFound;

        /// <summary>Products registered after the store connected (theme packs, once packs.json is read) are fetched then.</summary>
        public void Initialize(IReadOnlyList<StoreProduct> list)
        {
            bool added = false;
            if (list != null)
                foreach (var p in list)
                    if (!string.IsNullOrEmpty(p?.Id) && !productIds.Contains(p.Id))
                    {
                        productIds.Add(p.Id);
                        added = true;
                    }
            if (connected && added) FetchAllProducts();
            else Connect();
        }

        void FetchAllProducts()
        {
            var defs = new List<ProductDefinition>();
            foreach (var id in productIds) defs.Add(new ProductDefinition(id, ProductType.NonConsumable));
            store.FetchProducts(defs);
        }

        async void Connect()
        {
            if (connecting || connected) return;
            connecting = true;
            try
            {
                try
                {
                    if (UnityServices.State == ServicesInitializationState.Uninitialized)
                        await UnityServices.InitializeAsync();
                }
                catch (Exception e)
                {
                    // IAP still works without a linked cloud project; only Unity's revenue reporting needs it.
                    Debug.LogWarning("[Monetization] Unity Services init failed (continuing): " + e.Message);
                }

                if (store == null)
                {
                    store = UnityIAPServices.StoreController();
                    store.OnStoreConnected += OnStoreConnected;
                    store.OnStoreDisconnected += OnStoreDisconnected;
                    store.OnProductsFetched += OnProductsFetched;
                    store.OnProductsFetchFailed += OnProductsFetchFailed;
                    store.OnPurchasesFetched += OnPurchasesFetched;
                    store.OnPurchasesFetchFailed += OnPurchasesFetchFailed;
                    store.OnPurchasePending += OnPurchasePending;
                    store.OnPurchaseConfirmed += OnPurchaseConfirmed;
                    store.OnPurchaseFailed += OnPurchaseFailed;
                    store.OnPurchaseDeferred += OnPurchaseDeferred;
                }
                await store.Connect();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Monetization] Store connect failed: " + e.Message);
            }
            finally
            {
                connecting = false;
            }
        }

        public void Purchase(string productId, Action<PurchaseOutcome, string> onDone)
        {
            if (!IsReady(productId))
            {
                Connect();
                onDone(PurchaseOutcome.Failed, "The store isn't available right now. Check your connection and try again.");
                return;
            }
            if (pendingPurchase != null)
            {
                onDone(PurchaseOutcome.Failed, "A purchase is already in progress.");
                return;
            }
            pendingProductId = productId;
            pendingPurchase = onDone;
            store.PurchaseProduct(products[productId]);
        }

        public void Restore(Action<IReadOnlyList<string>, string> onDone)
        {
            if (!connected)
            {
                Connect();
                onDone(Array.Empty<string>(), "The store isn't available right now. Check your connection and try again.");
                return;
            }
            if (pendingRestore != null) return;
            pendingRestore = onDone;
            store.FetchPurchases();
        }

        // ------------------------------------------------------------ store callbacks

        void OnStoreConnected()
        {
            connected = true;
            FetchAllProducts();
        }

        void OnStoreDisconnected(StoreConnectionFailureDescription d)
        {
            connected = false;
            Debug.LogWarning("[Monetization] Store disconnected: " + d?.Message);
            FinishRestore(Array.Empty<string>(), "The store isn't available right now.");
        }

        void OnProductsFetched(List<Product> fetched)
        {
            foreach (var id in productIds)
            {
                var p = store.GetProductById(id);
                if (p != null) products[id] = p;
                else Debug.LogWarning("[Monetization] Product '" + id + "' was not returned by the store. Check the product id and that it is active in Play Console.");
            }
            store.FetchPurchases();
        }

        void OnProductsFetchFailed(ProductFetchFailed f)
        {
            Debug.LogWarning("[Monetization] Product fetch failed: " + f?.FailureReason);
        }

        void OnPurchasesFetched(Orders orders)
        {
            var owned = new List<string>();
            foreach (var order in orders.ConfirmedOrders)
                foreach (var id in OurProducts(order))
                    if (!owned.Contains(id)) owned.Add(id);
            foreach (var id in owned) OwnershipFound?.Invoke(id);
            FinishRestore(owned, null);
        }

        void OnPurchasesFetchFailed(PurchasesFetchFailureDescription d)
        {
            Debug.LogWarning("[Monetization] Fetching purchases failed: " + d?.Message);
            FinishRestore(Array.Empty<string>(), "Couldn't reach the store. Try again later.");
        }

        void OnPurchasePending(PendingOrder order)
        {
            var ids = OurProducts(order);
            if (ids.Count == 0) return;
            foreach (var id in ids) OwnershipFound?.Invoke(id);
            store.ConfirmPurchase(order);
            if (pendingProductId != null && ids.Contains(pendingProductId)) FinishPurchase(PurchaseOutcome.Purchased, null);
        }

        void OnPurchaseConfirmed(Order order)
        {
            if (order is FailedOrder failed)
                Debug.LogWarning("[Monetization] Purchase confirmation failed (" + failed.FailureReason + "): " + failed.Details + ". It will be retried on next launch.");
        }

        void OnPurchaseFailed(FailedOrder order)
        {
            switch (order.FailureReason)
            {
                case PurchaseFailureReason.UserCancelled:
                    FinishPurchase(PurchaseOutcome.Cancelled, null);
                    break;
                case PurchaseFailureReason.DuplicateTransaction:
                    store.FetchPurchases();
                    FinishPurchase(PurchaseOutcome.AlreadyOwned, null);
                    break;
                case PurchaseFailureReason.ExistingPurchasePending:
                    FinishPurchase(PurchaseOutcome.Failed, "A previous payment is still pending.");
                    break;
                default:
                    Debug.LogWarning("[Monetization] Purchase failed (" + order.FailureReason + "): " + order.Details);
                    FinishPurchase(PurchaseOutcome.Failed, "The purchase didn't go through.");
                    break;
            }
        }

        void OnPurchaseDeferred(DeferredOrder order)
        {
            FinishPurchase(PurchaseOutcome.Failed, "Payment pending. It unlocks as soon as the payment completes.");
        }

        List<string> OurProducts(Order order)
        {
            var ids = new List<string>();
            var items = order?.CartOrdered?.Items();
            if (items == null) return ids;
            foreach (var item in items)
            {
                string id = item?.Product?.definition?.id;
                if (id != null && productIds.Contains(id) && !ids.Contains(id)) ids.Add(id);
            }
            return ids;
        }

        void FinishPurchase(PurchaseOutcome outcome, string message)
        {
            var cb = pendingPurchase;
            pendingPurchase = null;
            pendingProductId = null;
            cb?.Invoke(outcome, message);
        }

        void FinishRestore(IReadOnlyList<string> owned, string message)
        {
            var cb = pendingRestore;
            pendingRestore = null;
            cb?.Invoke(owned, message);
        }
    }
}
