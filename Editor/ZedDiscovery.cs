using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.XPath;
using Unity.CodeEditor;

namespace UnityZed
{
    // Path-discovery for Zed installations.
    // Modelled after VSCodeDiscovery in com.unity.ide.vscode: uses Environment variables,
    // FileInfo.Exists, and a lazy-cached result list.
    public class ZedDiscovery
    {
        List<CodeEditor.Installation> m_Installations;

        public CodeEditor.Installation[] GetInstallations()
        {
            if (m_Installations == null)
            {
                m_Installations = new List<CodeEditor.Installation>();
                FindInstallationPaths();
            }
            return m_Installations.ToArray();
        }

        void FindInstallationPaths()
        {
            var candidates =
#if UNITY_EDITOR_OSX
            new[]
            {
                "/Applications/Zed.app/Contents/MacOS/cli",
                "/opt/homebrew/bin/zed", // Apple Silicon Homebrew
                "/usr/local/bin/zed",
            };
#elif UNITY_EDITOR_LINUX
            new[]
            {
                "/var/lib/flatpak/app/dev.zed.Zed/current/active/files/bin/zed",
                "/usr/bin/zeditor",
                "/usr/bin/zed",
                "/run/current-system/sw/bin/zeditor",
                $"/etc/profiles/per-user/{Environment.UserName}/bin/zed",
                $"/etc/profiles/per-user/{Environment.UserName}/bin/zeditor",
                $"{GetHome()}/.local/bin/zed",
            };
#else // UNITY_EDITOR_WIN
            new[]
            {
                // User-level installer (same layout as VS Code)
                GetLocalAppData() + "/Programs/Zed/Zed.exe",
                // System-level installers
                GetProgramFiles()    + "/Zed/Zed.exe",
                GetProgramFilesX86() + "/Zed/Zed.exe",
                // Scoop
                GetHome() + "/scoop/shims/zed.exe",
                GetHome() + "/scoop/apps/zed/current/Zed.exe",
            };
#endif

            var existing = candidates.Where(ZedExists).ToList();
            if (!existing.Any())
                return;

            foreach (var path in existing)
            {
                var name = "Zed";

#if UNITY_EDITOR_OSX
                if (TryGetVersionFromPlist(path, out var v))
                    name += $" [{v}]";
#endif
                m_Installations.Add(new CodeEditor.Installation { Name = name, Path = path });
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        static bool ZedExists(string path)
        {
#if UNITY_EDITOR_OSX
            // The CLI inside the .app bundle — check as a file.
            return new FileInfo(path).Exists;
#else
            return new FileInfo(path).Exists;
#endif
        }

        // Environment accessors — mirrors the VS Code plugin style.
        // Replace backslashes with forward slashes so string concatenation works uniformly.
        static string GetLocalAppData()  =>
            Environment.GetEnvironmentVariable("LOCALAPPDATA")?.Replace('\\', '/') ?? "";

        static string GetProgramFiles()  =>
            Environment.GetEnvironmentVariable("ProgramFiles")?.Replace('\\', '/') ?? "";

        static string GetProgramFilesX86() =>
            (Environment.GetEnvironmentVariable("ProgramFiles(x86)") ??
             Environment.GetEnvironmentVariable("ProgramFiles"))?.Replace('\\', '/') ?? "";

        static string GetHome() =>
            (Environment.GetEnvironmentVariable("USERPROFILE") ??   // Windows
             Environment.GetEnvironmentVariable("HOME") ??          // macOS / Linux
             "").Replace('\\', '/');

        // ── macOS version from Info.plist ────────────────────────────────────────

        static bool TryGetVersionFromPlist(string cliPath, out string version)
        {
            version = null;
            // cli lives at  Zed.app/Contents/MacOS/cli
            // Info.plist at Zed.app/Contents/Info.plist  (two levels up from cli)
            var plist = Path.GetFullPath(Path.Combine(cliPath, "..", "..", "Info.plist"));
            if (!File.Exists(plist))
                return false;

            try
            {
                var doc  = new XPathDocument(plist);
                var node = doc.CreateNavigator().SelectSingleNode(
                    "/plist/dict/key[text()='CFBundleShortVersionString']" +
                    "/following-sibling::string[1]/text()");
                if (node == null) return false;
                version = node.Value;
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
