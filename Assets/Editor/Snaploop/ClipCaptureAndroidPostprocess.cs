using System.IO;
using System.Xml;
using UnityEditor.Android;
using UnityEngine;

namespace Snaploop.EditorTools
{
    /// <summary>
    /// Gradle-project tweaks for the clip capture plugin (Assets/Plugins/Android/SnaploopCapture):
    /// - WRITE_EXTERNAL_STORAGE limited to Android 9 and below (maxSdkVersion 28), only needed there to
    ///   save clips to the gallery. Android 10+ saves through MediaStore without any permission.
    /// - A keep rule so a minified build doesn't strip the classes C# reaches through JNI.
    /// Failures only log a warning: the game still builds, it just can't save clips on Android 7-9.
    /// </summary>
    sealed class ClipCaptureAndroidPostprocess : IPostGenerateGradleAndroidProject
    {
        const string AndroidNs = "http://schemas.android.com/apk/res/android";
        const string Permission = "android.permission.WRITE_EXTERNAL_STORAGE";
        const string KeepRule = "-keep class com.snaploop.capture.** { *; }";

        public int callbackOrder => 100;

        public void OnPostGenerateGradleAndroidProject(string unityLibraryPath)
        {
            AddLegacyStoragePermission(Path.Combine(unityLibraryPath, "src", "main", "AndroidManifest.xml"));
            AddKeepRule(Path.Combine(unityLibraryPath, "proguard-unity.txt"));
        }

        static void AddLegacyStoragePermission(string manifestPath)
        {
            try
            {
                var doc = new XmlDocument();
                doc.Load(manifestPath);
                var manifest = doc.DocumentElement;
                if (manifest == null) return;
                foreach (XmlNode n in manifest.SelectNodes("uses-permission"))
                    if (n is XmlElement e && e.GetAttribute("name", AndroidNs) == Permission) return;

                var perm = doc.CreateElement("uses-permission");
                perm.SetAttribute("name", AndroidNs, Permission);
                perm.SetAttribute("maxSdkVersion", AndroidNs, "28");
                var application = manifest.SelectSingleNode("application");
                if (application != null) manifest.InsertBefore(perm, application);
                else manifest.AppendChild(perm);
                doc.Save(manifestPath);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[Snaploop] Could not add the clip storage permission to " + manifestPath + ": " + ex.Message);
            }
        }

        static void AddKeepRule(string proguardPath)
        {
            try
            {
                string text = File.Exists(proguardPath) ? File.ReadAllText(proguardPath) : "";
                if (text.Contains(KeepRule)) return;
                File.AppendAllText(proguardPath, "\n# Snaploop clip capture (called from C# through JNI)\n" + KeepRule + "\n");
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[Snaploop] Could not add the clip capture keep rule: " + ex.Message);
            }
        }
    }
}
