using System.Text.Json;

namespace NH5Toolkit
{
    /// <summary>
    /// Persists per-tool settings (currently just each tool's executable path)
    /// as a simple JSON dictionary: { "key": "C:\\path\\app.exe", ... }
    /// </summary>
    public static class ToolkitConfig
    {
        // NH5TK_DATA_DIR points the toolkit at another data folder (tests use it so they never touch the real one).
        private static readonly string ConfigDir =
            Environment.GetEnvironmentVariable("NH5TK_DATA_DIR") is { Length: > 0 } testDir
                ? testDir
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NH5Toolkit");

        private static readonly string ConfigPath = Path.Combine(ConfigDir, "config.json");

        /// <summary>%APPDATA%\NH5Toolkit, where the hub keeps its own files (config, scan report, hash cache).</summary>
        public static string Dir => ConfigDir;

        private static readonly Dictionary<string, string> _paths = new();

        public static void Load()
        {
            try
            {
                if (!File.Exists(ConfigPath))
                    return;

                using var doc = JsonDocument.Parse(File.ReadAllText(ConfigPath));
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    var value = prop.Value.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                        _paths[prop.Name] = value;
                }
            }
            catch
            {
                // Corrupt or unreadable config: start fresh rather than crashing the hub.
            }
        }

        public static void Save()
        {
            Directory.CreateDirectory(ConfigDir);
            File.WriteAllText(
                ConfigPath,
                JsonSerializer.Serialize(_paths, new JsonSerializerOptions { WriteIndented = true }));
        }

        public static string? GetPath(string key)
        {
            return _paths.TryGetValue(key, out var path) && !string.IsNullOrWhiteSpace(path)
                ? path
                : null;
        }

        public static void SetPath(string key, string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                _paths.Remove(key);
            else
                _paths[key] = path;

            Save();
        }
    }
}
