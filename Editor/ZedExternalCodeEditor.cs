using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Diagnostics;
#if UNITY_EDITOR_OSX
using System.Runtime.InteropServices;
#endif
using UnityEditor;
using UnityEngine;
using Unity.CodeEditor;
using Debug = UnityEngine.Debug;
// Explicit aliases to avoid CS0433 when Unity.Cursor.Editor also defines IGenerator.
using IGenerator = Microsoft.Unity.VisualStudio.Editor.IGenerator;

namespace UnityZed
{
    [InitializeOnLoad]
    public class ZedExternalCodeEditor : IExternalCodeEditor
    {
        // Lowercased, spaces-stripped filename whitelist — same approach as com.unity.ide.vscode.
        static readonly string[] k_SupportedFileNames =
        {
            "zed.exe",   // Windows
            "zed",       // Linux / macOS symlink
            "zeditor",   // Linux package-repo binary name
            "cli",       // macOS: Zed.app/Contents/MacOS/cli
        };

        readonly ZedDiscovery m_Discovery;
        readonly IGenerator   m_Generator;
        readonly ZedSettings  m_Settings;
        ZedPreferences        m_Preferences;

        // Always read the live EditorPrefs value — never rely on Initialize() having been called.
        // This is identical to how com.unity.ide.vscode handles it.
        static string DefaultApp  => EditorPrefs.GetString("kScriptsDefaultApp");
        static bool   IsOSX       => Application.platform == RuntimePlatform.OSXEditor;
        static string ProjectDir  => Directory.GetParent(Application.dataPath).FullName;

        // ── Bootstrap ────────────────────────────────────────────────────────────

        static ZedExternalCodeEditor()
        {
            IGenerator generator;
            try
            {
                var asm  = typeof(IGenerator).Assembly;
                var type = asm.GetType("Microsoft.Unity.VisualStudio.Editor.SdkStyleProjectGeneration");
                if (type == null)
                    throw new InvalidOperationException("SdkStyleProjectGeneration not found.");
                generator = (IGenerator)Activator.CreateInstance(type);
            }
            catch (Exception e)
            {
                Debug.LogError("[ZedEditor] Cannot create project generator: " + e.Message +
                               "\nEnsure com.unity.ide.visualstudio is installed.");
                return;
            }

            CodeEditor.Register(new ZedExternalCodeEditor(new ZedDiscovery(), generator));
        }

        public ZedExternalCodeEditor(ZedDiscovery discovery, IGenerator generator)
        {
            m_Discovery   = discovery;
            m_Generator   = generator;
            m_Settings    = new ZedSettings();
            m_Preferences = new ZedPreferences(generator);
        }

        // Unity calls this when the user switches editors in External Tools.
        // Intentionally empty: DefaultApp is read fresh on every OpenProject call,
        // so there is no per-instance state to set up here.
        public void Initialize(string editorInstallationPath) { }

        static bool IsZedPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            var name = Path.GetFileName(path.ToLower()).Replace(" ", "");
            return k_SupportedFileNames.Contains(name);
        }

        // ── Discovery ────────────────────────────────────────────────────────────

        public CodeEditor.Installation[] Installations => m_Discovery.GetInstallations();

        public bool TryGetInstallationForPath(string editorPath, out CodeEditor.Installation installation)
        {
            // Only check the filename — same as VS Code plugin.
            // This lets the user pick any path to Zed and have our plugin claim it.
            if (!IsZedPath(editorPath))
            {
                installation = default;
                return false;
            }

            var all = Installations;
            if (!all.Any())
            {
                installation = new CodeEditor.Installation { Name = "Zed", Path = editorPath };
            }
            else
            {
                try
                {
                    installation = all.First(i =>
                        i.Path.Equals(editorPath, StringComparison.OrdinalIgnoreCase));
                }
                catch (InvalidOperationException)
                {
                    // Path not in auto-discovered list (e.g. user browsed to it manually).
                    installation = new CodeEditor.Installation { Name = "Zed", Path = editorPath };
                }
            }
            return true;
        }

        // ── Sync ─────────────────────────────────────────────────────────────────

        public void SyncAll()
        {
            AssetDatabase.Refresh();
            m_Generator.Sync();
            m_Settings.Sync();
        }

        public void SyncIfNeeded(string[] addedFiles, string[] deletedFiles, string[] movedFiles,
                                  string[] movedFromFiles, string[] importedFiles)
        {
            m_Generator.SyncIfNeeded(
                addedFiles.Union(deletedFiles).Union(movedFiles).Union(movedFromFiles),
                importedFiles);
        }

        // ── Open ─────────────────────────────────────────────────────────────────

        public bool OpenProject(string filePath = "", int line = -1, int column = -1)
        {
            // Empty path = "Assets → Open C# Project" menu item.
            if (filePath != "" && !File.Exists(filePath))
                return false;

            if (line   == -1) line   = 1;
            if (column == -1) column = 0;

            var app   = DefaultApp;
            var isCli = IsBinCli(app);
            var args  = BuildArguments(filePath, line, column, isCli);

            Debug.Log($"[ZedEditor] \"{app}\"  {args}");

            // macOS: do not use `open -n … --args …` with /usr/local/bin/zed — it often fails
            // silently or mis-handles argv. Spawn the CLI directly like a terminal would.
            // Also, IsBinCli must NOT match macOS paths like …/usr/local/bin/zed (parent dir
            // name is "bin"), which incorrectly triggered the Windows-only zed.exe workaround.
            if (IsOSX)
                return LaunchMac(app, args);

            return Launch(app, args, isCli);
        }

        // Windows only: bin\zed.exe is the console CLI shim; Zed.exe (one level up) is the GUI app.
        // On macOS/Linux, …/bin/zed is a normal install location, not the Windows shim layout.
        static bool IsBinCli(string app)
        {
#if UNITY_EDITOR_WIN
            return Path.GetFileName(Path.GetDirectoryName(app) ?? "")
                .Equals("bin", StringComparison.OrdinalIgnoreCase);
#else
            return false;
#endif
        }

        static string BuildArguments(string filePath, int line, int column, bool isCli)
        {
            if (isCli)
            {
                // Windows bin\zed.exe has two known bugs that make passing a project dir
                // or file:line:col unreliable:
                //   1. Project dir gets concatenated with the IPC pipe path (zed-cli:\UUID),
                //      causing "Error: opening project path …\zed-cli:\UUID".
                //   2. The file:line:col suffix triggers a \\?\ extended-path prefix bug
                //      (zed-industries/zed issue #46943, open as of 2026-01).
                // Workaround: pass only the file path (or project dir when no file given).
                return filePath != "" ? $@"""{filePath}""" : $@"""{ProjectDir}""";
            }

            // Zed.exe (GUI) or macOS CLI: pass project root + file with line:col.
            var args = $@"""{ProjectDir}""";
            if (filePath != "" && filePath != ProjectDir)
                args += $@" ""{filePath}"":{line}:{column}";
            return args;
        }

        static bool Launch(string app, string args, bool isCli)
        {
            // CLI shim: UseShellExecute = false + CreateNoWindow so it runs silently
            // in the background and forwards the request to the Zed GUI server.
            // GUI app: UseShellExecute = true — standard Windows approach, no CMD window.
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName        = app,
                    Arguments       = args,
                    UseShellExecute = !isCli,
                    CreateNoWindow  = isCli,
                    WindowStyle     = isCli ? ProcessWindowStyle.Hidden : ProcessWindowStyle.Normal,
                };
                if (isCli && !string.IsNullOrEmpty(ProjectDir))
                    psi.WorkingDirectory = ProjectDir;

                Process.Start(psi);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[ZedEditor] Failed to launch.\nApp : {app}\nArgs: {args}\n{e.Message}");
                return false;
            }
        }

#if UNITY_EDITOR_OSX
        [DllImport("libc", CallingConvention = CallingConvention.Cdecl, EntryPoint = "readlink")]
        static extern long readlink_posix(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
            byte[] buf,
            UIntPtr buflen);

        static string PosixReadLink(string path)
        {
            var buf = new byte[8192];
            var n   = readlink_posix(path, buf, (UIntPtr)buf.Length);
            if (n < 0)
                return null;
            return System.Text.Encoding.UTF8.GetString(buf, 0, (int)n);
        }

        static string ResolveSymlinkChain(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;
            var cur = path;
            for (var depth = 0; depth < 64; depth++)
            {
                if (!File.Exists(cur))
                    return cur;
                var target = PosixReadLink(cur);
                if (string.IsNullOrEmpty(target))
                    return cur;
                cur = Path.IsPathRooted(target)
                    ? target
                    : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(cur) ?? "", target));
            }
            return cur;
        }

        // EditorPrefs may point at a stale path (e.g. /usr/local/bin/zed on Homebrew Apple Silicon).
        // Unity/Mono may also fail to exec through a symlink ("Cannot find the specified file").
        static IEnumerable<string> MacZedExecutableCandidates(string editorPrefPath)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in new[]
                     {
                         editorPrefPath,
                         ResolveSymlinkChain(editorPrefPath),
                         "/opt/homebrew/bin/zed",
                         "/usr/local/bin/zed",
                         "/Applications/Zed.app/Contents/MacOS/cli",
                     })
            {
                if (string.IsNullOrEmpty(p))
                    continue;
                var q = p.Trim();
                if (seen.Add(q))
                    yield return q;
            }
        }

        static bool LaunchMac(string app, string args)
        {
            foreach (var exe in MacZedExecutableCandidates(app))
            {
                if (!File.Exists(exe))
                    continue;

                var tryPath = exe;
                var resolved = ResolveSymlinkChain(exe);
                if (File.Exists(resolved))
                    tryPath = resolved;

                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName        = tryPath,
                        Arguments       = args,
                        UseShellExecute = false,
                        CreateNoWindow  = true,
                        WindowStyle     = ProcessWindowStyle.Hidden,
                        WorkingDirectory = ProjectDir,
                    });
                    return true;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[ZedEditor] Direct launch failed ({tryPath}): {e.Message}");
                }
            }

            // Last resort: let Launch Services start Zed.app (argv still reach the CLI entry).
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName         = "/usr/bin/open",
                    Arguments        = $"-na Zed --args {args}",
                    UseShellExecute  = false,
                    CreateNoWindow   = true,
                    WindowStyle      = ProcessWindowStyle.Hidden,
                    WorkingDirectory = ProjectDir,
                });
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError(
                    "[ZedEditor] Failed to launch Zed on macOS. Tried zed binaries under EditorPrefs, " +
                    "/opt/homebrew/bin, /usr/local/bin, Zed.app cli, then `open -na Zed`.\n" +
                    $"Original app path: {app}\nArgs: {args}\n{e.Message}\n" +
                    "Fix: External Tools → set Zed to /Applications/Zed.app/Contents/MacOS/cli " +
                    "or run `which zed` in Terminal and browse to that path.");
                return false;
            }
        }
#else
        static bool LaunchMac(string app, string args) => false;
#endif

        // ── GUI ──────────────────────────────────────────────────────────────────

        public void OnGUI() => m_Preferences.OnGUI();
    }
}
