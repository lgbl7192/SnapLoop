using UnityEngine;

namespace Snaploop.Core
{
    /// <summary>
    /// Tiny synthesized sound effects so the prototype ships without audio assets.
    /// Swap the generated clips for real ones by assigning the public fields.
    /// </summary>
    public sealed class Sfx : MonoBehaviour
    {
        public static Sfx Instance { get; private set; }

        public AudioClip Pickup;
        public AudioClip Snap;
        public AudioClip Win;
        public AudioClip Click;

        AudioSource source;

        void Awake()
        {
            Instance = this;
            source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            if (Pickup == null) Pickup = Tone("pickup", 0.05f, 660f, 880f, 0.18f);
            if (Snap == null) Snap = Tone("snap", 0.09f, 520f, 1040f, 0.35f);
            if (Click == null) Click = Tone("click", 0.04f, 900f, 700f, 0.2f);
            if (Win == null) Win = Chime("win", new[] { 523.25f, 659.25f, 783.99f, 1046.5f }, 0.11f, 0.28f);
        }

        public static void Play(AudioClip clip, float volume = 1f)
        {
            if (Instance == null || clip == null || !GameSettings.SoundOn) return;
            Instance.source.PlayOneShot(clip, volume);
        }

        public static void PlayPickup() { if (Instance != null) Play(Instance.Pickup, 0.6f); }
        public static void PlaySnap() { if (Instance != null) Play(Instance.Snap); }
        public static void PlayWin() { if (Instance != null) Play(Instance.Win); }
        public static void PlayClick() { if (Instance != null) Play(Instance.Click, 0.7f); }

        const int Rate = 44100;

        static AudioClip Tone(string name, float dur, float f0, float f1, float gain)
        {
            int n = Mathf.CeilToInt(dur * Rate);
            var data = new float[n];
            float phase = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)n;
                float f = Mathf.Lerp(f0, f1, t);
                phase += 2f * Mathf.PI * f / Rate;
                float env = Mathf.Min(1f, i / (Rate * 0.004f)) * (1f - t) * (1f - t);
                data[i] = Mathf.Sin(phase) * env * gain;
            }
            var clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        static AudioClip Chime(string name, float[] notes, float step, float gain)
        {
            float noteDur = 0.45f;
            int n = Mathf.CeilToInt((step * (notes.Length - 1) + noteDur) * Rate);
            var data = new float[n];
            for (int k = 0; k < notes.Length; k++)
            {
                int start = Mathf.RoundToInt(k * step * Rate);
                int len = Mathf.RoundToInt(noteDur * Rate);
                for (int i = 0; i < len && start + i < n; i++)
                {
                    float t = i / (float)Rate;
                    float env = Mathf.Min(1f, t / 0.005f) * Mathf.Exp(-t * 7f);
                    float s = Mathf.Sin(2f * Mathf.PI * notes[k] * t) + 0.3f * Mathf.Sin(4f * Mathf.PI * notes[k] * t);
                    data[start + i] += s * env * gain * 0.6f;
                }
            }
            var clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
