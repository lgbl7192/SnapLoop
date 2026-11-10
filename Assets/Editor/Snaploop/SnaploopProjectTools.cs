using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Snaploop.Levels;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Snaploop.EditorTools
{
    /// <summary>Editor helpers: re-apply Snaploop's Android identity settings and validate levels.json.</summary>
    public static class SnaploopProjectTools
    {
        public const string ProductName = "Snaploop";
        public const string PackageId = "com.snaploop.game"; // placeholder; change before the first store upload
        public const string MainScene = "Assets/Scenes/Main.unity";

        /// <summary>
        /// Writes the settings committed in ProjectSettings.asset again, in case they drift.
        /// Signing, versioning and release-only switches belong to Assets/Editor/Build (build pipeline).
        /// </summary>
        [MenuItem("Snaploop/Apply Player Settings")]
        public static void ApplyPlayerSettings()
        {
            PlayerSettings.productName = ProductName;
            PlayerSettings.companyName = ProductName;
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, PackageId);
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.allowedAutorotateToPortrait = true;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = false;
            PlayerSettings.allowedAutorotateToLandscapeRight = false;
            PlayerSettings.SplashScreen.show = false; // the game shows its own "Snaploop" splash
            PlayerSettings.colorSpace = ColorSpace.Gamma;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel24;
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);

            if (!EditorBuildSettings.scenes.Any(s => s.path == MainScene))
            {
                var scenes = EditorBuildSettings.scenes.ToList();
                scenes.Insert(0, new EditorBuildSettingsScene(MainScene, true));
                EditorBuildSettings.scenes = scenes.ToArray();
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[Snaploop] Player settings applied (Snaploop, " + PackageId + ", portrait, IL2CPP ARM64).");
        }

        [MenuItem("Snaploop/Open Main Scene")]
        public static void OpenMainScene()
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(MainScene);
        }

        [MenuItem("Snaploop/Validate Levels")]
        public static void ValidateLevelsMenu()
        {
            bool ok = ValidateLevels(out string report);
            if (ok) Debug.Log(report);
            else Debug.LogError(report);
            EditorUtility.DisplayDialog("Snaploop levels", report, "OK");
        }

        /// <summary>Checks levels.json parses and every referenced clip exists. Usable from -executeMethod.</summary>
        public static bool ValidateLevels(out string report)
        {
            string folder = Path.Combine(Application.streamingAssetsPath, LevelCatalog.FolderName);
            string manifest = Path.Combine(folder, LevelCatalog.ManifestName);
            var sb = new StringBuilder();
            if (!File.Exists(manifest))
            {
                report = "Missing " + manifest;
                return false;
            }

            var levels = new List<LevelInfo>();
            var warnings = new List<string>();
            bool ok = LevelCatalog.TryParse(File.ReadAllText(manifest), levels, warnings, out string error);
            if (!ok) sb.AppendLine("ERROR: " + error);

            foreach (var l in levels)
            {
                string clip = Path.Combine(folder, l.file);
                if (!File.Exists(clip))
                {
                    ok = false;
                    sb.AppendLine("ERROR: " + l.id + ": missing clip " + l.file);
                    continue;
                }
                long mb = new FileInfo(clip).Length / (1024 * 1024);
                if (mb > 8) warnings.Add(l.id + ": clip is " + mb + " MB (aim for ~2-4 MB: 720x1280, 2-3 Mbps, 8-12 s).");
                if (string.IsNullOrEmpty(l.credit)) warnings.Add(l.id + ": no credit.");
            }
            foreach (var w in warnings) sb.AppendLine("Warning: " + w);
            sb.Insert(0, levels.Count + " level(s) in levels.json" + (ok ? ", all clips present." : ".") + "\n");
            report = sb.ToString().TrimEnd();
            return ok;
        }

        /// <summary>Batch-mode entry point: exits 1 if validation fails.</summary>
        public static void ValidateLevelsFromCommandLine()
        {
            bool ok = ValidateLevels(out string report);
            Debug.Log(report);
            EditorApplication.Exit(ok ? 0 : 1);
        }
    }
}
