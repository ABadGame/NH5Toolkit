using System.Text.Json;

namespace NH5Toolkit
{
    /// <summary>
    /// Persists per-tool settings (currently just each tool's executable path)
    /// as a simple JSON dictionary: { "key": "C:\\path\\app.exe", ... }
    /// </summary>
    public static class ToolkitConfig
    {
        private static readonly string ConfigDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NH5Toolkit");

        private static readonly string ConfigPath = Path.Combine(ConfigDir, "config.json");

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
