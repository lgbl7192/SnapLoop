using System.Text;
using Snaploop.Core;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Snaploop.UI
{
    /// <summary>Shorthand colours for code outside the UI (capture, monetization). The source of truth is <see cref="UITheme"/>.</summary>
    public static class Palette
    {
        static UITheme T => UITheme.Current;
        public static Color Background => T.bgBottom;
        public static Color Surface => T.surface;
        public static Color SurfaceHigh => T.surfaceHigh;
        /// <summary>Snaploop's signature colour (golden-hour coral).</summary>
        public static Color Accent => T.sunsetB;
        public static Color Accent2 => T.cyan;
        public static Color Text => T.text;
        public static Color TextDim => T.textDim;
        public static Color Star => T.star;
        /// <summary>Every button that plays a rewarded ad is green with the AD badge (<see cref="UIFactory.SetRewardedLook(Button,bool)"/>).</summary>
        public static Color Rewarded => T.green;
        public static Color StarEmpty => T.surfaceSunk;
        public static Color Scrim => T.scrim;
    }

    /// <summary>Per-button references so tone and the AD badge can change after creation.</summary>
    public sealed class JuicyButtonParts : MonoBehaviour
    {
        public ButtonTone tone;
        public ButtonTone baseTone;
        public Image face, lip;
        public UIGradient faceGradient;
        public InkOutline labelEmboss;
        public RectTransform content;
        public Text label, subLabel;
        public Image icon;
    }

    /// <summary>
    /// Builds the uGUI in code (no prefabs) from <see cref="UITheme"/> tokens and <see cref="UISprites"/> shapes:
    /// sticker-style 3D buttons, cards, ribbons, chips, stars, rings and switches.
    /// </summary>
    public static class UIFactory
    {
        static Font legacy;
        static UITheme T => UITheme.Current;

        /// <summary>Body/label font for code that draws text itself (Nunito ExtraBold).</summary>
        public static Font Font => T.labelFont != null ? T.labelFont : Legacy;

        static Font Legacy => legacy != null ? legacy : (legacy = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));

        public static Font FontFor(TextRole role)
        {
            Font f;
            switch (role)
            {
                case TextRole.Title:
                case TextRole.Number: f = T.displayFont; break;
                case TextRole.Label: f = T.labelFont; break;
                case TextRole.Caps: f = T.heavyFont; break;
                default: f = T.bodyFont; break;
            }
            return f != null ? f : Legacy;
        }

        public static Sprite Rounded => UISprites.Rounded;
        public static Sprite Circle => UISprites.Circle;
        public static Sprite Star => UISprites.Star;
        public static Sprite Check => UISprites.Check;

        // ------------------------------------------------------------ layout

        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = 5; // UI
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        public static RectTransform Stretch(RectTransform rt, float left = 0, float right = 0, float top = 0, float bottom = 0)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
            return rt;
        }

        /// <summary>Anchors at a normalized point with a fixed size.</summary>
        public static RectTransform Place(RectTransform rt, Vector2 anchor, Vector2 size, Vector2 offset = default)
        {
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = anchor;
            rt.sizeDelta = size;
            rt.anchoredPosition = offset;
            return rt;
        }

        /// <summary>Full-width band anchored to the top (anchorY = 1) or bottom (anchorY = 0).</summary>
        public static RectTransform Band(RectTransform rt, float anchorY, float height, float inset = 0f)
        {
            rt.anchorMin = new Vector2(0f, anchorY);
            rt.anchorMax = new Vector2(1f, anchorY);
            rt.pivot = new Vector2(0.5f, anchorY);
            rt.sizeDelta = new Vector2(-2f * inset, height);
            rt.anchoredPosition = Vector2.zero;
            return rt;
        }

        /// <summary>Horizontal band at a fixed distance from the top (anchorY = 1) or bottom (anchorY = 0) edge.</summary>
        public static RectTransform Row(RectTransform rt, float anchorY, float height, float fromEdge, float inset = 0f)
        {
            Band(rt, anchorY, height, inset);
            rt.anchoredPosition = new Vector2(0f, anchorY > 0.5f ? -fromEdge : fromEdge);
            return rt;
        }

        // ------------------------------------------------------------ primitives

        public static Image Image(string name, Transform parent, Color color, Sprite sprite = null, bool raycast = false)
        {
            var rt = Rect(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = raycast;
            if (sprite != null)
            {
                img.sprite = sprite;
                img.type = sprite.border.sqrMagnitude > 0 ? UnityEngine.UI.Image.Type.Sliced : UnityEngine.UI.Image.Type.Simple;
            }
            return img;
        }

        /// <summary>Sets the visible corner radius of an image using a <see cref="UISprites"/> 9-slice.</summary>
        public static Image Radius(Image img, float radiusDp)
        {
            float units = Mathf.Max(1f, T.U(radiusDp));
            img.pixelsPerUnitMultiplier = UISprites.SpriteRadius / units;
            return img;
        }

        public static Image RoundRect(string name, Transform parent, Color color, float radiusDp, bool raycast = false)
        {
            return Radius(Image(name, parent, color, UISprites.Rounded, raycast), radiusDp);
        }

        /// <summary>Soft drop shadow behind a rect: stretch it over the caster, it extends by the blur and drops by the offset.</summary>
        public static Image SoftShadow(string name, Transform parent, float blurDp, float dropDp, float alpha)
        {
            var img = Image(name, parent, new Color(0.02f, 0.01f, 0.08f, alpha), UISprites.Shadow);
            float b = T.U(blurDp);
            img.pixelsPerUnitMultiplier = UISprites.ShadowBlur / b;
            Stretch(img.rectTransform, -b, -b, -b + T.U(dropDp), -b - T.U(dropDp));
            return img;
        }

        public static Image Icon(string name, Transform parent, Sprite sprite, Color color, float sizeDp)
        {
            var img = Image(name, parent, color, sprite);
            img.rectTransform.sizeDelta = new Vector2(T.U(sizeDp), T.U(sizeDp));
            img.preserveAspect = true;
            return img;
        }

        public static UIGradient Gradient(Graphic g, Color top, Color bottom, GradientDirection dir = GradientDirection.Vertical)
        {
            var grad = g.GetComponent<UIGradient>();
            if (grad == null) grad = g.gameObject.AddComponent<UIGradient>();
            grad.Set(top, bottom, dir);
            return grad;
        }

        /// <summary>Hairline or ink stroke traced on a rounded rect's edge (child stretched over the parent).</summary>
        public static Image Stroke(string name, Transform parent, Color color, float radiusDp, float inflateDp = 0f)
        {
            var img = Radius(Image(name, parent, color, UISprites.Stroke), radiusDp + inflateDp);
            float i = T.U(inflateDp);
            Stretch(img.rectTransform, -i, -i, -i, -i);
            return img;
        }

        public static Image Panel(string name, Transform parent, Color color, bool raycast = true)
        {
            return RoundRect(name, parent, color, T.radiusMd, raycast);
        }

        // ------------------------------------------------------------ text

        /// <summary>Legacy label: Bold maps to the label weight, everything else to body copy. Sizes are in canvas units.</summary>
        public static Text Label(string name, Transform parent, string text, int size, Color color,
            TextAnchor align = TextAnchor.MiddleCenter, FontStyle style = FontStyle.Normal)
        {
            var t = MakeText(name, parent, text, size, color, align);
            t.font = FontFor(style == FontStyle.Bold || style == FontStyle.BoldAndItalic ? TextRole.Label : TextRole.Body);
            return t;
        }

        /// <summary>Text in a type role at a size in dp. Titles and numbers get the display face with an ink outline.</summary>
        public static Text Text(string name, Transform parent, string text, TextRole role, float sizeDp, Color color,
            TextAnchor align = TextAnchor.MiddleCenter)
        {
            var t = MakeText(name, parent, role == TextRole.Caps ? Track(text) : text, T.F(sizeDp), color, align);
            t.font = FontFor(role);
            if (role == TextRole.Title || role == TextRole.Number)
                Ink(t, Mathf.Clamp(sizeDp * 0.06f, 1.2f, 2.6f), sizeDp * 0.06f);
            return t;
        }

        static Text MakeText(string name, Transform parent, string text, int size, Color color, TextAnchor align)
        {
            var rt = Rect(name, parent);
            var t = rt.gameObject.AddComponent<Text>();
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.fontStyle = FontStyle.Normal;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.supportRichText = false;
            t.lineSpacing = 0.95f;
            return t;
        }

        /// <summary>Sticker ink: an outline in the theme ink plus an optional hard drop below.</summary>
        public static InkOutline Ink(Graphic g, float widthDp, float dropDp = 0f, Color? dropColor = null)
        {
            var ink = g.GetComponent<InkOutline>();
            if (ink == null) ink = g.gameObject.AddComponent<InkOutline>();
            ink.Set(T.InkColor, T.U(widthDp), dropColor ?? T.InkColor, T.U(dropDp));
            return ink;
        }

        /// <summary>Caps label with hair-space tracking (Nunito has U+200A; the display face does not).</summary>
        public static string Track(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            var sb = new StringBuilder(s.Length * 2);
            for (int i = 0; i < s.Length; i++)
            {
                if (i > 0) sb.Append('\u200A');
                sb.Append(char.ToUpperInvariant(s[i]));
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------ buttons

        public static float ButtonHeight(ButtonSize size)
        {
            T.Metrics(size, out float face, out float lip, out _, out _);
            return T.U(face + lip);
        }

        static float ButtonRadius(ButtonSize size) => size == ButtonSize.Hero ? 22f : size == ButtonSize.Large ? 19f : 16f;

        /// <summary>
        /// Sticker-style 3D button: darker lip under a gradient face with gloss and ink outline; the face sinks onto
        /// the lip when pressed. Size it with Place/Stretch; the face fills everything above the lip.
        /// </summary>
        public static Button JuicyButton(string name, Transform parent, ButtonTone tone, ButtonSize size, string label,
            UnityAction onClick, Sprite icon = null, string subLabel = null)
        {
            T.Metrics(size, out float faceDp, out float lipDp, out float labelDp, out float subDp);
            float radius = ButtonRadius(size);
            float lip = T.U(lipDp);

            var root = Rect(name, parent);
            root.sizeDelta = new Vector2(T.U(220f), T.U(faceDp + lipDp));
            var hit = root.gameObject.AddComponent<Image>();
            hit.color = Color.clear;
            hit.raycastTarget = true;

            SoftShadow("Shadow", root, 10f, 5f, 0.45f);
            var lipImg = RoundRect("Lip", root, Color.white, radius);
            Stretch(lipImg.rectTransform);
            Stroke("Ink", lipImg.transform, T.InkColor, radius, T.inkWidth * 0.5f);

            var face = RoundRect("Face", root, Color.white, radius);
            Stretch(face.rectTransform, 0, 0, 0, lip);
            var grad = Gradient(face, Color.white, Color.white);

            var gloss = RoundRect("Gloss", face.transform, Color.white, radius * 0.7f);
            var grt = gloss.rectTransform;
            grt.anchorMin = new Vector2(0f, 0.52f);
            grt.anchorMax = new Vector2(1f, 1f);
            grt.offsetMin = new Vector2(T.U(6f), 0f);
            grt.offsetMax = new Vector2(-T.U(6f), -T.U(3f));
            Gradient(gloss, new Color(1, 1, 1, 0.42f), new Color(1, 1, 1, 0.04f));
            Stroke("FaceInk", face.transform, new Color(T.ink.r, T.ink.g, T.ink.b, 0.35f), radius);

            var content = Rect("Content", face.transform);
            Stretch(content, T.U(14f), T.U(14f), 0, 0);
            var row = content.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.childAlignment = TextAnchor.MiddleCenter;
            row.spacing = T.U(10f);
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = row.childForceExpandHeight = false;

            var parts = root.gameObject.AddComponent<JuicyButtonParts>();
            parts.face = face; parts.lip = lipImg; parts.faceGradient = grad; parts.content = content;

            if (icon != null)
            {
                var ic = Icon("Icon", content, icon, Color.white, labelDp * 1.05f);
                var le = ic.gameObject.AddComponent<LayoutElement>();
                le.preferredWidth = le.preferredHeight = T.U(labelDp * 1.05f);
                Ink(ic, 1.2f);
                parts.icon = ic;
            }

            var texts = Rect("Texts", content);
            var col = texts.gameObject.AddComponent<VerticalLayoutGroup>();
            col.childAlignment = TextAnchor.MiddleCenter;
            col.spacing = -T.U(2f);
            col.childControlWidth = col.childControlHeight = true;
            col.childForceExpandWidth = col.childForceExpandHeight = false;

            var lbl = MakeText("Label", texts, label ?? "", T.F(labelDp), Color.white, TextAnchor.MiddleCenter);
            lbl.font = FontFor(TextRole.Title);
            lbl.horizontalOverflow = HorizontalWrapMode.Overflow;
            parts.label = lbl;
            parts.labelEmboss = lbl.gameObject.AddComponent<InkOutline>();
            if (string.IsNullOrEmpty(label)) lbl.gameObject.SetActive(false);

            if (!string.IsNullOrEmpty(subLabel))
            {
                var sub = MakeText("Sub", texts, subLabel, T.F(subDp), new Color(1, 1, 1, 0.92f), TextAnchor.MiddleCenter);
                sub.font = FontFor(TextRole.Label);
                sub.horizontalOverflow = HorizontalWrapMode.Overflow;
                parts.subLabel = sub;
            }

            var btn = root.gameObject.AddComponent<Button>();
            btn.transition = Selectable.Transition.ColorTint;
            btn.targetGraphic = face;
            var colors = btn.colors;
            colors.normalColor = colors.highlightedColor = colors.pressedColor = colors.selectedColor = Color.white;
            colors.disabledColor = new Color(0.62f, 0.6f, 0.7f, 0.75f);
            colors.fadeDuration = 0.08f;
            btn.colors = colors;
            var nav = btn.navigation; nav.mode = Navigation.Mode.None; btn.navigation = nav;

            var press = root.gameObject.AddComponent<JuicyPress>();
            press.Bind(face.rectTransform, lip - T.U(1.5f), 0.03f);

            if (onClick != null)
            {
                btn.onClick.AddListener(Sfx.PlayClick);
                btn.onClick.AddListener(onClick);
            }
            parts.baseTone = tone;
            SetTone(btn, tone);
            return btn;
        }

        /// <summary>Round sticker button with an icon (back, settings, close). At least 48 dp, the minimum touch target.</summary>
        public static Button RoundButton(string name, Transform parent, Sprite icon, UnityAction onClick,
            ButtonTone tone = ButtonTone.Neutral, float sizeDp = 0f)
        {
            float size = Mathf.Max(T.minTouch, sizeDp > 0f ? sizeDp : T.roundButton);
            float lipDp = 4f;
            var btn = JuicyButton(name, parent, tone, ButtonSize.Medium, null, onClick);
            var parts = btn.GetComponent<JuicyButtonParts>();
            var rt = (RectTransform)btn.transform;
            rt.sizeDelta = new Vector2(T.U(size), T.U(size));
            Radius(parts.lip, size * 0.5f);
            Radius(parts.face, size * 0.5f);
            foreach (var s in btn.GetComponentsInChildren<Image>(true))
                if (s.sprite == UISprites.Stroke) Radius(s, size * 0.5f + (s.name == "Ink" ? T.inkWidth * 0.5f : 0f));
            parts.face.rectTransform.offsetMin = new Vector2(0f, T.U(lipDp));
            var gloss = parts.face.transform.Find("Gloss");
            if (gloss != null) Radius(gloss.GetComponent<Image>(), size * 0.32f);
            btn.GetComponent<JuicyPress>().Bind(parts.face.rectTransform, T.U(lipDp - 1f), 0.06f);
            if (icon != null)
            {
                var ic = Icon("Icon", parts.face.transform, icon, Color.white, size * 0.5f);
                Place(ic.rectTransform, new Vector2(0.5f, 0.5f), ic.rectTransform.sizeDelta);
                Ink(ic, 1f);
                parts.icon = ic;
            }
            return btn;
        }

        /// <summary>Legacy entry point (sizes in canvas units). Picks a tone from the old background colour.</summary>
        public static Button Button(string name, Transform parent, string label, Color bg, UnityAction onClick,
            int fontSize = 44, Color? textColor = null)
        {
            var tone = bg == Palette.Rewarded ? ButtonTone.Reward
                : bg == Palette.Accent || bg == T.orange ? ButtonTone.Primary
                : ButtonTone.Neutral;
            var b = JuicyButton(name, parent, tone, ButtonSize.Medium, label, onClick);
            var parts = b.GetComponent<JuicyButtonParts>();
            parts.label.fontSize = fontSize;
            if (textColor.HasValue) parts.label.color = textColor.Value;
            return b;
        }

        public static Text ButtonLabel(Button b)
        {
            var parts = b.GetComponent<JuicyButtonParts>();
            return parts != null && parts.label != null ? parts.label : b.GetComponentInChildren<Text>(true);
        }

        public static void SetButtonText(Button b, string label, string subLabel = null)
        {
            var parts = b.GetComponent<JuicyButtonParts>();
            if (parts == null) { var t = ButtonLabel(b); if (t != null) t.text = label; return; }
            parts.label.text = label;
            parts.label.gameObject.SetActive(!string.IsNullOrEmpty(label));
            if (parts.subLabel != null)
            {
                parts.subLabel.text = subLabel ?? "";
                parts.subLabel.gameObject.SetActive(!string.IsNullOrEmpty(subLabel));
            }
        }

        public static void SetTone(Button b, ButtonTone tone)
        {
            var parts = b.GetComponent<JuicyButtonParts>();
            if (parts == null) return;
            T.Tone(tone, out Color top, out Color bottom, out Color lip);
            parts.tone = tone;
            parts.faceGradient.Set(top, bottom);
            parts.lip.color = lip;
            parts.labelEmboss.Set(new Color(0, 0, 0, 0), 0f, lip, T.U(2f));
        }

        const string AdBadgeName = "AdBadge";

        /// <summary>
        /// The shared look of a button that plays a rewarded ad: green tone and a "▶ AD" pill.
        /// Passing false restores the button's own tone. Hide the button when no ad is ready rather than disabling it.
        /// </summary>
        public static void SetRewardedLook(Button b, bool rewarded)
        {
            var parts = b.GetComponent<JuicyButtonParts>();
            if (parts == null) return;
            SetTone(b, rewarded ? ButtonTone.Reward : parts.baseTone);
            var badge = parts.content.Find(AdBadgeName);
            if (badge == null && rewarded) badge = AdBadge(parts.content);
            if (badge != null) badge.gameObject.SetActive(rewarded);
        }

        public static void SetRewardedLook(Button b, bool rewarded, Color normalColor) => SetRewardedLook(b, rewarded);

        static Transform AdBadge(Transform content)
        {
            var pill = RoundRect(AdBadgeName, content, new Color(0f, 0f, 0f, 0.25f), 9f);
            var le = pill.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = T.U(46f);
            le.preferredHeight = T.U(20f);
            var play = Icon("Play", pill.transform, UISprites.Play, Color.white, 10f);
            Place(play.rectTransform, new Vector2(0f, 0.5f), play.rectTransform.sizeDelta, new Vector2(T.U(6f), 0f));
            var t = Text("Text", pill.transform, "AD", TextRole.Caps, 11f, Color.white, TextAnchor.MiddleRight);
            Stretch(t.rectTransform, 0, T.U(7f), 0, 0);
            return pill.transform;
        }

        // ------------------------------------------------------------ containers

        /// <summary>Raised card: soft shadow, panel gradient and hairline. Children added to the returned rect draw on top.</summary>
        public static RectTransform Card(string name, Transform parent, float radiusDp = 0f, bool shadow = true)
        {
            float r = radiusDp > 0f ? radiusDp : T.radiusLg;
            var root = Rect(name, parent);
            if (shadow) SoftShadow("Shadow", root, T.shadowBlur, T.shadowOffset, T.shadowAlpha);
            var face = RoundRect("Face", root, Color.white, r, raycast: true);
            Stretch(face.rectTransform);
            Gradient(face, T.panelTop, T.panelBottom);
            Stroke("Hairline", root, T.stroke, r);
            return root;
        }

        /// <summary>Recessed box for lists and breakdowns.</summary>
        public static Image Sunk(string name, Transform parent, float radiusDp = 0f)
        {
            var img = RoundRect(name, parent, T.surfaceSunk, radiusDp > 0f ? radiusDp : T.radiusMd);
            Stroke("Hairline", img.transform, new Color(0, 0, 0, 0.25f), radiusDp > 0f ? radiusDp : T.radiusMd);
            return img;
        }

        /// <summary>Pill chip that sizes itself to its content: optional icon plus a short label.</summary>
        public static RectTransform Chip(string name, Transform parent, string text, Sprite icon = null, Color? iconColor = null,
            float heightDp = 30f, Color? fill = null)
        {
            var bg = RoundRect(name, parent, fill ?? new Color(T.surfaceSunk.r, T.surfaceSunk.g, T.surfaceSunk.b, 0.85f), heightDp * 0.5f);
            var hairline = Stroke("Hairline", bg.transform, T.strokeSoft, heightDp * 0.5f);
            hairline.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            var rt = bg.rectTransform;
            rt.sizeDelta = new Vector2(0f, T.U(heightDp));
            var row = bg.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.padding = new RectOffset((int)T.U(icon != null ? 8f : 12f), (int)T.U(12f), 0, 0);
            row.spacing = T.U(5f);
            row.childAlignment = TextAnchor.MiddleCenter;
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = row.childForceExpandHeight = false;
            // Inside a layout group the group sizes the chip (it must control child width); alone it sizes itself.
            if (parent.GetComponent<LayoutGroup>() == null)
                bg.gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            if (icon != null)
            {
                var ic = Icon("Icon", rt, icon, iconColor ?? T.star, heightDp * 0.55f);
                var le = ic.gameObject.AddComponent<LayoutElement>();
                le.preferredWidth = le.preferredHeight = T.U(heightDp * 0.55f);
            }
            var t = Text("Text", rt, text, TextRole.Label, heightDp * 0.45f, T.text);
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            return rt;
        }

        public static Text ChipText(RectTransform chip) => chip.Find("Text").GetComponent<Text>();

        /// <summary>Ribbon banner crowning a card: forked tails, gradient band, ink outline, display-face title.</summary>
        public static RectTransform Ribbon(string name, Transform parent, string title, Color color, float widthDp = 220f)
        {
            float h = 50f;
            var root = Rect(name, parent);
            root.sizeDelta = new Vector2(T.U(widthDp), T.U(h));
            Color dark = Color.Lerp(color, T.ink, 0.45f);
            for (int side = -1; side <= 1; side += 2)
            {
                var tail = Image(side < 0 ? "TailL" : "TailR", root, dark, UISprites.RibbonTail);
                var trt = tail.rectTransform;
                trt.anchorMin = trt.anchorMax = new Vector2(side < 0 ? 0f : 1f, 0.5f);
                trt.sizeDelta = new Vector2(T.U(38f), T.U(40f));
                trt.anchoredPosition = new Vector2(side * T.U(8f), -T.U(9f));
                if (side < 0) trt.localScale = new Vector3(-1f, 1f, 1f);
                Ink(tail, 1.2f);
            }
            var band = RoundRect("Band", root, Color.white, 12f);
            Stretch(band.rectTransform);
            Gradient(band, Color.Lerp(color, Color.white, 0.28f), color);
            var gloss = RoundRect("Gloss", band.transform, new Color(1, 1, 1, 0.16f), 8f);
            gloss.rectTransform.anchorMin = new Vector2(0f, 0.55f);
            gloss.rectTransform.anchorMax = Vector2.one;
            gloss.rectTransform.offsetMin = new Vector2(T.U(6f), 0f);
            gloss.rectTransform.offsetMax = new Vector2(-T.U(6f), -T.U(3f));
            Stroke("Ink", band.transform, T.InkColor, 12f, T.inkWidth * 0.5f);
            var t = Text("Title", band.transform, title, TextRole.Title, 27f, Color.white);
            Stretch(t.rectTransform, T.U(10f), T.U(10f), 0, T.U(3f));
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            return root;
        }

        /// <summary>Full-rect dimmer that swallows taps.</summary>
        public static Image Scrim(string name, Transform parent, Color color)
        {
            var img = Image(name, parent, color, null, raycast: true);
            Stretch(img.rectTransform);
            return img;
        }

        // ------------------------------------------------------------ stars

        static readonly Color StarTop = new Color32(0xFF, 0xE6, 0x7A, 0xFF);
        static readonly Color StarBottom = new Color32(0xFF, 0xB0, 0x20, 0xFF);
        static readonly Color StarRim = new Color32(0xB4, 0x53, 0x09, 0xFF);

        /// <summary>One sticker star (rim, gradient face, gloss). Returns the face; <see cref="SetStar"/> toggles it.</summary>
        public static Image StarIcon(string name, Transform parent, float sizeUnits)
        {
            var root = Rect(name, parent);
            root.sizeDelta = new Vector2(sizeUnits, sizeUnits);
            var rim = Image("Rim", root, StarRim, UISprites.StarInk);
            Stretch(rim.rectTransform);
            var face = Image("Face", root, Color.white, UISprites.Star);
            Stretch(face.rectTransform);
            Gradient(face, StarTop, StarBottom);
            var gloss = Image("Gloss", face.transform, new Color(1, 1, 1, 0.35f), UISprites.Circle);
            Place(gloss.rectTransform, new Vector2(0.42f, 0.62f), new Vector2(sizeUnits * 0.16f, sizeUnits * 0.1f));
            gloss.rectTransform.localEulerAngles = new Vector3(0, 0, 30f);
            return face;
        }

        public static void SetStar(Image face, bool on)
        {
            var grad = face.GetComponent<UIGradient>();
            if (on) grad.Set(StarTop, StarBottom);
            else grad.Set(Color.Lerp(T.surfaceSunk, Color.white, 0.08f), T.surfaceSunk);
            var rim = face.transform.parent.Find("Rim");
            if (rim != null) rim.GetComponent<Image>().color = on ? StarRim : T.InkColor;
            var gloss = face.transform.Find("Gloss");
            if (gloss != null) gloss.gameObject.SetActive(on);
        }

        /// <summary>Row of three stars (the middle one larger and raised); returns the faces left to right. Sizes in canvas units.</summary>
        public static Image[] Stars(Transform parent, float size, float spacing, bool raisedMiddle = false)
        {
            var row = Rect("Stars", parent);
            row.sizeDelta = new Vector2(size * 3f + spacing * 2f, size);
            var stars = new Image[3];
            for (int i = 0; i < 3; i++)
            {
                float s = raisedMiddle && i == 1 ? size * 1.2f : size;
                var face = StarIcon("Star" + i, row, s);
                var rt = (RectTransform)face.transform.parent;
                Place(rt, new Vector2(0.5f, 0.5f), new Vector2(s, s),
                    new Vector2((i - 1) * (size + spacing), raisedMiddle && i == 1 ? size * 0.22f : 0f));
                stars[i] = face;
                SetStar(face, false);
            }
            return stars;
        }

        public static void SetStars(Image[] stars, int count)
        {
            for (int i = 0; i < stars.Length; i++) SetStar(stars[i], i < count);
        }

        // ------------------------------------------------------------ tokens

        /// <summary>
        /// Snaploop's token glyph (our own mark, not a coin): a rounded hexagon in the golden-hour gradient over a
        /// purple rim, with an embossed jigsaw piece and a gloss. Size in canvas units.
        /// </summary>
        public static RectTransform TokenIcon(string name, Transform parent, float sizeUnits)
        {
            var root = Rect(name, parent);
            root.sizeDelta = new Vector2(sizeUnits, sizeUnits);
            var rim = Image("Rim", root, T.tokenRim, UISprites.TokenHexInk);
            Stretch(rim.rectTransform, 0, 0, sizeUnits * 0.07f, -sizeUnits * 0.07f);
            var ink = Image("Ink", root, T.InkColor, UISprites.TokenHexInk);
            Stretch(ink.rectTransform);
            var face = Image("Face", root, Color.white, UISprites.TokenHex);
            Stretch(face.rectTransform);
            Gradient(face, T.sunsetA, T.sunsetB);
            var emboss = Image("PieceShadow", face.transform, new Color(T.tokenRim.r, T.tokenRim.g, T.tokenRim.b, 0.35f), UISprites.TokenPiece);
            Stretch(emboss.rectTransform, sizeUnits * 0.22f, sizeUnits * 0.22f, sizeUnits * 0.25f, sizeUnits * 0.19f);
            var piece = Image("Piece", face.transform, new Color(1f, 1f, 1f, 0.92f), UISprites.TokenPiece);
            Stretch(piece.rectTransform, sizeUnits * 0.22f, sizeUnits * 0.22f, sizeUnits * 0.22f, sizeUnits * 0.22f);
            var gloss = Image("Gloss", face.transform, new Color(1, 1, 1, 0.45f), UISprites.Circle);
            Place(gloss.rectTransform, new Vector2(0.33f, 0.74f), new Vector2(sizeUnits * 0.14f, sizeUnits * 0.08f));
            gloss.rectTransform.localEulerAngles = new Vector3(0, 0, 30f);
            return root;
        }

        /// <summary>A <see cref="Chip"/> led by the token glyph, e.g. "+8" on the win card.</summary>
        public static RectTransform TokenChip(string name, Transform parent, string text, float heightDp = 30f)
        {
            var chip = Chip(name, parent, text, null, null, heightDp);
            chip.GetComponent<HorizontalLayoutGroup>().padding.left = (int)T.U(6f);
            var icon = TokenIcon("Token", chip, T.U(heightDp * 0.72f));
            icon.SetAsFirstSibling();
            var le = icon.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = le.preferredHeight = T.U(heightDp * 0.72f);
            return chip;
        }

        // ------------------------------------------------------------ gauges and switches

        public sealed class RingGauge
        {
            public RectTransform Root;
            public Image Fill;
            public Text Label;
            float shown, target;

            public void Set(float fraction, string label, bool instant = false)
            {
                target = Mathf.Clamp01(fraction);
                if (instant) { shown = target; Fill.fillAmount = shown; }
                if (Label.text != label) Label.text = label;
            }

            public void Tick(float dt)
            {
                if (Mathf.Approximately(shown, target)) return;
                shown = Mathf.MoveTowards(shown, target, dt * 2.2f);
                Fill.fillAmount = shown;
            }
        }

        /// <summary>Circular progress (pieces placed) in the signature gradient, with a count in the middle.</summary>
        public static RingGauge Ring(string name, Transform parent, float sizeDp)
        {
            var root = Rect(name, parent);
            root.sizeDelta = new Vector2(T.U(sizeDp), T.U(sizeDp));
            var bg = Image("Disc", root, T.surfaceSunk, UISprites.Circle);
            Stretch(bg.rectTransform, T.U(2f), T.U(2f), T.U(2f), T.U(2f));
            var track = Image("Track", root, new Color(1, 1, 1, 0.1f), UISprites.Annulus);
            Stretch(track.rectTransform);
            var fill = Image("Fill", root, Color.white, UISprites.Annulus);
            Stretch(fill.rectTransform);
            fill.type = UnityEngine.UI.Image.Type.Filled;
            fill.fillMethod = UnityEngine.UI.Image.FillMethod.Radial360;
            fill.fillOrigin = (int)UnityEngine.UI.Image.Origin360.Top;
            fill.fillClockwise = true;
            fill.fillAmount = 0f;
            Gradient(fill, T.sunsetA, T.sunsetB);
            var label = Text("Count", root, "", TextRole.Number, sizeDp * 0.3f, Color.white);
            Stretch(label.rectTransform);
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            return new RingGauge { Root = root, Fill = fill, Label = label };
        }

        /// <summary>Animates a switch's knob and track colour toward its toggle state.</summary>
        public sealed class SwitchVisual : MonoBehaviour
        {
            public Toggle toggle;
            public Image track;
            public RectTransform knob;
            public float travel;
            float k = -1f;

            void OnEnable() { k = toggle != null && toggle.isOn ? 1f : 0f; Apply(); }

            void Update()
            {
                float target = toggle.isOn ? 1f : 0f;
                if (Mathf.Approximately(k, target)) return;
                k = Mathf.MoveTowards(k, target, Time.unscaledDeltaTime / 0.16f);
                Apply();
            }

            void Apply()
            {
                var t = UITheme.Current;
                float e = Ease.Apply(EaseType.OutCubic, k);
                knob.anchoredPosition = new Vector2(Mathf.Lerp(-travel, travel, e), 0f);
                track.color = Color.Lerp(t.surfaceSunk, t.green, e);
            }
        }

        /// <summary>On/off switch: green track when on, sunk when off. The whole row height is tappable via the toggle's rect.</summary>
        public static Toggle Switch(string name, Transform parent, bool on, UnityAction<bool> onChanged)
        {
            var root = Rect(name, parent);
            root.sizeDelta = new Vector2(T.U(58f), T.U(T.minTouch));
            var hit = root.gameObject.AddComponent<Image>();
            hit.color = Color.clear;
            hit.raycastTarget = true;
            var track = RoundRect("Track", root, T.surfaceSunk, 16f);
            Place(track.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(T.U(54f), T.U(32f)));
            Stroke("Hairline", track.transform, new Color(0, 0, 0, 0.3f), 16f);
            var knob = Rect("Knob", track.transform);
            Place(knob, new Vector2(0.5f, 0.5f), new Vector2(T.U(26f), T.U(26f)));
            var sh = Image("Shadow", knob, new Color(0, 0, 0, 0.3f), UISprites.SoftCircle);
            Stretch(sh.rectTransform, -T.U(4f), -T.U(4f), -T.U(2f), -T.U(6f));
            var knobFace = Image("Face", knob, Color.white, UISprites.Circle);
            Stretch(knobFace.rectTransform);

            var toggle = root.gameObject.AddComponent<Toggle>();
            toggle.transition = Selectable.Transition.None;
            toggle.targetGraphic = hit;
            toggle.isOn = on;
            var nav = toggle.navigation; nav.mode = Navigation.Mode.None; toggle.navigation = nav;
            var vis = root.gameObject.AddComponent<SwitchVisual>();
            vis.toggle = toggle; vis.track = track; vis.knob = knob; vis.travel = T.U(11f);
            toggle.onValueChanged.AddListener(_ => Sfx.PlayClick());
            if (onChanged != null) toggle.onValueChanged.AddListener(onChanged);
            return toggle;
        }

        /// <summary>Tinted rounded-square badge holding an icon (settings rows).</summary>
        public static Image IconBadge(string name, Transform parent, Sprite icon, Color color, float sizeDp = 38f)
        {
            var bg = RoundRect(name, parent, new Color(color.r, color.g, color.b, 0.2f), sizeDp * 0.3f);
            bg.rectTransform.sizeDelta = new Vector2(T.U(sizeDp), T.U(sizeDp));
            Stroke("Hairline", bg.transform, new Color(color.r, color.g, color.b, 0.45f), sizeDp * 0.3f);
            var ic = Icon("Icon", bg.transform, icon, color, sizeDp * 0.56f);
            Place(ic.rectTransform, new Vector2(0.5f, 0.5f), ic.rectTransform.sizeDelta);
            return bg;
        }
    }
}
