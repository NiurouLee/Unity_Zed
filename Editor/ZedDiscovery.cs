using Unity.CodeEditor;
using System;
using System.Collections.Generic;
using System.Xml.XPath;
using System.Text;
using NiceIO;

namespace UnityZed
{
    public class ZedDiscovery
    {
        public CodeEditor.Installation[] GetInstallations()
        {
            var results = new List<CodeEditor.Installation>();
            var candidates = BuildCandidates();

            foreach (var (path, tryGetVersion) in candidates)
            {
                if (!path.FileExists())
                    continue;

                var name = new StringBuilder("Zed");
                var getVersion = tryGetVersion ?? TryGetVersionFallback;

                if (getVersion(path, out var version))
                    name.Append($" [{version}]");

                results.Add(new CodeEditor.Installation
                {
                    Name = name.ToString(),
                    Path = path.MakeAbsolute().ToString(),
                });
            }

            return results.ToArray();
        }

        public bool TryGetInstallationForPath(string editorPath, out CodeEditor.Installation installation)
        {
            installation = default;

            if (string.IsNullOrEmpty(editorPath))
                return false;

            var path = new NPath(editorPath);
            if (!path.FileExists())
                return false;

            if (!IsZedExecutable(path))
                return false;

            var name = new StringBuilder("Zed");
            if (TryGetVersionFromPlist(path, out var version))
                name.Append($" [{version}]");

            installation = new CodeEditor.Installation
            {
                Name = name.ToString(),
                Path = path.MakeAbsolute().ToString(),
            };
            return true;
        }

        // Returns true if the path looks like a Zed executable.
        // Covers: zed / zeditor (all platforms), Zed.exe (Windows), and the macOS .app bundle CLI.
        private static bool IsZedExecutable(NPath path)
        {
            var fileName = path.FileNameWithoutExtension.ToLowerInvariant();

            if (fileName == "zed" || fileName == "zeditor")
                return true;

            // macOS bundle: Zed.app/Contents/MacOS/cli
            if (fileName == "cli" && path.ToString().IndexOf("Zed.app", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            return false;
        }

        private static List<(NPath path, TryGetVersion tryGetVersion)> BuildCandidates()
        {
            var list = new List<(NPath, TryGetVersion)>();

#if UNITY_EDITOR_OSX
            // [macOS]
            list.Add((new NPath("/Applications/Zed.app/Contents/MacOS/cli"), TryGetVersionFromPlist));
            list.Add((new NPath("/usr/local/bin/zed"), null));
#elif UNITY_EDITOR_LINUX
            // [Linux] Flatpak
            list.Add((new NPath("/var/lib/flatpak/app/dev.zed.Zed/current/active/files/bin/zed"), null));
            // [Linux] Package repos
            list.Add((new NPath("/usr/bin/zeditor"), null));
            list.Add((new NPath("/usr/bin/zed"), null));
            // [Linux] NixOS system profile
            list.Add((new NPath("/run/current-system/sw/bin/zeditor"), null));
            // [Linux] NixOS HomeManager (use current user, not hardcoded name)
            var nixUser = Environment.UserName;
            list.Add((new NPath($"/etc/profiles/per-user/{nixUser}/bin/zed"), null));
            list.Add((new NPath($"/etc/profiles/per-user/{nixUser}/bin/zeditor"), null));
            // [Linux] Official website tarball / manual install
            list.Add((NPath.HomeDirectory.Combine(".local/bin/zed"), null));
#elif UNITY_EDITOR_WIN
            // [Windows] common installation paths
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            // Official installer (user-level, same layout as VS Code)
            list.Add((new NPath(localAppData).Combine("Programs").Combine("Zed").Combine("Zed.exe"), null));
            // System-level install
            list.Add((new NPath(programFiles).Combine("Zed").Combine("Zed.exe"), null));
            list.Add((new NPath(programFilesX86).Combine("Zed").Combine("Zed.exe"), null));
            // Scoop
            list.Add((NPath.HomeDirectory.Combine("scoop").Combine("shims").Combine("zed.exe"), null));
            list.Add((NPath.HomeDirectory.Combine("scoop").Combine("apps").Combine("zed").Combine("current").Combine("Zed.exe"), null));
#endif
            return list;
        }

        //
        // TryGetVersion implementations
        //
        private delegate bool TryGetVersion(NPath path, out string version);

        private static bool TryGetVersionFallback(NPath path, out string version)
        {
            version = null;
            return false;
        }

        private static bool TryGetVersionFromPlist(NPath path, out string version)
        {
            version = null;

            var plistPath = path.Combine("../../Info.plist");
            if (!plistPath.FileExists())
                return false;

            var xDoc = new XPathDocument(plistPath.ToString());
            var node = xDoc.CreateNavigator().SelectSingleNode(
                "/plist/dict/key[text()='CFBundleShortVersionString']/following-sibling::string[1]/text()");

            if (node == null)
                return false;

            version = node.Value;
            return true;
        }
    }
}
