using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Snaploop.Build
{
    public enum BuildVariant
    {
        Debug,
        Release,
    }

    public enum AndroidPackageFormat
    {
        Apk,
        Aab,
    }

    /// <summary>
    /// Android build entrypoints for the Editor menu and for batch mode / CI.
    ///
    /// Batch mode:
    ///   Unity -batchmode -quit -projectPath . -buildTarget Android \
    ///     -executeMethod Snaploop.Build.SnaploopBuild.BuildFromCommandLine -variant debug
    ///
    /// Inputs (command-line flag wins over environment variable):
    ///   -variant debug|release                    SNAPLOOP_BUILD_VARIANT       (default debug)
    ///   -format apk|aab, or GameCI's
    ///   -androidExportType androidPackage|androidAppBundle  SNAPLOOP_BUILD_FORMAT (default apk)
    ///   -customBuildPath path/to/output.apk       SNAPLOOP_BUILD_PATH          (default Builds/Android/Snaploop-&lt;variant&gt;.&lt;ext&gt;)
    ///   -buildVersion 0.1.42                      SNAPLOOP_VERSION             (default: Player Settings)
    ///   -androidVersionCode 42                    SNAPLOOP_ANDROID_VERSION_CODE (default: Player Settings)
    ///   -androidKeystoreName path.keystore        ANDROID_KEYSTORE_NAME        (absolute or relative to the project root)
    ///   -androidKeystorePass ...                  ANDROID_KEYSTORE_PASS
    ///   -androidKeyaliasName ...                  ANDROID_KEYALIAS_NAME
    ///   -androidKeyaliasPass ...                  ANDROID_KEYALIAS_PASS        (default: keystore password)
    ///   -requireReleaseKeystore                   SNAPLOOP_REQUIRE_RELEASE_KEYSTORE=true
    ///
    /// Signing: an explicitly provided keystore is used for any variant. Otherwise a keystore
    /// configured in Player Settings > Publishing Settings is used if its passwords are entered
    /// in this Editor session. Otherwise the build falls back to Unity's debug keystore
    /// (~/.android/debug.keystore), which is fine for sideloading but cannot be uploaded to
    /// Google Play. -requireReleaseKeystore turns that fallback into a failure for release builds.
    ///
    /// Player Settings touched by the build are restored afterwards, so building never leaves
    /// ProjectSettings modified or writes passwords to disk.
    /// </summary>
    public static class SnaploopBuild
    {
        const string LogPrefix = "[SnaploopBuild]";
        const string DefaultOutputDirectory = "Builds/Android";

        [MenuItem("Snaploop/Build/Android Debug APK", priority = 1)]
        public static void BuildDebugApk() => Build(BuildVariant.Debug, AndroidPackageFormat.Apk);

        [MenuItem("Snaploop/Build/Android Release APK", priority = 2)]
        public static void BuildReleaseApk() => Build(BuildVariant.Release, AndroidPackageFormat.Apk);

        [MenuItem("Snaploop/Build/Android Release AAB (Google Play)", priority = 3)]
        public static void BuildReleaseAab() => Build(BuildVariant.Release, AndroidPackageFormat.Aab);

        public static void BuildFromCommandLine()
        {
            try
            {
                var variant = ParseVariant(Arg("variant") ?? Env("SNAPLOOP_BUILD_VARIANT") ?? "debug");
                var format = ParseFormat(Arg("format") ?? Arg("androidExportType") ?? Env("SNAPLOOP_BUILD_FORMAT") ?? "apk");
                Build(variant, format);
            }
            catch (Exception e)
            {
                Fail(e.Message);
            }
        }

        public static BuildReport Build(BuildVariant variant, AndroidPackageFormat format)
        {
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0)
            {
                Fail("No enabled scenes. Add the game scenes under File > Build Profiles (Scene List).");
                return null;
            }

            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android
                && !EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android))
            {
                Fail("Could not switch the active build target to Android. Is Android Build Support installed?");
                return null;
            }

            var extension = format == AndroidPackageFormat.Aab ? "aab" : "apk";
            var outputPath = Arg("customBuildPath") ?? Env("SNAPLOOP_BUILD_PATH")
                ?? $"{DefaultOutputDirectory}/Snaploop-{variant.ToString().ToLowerInvariant()}.{extension}";
            var outputDirectory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
            if (!string.IsNullOrEmpty(outputDirectory))
                Directory.CreateDirectory(outputDirectory);

            BuildReport report;
            string signing;
            var snapshot = PlayerSettingsSnapshot.Capture();
            try
            {
                EditorUserBuildSettings.buildAppBundle = format == AndroidPackageFormat.Aab;
                EditorUserBuildSettings.exportAsGoogleAndroidProject = false;

                if (variant == BuildVariant.Release)
                {
                    // Google Play requires 64-bit; ARMv7-only devices are out of scope for the MVP.
                    PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
                    PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
                }

                ApplyVersion();
                signing = ApplySigning(variant);

                var options = new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = outputPath,
                    target = BuildTarget.Android,
                    targetGroup = BuildTargetGroup.Android,
                    options = variant == BuildVariant.Debug ? BuildOptions.Development : BuildOptions.None,
                };

                Debug.Log($"{LogPrefix} Building {variant} {extension.ToUpperInvariant()} -> {outputPath}\n"
                    + $"  application id: {PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android)}\n"
                    + $"  version: {PlayerSettings.bundleVersion} (code {PlayerSettings.Android.bundleVersionCode})\n"
                    + $"  scripting backend: {PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android)}, architectures: {PlayerSettings.Android.targetArchitectures}\n"
                    + $"  signing: {signing}\n"
                    + $"  scenes: {string.Join(", ", scenes)}");

                report = BuildPipeline.BuildPlayer(options);
            }
            finally
            {
                snapshot.Restore();
            }

            var summary = report.summary;
            if (summary.result != BuildResult.Succeeded)
            {
                Fail($"Build {summary.result} with {summary.totalErrors} error(s). See the Unity log above for details.");
                return report;
            }

            Debug.Log($"{LogPrefix} Build succeeded: {summary.outputPath} "
                + $"({summary.totalSize / (1024f * 1024f):F1} MB, {summary.totalTime:hh\\:mm\\:ss}, signing: {signing})");
            if (!Application.isBatchMode)
                EditorUtility.RevealInFinder(summary.outputPath);
            return report;
        }

        static void ApplyVersion()
        {
            var version = Arg("buildVersion") ?? Env("SNAPLOOP_VERSION");
            if (version != null && !version.Equals("none", StringComparison.OrdinalIgnoreCase))
                PlayerSettings.bundleVersion = version;

            var versionCode = Arg("androidVersionCode") ?? Env("SNAPLOOP_ANDROID_VERSION_CODE");
            if (versionCode == null)
                return;
            if (!int.TryParse(versionCode, out var code) || code <= 0)
                throw new ArgumentException($"Android version code must be a positive integer, got '{versionCode}'.");
            PlayerSettings.Android.bundleVersionCode = code;
        }

        static string ApplySigning(BuildVariant variant)
        {
            var keystore = Arg("androidKeystoreName") ?? Env("ANDROID_KEYSTORE_NAME");
            var keystorePass = Arg("androidKeystorePass", allowLeadingDash: true) ?? Env("ANDROID_KEYSTORE_PASS");
            var alias = Arg("androidKeyaliasName") ?? Env("ANDROID_KEYALIAS_NAME");
            var aliasPass = Arg("androidKeyaliasPass", allowLeadingDash: true) ?? Env("ANDROID_KEYALIAS_PASS") ?? keystorePass;

            if (keystore != null)
            {
                var keystorePath = Path.IsPathRooted(keystore) ? keystore : Path.GetFullPath(keystore);
                var missing = new[]
                {
                    File.Exists(keystorePath) ? null : $"keystore file ({keystorePath})",
                    keystorePass == null ? "keystore password" : null,
                    alias == null ? "key alias" : null,
                }.Where(m => m != null).ToArray();

                if (missing.Length == 0)
                {
                    PlayerSettings.Android.useCustomKeystore = true;
                    PlayerSettings.Android.keystoreName = keystorePath;
                    PlayerSettings.Android.keystorePass = keystorePass;
                    PlayerSettings.Android.keyaliasName = alias;
                    PlayerSettings.Android.keyaliasPass = aliasPass;
                    return $"release keystore from build inputs (alias '{alias}')";
                }

                Debug.LogWarning($"{LogPrefix} A keystore was requested but is incomplete; missing: {string.Join(", ", missing)}.");
            }
            else if (PlayerSettings.Android.useCustomKeystore
                     && File.Exists(PlayerSettings.Android.keystoreName)
                     && !string.IsNullOrEmpty(PlayerSettings.Android.keystorePass)
                     && !string.IsNullOrEmpty(PlayerSettings.Android.keyaliasPass))
            {
                return $"keystore from Player Settings (alias '{PlayerSettings.Android.keyaliasName}')";
            }

            if (variant == BuildVariant.Release && (Flag("requireReleaseKeystore") || EnvFlag("SNAPLOOP_REQUIRE_RELEASE_KEYSTORE")))
                throw new InvalidOperationException("Release keystore required but not provided. See docs/BUILD.md (Release keystore).");

            PlayerSettings.Android.useCustomKeystore = false;
            if (variant == BuildVariant.Release)
                Debug.LogWarning($"{LogPrefix} No release keystore provided: signing with the Unity debug keystore. "
                    + "This build can be sideloaded but cannot be uploaded to Google Play.");
            return "Unity debug keystore";
        }

        static BuildVariant ParseVariant(string value)
        {
            switch (value.Trim().ToLowerInvariant())
            {
                case "debug":
                case "development":
                    return BuildVariant.Debug;
                case "release":
                    return BuildVariant.Release;
                default:
                    throw new ArgumentException($"Unknown build variant '{value}'. Use 'debug' or 'release'.");
            }
        }

        static AndroidPackageFormat ParseFormat(string value)
        {
            switch (value.Trim().ToLowerInvariant())
            {
                case "apk":
                case "androidpackage":
                    return AndroidPackageFormat.Apk;
                case "aab":
                case "androidappbundle":
                    return AndroidPackageFormat.Aab;
                default:
                    throw new ArgumentException($"Unsupported Android format '{value}'. Use 'apk' or 'aab'.");
            }
        }

        static void Fail(string message)
        {
            Debug.LogError($"{LogPrefix} {message}");
            if (Application.isBatchMode)
                EditorApplication.Exit(1);
            else
                throw new BuildFailedException(message);
        }

        // GameCI passes every flag with a value, empty when unset, so blank values count as absent.
        // Values that look like another flag are treated as absent unless a password may start with '-'.
        static string Arg(string name, bool allowLeadingDash = false)
        {
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (!string.Equals(args[i], "-" + name, StringComparison.OrdinalIgnoreCase))
                    continue;
                var value = args[i + 1];
                if (string.IsNullOrWhiteSpace(value) || (!allowLeadingDash && value.StartsWith("-")))
                    return null;
                return value;
            }
            return null;
        }

        static bool Flag(string name) =>
            Environment.GetCommandLineArgs().Any(a => string.Equals(a, "-" + name, StringComparison.OrdinalIgnoreCase));

        static string Env(string name)
        {
            var value = Environment.GetEnvironmentVariable(name);
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        static bool EnvFlag(string name)
        {
            var value = Env(name);
            return value != null && (value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase));
        }

        sealed class PlayerSettingsSnapshot
        {
            string bundleVersion;
            int bundleVersionCode;
            bool useCustomKeystore;
            string keystoreName;
            string keystorePass;
            string keyaliasName;
            string keyaliasPass;
            ScriptingImplementation scriptingBackend;
            AndroidArchitecture architectures;
            bool buildAppBundle;
            bool exportAsGoogleAndroidProject;

            public static PlayerSettingsSnapshot Capture() => new PlayerSettingsSnapshot
            {
                bundleVersion = PlayerSettings.bundleVersion,
                bundleVersionCode = PlayerSettings.Android.bundleVersionCode,
                useCustomKeystore = PlayerSettings.Android.useCustomKeystore,
                keystoreName = PlayerSettings.Android.keystoreName,
                keystorePass = PlayerSettings.Android.keystorePass,
                keyaliasName = PlayerSettings.Android.keyaliasName,
                keyaliasPass = PlayerSettings.Android.keyaliasPass,
                scriptingBackend = PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android),
                architectures = PlayerSettings.Android.targetArchitectures,
                buildAppBundle = EditorUserBuildSettings.buildAppBundle,
                exportAsGoogleAndroidProject = EditorUserBuildSettings.exportAsGoogleAndroidProject,
            };

            public void Restore()
            {
                PlayerSettings.bundleVersion = bundleVersion;
                PlayerSettings.Android.bundleVersionCode = bundleVersionCode;
                PlayerSettings.Android.useCustomKeystore = useCustomKeystore;
                PlayerSettings.Android.keystoreName = keystoreName;
                PlayerSettings.Android.keystorePass = keystorePass;
                PlayerSettings.Android.keyaliasName = keyaliasName;
                PlayerSettings.Android.keyaliasPass = keyaliasPass;
                if (PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android) != scriptingBackend)
                    PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, scriptingBackend);
                PlayerSettings.Android.targetArchitectures = architectures;
                EditorUserBuildSettings.buildAppBundle = buildAppBundle;
                EditorUserBuildSettings.exportAsGoogleAndroidProject = exportAsGoogleAndroidProject;
            }
        }
    }
}
