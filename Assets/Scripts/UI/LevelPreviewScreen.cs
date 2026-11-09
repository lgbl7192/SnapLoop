using System;
using Snaploop.Core;
using Snaploop.Levels;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace Snaploop.UI
{
    /// <summary>
    /// Preview of a puzzle in a locked pack: its clip loops silently (thumbnail while it prepares) above the pack's
    /// unlock card. It never starts the puzzle; unlocking or buying the pack closes it.
    /// </summary>
    public sealed class LevelPreviewScreen : ModalScreen
    {
        const float CardW = 360f, CardH = 590f, VideoW = 160f, VideoH = 284f;
        const int MaxTextureHeight = 640;

        readonly MonoBehaviour host;
        readonly VideoPlayer player;
        readonly RawImage video;
        readonly Text title, caps;
        readonly PackCard packCard;
        RenderTexture rt;
        LevelInfo level;

        public LevelPreviewScreen(Transform canvasRoot, MonoBehaviour coroutineHost)
        {
            host = coroutineHost;
            BuildFrame(canvasRoot, "LevelPreview", null, T.ribbonInfo, CardW, CardH);
            var close = UIFactory.RoundButton("Close", Card, UISprites.Close, Close);
            UIFactory.Place((RectTransform)close.transform, new Vector2(1f, 1f), ((RectTransform)close.transform).sizeDelta, new Vector2(-T.U(10f), -T.U(10f)));

            var mask = UIFactory.RoundRect("VideoMask", Card, T.surfaceSunk, 18f);
            UIFactory.Place(mask.rectTransform, new Vector2(0.5f, 1f), new Vector2(T.U(VideoW), T.U(VideoH)), new Vector2(0f, -T.U(18f)));
            mask.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            video = UIFactory.Stretch(UIFactory.Rect("Video", mask.transform)).gameObject.AddComponent<RawImage>();
            video.raycastTarget = false;
            UIFactory.Stroke("Hairline", mask.transform, T.stroke, 18f);
            var lockBadge = UIFactory.Image("Lock", mask.transform, T.pink, UISprites.Circle);
            UIFactory.Place(lockBadge.rectTransform, new Vector2(1f, 1f), new Vector2(T.U(34f), T.U(34f)), new Vector2(-T.U(8f), -T.U(8f)));
            UIFactory.Ink(lockBadge, 1.5f);
            var lockIcon = UIFactory.Icon("Icon", lockBadge.transform, UISprites.Lock, Color.white, 18f);
            UIFactory.Place(lockIcon.rectTransform, new Vector2(0.5f, 0.5f), lockIcon.rectTransform.sizeDelta);

            float y = 18f + VideoH + 10f;
            caps = UIFactory.Text("Caps", Card, "", TextRole.Caps, T.caps, T.pink);
            UIFactory.Place(caps.rectTransform, new Vector2(0.5f, 1f), new Vector2(T.U(CardW - 32f), T.U(18f)), new Vector2(0f, -T.U(y)));
            title = UIFactory.Text("Title", Card, "", TextRole.Title, T.titleS, Color.white);
            UIFactory.Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(T.U(CardW - 32f), T.U(32f)), new Vector2(0f, -T.U(y + 20f)));

            packCard = new PackCard("PackCard", Card, host, null, showPlay: false);
            var prt = packCard.Root;
            prt.anchorMin = new Vector2(0f, 0f); prt.anchorMax = new Vector2(1f, 0f); prt.pivot = new Vector2(0.5f, 0f);
            prt.offsetMin = new Vector2(T.U(12f), T.U(12f)); prt.offsetMax = new Vector2(-T.U(12f), T.U(12f + PackCard.HeightDp));

            var go = new GameObject("PreviewVideo");
            go.transform.SetParent(host.transform, false);
            player = go.AddComponent<VideoPlayer>();
            player.playOnAwake = false;
            player.isLooping = true;
            player.source = VideoSource.Url;
            player.renderMode = VideoRenderMode.RenderTexture;
            player.audioOutputMode = VideoAudioOutputMode.None;
            player.skipOnDrop = true;
            player.aspectRatio = VideoAspectRatio.Stretch;
            player.prepareCompleted += OnPrepared;
            player.errorReceived += (vp, msg) => Debug.LogWarning("[Snaploop] Preview video failed: " + msg);

            GameServices.PacksChanged += OnPacksChanged;
        }

        /// <summary>Binds the preview and starts preparing the clip; open it with the app's modal flow afterwards.</summary>
        public void ShowFor(LevelInfo previewed)
        {
            level = previewed;
            var pack = PackCatalog.For(level);
            caps.text = UIFactory.Track("Locked · " + (pack != null ? pack.DisplayTitle : "pack"));
            title.text = level.DisplayTitle;
            packCard.Bind(pack);

            video.texture = null;
            video.color = Color.Lerp(T.Category(level.category), T.surfaceLow, 0.35f);
            if (Thumbnails.TryGet(level.id, out var tex)) ApplyThumb(level, tex);
            else host.StartCoroutine(Thumbnails.Load(level.id, t => ApplyThumb(previewed, t)));

            player.Stop();
            player.url = LevelCatalog.GetLevelFilePath(level.file);
            player.Prepare();
        }

        void ApplyThumb(LevelInfo forLevel, Texture2D tex)
        {
            if (tex == null || forLevel != level || (rt != null && video.texture == rt)) return;
            video.texture = tex;
            video.color = Color.white;
            video.uvRect = Thumbnails.CoverRect(video.rectTransform.rect, tex);
        }

        void OnPrepared(VideoPlayer vp)
        {
            if (!IsVisible) { vp.Stop(); return; }
            int w = (int)vp.width, h = (int)vp.height;
            if (w <= 0 || h <= 0) return;
            if (h > MaxTextureHeight)
            {
                w = Mathf.RoundToInt(w * (MaxTextureHeight / (float)h));
                h = MaxTextureHeight;
            }
            if (rt == null || rt.width != w || rt.height != h)
            {
                ReleaseTexture();
                rt = new RenderTexture(w, h, 0) { name = "PreviewVideo" };
                rt.Create();
            }
            vp.targetTexture = rt;
            vp.Play();
            video.texture = rt;
            video.color = Color.white;
            video.uvRect = Thumbnails.CoverRect(video.rectTransform.rect, rt);
        }

        void OnPacksChanged()
        {
            if (IsVisible && level != null && PackAccess.IsUnlocked(PackCatalog.For(level))) Close();
        }

        public override void Hide()
        {
            base.Hide();
            if (player != null)
            {
                player.Stop();
                player.targetTexture = null;
            }
            if (video != null) video.texture = null;
            ReleaseTexture();
        }

        void ReleaseTexture()
        {
            if (rt == null) return;
            rt.Release();
            UnityEngine.Object.Destroy(rt);
            rt = null;
        }
    }
}
