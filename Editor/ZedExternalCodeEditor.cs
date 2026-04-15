using System;
using System.Linq;
using Unity.CodeEditor;
using UnityEngine;
using UnityEditor;
using Microsoft.Unity.VisualStudio.Editor;

namespace UnityZed
{
    public partial class ZedExternalCodeEditor : IExternalCodeEditor
    {
        [InitializeOnLoadMethod]
        private static void Initialize()
            => CodeEditor.Register(new ZedExternalCodeEditor());

        private static IGenerator CreateSdkStyleGeneration()
        {
            try
            {
                var assembly = typeof(IGenerator).Assembly;
                var type = assembly.GetType("Microsoft.Unity.VisualStudio.Editor.SdkStyleProjectGeneration");
                if (type == null)
                    throw new InvalidOperationException("Type SdkStyleProjectGeneration not found. " +
                        "Ensure com.unity.ide.visualstudio is installed and up to date.");
                return (IGenerator)Activator.CreateInstance(type);
            }
            catch (Exception e)
            {
                Debug.LogError($"[ZedEditor] Failed to create project generator: {e.Message}\n" +
                    "Check that com.unity.ide.visualstudio is present in your project.");
                throw;
            }
        }

        private static readonly ILogger sLogger = ZedLogger.Create();
        private static readonly ZedDiscovery sDiscovery = new();

        private ZedProcess m_Process;
        private ZedPreferences m_Preferences;
        private ZedSettings m_Settings;
        private IGenerator m_Generator;

        public void Initialize(string editorInstallationPath)
        {
            sLogger.Log($"Initialize path={editorInstallationPath}");
            m_Process = new ZedProcess(editorInstallationPath);
            m_Generator = CreateSdkStyleGeneration();
            m_Preferences = new ZedPreferences(m_Generator);
            m_Settings = new ZedSettings();
        }

        //
        // Discovery
        //

        public CodeEditor.Installation[] Installations
            => sDiscovery.GetInstallations();

        public bool TryGetInstallationForPath(string editorPath, out CodeEditor.Installation installation)
            => sDiscovery.TryGetInstallationForPath(editorPath, out installation);

        //
        // Interopt
        //

        public bool OpenProject(string filePath = "", int line = -1, int column = -1)
        {
            // m_Process / m_Generator are null when Initialize() has not been called yet.
            // This can happen if TryGetInstallationForPath returned false for the stored path
            // during the last domain reload. Log a clear message instead of throwing an assertion.
            if (m_Process == null || m_Generator == null)
            {
                Debug.LogError("[ZedEditor] OpenProject called before Initialize. " +
                    "Please re-select Zed in Preferences → External Tools to reinitialize.");
                return false;
            }

            // Only sync the project generator for file types it understands (.cs, .asmdef, etc.).
            // For other assets (.shader, .uxml, .json, …) we still open them in Zed — just skip
            // the potentially expensive generator sync, matching VS Code package behavior.
            if (!string.IsNullOrEmpty(filePath) && m_Generator.IsSupportedFile(filePath))
                m_Generator.Sync();

            m_Settings.Sync();

            return m_Process.OpenProject(filePath, line, column);
        }

        public void SyncAll()
        {
            if (m_Generator == null) return;

            m_Generator.Sync();
            m_Settings?.Sync();
        }

        public void SyncIfNeeded(string[] addedFiles, string[] deletedFiles, string[] movedFiles, string[] movedFromFiles, string[] importedFiles)
        {
            if (m_Generator == null) return;

            m_Generator.SyncIfNeeded(addedFiles.Union(deletedFiles).Union(movedFiles).Union(movedFromFiles), importedFiles);
            m_Settings?.Sync();
        }

        //
        // Preference GUI
        //

        public void OnGUI()
            => m_Preferences?.OnGUI();
    }
}
