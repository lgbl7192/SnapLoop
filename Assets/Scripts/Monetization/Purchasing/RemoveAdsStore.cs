using Snaploop.Core;

namespace Snaploop.Monetization
{
    /// <summary>
    /// The Remove Ads product and its Settings rows. It removes interstitials and banners; rewarded ads
    /// (hints, double tokens) stay available because the player opts in. It does not unlock theme packs.
    /// </summary>
    public sealed class RemoveAdsStore
    {
        readonly MonetizationConfig config;
        readonly PurchaseService purchases;

        public RemoveAdsStore(MonetizationConfig config, PurchaseService purchases)
        {
            this.config = config;
            this.purchases = purchases;
            purchases.Register(new StoreProduct { Id = config.removeAdsProductId, Title = "Remove Ads", FallbackPrice = config.removeAdsFallbackPrice });
        }

        public void AddSettingsRows()
        {
            SettingsEntry buy = null;
            // Settings reads Label right after IsVisible, so this is where the store price gets picked up.
            buy = new SettingsEntry { Label = BuyLabel, OnClick = Buy, IsVisible = () => { buy.Label = BuyLabel; return !Entitlements.RemoveAds; } };
            GameServices.SettingsEntries.Add(buy);
            purchases.AddRestoreRow();
            GameServices.AddSettingsEntry("Ads removed. Thank you!", null, () => Entitlements.RemoveAds);
        }

        string BuyLabel => "Remove Ads - " + purchases.PriceLabel(config.removeAdsProductId);

        void Buy() =>
            purchases.Buy(config.removeAdsProductId, "Thank you! No more ads between puzzles or banners. Rewarded ads stay optional.", null);
    }
}
