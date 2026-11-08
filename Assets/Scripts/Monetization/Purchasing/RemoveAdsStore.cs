using Snaploop.Core;
using UnityEngine;

namespace Snaploop.Monetization
{
    /// <summary>The Remove Ads product: Settings rows, purchase and restore flows, entitlement grant.</summary>
    public sealed class RemoveAdsStore
    {
        readonly MonetizationConfig config;
        readonly MonetizationOverlay overlay;
        bool busy;

        public IPurchaseBackend Backend { get; }

        public RemoveAdsStore(MonetizationConfig config, IPurchaseBackend backend, MonetizationOverlay overlay)
        {
            this.config = config;
            this.overlay = overlay;
            Backend = backend;
            Backend.OwnershipFound += Grant;
        }

        public void Initialize()
        {
            Backend.Initialize(config.removeAdsProductId);
            SettingsEntry buy = null;
            // Settings reads Label right after IsVisible, so this is where the store price gets picked up.
            buy = new SettingsEntry { Label = BuyLabel, OnClick = Buy, IsVisible = () => { buy.Label = BuyLabel; return !Entitlements.RemoveAds; } };
            GameServices.SettingsEntries.Add(buy);
            GameServices.AddSettingsEntry("Restore Purchases", Restore, () => !Entitlements.RemoveAds);
            GameServices.AddSettingsEntry("Ads removed. Thank you!", null, () => Entitlements.RemoveAds);
        }

        string BuyLabel => "Remove Ads - " + (Backend.LocalizedPrice ?? config.removeAdsFallbackPrice);

        static void Grant()
        {
            if (Entitlements.RemoveAds) return;
            Entitlements.RemoveAds = true;
            Debug.Log("[Monetization] Remove Ads granted.");
        }

        void Buy()
        {
            if (busy || Entitlements.RemoveAds) return;
            busy = true;
            overlay.ShowBusy("Contacting the store…");
            Backend.Purchase((outcome, message) =>
            {
                busy = false;
                overlay.HideBusy();
                switch (outcome)
                {
                    case PurchaseOutcome.Purchased:
                        overlay.Toast("Ads removed. Thank you!");
                        break;
                    case PurchaseOutcome.AlreadyOwned:
                        overlay.Toast(Entitlements.RemoveAds ? "You already own Remove Ads." : "You already own this. Restoring…");
                        break;
                    case PurchaseOutcome.Cancelled:
                        break;
                    default:
                        overlay.Toast(message ?? "The purchase didn't go through.");
                        break;
                }
                GameServices.NotifySettingsChanged();
            });
        }

        void Restore()
        {
            if (busy) return;
            busy = true;
            overlay.ShowBusy("Restoring purchases…");
            Backend.Restore((found, message) =>
            {
                busy = false;
                overlay.HideBusy();
                overlay.Toast(found ? "Remove Ads restored." : message ?? "No purchases to restore on this account.");
                GameServices.NotifySettingsChanged();
            });
        }
    }
}
