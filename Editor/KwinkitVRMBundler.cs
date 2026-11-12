#if UNITY_EDITOR
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Kwinkit.VS.EditorTools
{
    // Put this file in Assets/Kwinkit-VS/Editor. Bundle locally, then export Assets/Kwinkit-VS.
    [InitializeOnLoad]
    public static class KwinkitVRMBundler
    {
        const string ArchiveFolder = "Assets/Kwinkit-VS/Dependencies/UniVRM";
        static readonly string[] Packages = { "com.vrmc.gltf", "com.vrmc.vrm" };
        const string InstallKey = "KwinkitVS.UniVRM.InstallAttempted.";

        static KwinkitVRMBundler()
        {
            EditorApplication.delayCall += AutoInstall;
        }

        [MenuItem("Tools/Kwinkit-VS/Prepare UniVRM Bundle")]
        public static void PrepareBundle()
        {
            var root = Directory.GetCurrentDirectory();
            var archiveDir = Path.Combine(root, ArchiveFolder);
            Directory.CreateDirectory(archiveDir);
            foreach (var package in Packages)
            {
                var source = Path.Combine(root, "Packages", package);
                if (!Directory.Exists(source) || !File.Exists(Path.Combine(source, "package.json")))
                {
                    EditorUtility.DisplayDialog("Kwinkit-VS", "Missing local package: Packages/" + package + ". Both local package folders must exist.", "OK");
                    return;
                }
            }
            foreach (var package in Packages)
            {
                var source = Path.Combine(root, "Packages", package);
                var destination = Path.Combine(archiveDir, package + ".zip");
                if (File.Exists(destination)) File.Delete(destination);
                ZipFile.CreateFromDirectory(source, destination, System.IO.Compression.CompressionLevel.Optimal, false);
            }
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("Kwinkit-VS", "UniVRM archives created in " + ArchiveFolder + ". Export the entire Assets/Kwinkit-VS folder as one .unitypackage.", "OK");
        }

        static void AutoInstall()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
            var root = Directory.GetCurrentDirectory();
            var archiveDir = Path.Combine(root, ArchiveFolder);
            if (!Packages.All(p => File.Exists(Path.Combine(archiveDir, p + ".zip")))) return;
            var missing = Packages.Where(p => !Directory.Exists(Path.Combine(root, "Packages", p))).ToArray();
            if (missing.Length == 0) return;
            // Only attempt once per archive set per editor session, to avoid repeated errors.
            var stamp = string.Join("|", Packages.Select(p => new FileInfo(Path.Combine(archiveDir, p + ".zip")).Length.ToString()));
            var key = InstallKey + stamp;
            if (SessionState.GetBool(key, false)) return;
            SessionState.SetBool(key, true);
            try
            {
                foreach (var package in missing)
                {
                    var zipPath = Path.Combine(archiveDir, package + ".zip");
                    var target = Path.Combine(root, "Packages", package);
                    var staging = target + ".kwinkit-tmp";
                    if (Directory.Exists(staging)) Directory.Delete(staging, true);
                    Directory.CreateDirectory(staging);
                    // Prevent malicious zip entries from escaping the target folder.
                    var fullStage = Path.GetFullPath(staging) + Path.DirectorySeparatorChar;
                    using (var zip = ZipFile.OpenRead(zipPath))
                    foreach (var entry in zip.Entries)
                    {
                        var path = Path.GetFullPath(Path.Combine(staging, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
                        if (!path.StartsWith(fullStage, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Unsafe archive entry");
                        if (string.IsNullOrEmpty(entry.Name)) { Directory.CreateDirectory(path); continue; }
                        Directory.CreateDirectory(Path.GetDirectoryName(path));
                        entry.ExtractToFile(path, true);
                    }
                    if (!File.Exists(Path.Combine(staging, "package.json"))) throw new InvalidDataException(package + " archive lacks package.json");
                    Directory.Move(staging, target);
                }
                RegisterLocalPackages(root);
                Debug.Log("[Kwinkit-VS] UniVRM local packages installed. Unity will refresh package dependencies.");
                AssetDatabase.Refresh();
            }
            catch (Exception ex)
            {
                Debug.LogError("[Kwinkit-VS] UniVRM installation failed: " + ex);
            }
        }

        static void RegisterLocalPackages(string root)
        {
            var path = Path.Combine(root, "Packages", "manifest.json");
            var json = File.ReadAllText(path);
            var match = Regex.Match(json, "\\\"dependencies\\\"\\s*:\\s*\\{");
            if (!match.Success) throw new InvalidDataException("Cannot locate dependencies in Packages/manifest.json");
            foreach (var package in Packages)
            {
                var line = "\"" + package + "\": \"file:" + package + "\"";
                var existing = Regex.Match(json, "\\\"" + Regex.Escape(package) + "\\\"\\s*:\\s*\\\"[^\\\"]*\\\"");
                if (existing.Success) continue; // Do not overwrite an existing user's package reference.
                var insertion = Regex.Match(json, "\\\"dependencies\\\"\\s*:\\s*\\{");
                json = json.Insert(insertion.Index + insertion.Length, "\n    " + line + ",");
            }
            // Clean a trailing comma only if dependencies was originally empty.
            json = Regex.Replace(json, "(\\\"dependencies\\\"\\s*:\\s*\\{(?:\\s*\\\"[^\\\"]+\\\"\\s*:\\s*\\\"[^\\\"]*\\\"\\s*,)+)\\s*\\}", m => m.Groups[1].Value.TrimEnd().TrimEnd(',') + "\n  }");
            File.WriteAllText(path, json);
        }
    }
}
#endif
