using UnityEngine;

namespace Snaploop.Core
{
    /// <summary>
    /// Short vibration ticks. Uses the Android Vibrator directly because Handheld.Vibrate is too long for a snap.
    /// The Handheld.Vibrate fallback reference is also what makes Unity add the VIBRATE permission.
    /// </summary>
    public static class Haptics
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        static AndroidJavaObject vibrator;
        static AndroidJavaClass effectClass;
        static bool initialized;
        static int sdkInt;

        static void Init()
        {
            initialized = true;
            try
            {
                using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
                    sdkInt = version.GetStatic<int>("SDK_INT");
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                    vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator");
                if (sdkInt >= 26) effectClass = new AndroidJavaClass("android.os.VibrationEffect");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("Haptics unavailable: " + e.Message);
                vibrator = null;
            }
        }
#endif

        /// <summary>Light tick, e.g. a piece snapping into place.</summary>
        public static void Tick() => Vibrate(18, 120);

        /// <summary>Stronger buzz, e.g. puzzle complete.</summary>
        public static void Success() => Vibrate(45, 255);

        static void Vibrate(long ms, int amplitude)
        {
            if (!GameSettings.HapticsOn) return;
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!initialized) Init();
            if (vibrator == null) { Handheld.Vibrate(); return; }
            try
            {
                if (effectClass != null)
                {
                    using (var effect = effectClass.CallStatic<AndroidJavaObject>("createOneShot", ms, amplitude))
                        vibrator.Call("vibrate", effect);
                }
                else
                {
                    vibrator.Call("vibrate", ms);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("Vibrate failed: " + e.Message);
            }
#endif
        }
    }
}
