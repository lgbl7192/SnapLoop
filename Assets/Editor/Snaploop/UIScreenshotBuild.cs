using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

namespace Snaploop.EditorTools
{
    /// <summary>
    /// Builds the Linux development player that .github/workflows/ui-screenshots.yml runs under Xvfb to
    /// capture every screen (see Snaploop.Dev.UIScreenshotRunner). Not used for shipping builds.
    ///
    /// Batch mode:
    ///   Unity -batchmode -quit -projectPath . -buildTarget StandaloneLinux64 \
    ///     -executeMethod Snaploop.EditorTools.UIScreenshotBuild.BuildFromCommandLine [-customBuildPath build/Snaploop.x86_64]
    ///
    /// Player Settings it changes (windowed, runs unfocused, OpenGL Core, Mono) are restored afterwards.
    /// </summary>
    public static class UIScreenshotBuild
    {
        const string DefaultPath = "build/UIScreenshots/Snaploop.x86_64";

        public static void BuildFromCommandLine()
        {
            var path = Arg("customBuildPath") ?? DefaultPath;
            if (string.IsNullOrEmpty(Path.GetExtension(path))) path += ".x86_64";
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));

            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            var target = BuildTarget.StandaloneLinux64;
            var named = NamedBuildTarget.Standalone;

            bool runInBackground = PlayerSettings.runInBackground;
            bool visibleInBackground = PlayerSettings.visibleInBackground;
            bool resizable = PlayerSettings.resizableWindow;
            var fullScreenMode = PlayerSettings.fullScreenMode;
            int width = PlayerSettings.defaultScreenWidth, height = PlayerSettings.defaultScreenHeight;
            bool defaultApis = PlayerSettings.GetUseDefaultGraphicsAPIs(target);
            var apis = PlayerSettings.GetGraphicsAPIs(target);
            var backend = PlayerSettings.GetScriptingBackend(named);

            BuildReport report;
            try
            {
                PlayerSettings.runInBackground = true;
                PlayerSettings.visibleInBackground = true;
                PlayerSettings.resizableWindow = true;
                PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
                PlayerSettings.defaultScreenWidth = 1080;
                PlayerSettings.defaultScreenHeight = 2400;
                PlayerSettings.SetUseDefaultGraphicsAPIs(target, false);
                PlayerSettings.SetGraphicsAPIs(target, new[] { GraphicsDeviceType.OpenGLCore });
                PlayerSettings.SetScriptingBackend(named, ScriptingImplementation.Mono2x);

                Debug.Log("[UIScreenshotBuild] Building Linux development player -> " + path);
                report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = path,
                    target = target,
                    targetGroup = BuildTargetGroup.Standalone,
                    options = BuildOptions.Development,
                });
            }
            finally
            {
                PlayerSettings.runInBackground = runInBackground;
                PlayerSettings.visibleInBackground = visibleInBackground;
                PlayerSettings.resizableWindow = resizable;
                PlayerSettings.fullScreenMode = fullScreenMode;
                PlayerSettings.defaultScreenWidth = width;
                PlayerSettings.defaultScreenHeight = height;
                PlayerSettings.SetGraphicsAPIs(target, apis);
                PlayerSettings.SetUseDefaultGraphicsAPIs(target, defaultApis);
                PlayerSettings.SetScriptingBackend(named, backend);
            }

            var summary = report.summary;
            var eol = Environment.NewLine;
            // GameCI's unity-builder looks for this block to decide whether a custom build method succeeded.
            Console.WriteLine(
                $"{eol}###########################{eol}#      Build results      #{eol}###########################{eol}{eol}"
                + $"Duration: {summary.totalTime}{eol}Warnings: {summary.totalWarnings}{eol}Errors: {summary.totalErrors}{eol}Size: {summary.totalSize} bytes{eol}");
            Console.WriteLine(summary.result == BuildResult.Succeeded ? "Build succeeded!" : $"Build {summary.result}!");
            if (summary.result != BuildResult.Succeeded && Application.isBatchMode) EditorApplication.Exit(1);
        }

        static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-" + name) return args[i + 1];
            return null;
        }
    }
}
