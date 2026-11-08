using System;
using System.Collections.Generic;
using Unity.Services.Core;
using UnityEngine;
using UnityEngine.Purchasing;

namespace Snaploop.Monetization
{
    /// <summary>
    /// Google Play billing through Unity IAP 5 (com.unity.purchasing). Sells one non-consumable.
    /// The entitlement is granted on <c>OnPurchasePending</c>, before the purchase is confirmed,
    /// so an interrupted confirmation is re-delivered on the next launch instead of being lost.
    /// </summary>
    public sealed class UnityIapBackend : IPurchaseBackend
    {
        StoreController store;
        string productId;
        Product product;
        bool connecting, connected;
        Action<PurchaseOutcome, string> pendingPurchase;
        Action<bool, string> pendingRestore;

        public string Name => "Unity IAP";
        public bool IsReady => connected && product != null && product.availableToPurchase;

        public string LocalizedPrice
        {
            get
            {
                string price = product?.metadata?.localizedPriceString;
                return string.IsNullOrEmpty(price) ? null : price;
            }
        }

        public event Action OwnershipFound;

        public async void Initialize(string id)
        {
            productId = id;
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

        public void Purchase(Action<PurchaseOutcome, string> onDone)
        {
            if (!IsReady)
            {
                Initialize(productId);
                onDone(PurchaseOutcome.Failed, "The store isn't available right now. Check your connection and try again.");
                return;
            }
            if (pendingPurchase != null)
            {
                onDone(PurchaseOutcome.Failed, "A purchase is already in progress.");
                return;
            }
            pendingPurchase = onDone;
            store.PurchaseProduct(product);
        }

        public void Restore(Action<bool, string> onDone)
        {
            if (!connected)
            {
                Initialize(productId);
                onDone(false, "The store isn't available right now. Check your connection and try again.");
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
            store.FetchProducts(new List<ProductDefinition> { new ProductDefinition(productId, ProductType.NonConsumable) });
        }

        void OnStoreDisconnected(StoreConnectionFailureDescription d)
        {
            connected = false;
            Debug.LogWarning("[Monetization] Store disconnected: " + d?.Message);
            FinishRestore(false, "The store isn't available right now.");
        }

        void OnProductsFetched(List<Product> products)
        {
            product = store.GetProductById(productId);
            if (product == null) Debug.LogWarning("[Monetization] Product '" + productId + "' was not returned by the store. Check the product id and that it is active in Play Console.");
            store.FetchPurchases();
        }

        void OnProductsFetchFailed(ProductFetchFailed f)
        {
            Debug.LogWarning("[Monetization] Product fetch failed: " + f?.FailureReason);
        }

        void OnPurchasesFetched(Orders orders)
        {
            bool owned = false;
            foreach (var order in orders.ConfirmedOrders)
                if (Contains(order)) owned = true;
            if (owned) OwnershipFound?.Invoke();
            FinishRestore(owned, null);
        }

        void OnPurchasesFetchFailed(PurchasesFetchFailureDescription d)
        {
            Debug.LogWarning("[Monetization] Fetching purchases failed: " + d?.Message);
            FinishRestore(false, "Couldn't reach the store. Try again later.");
        }

        void OnPurchasePending(PendingOrder order)
        {
            if (!Contains(order)) return;
            OwnershipFound?.Invoke();
            store.ConfirmPurchase(order);
            FinishPurchase(PurchaseOutcome.Purchased, null);
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
            FinishPurchase(PurchaseOutcome.Failed, "Payment pending. Ads will be removed once it completes.");
        }

        bool Contains(Order order)
        {
            var items = order?.CartOrdered?.Items();
            if (items == null) return false;
            foreach (var item in items)
                if (item?.Product?.definition?.id == productId) return true;
            return false;
        }

        void FinishPurchase(PurchaseOutcome outcome, string message)
        {
            var cb = pendingPurchase;
            pendingPurchase = null;
            cb?.Invoke(outcome, message);
        }

        void FinishRestore(bool found, string message)
        {
            var cb = pendingRestore;
            pendingRestore = null;
            cb?.Invoke(found, message);
        }
    }
}
