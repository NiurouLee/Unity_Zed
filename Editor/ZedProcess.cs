using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using NiceIO;
using UnityEngine;

namespace UnityZed
{
    public class ZedProcess
    {
        private static readonly ILogger sLogger = ZedLogger.Create();

        // Keep the raw string from Unity so we always launch with the exact path the user selected.
        private readonly string m_ExecPath;
        private readonly NPath m_ProjectPath;

        public ZedProcess(string execPath)
        {
            m_ExecPath = execPath;
            m_ProjectPath = new NPath(Application.dataPath).Parent;
        }

        public bool OpenProject(string filePath = "", int line = -1, int column = -1)
        {
            var args = BuildArguments(filePath, line, column);

            sLogger.Log($"Launch: \"{m_ExecPath}\" {args}");

            try
            {
                // Use Process.Start directly so we have full control over the executable and
                // arguments. CodeEditor.OSOpenFile is designed for shell-open semantics and is
                // unreliable for passing arguments to GUI applications on Windows.
                var info = new ProcessStartInfo
                {
                    FileName = m_ExecPath,
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = false,
                };
                Process.Start(info);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[ZedEditor] Failed to launch Zed.\nPath: {m_ExecPath}\nArgs: {args}\n{e.Message}");
                return false;
            }
        }

        private string BuildArguments(string filePath, int line, int column)
        {
            // Normalise project path to native slashes so Zed can resolve it on every OS.
            var projectPath = m_ProjectPath.ToString(SlashMode.Native);
            var args = new StringBuilder($"\"{projectPath}\"");

            if (!string.IsNullOrEmpty(filePath))
            {
                // Normalise file path as well.
                var nativeFilePath = Path.GetFullPath(filePath);

                // Zed CLI: zed <project> -a <file>[:line[:col]]
                // The :line:col suffix must come after the closing quote so the shell / Process
                // tokeniser sees them as part of the same argument on all platforms.
                args.Append($" -a \"{nativeFilePath}\"");

                if (line >= 0)
                {
                    args.Append(':');
                    args.Append(line);

                    if (column >= 0)
                    {
                        args.Append(':');
                        args.Append(column);
                    }
                }
            }

            return args.ToString();
        }
    }
}
