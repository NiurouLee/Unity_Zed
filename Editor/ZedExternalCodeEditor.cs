using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
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

            var app  = DefaultApp;
            var args = BuildArguments(filePath, line, column);

            Debug.Log($"[ZedEditor] \"{app}\"  {args}");

            return IsOSX
                ? Launch("open", $"-n \"{app}\" --args {args}")
                : Launch(app, args);
        }

        static string BuildArguments(string filePath, int line, int column)
        {
            // Always pass the project root so Zed opens it as a workspace.
            var args = $@"""{ProjectDir}""";

            if (filePath != "" && filePath != ProjectDir)
            {
                // Zed CLI: zed <project> <file[:line[:col]]>
                // Mirrors the VS Code pattern: "project" -g "file":line:col
                // but Zed uses no flag — file is a plain positional argument.
                args += $@" ""{filePath}"":{line}:{column}";
            }

            return args;
        }

        static bool Launch(string app, string args)
        {
            try
            {
                new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName        = app,
                        Arguments       = args,
                        WindowStyle     = ProcessWindowStyle.Normal,
                        CreateNoWindow  = true,
                        UseShellExecute = true,
                    }
                }.Start();
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[ZedEditor] Failed to launch.\nApp : {app}\nArgs: {args}\n{e.Message}");
                return false;
            }
        }

        // ── GUI ──────────────────────────────────────────────────────────────────

        public void OnGUI() => m_Preferences.OnGUI();
    }
}
