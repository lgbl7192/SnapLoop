using UnityEngine;

namespace Snaploop.UI
{
    /// <summary>Text roles of the type scale.</summary>
    public enum TextRole
    {
        /// <summary>Lilita One with a hard drop shadow: headings, level names, ribbons.</summary>
        Title,
        /// <summary>Lilita One for counts and stats.</summary>
        Number,
        /// <summary>Nunito 800: row labels, chips, small UI copy.</summary>
        Label,
        /// <summary>Nunito 900, uppercase, letter-spaced, muted: section labels ("LEVEL 4").</summary>
        Caps,
        /// <summary>Nunito 600, dim: running text.</summary>
        Body,
    }

    /// <summary>Button tones. Orange marks the one next action; green is only for rewarded ads.</summary>
    public enum ButtonTone
    {
        Primary,
        Reward,
        Blue,
        Neutral,
    }

    /// <summary>Button sizes (face height, lip, label size in dp).</summary>
    public enum ButtonSize
    {
        Hero,
        Large,
        Medium,
    }

    /// <summary>
    /// Every design token in one place: palette and gradients, fonts, type scale, spacing, radii,
    /// button metrics, shadows and motion. Loaded from <c>Assets/Resources/UI/UITheme.asset</c>;
    /// values the asset leaves out fall back to the defaults below.
    ///
    /// The system follows the "Midnight Arcade" look of Lance's Ballbreaker (internal reference:
    /// ballbreaker-ui-reference.md) with Snaploop's own signature: the golden-hour gradient used by the
    /// wordmark, logo, ribbons and progress ring, and a jigsaw lattice in the backdrop.
    ///
    /// Sizes are in dp. One dp is <see cref="dp"/> canvas units (canvas reference width 1080 = 393 dp).
    /// </summary>
    [CreateAssetMenu(menuName = "Snaploop/UI Theme", fileName = "UITheme")]
    public sealed class UITheme : ScriptableObject
    {
        public const string ResourcePath = "UI/UITheme";

        [Header("Fonts (bundled, SIL Open Font License; see Assets/Art/Fonts)")]
        [Tooltip("Display: titles, numbers, button labels (Lilita One).")]
        public Font displayFont;
        [Tooltip("Body copy (Nunito SemiBold 600).")]
        public Font bodyFont;
        [Tooltip("UI labels (Nunito ExtraBold 800).")]
        public Font labelFont;
        [Tooltip("Caps labels and badges (Nunito Black 900).")]
        public Font heavyFont;

        [Header("Units")]
        [Tooltip("Canvas units per dp.")]
        public float dp = 2.75f;

        [Header("Backdrop")]
        public Color bgTop = Hex(0x231A5C);
        public Color bgMid = Hex(0x141139);
        public Color bgBottom = Hex(0x0A0A1F);
        public Color bgGlow = new Color32(0x8B, 0x5C, 0xF6, 77);
        [Range(0f, 0.2f)] public float latticeOpacity = 0.05f;

        [Header("Surfaces")]
        public Color surface = Hex(0x241D5E);
        public Color surfaceHigh = Hex(0x2F2777);
        public Color surfaceLow = Hex(0x1A1547);
        public Color surfaceSunk = Hex(0x120F33);
        public Color panelTop = Hex(0x30287A);
        public Color panelBottom = Hex(0x211A58);
        public Color tileTop = Hex(0x3B3290);
        public Color tileBottom = Hex(0x2A2269);
        public Color stroke = new Color(1f, 1f, 1f, 0.2f);
        public Color strokeSoft = new Color(1f, 1f, 1f, 0.1f);
        public Color ink = Hex(0x15103A);
        public Color scrim = new Color32(0x07, 0x06, 0x1A, 0xCC);
        public Color dialogScrim = new Color32(0x07, 0x06, 0x1A, 0xB3);

        [Header("Text")]
        public Color text = Color.white;
        public Color textDim = Hex(0xC3BDF0);
        public Color textMute = Hex(0x8A82C4);

        [Header("Accents")]
        public Color orange = Hex(0xFF7A2E);
        public Color green = Hex(0x3DD45C);
        public Color star = Hex(0xFFC93C);
        public Color gold = Hex(0xFFD43B);
        public Color danger = Hex(0xFF4D6D);
        public Color cyan = Hex(0x4DE1FF);
        public Color purple = Hex(0xC084FC);
        public Color pink = Hex(0xFF5FA2);
        public Color dot = Hex(0xFF3B5C);

        [Header("Snaploop signature: golden-hour gradient")]
        public Color sunsetA = Hex(0xFFD166);
        public Color sunsetB = Hex(0xFF5E7E);
        public Color sunsetC = Hex(0xA855F7);
        public Color sunsetExtrude = Hex(0x4C1D95);
        [Tooltip("Win ribbon.")]
        public Color ribbonWin = Hex(0xFF5E7E);
        [Tooltip("Info dialogs (settings, dev menu).")]
        public Color ribbonInfo = Hex(0x7C5CFF);
        [Tooltip("Test store (always clearly labelled).")]
        public Color ribbonTest = Hex(0xF59F00);

        [Header("Button tones: face top, face bottom, lip, glow")]
        public Color primaryTop = Hex(0xFFB347);
        public Color primaryBottom = Hex(0xFF6A1F);
        public Color primaryLip = Hex(0xB8400C);
        public Color rewardTop = Hex(0x8BF06A);
        public Color rewardBottom = Hex(0x2DB84A);
        public Color rewardLip = Hex(0x17772E);
        public Color blueTop = Hex(0x76A4FF);
        public Color blueBottom = Hex(0x3F5BEA);
        public Color blueLip = Hex(0x2232A3);
        public Color neutralTop = Hex(0x4A4199);
        public Color neutralBottom = Hex(0x332B78);
        public Color neutralLip = Hex(0x1B1650);

        [Header("Category accents")]
        public Color sunsets = Hex(0xFF8A3D);
        public Color crowds = Hex(0xFF5FA2);
        public Color downtown = Hex(0x4DA3FF);
        public Color nature = Hex(0x2DD4BF);
        public Color everyday = Hex(0xC084FC);

        [Header("Type scale (dp)")]
        public float titleXL = 40f;
        public float titleL = 34f;
        public float titleM = 28f;
        public float titleS = 22f;
        public float titleXS = 17f;
        public float label = 16f;
        public float labelS = 14f;
        public float caps = 12f;
        public float body = 15f;
        public float bodyS = 13f;

        [Header("Spacing (dp)")]
        public float screenPad = 16f;
        public float gap = 10f;
        public float sectionGap = 14f;
        public float cardPad = 16f;

        [Header("Radii (dp)")]
        public float radiusLg = 24f;
        public float radiusMd = 18f;
        public float radiusSm = 12f;

        [Header("Outlines and shadows (dp)")]
        public float inkWidth = 2f;
        [Range(0f, 1f)] public float inkAlpha = 0.7f;
        public float hairline = 1.5f;
        public float shadowBlur = 18f;
        public float shadowOffset = 8f;
        [Range(0f, 1f)] public float shadowAlpha = 0.4f;

        [Header("Touch")]
        [Tooltip("Minimum hit size for anything tappable.")]
        public float minTouch = 48f;
        public float roundButton = 48f;

        [Header("Motion (seconds)")]
        public float tap = 0.09f;
        public float quick = 0.22f;
        public float medium = 0.38f;
        public float slow = 0.65f;

        static UITheme current;

        public static UITheme Current
        {
            get
            {
                if (current != null) return current;
                current = Resources.Load<UITheme>(ResourcePath);
                if (current == null)
                {
                    Debug.LogWarning("[Snaploop] " + ResourcePath + " not found in Resources; using default UI theme.");
                    current = CreateInstance<UITheme>();
                }
                return current;
            }
        }

        /// <summary>dp to canvas units.</summary>
        public float U(float dpValue) => dpValue * dp;

        /// <summary>dp to a font size in canvas units.</summary>
        public int F(float dpValue) => Mathf.RoundToInt(dpValue * dp);

        public void Tone(ButtonTone tone, out Color top, out Color bottom, out Color lip)
        {
            switch (tone)
            {
                case ButtonTone.Reward: top = rewardTop; bottom = rewardBottom; lip = rewardLip; break;
                case ButtonTone.Blue: top = blueTop; bottom = blueBottom; lip = blueLip; break;
                case ButtonTone.Neutral: top = neutralTop; bottom = neutralBottom; lip = neutralLip; break;
                default: top = primaryTop; bottom = primaryBottom; lip = primaryLip; break;
            }
        }

        /// <summary>Face height, lip and label size, in dp.</summary>
        public void Metrics(ButtonSize size, out float face, out float lip, out float labelSize, out float subLabel)
        {
            switch (size)
            {
                case ButtonSize.Hero: face = 76f; lip = 7f; labelSize = 30f; subLabel = 15f; break;
                case ButtonSize.Large: face = 60f; lip = 6f; labelSize = 23f; subLabel = 12f; break;
                default: face = 50f; lip = 5f; labelSize = 19f; subLabel = 10f; break;
            }
        }

        public Color Category(string category)
        {
            switch (category)
            {
                case "sunsets": return sunsets;
                case "crowds": return crowds;
                case "downtown": return downtown;
                case "nature": return nature;
                default: return everyday;
            }
        }

        public Color InkColor => new Color(ink.r, ink.g, ink.b, inkAlpha);

        static Color Hex(int rgb) => new Color32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255);
    }
}
