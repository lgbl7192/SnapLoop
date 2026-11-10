using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace Snaploop.Levels
{
    /// <summary>Loads and validates StreamingAssets/Levels/levels.json.</summary>
    public static class LevelCatalog
    {
        public const string FolderName = "Levels";
        public const string ManifestName = "levels.json";

        public static IReadOnlyList<LevelInfo> Levels => levels;
        public static bool IsLoaded { get; private set; }
        public static string LoadError { get; private set; }

        static readonly List<LevelInfo> levels = new List<LevelInfo>();

        public static LevelInfo Get(int index) => index >= 0 && index < levels.Count ? levels[index] : null;

        /// <summary>
        /// Full URL/path for a file in StreamingAssets/Levels. On Android this is a jar:file:// URL
        /// inside the APK, which VideoPlayer and UnityWebRequest both accept.
        /// </summary>
        public static string GetLevelFilePath(string fileName)
        {
            return Application.streamingAssetsPath + "/" + FolderName + "/" + fileName;
        }

        /// <summary>URL form (file:// prefixed on platforms where streamingAssetsPath is a plain path).</summary>
        public static string GetLevelFileUrl(string fileName)
        {
            string path = GetLevelFilePath(fileName);
            return path.Contains("://") ? path : "file://" + path;
        }

        /// <summary>Reads a text file from StreamingAssets/Levels. Calls <paramref name="onDone"/> with (text, null) or (null, error).</summary>
        public static IEnumerator ReadText(string fileName, Action<string, string> onDone)
        {
            string path = GetLevelFilePath(fileName);
            if (path.Contains("://"))
            {
                using (var req = UnityWebRequest.Get(path))
                {
                    yield return req.SendWebRequest();
                    if (req.result == UnityWebRequest.Result.Success) onDone(req.downloadHandler.text, null);
                    else onDone(null, "Could not read " + fileName + ": " + req.error);
                }
                yield break;
            }
            string text = null, error = null;
            try { text = File.ReadAllText(path); }
            catch (Exception e) { error = "Could not read " + fileName + ": " + e.Message; }
            onDone(text, error);
        }

        public static IEnumerator Load(Action onDone)
        {
            levels.Clear();
            LoadError = null;
            string json = null;
            yield return ReadText(ManifestName, (text, error) => { json = text; LoadError = error; });

            if (json != null)
            {
                var warnings = new List<string>();
                if (!TryParse(json, levels, warnings, out string error)) LoadError = error;
                foreach (var w in warnings) Debug.LogWarning("[Snaploop] " + ManifestName + ": " + w);
            }
            IsLoaded = true;
            if (LoadError != null) Debug.LogError("[Snaploop] " + LoadError);
            onDone?.Invoke();
        }

        /// <summary>Parses and validates a manifest into <paramref name="target"/>. Shared with the editor validator.</summary>
        public static bool TryParse(string json, List<LevelInfo> target, List<string> warnings, out string error)
        {
            error = null;
            LevelManifest manifest = null;
            try { manifest = JsonUtility.FromJson<LevelManifest>(json); }
            catch (Exception e) { error = ManifestName + " is not valid JSON: " + e.Message; return false; }

            if (manifest == null || manifest.levels == null)
            {
                error = ManifestName + " has no \"levels\" array.";
                return false;
            }

            var seen = new HashSet<string>();
            var perPack = new Dictionary<string, int>();
            for (int i = 0; i < manifest.levels.Length; i++)
            {
                var l = manifest.levels[i];
                if (l == null || string.IsNullOrEmpty(l.file))
                {
                    warnings.Add("Entry " + i + " has no \"file\"; skipped.");
                    continue;
                }
                if (string.IsNullOrEmpty(l.id)) l.id = Path.GetFileNameWithoutExtension(l.file);
                if (!seen.Add(l.id))
                {
                    warnings.Add("Duplicate id \"" + l.id + "\"; skipped.");
                    continue;
                }

                l.Index = target.Count;
                l.PackId = string.IsNullOrWhiteSpace(l.pack) ? PackInfo.StarterId : l.pack.Trim();
                perPack.TryGetValue(l.PackId, out int packIndex);
                l.PackIndex = packIndex;
                perPack[l.PackId] = packIndex + 1;
                if (l.cols >= DifficultyRamp.MinSide && l.rows >= DifficultyRamp.MinSide)
                {
                    l.Cols = Mathf.Min(l.cols, DifficultyRamp.MaxSide);
                    l.Rows = Mathf.Min(l.rows, DifficultyRamp.MaxSide);
                    if (l.Cols != l.cols || l.Rows != l.rows)
                        warnings.Add(l.id + ": grid clamped to " + l.Cols + "x" + l.Rows + ".");
                }
                else
                {
                    var g = DifficultyRamp.ForIndex(l.PackIndex);
                    l.Cols = g.x;
                    l.Rows = g.y;
                    if (l.cols != 0 || l.rows != 0)
                        warnings.Add(l.id + ": invalid cols/rows, using " + g.x + "x" + g.y + ".");
                }
                target.Add(l);
            }

            if (target.Count == 0)
            {
                error = ManifestName + " contains no playable levels.";
                return false;
            }
            return true;
        }
    }
}
