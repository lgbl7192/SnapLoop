using System;
using System.Collections.Generic;
using Snaploop.Core;
using Snaploop.Levels;
using UnityEngine;
using UnityEngine.UI;

namespace Snaploop.UI
{
    /// <summary>
    /// Theme packs: every pack from packs.json as a card (seasonal packs first), with unlock-for-tokens and buy on
    /// locked packs. Picking an unlocked pack opens it in the hub. The token wallet shows here and on the hub only.
    /// </summary>
    public sealed class PackScreen : UIScreen
    {
        static UITheme T => UITheme.Current;

        readonly MonoBehaviour host;
        readonly Action<PackInfo> onOpenPack;
        readonly RectTransform header, list, content;
        readonly ScrollRect scroll;
        readonly TokenPill tokenPill;
        readonly List<PackCard> cards = new List<PackCard>();
        readonly Text footer;

        public PackScreen(Transform safeRoot, MonoBehaviour coroutineHost, Action onBack, Action<PackInfo> onOpenPack)
        {
            host = coroutineHost;
            this.onOpenPack = onOpenPack;
            float pad = T.U(T.screenPad);
            Root = UIFactory.Stretch(UIFactory.Rect("Packs", safeRoot));

            float headerH = T.U(68f);
            header = UIFactory.Band(UIFactory.Rect("Header", Root), 1f, headerH);
            var back = UIFactory.RoundButton("Back", header, UISprites.Back, () => onBack());
            UIFactory.Place((RectTransform)back.transform, new Vector2(0f, 0.5f), ((RectTransform)back.transform).sizeDelta, new Vector2(pad, 0f));
            var title = UIFactory.Text("Title", header, "Theme packs", TextRole.Title, T.titleS, Color.white, TextAnchor.MiddleLeft);
            UIFactory.Stretch(title.rectTransform, pad + T.U(T.roundButton + 12f), T.U(140f), 0, 0);
            title.horizontalOverflow = HorizontalWrapMode.Overflow;
            var pillHolder = UIFactory.Rect("Wallet", header);
            pillHolder.anchorMin = new Vector2(1f, 0f); pillHolder.anchorMax = new Vector2(1f, 1f); pillHolder.pivot = new Vector2(1f, 0.5f);
            pillHolder.sizeDelta = new Vector2(T.U(130f), 0f);
            pillHolder.anchoredPosition = new Vector2(-pad, 0f);
            var pillRow = pillHolder.gameObject.AddComponent<HorizontalLayoutGroup>();
            pillRow.childAlignment = TextAnchor.MiddleRight;
            pillRow.childControlWidth = true;
            pillRow.childControlHeight = false;
            pillRow.childForceExpandWidth = pillRow.childForceExpandHeight = false;
            tokenPill = new TokenPill("TokenPill", pillHolder, 36f);

            var viewportImage = UIFactory.Image("Viewport", Root, new Color(0, 0, 0, 0), null, raycast: true);
            var viewport = UIFactory.Stretch(viewportImage.rectTransform, 0, 0, headerH, 0);
            viewportImage.gameObject.AddComponent<RectMask2D>();
            scroll = viewportImage.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.scrollSensitivity = 30f;
            scroll.decelerationRate = 0.12f;

            content = UIFactory.Rect("Content", viewport);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.sizeDelta = Vector2.zero;
            var col = content.gameObject.AddComponent<VerticalLayoutGroup>();
            col.padding = new RectOffset((int)pad, (int)pad, (int)T.U(8f), (int)T.U(32f));
            col.spacing = T.U(T.sectionGap);
            col.childControlWidth = col.childControlHeight = true;
            col.childForceExpandWidth = true;
            col.childForceExpandHeight = false;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = content;
            scroll.viewport = viewport;

            list = UIFactory.Rect("List", content);
            var listCol = list.gameObject.AddComponent<VerticalLayoutGroup>();
            listCol.spacing = T.U(T.sectionGap);
            listCol.childControlWidth = listCol.childControlHeight = true;
            listCol.childForceExpandWidth = true;
            listCol.childForceExpandHeight = false;

            footer = UIFactory.Text("HowTo", content, "", TextRole.Body, T.bodyS, T.textDim);
            footer.gameObject.AddComponent<LayoutElement>().preferredHeight = T.U(64f);

            Tokens.Changed += OnChanged;
            GameServices.PacksChanged += OnChanged;
            Hide();
        }

        void OnChanged()
        {
            if (IsVisible) tokenPill.Refresh(true);
        }

        public override void Show()
        {
            base.Show();
            Rebuild();
            tokenPill.Refresh(false);
            scroll.StopMovement();
            content.anchoredPosition = Vector2.zero;
            UITween.Get(Root).Fade(0f, 1f).Move(new Vector2(T.U(40f), 0f), Vector2.zero).Play(T.medium * 0.8f, EaseType.OutCubic);
            for (int i = 0; i < cards.Count; i++)
                UITween.Get(cards[i].Root).Fade(0f, 1f).Scale(0.92f, 1f).Play(T.medium, EaseType.OutBack, 0.05f + 0.05f * i);
        }

        /// <summary>Re-reads the order (the seasonal badge depends on the month) and binds a card per pack.</summary>
        void Rebuild()
        {
            var packs = PackAccess.InDisplayOrder();
            while (cards.Count < packs.Count)
            {
                var card = new PackCard("Pack" + cards.Count, list, host, OpenPack);
                card.Root.gameObject.AddComponent<LayoutElement>().preferredHeight = T.U(PackCard.HeightDp);
                cards.Add(card);
            }
            for (int i = 0; i < cards.Count; i++)
            {
                bool used = i < packs.Count;
                cards[i].Root.gameObject.SetActive(used);
                if (used)
                {
                    cards[i].Root.name = "Pack_" + packs[i].id;
                    cards[i].Bind(packs[i]);
                }
            }
            footer.text = "Clear puzzles to earn tokens: " + TokenRules.OneStar + ", " + TokenRules.TwoStars + " or " +
                          TokenRules.ThreeStars + " for 1, 2 or 3 stars. Beat your best for the difference.";
        }

        /// <summary>Locked packs open too: the hub shows their tiles as previews under the unlock card.</summary>
        void OpenPack(PackInfo pack)
        {
            if (pack != null && !pack.IsEmpty) onOpenPack?.Invoke(pack);
        }
    }
}
