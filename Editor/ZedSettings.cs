using System.IO;
using UnityEngine;

namespace UnityZed
{
    // Creates .zed/settings.json in the project root the first time SyncAll() is called.
    // Only writes the file if it does not already exist — never overwrites user edits.
    public class ZedSettings
    {
        readonly string m_SettingsPath;

        public ZedSettings()
        {
            m_SettingsPath = Path.Combine(
                Directory.GetParent(Application.dataPath).FullName,
                ".zed", "settings.json");
        }

        public void Sync()
        {
            if (File.Exists(m_SettingsPath))
                return;

            Directory.CreateDirectory(Path.GetDirectoryName(m_SettingsPath));
            File.WriteAllText(m_SettingsPath, kDefaultSettings);
        }

        // Default Zed workspace settings for Unity projects.
        // Written once; edit the file directly in your project to customise.
        const string kDefaultSettings =
@"{
  ""file_scan_exclusions"": [
    ""**/.*"",
    ""**/*~"",
    ""*.csproj"",
    ""*.sln"",
    ""**/*.meta"",
    ""**/*.dll"",
    ""**/*.exe"",
    ""**/*.pdf"",
    ""**/*.mid"",
    ""**/*.midi"",
    ""**/*.wav"",
    ""**/*.gif"",
    ""**/*.ico"",
    ""**/*.jpg"",
    ""**/*.jpeg"",
    ""**/*.png"",
    ""**/*.psd"",
    ""**/*.tga"",
    ""**/*.tif"",
    ""**/*.tiff"",
    ""**/*.3ds"",
    ""**/*.3DS"",
    ""**/*.fbx"",
    ""**/*.FBX"",
    ""**/*.lxo"",
    ""**/*.LXO"",
    ""**/*.ma"",
    ""**/*.MA"",
    ""**/*.obj"",
    ""**/*.OBJ"",
    ""**/*.asset"",
    ""**/*.cubemap"",
    ""**/*.flare"",
    ""**/*.mat"",
    ""**/*.prefab"",
    ""**/*.unity"",
    ""build/"",
    ""Build/"",
    ""Library/"",
    ""Obj/"",
    ""ProjectSettings/"",
    ""UserSettings/"",
    ""Temp/"",
    ""Logs/""
  ]
}
";
    }
}
