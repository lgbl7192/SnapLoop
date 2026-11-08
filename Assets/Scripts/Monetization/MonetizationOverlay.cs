using System;
using Snaploop.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Snaploop.Monetization
{
    /// <summary>
    /// Monetization UI on its own canvas above the game: toasts, a busy blocker, the TEST AD overlay
    /// and the TEST STORE dialog. Built lazily on first use.
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
        Action<AdShowResult> adDone;
        bool adRewarded;
        float adDuration, adElapsed;

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

        /// <param name="rewarded">Rewarded: the reward needs the full <paramref name="seconds"/>. Interstitial: closable after <paramref name="seconds"/>.</param>
        public void ShowTestAd(bool rewarded, string placement, float seconds, Action<AdShowResult> onDone)
        {
            Build();
            adDone = onDone;
            adRewarded = rewarded;
            adDuration = Mathf.Max(0.01f, seconds);
            adElapsed = 0f;
            adKind.text = (rewarded ? "Rewarded ad" : "Interstitial ad") + "\nplacement: " + (string.IsNullOrEmpty(placement) ? "(default)" : placement);
            adCloseLabel.text = rewarded ? "Close (no hint)" : "Close";
            adClose.gameObject.SetActive(rewarded);
            adClaim.gameObject.SetActive(false);
            adRoot.SetAsLastSibling();
            adRoot.gameObject.SetActive(true);
            UpdateAd();
        }

        void FinishAd(AdShowResult result)
        {
            adRoot.gameObject.SetActive(false);
            var cb = adDone;
            adDone = null;
            cb?.Invoke(result);
        }

        void UpdateAd()
        {
            float k = Mathf.Clamp01(adElapsed / adDuration);
            adProgress.anchorMax = new Vector2(k, 1f);
            adArt.color = Color.HSVToRGB(Mathf.Repeat(adElapsed * 0.12f, 1f), 0.45f, 0.55f);
            int left = Mathf.CeilToInt(adDuration - adElapsed);
            if (adRewarded)
            {
                adCountdown.text = k < 1f ? "Hint unlocks in " + left + " s" : "Hint unlocked";
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

            BuildAd();
            BuildStore();
            BuildBusy();
            BuildToast();
        }

        void BuildToast()
        {
            var bg = UIFactory.Panel("Toast", root, new Color(0f, 0f, 0f, 0.85f), raycast: false);
            toastRoot = UIFactory.Place(bg.rectTransform, new Vector2(0.5f, 0f), new Vector2(940, 130), new Vector2(0, UILayout.BottomBar + 170));
            toastGroup = bg.gameObject.AddComponent<CanvasGroup>();
            toastGroup.alpha = 0f;
            toastGroup.blocksRaycasts = false;
            toastText = UIFactory.Label("Text", bg.transform, "", 36, Palette.Text);
            UIFactory.Stretch(toastText.rectTransform, 24, 24, 8, 8);
        }

        void BuildBusy()
        {
            var scrim = UIFactory.Image("Busy", root, Palette.Scrim, null, raycast: true);
            busyRoot = UIFactory.Stretch(scrim.rectTransform);
            var card = UIFactory.Panel("Card", busyRoot, Palette.Surface, raycast: false);
            UIFactory.Place(card.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(760, 220));
            busyText = UIFactory.Label("Text", card.transform, "", 42, Palette.Text);
            UIFactory.Stretch(busyText.rectTransform, 30, 30, 10, 10);
            busyRoot.gameObject.SetActive(false);
        }

        void BuildAd()
        {
            var bg = UIFactory.Image("TestAd", root, new Color(0.04f, 0.04f, 0.05f, 1f), null, raycast: true);
            adRoot = UIFactory.Stretch(bg.rectTransform);

            adArt = UIFactory.Image("Art", adRoot, Color.gray);
            UIFactory.Stretch(adArt.rectTransform, 0, 0, 380, 420);

            var banner = UIFactory.Image("Banner", adRoot, TestYellow);
            UIFactory.Band(banner.rectTransform, 1f, 300f);
            var big = UIFactory.Label("TestAd", banner.transform, "TEST AD", 110, TestInk, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Place(big.rectTransform, new Vector2(0.5f, 0f), new Vector2(1000, 140), new Vector2(0, 110));
            var sub = UIFactory.Label("Sub", banner.transform, "Not a real advertisement. Development build only.", 34, TestInk);
            UIFactory.Place(sub.rectTransform, new Vector2(0.5f, 0f), new Vector2(1000, 60), new Vector2(0, 40));

            adKind = UIFactory.Label("Kind", adRoot, "", 56, Palette.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Place(adKind.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(1000, 220), new Vector2(0, 80));
            var brand = UIFactory.Label("Brand", adRoot, "Snaploop dummy ad adapter", 34, Palette.TextDim);
            UIFactory.Place(brand.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(1000, 60), new Vector2(0, -60));

            var track = UIFactory.Image("Track", adRoot, new Color(1f, 1f, 1f, 0.15f));
            UIFactory.Place(track.rectTransform, new Vector2(0.5f, 0f), new Vector2(900, 14), new Vector2(0, 360));
            var fill = UIFactory.Image("Fill", track.transform, TestYellow);
            adProgress = fill.rectTransform;
            adProgress.anchorMin = Vector2.zero;
            adProgress.anchorMax = new Vector2(0f, 1f);
            adProgress.offsetMin = adProgress.offsetMax = Vector2.zero;

            adCountdown = UIFactory.Label("Countdown", adRoot, "", 40, Palette.Text);
            UIFactory.Place(adCountdown.rectTransform, new Vector2(0.5f, 0f), new Vector2(900, 70), new Vector2(0, 270));

            adClose = UIFactory.Button("Close", adRoot, "Close", Palette.SurfaceHigh,
                () => FinishAd(adRewarded ? AdShowResult.Skipped : AdShowResult.Completed), 40);
            UIFactory.Place((RectTransform)adClose.transform, new Vector2(0.5f, 0f), new Vector2(520, 130), new Vector2(0, 100));
            adCloseLabel = UIFactory.ButtonLabel(adClose);

            adClaim = UIFactory.Button("Claim", adRoot, "Get hint", Palette.Accent, () => FinishAd(AdShowResult.Completed), 46);
            UIFactory.Place((RectTransform)adClaim.transform, new Vector2(0.5f, 0f), new Vector2(520, 130), new Vector2(0, 100));

            adRoot.gameObject.SetActive(false);
        }

        void BuildStore()
        {
            var scrim = UIFactory.Image("TestStore", root, Palette.Scrim, null, raycast: true);
            storeRoot = UIFactory.Stretch(scrim.rectTransform);
            var card = UIFactory.Panel("Card", storeRoot, Palette.Surface);
            var c = UIFactory.Place(card.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(900, 900));

            var banner = UIFactory.Panel("Banner", c, TestYellow, raycast: false);
            UIFactory.Place(banner.rectTransform, new Vector2(0.5f, 1f), new Vector2(840, 110), new Vector2(0, -30));
            var bannerText = UIFactory.Label("Text", banner.transform, "TEST STORE", 60, TestInk, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Stretch(bannerText.rectTransform);

            storeTitle = UIFactory.Label("Title", c, "", 62, Palette.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Place(storeTitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(840, 90), new Vector2(0, -180));
            storePrice = UIFactory.Label("Price", c, "", 56, Palette.Star, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Place(storePrice.rectTransform, new Vector2(0.5f, 1f), new Vector2(840, 80), new Vector2(0, -275));
            var note = UIFactory.Label("Note", c, "Fake purchase for testing. No money is charged.\nEditor and development builds only.", 34, Palette.TextDim);
            UIFactory.Place(note.rectTransform, new Vector2(0.5f, 1f), new Vector2(840, 110), new Vector2(0, -370));

            var buy = UIFactory.Button("Buy", c, "Buy (test)", Palette.Accent, () => FinishStore(PurchaseOutcome.Purchased), 46);
            UIFactory.Place((RectTransform)buy.transform, new Vector2(0.5f, 0f), new Vector2(380, 130), new Vector2(-200, 190));
            var cancel = UIFactory.Button("Cancel", c, "Cancel", Palette.SurfaceHigh, () => FinishStore(PurchaseOutcome.Cancelled), 46);
            UIFactory.Place((RectTransform)cancel.transform, new Vector2(0.5f, 0f), new Vector2(380, 130), new Vector2(200, 190));
            var fail = UIFactory.Button("Fail", c, "Simulate a failed payment", Palette.SurfaceHigh, () => FinishStore(PurchaseOutcome.Failed), 32);
            UIFactory.Place((RectTransform)fail.transform, new Vector2(0.5f, 0f), new Vector2(780, 100), new Vector2(0, 50));

            storeRoot.gameObject.SetActive(false);
        }
    }
}
