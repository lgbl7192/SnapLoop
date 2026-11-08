using System;
using Snaploop.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Snaploop.Monetization
{
    /// <summary>
    /// Monetization UI on its own canvas above the game: toasts, a busy blocker, the TEST AD overlay,
    /// the TEST BANNER strip and the TEST STORE dialog. Built lazily on first use.
    /// </summary>
    public sealed class MonetizationOverlay
    {
        static readonly Color TestYellow = new Color32(255, 214, 0, 255);
        static readonly Color TestInk = new Color32(20, 20, 20, 255);

        readonly Transform parent;
        RectTransform root;

        RectTransform toastRoot;
        Text toastText;
        CanvasGroup toastGroup;
        float toastTime;

        RectTransform busyRoot;
        Text busyText;
        float busyTime;

        RectTransform adRoot;
        Image adArt;
        Text adKind, adCountdown;
        RectTransform adProgress;
        Button adClose, adClaim;
        Text adCloseLabel;
        AdShowHandle adHandle;
        bool adRewarded;
        float adDuration, adElapsed;

        const float TestBannerHeight = 150f;
        RectTransform bannerRoot;
        Text bannerText;
        string bannerPlacement;
        int bannerLoads;

        RectTransform storeRoot;
        Text storeTitle, storePrice;
        Action<PurchaseOutcome> storeDone;

        public MonetizationOverlay(Transform parent) => this.parent = parent;

        public bool IsAdShowing => adRoot != null && adRoot.gameObject.activeSelf;

        // ------------------------------------------------------------ toast / busy

        public void Toast(string message, float seconds = 3f)
        {
            Build();
            toastRoot.SetAsLastSibling();
            toastText.text = message;
            toastTime = seconds;
            toastGroup.alpha = 1f;
        }

        public void ShowBusy(string message)
        {
            Build();
            busyRoot.SetAsLastSibling();
            busyText.text = message;
            busyTime = 0f;
            busyRoot.gameObject.SetActive(true);
        }

        public void HideBusy()
        {
            if (busyRoot != null) busyRoot.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------ test ad

        /// <param name="seconds">Rewarded: the reward needs the full duration. Interstitial: closable after it.</param>
        public void ShowTestAd(AdShowHandle handle, float seconds)
        {
            Build();
            adHandle = handle;
            adRewarded = handle.Format == AdFormat.Rewarded;
            adDuration = Mathf.Max(0.01f, seconds);
            adElapsed = 0f;
            adKind.text = (adRewarded ? "Rewarded ad" : "Interstitial ad") + "\nplacement: " +
                          (string.IsNullOrEmpty(handle.Placement) ? "(default)" : handle.Placement);
            adCloseLabel.text = adRewarded ? "Close (no reward)" : "Close";
            adClose.gameObject.SetActive(adRewarded);
            adClaim.gameObject.SetActive(false);
            adRoot.SetAsLastSibling();
            adRoot.gameObject.SetActive(true);
            UpdateAd();
            handle.Displayed();
        }

        void FinishAd(bool earned)
        {
            adRoot.gameObject.SetActive(false);
            var h = adHandle;
            adHandle = null;
            if (h == null) return;
            if (earned) h.Earned();
            h.Closed();
        }

        // ------------------------------------------------------------ test banner

        public float TestBannerHeightPixels => TestBannerHeight * UILayout.Scale;

        public void ShowTestBanner(string placement)
        {
            Build();
            bannerPlacement = placement;
            bannerRoot.SetAsFirstSibling();
            bannerRoot.gameObject.SetActive(true);
            UpdateBannerText();
        }

        public void HideTestBanner()
        {
            if (bannerRoot != null) bannerRoot.gameObject.SetActive(false);
        }

        /// <summary>A banner load finished; the label shows the count so app-driven refreshes are visible.</summary>
        public void RefreshTestBanner()
        {
            bannerLoads++;
            if (bannerRoot != null && bannerRoot.gameObject.activeSelf) UpdateBannerText();
        }

        void UpdateBannerText()
        {
            bannerText.text = "TEST BANNER · " + (string.IsNullOrEmpty(bannerPlacement) ? "(default)" : bannerPlacement) +
                              " · load #" + bannerLoads + "\nNot a real advertisement. Development build only.";
        }

        void UpdateAd()
        {
            float k = Mathf.Clamp01(adElapsed / adDuration);
            adProgress.anchorMax = new Vector2(k, 1f);
            adArt.color = Color.HSVToRGB(Mathf.Repeat(adElapsed * 0.12f, 1f), 0.45f, 0.55f);
            int left = Mathf.CeilToInt(adDuration - adElapsed);
            if (adRewarded)
            {
                adCountdown.text = k < 1f ? "Reward in " + left + " s" : "Reward earned";
                adClaim.gameObject.SetActive(k >= 1f);
                adClose.gameObject.SetActive(k < 1f);
            }
            else
            {
                adCountdown.text = k < 1f ? "You can close this in " + left + " s" : "";
                adClose.gameObject.SetActive(k >= 1f);
            }
        }

        // ------------------------------------------------------------ fake store

        public void ShowFakeStore(string product, string price, Action<PurchaseOutcome> onDone)
        {
            Build();
            storeDone = onDone;
            storeTitle.text = product;
            storePrice.text = price;
            storeRoot.SetAsLastSibling();
            storeRoot.gameObject.SetActive(true);
        }

        void FinishStore(PurchaseOutcome outcome)
        {
            storeRoot.gameObject.SetActive(false);
            var cb = storeDone;
            storeDone = null;
            cb?.Invoke(outcome);
        }

        // ------------------------------------------------------------ per frame

        public void Tick(float dt)
        {
            if (root == null) return;
            if (toastTime > 0f)
            {
                toastTime -= dt;
                toastGroup.alpha = Mathf.Clamp01(toastTime / 0.3f);
            }
            if (busyRoot.gameObject.activeSelf)
            {
                busyTime += dt;
                // Never leave the player stuck behind a blocker if a store callback goes missing.
                if (busyTime > 90f) HideBusy();
            }
            if (IsAdShowing)
            {
                // The show watchdog may have ended this show already.
                if (adHandle == null || adHandle.IsFinished)
                {
                    adHandle = null;
                    adRoot.gameObject.SetActive(false);
                    return;
                }
                adElapsed += dt;
                UpdateAd();
            }
        }

        // ------------------------------------------------------------ construction

        void Build()
        {
            if (root != null) return;

            var canvasGo = new GameObject("MonetizationCanvas", typeof(RectTransform));
            canvasGo.layer = 5;
            canvasGo.transform.SetParent(parent, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 900;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(UILayout.ReferenceWidth, UILayout.ReferenceHeight);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0f;
            canvasGo.AddComponent<GraphicRaycaster>();
            root = (RectTransform)canvasGo.transform;

            BuildBanner();
            BuildAd();
            BuildStore();
            BuildBusy();
            BuildToast();
        }

        static UITheme T => UITheme.Current;

        void BuildToast()
        {
            var bg = UIFactory.RoundRect("Toast", root, new Color(T.ink.r, T.ink.g, T.ink.b, 0.94f), 18f);
            toastRoot = UIFactory.Place(bg.rectTransform, new Vector2(0.5f, 0f), new Vector2(T.U(352f), T.U(54f)), new Vector2(0, UILayout.BottomBar + 170));
            UIFactory.Stroke("Hairline", bg.transform, T.stroke, 18f);
            toastGroup = bg.gameObject.AddComponent<CanvasGroup>();
            toastGroup.alpha = 0f;
            toastGroup.blocksRaycasts = false;
            toastText = UIFactory.Text("Text", bg.transform, "", TextRole.Label, T.labelS, Palette.Text);
            UIFactory.Stretch(toastText.rectTransform, T.U(16f), T.U(16f), T.U(4f), T.U(4f));
        }

        void BuildBusy()
        {
            var scrim = UIFactory.Image("Busy", root, Palette.Scrim, null, raycast: true);
            busyRoot = UIFactory.Stretch(scrim.rectTransform);
            var card = UIFactory.Card("Card", busyRoot, T.radiusLg);
            UIFactory.Place(card, new Vector2(0.5f, 0.5f), new Vector2(T.U(300f), T.U(130f)));
            var spinner = UIFactory.Icon("Spinner", card, UISprites.Spinner, Color.white, 34f);
            UIFactory.Place(spinner.rectTransform, new Vector2(0.5f, 1f), spinner.rectTransform.sizeDelta, new Vector2(0f, -T.U(20f)));
            UIFactory.Gradient(spinner, T.sunsetA, T.sunsetB);
            spinner.gameObject.AddComponent<Spin>().degreesPerSecond = -300f;
            busyText = UIFactory.Text("Text", card, "", TextRole.Label, T.label, Palette.Text);
            UIFactory.Stretch(busyText.rectTransform, T.U(16f), T.U(16f), T.U(62f), T.U(12f));
            busyRoot.gameObject.SetActive(false);
        }

        void BuildBanner()
        {
            var bg = UIFactory.Image("TestBanner", root, TestYellow, null, raycast: true);
            bannerRoot = UIFactory.Band(bg.rectTransform, 0f, TestBannerHeight);
            var edge = UIFactory.Image("Edge", bannerRoot, TestInk);
            UIFactory.Band(edge.rectTransform, 1f, 4f);
            bannerText = UIFactory.Text("Text", bannerRoot, "", TextRole.Label, 12f, TestInk);
            UIFactory.Stretch(bannerText.rectTransform, 20, 20, 8, 6);
            bannerRoot.gameObject.SetActive(false);
        }

        void BuildAd()
        {
            var bg = UIFactory.Image("TestAd", root, Color.white, null, raycast: true);
            adRoot = UIFactory.Stretch(bg.rectTransform);
            UIFactory.Gradient(bg, T.surfaceLow, T.bgBottom);

            var banner = UIFactory.Image("Banner", adRoot, TestYellow);
            UIFactory.Band(banner.rectTransform, 1f, T.U(118f));
            var big = UIFactory.Label("TestAd", banner.transform, "TEST AD", T.F(46f), TestInk);
            big.font = UIFactory.FontFor(TextRole.Title);
            UIFactory.Place(big.rectTransform, new Vector2(0.5f, 0f), new Vector2(T.U(360f), T.U(56f)), new Vector2(0, T.U(30f)));
            var sub = UIFactory.Text("Sub", banner.transform, "Not a real advertisement. Development build only.", TextRole.Label, 12f, TestInk);
            UIFactory.Place(sub.rectTransform, new Vector2(0.5f, 0f), new Vector2(T.U(360f), T.U(22f)), new Vector2(0, T.U(8f)));

            var artCard = UIFactory.RoundRect("ArtFrame", adRoot, Color.white, T.radiusLg);
            UIFactory.Stretch(artCard.rectTransform, T.U(20f), T.U(20f), T.U(140f), T.U(190f));
            artCard.color = new Color(1f, 1f, 1f, 0.12f);
            adArt = UIFactory.RoundRect("Art", artCard.transform, Color.gray, T.radiusLg - 3f);
            UIFactory.Stretch(adArt.rectTransform, T.U(4f), T.U(4f), T.U(4f), T.U(4f));

            adKind = UIFactory.Text("Kind", adRoot, "", TextRole.Title, 28f, Palette.Text);
            UIFactory.Place(adKind.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(T.U(340f), T.U(90f)), new Vector2(0, T.U(30f)));
            var brand = UIFactory.Text("Brand", adRoot, "Snaploop dummy ad adapter", TextRole.Label, T.labelS, new Color(1f, 1f, 1f, 0.8f));
            UIFactory.Place(brand.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(T.U(340f), T.U(24f)), new Vector2(0, -T.U(30f)));

            var track = UIFactory.RoundRect("Track", adRoot, new Color(1f, 1f, 1f, 0.15f), 4f);
            UIFactory.Place(track.rectTransform, new Vector2(0.5f, 0f), new Vector2(T.U(330f), T.U(8f)), new Vector2(0, T.U(160f)));
            var fill = UIFactory.RoundRect("Fill", track.transform, TestYellow, 4f);
            adProgress = fill.rectTransform;
            adProgress.anchorMin = Vector2.zero;
            adProgress.anchorMax = new Vector2(0f, 1f);
            adProgress.offsetMin = adProgress.offsetMax = Vector2.zero;

            adCountdown = UIFactory.Text("Countdown", adRoot, "", TextRole.Label, T.label, Palette.Text);
            UIFactory.Place(adCountdown.rectTransform, new Vector2(0.5f, 0f), new Vector2(T.U(330f), T.U(28f)), new Vector2(0, T.U(124f)));

            float h = UIFactory.ButtonHeight(ButtonSize.Large);
            adClose = UIFactory.JuicyButton("Close", adRoot, ButtonTone.Neutral, ButtonSize.Large, "Close", () => FinishAd(false), UISprites.Close);
            UIFactory.Place((RectTransform)adClose.transform, new Vector2(0.5f, 0f), new Vector2(T.U(300f), h), new Vector2(0, T.U(40f)));
            adCloseLabel = UIFactory.ButtonLabel(adClose);

            adClaim = UIFactory.JuicyButton("Claim", adRoot, ButtonTone.Reward, ButtonSize.Large, "Get reward", () => FinishAd(true), UISprites.Sparkle);
            UIFactory.Place((RectTransform)adClaim.transform, new Vector2(0.5f, 0f), new Vector2(T.U(300f), h), new Vector2(0, T.U(40f)));

            adRoot.gameObject.SetActive(false);
        }

        void BuildStore()
        {
            var scrim = UIFactory.Image("TestStore", root, Palette.Scrim, null, raycast: true);
            storeRoot = UIFactory.Stretch(scrim.rectTransform);
            var c = UIFactory.Card("Card", storeRoot, T.radiusLg);
            UIFactory.Place(c, new Vector2(0.5f, 0.5f), new Vector2(T.U(340f), T.U(360f)));

            var banner = UIFactory.RoundRect("Banner", c, TestYellow, 12f);
            UIFactory.Place(banner.rectTransform, new Vector2(0.5f, 1f), new Vector2(T.U(230f), T.U(48f)), new Vector2(0, T.U(22f)));
            UIFactory.Stroke("Ink", banner.transform, T.InkColor, 12f, T.inkWidth * 0.5f);
            var bannerText = UIFactory.Label("Text", banner.transform, "TEST STORE", T.F(26f), TestInk);
            bannerText.font = UIFactory.FontFor(TextRole.Title);
            UIFactory.Stretch(bannerText.rectTransform, 0, 0, 0, T.U(2f));

            storeTitle = UIFactory.Text("Title", c, "", TextRole.Title, T.titleS, Palette.Text);
            UIFactory.Place(storeTitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(T.U(310f), T.U(36f)), new Vector2(0, -T.U(48f)));
            storePrice = UIFactory.Text("Price", c, "", TextRole.Number, T.titleM, Palette.Star);
            UIFactory.Place(storePrice.rectTransform, new Vector2(0.5f, 1f), new Vector2(T.U(310f), T.U(40f)), new Vector2(0, -T.U(88f)));
            var note = UIFactory.Text("Note", c, "Fake purchase for testing. No money is charged.\nEditor and development builds only.", TextRole.Body, T.bodyS, Palette.TextDim);
            UIFactory.Place(note.rectTransform, new Vector2(0.5f, 1f), new Vector2(T.U(300f), T.U(44f)), new Vector2(0, -T.U(136f)));

            float h = UIFactory.ButtonHeight(ButtonSize.Large), inner = T.U(16f), gap = T.U(5f);
            var buy = UIFactory.JuicyButton("Buy", c, ButtonTone.Primary, ButtonSize.Large, "Buy (test)", () => FinishStore(PurchaseOutcome.Purchased));
            var brt = (RectTransform)buy.transform;
            brt.anchorMin = new Vector2(0f, 0f); brt.anchorMax = new Vector2(0.5f, 0f); brt.pivot = new Vector2(0.5f, 0f);
            brt.offsetMin = new Vector2(inner, T.U(74f)); brt.offsetMax = new Vector2(-gap, T.U(74f) + h);
            var cancel = UIFactory.JuicyButton("Cancel", c, ButtonTone.Neutral, ButtonSize.Large, "Cancel", () => FinishStore(PurchaseOutcome.Cancelled));
            var crt = (RectTransform)cancel.transform;
            crt.anchorMin = new Vector2(0.5f, 0f); crt.anchorMax = new Vector2(1f, 0f); crt.pivot = new Vector2(0.5f, 0f);
            crt.offsetMin = new Vector2(gap, T.U(74f)); crt.offsetMax = new Vector2(-inner, T.U(74f) + h);
            var fail = UIFactory.JuicyButton("Fail", c, ButtonTone.Neutral, ButtonSize.Medium, "Simulate a failed payment", () => FinishStore(PurchaseOutcome.Failed));
            var fl = UIFactory.ButtonLabel(fail);
            fl.font = UIFactory.FontFor(TextRole.Label);
            fl.fontSize = T.F(T.labelS);
            var frt = (RectTransform)fail.transform;
            frt.anchorMin = new Vector2(0f, 0f); frt.anchorMax = new Vector2(1f, 0f); frt.pivot = new Vector2(0.5f, 0f);
            frt.offsetMin = new Vector2(inner, T.U(16f)); frt.offsetMax = new Vector2(-inner, T.U(16f) + UIFactory.ButtonHeight(ButtonSize.Medium));

            storeRoot.gameObject.SetActive(false);
        }
    }
}
