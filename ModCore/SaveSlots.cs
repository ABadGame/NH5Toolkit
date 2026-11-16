using System.Text.Json;

namespace NH5Toolkit.ModCore
{
    /// <summary>
    /// Saves per profile (2026-11-16), the old NASCAR Heat 5 Mod Manager's way (SaveProfileManager): the live save folder stays
    /// where the game reads it, every other profile's save is parked in &lt;profiles root&gt;\&lt;profile&gt;\Save, and Deploy swaps them.
    /// The profiles root is the old manager's Profiles folder when it has a save store (.SaveStore), so saves it parked carry over
    /// and the two apps agree on whose save is live; otherwise %APPDATA%\NH5Toolkit\Profiles.
    /// </summary>
    public static class SaveSlots
    {
        public static string OldManagerProfiles => Path.Combine(GameScanner.OldManagerDir, "Profiles");

        // tests (NH5TK_DATA_DIR) never touch the old manager's store or the real save folder
        private static bool TestMode => Environment.GetEnvironmentVariable("NH5TK_DATA_DIR") is { Length: > 0 };

        public static string ProfilesRoot =>
            !TestMode && Directory.Exists(Path.Combine(OldManagerProfiles, ".SaveStore")) ? OldManagerProfiles : Path.Combine(ToolkitConfig.Dir, "Profiles");

        /// <summary>The game's live save folder: the toolkit's own setting, the old manager's, then %USERPROFILE%\NASCAR Heat 5.</summary>
        public static string LiveSaveDir
        {
            get
            {
                if (ToolkitConfig.GetPath("saveDir") is { } own) return own;
                if (TestMode) return Path.Combine(ToolkitConfig.Dir, "LiveSave");
                try
                {
                    string settings = Path.Combine(GameScanner.OldManagerDir, "app_settings.json");
                    if (File.Exists(settings))
                    {
                        using var doc = JsonDocument.Parse(File.ReadAllText(settings));
                        if (doc.RootElement.TryGetProperty("SaveDirectory", out var d) && d.GetString() is { Length: > 0 } old) return old;
                    }
                }
                catch { /* unreadable: default below */ }
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "NASCAR Heat 5");
            }
        }

        public static SaveProfileManager Manager() => new(LiveSaveDir, ProfilesRoot);

        /// <summary>null = fine as the live save folder; else why not (from the old manager's DescribeUnsafeSaveFolder).</summary>
        public static string? Unsafe(string chosen)
        {
            chosen = Path.TrimEndingDirectorySeparator(Path.GetFullPath(chosen));
            if (string.IsNullOrEmpty(Path.GetDirectoryName(chosen)))
                return "A drive root can't be the save folder. Choose the game's save folder (normally C:\\Users\\<you>\\NASCAR Heat 5).";
            if (Directory.Exists(Path.Combine(chosen, "NASCARHeat5_Data")))
                return "That's the game's installation folder. Choose its save folder (normally C:\\Users\\<you>\\NASCAR Heat 5).";
            foreach (var root in new[] { ProfilesRoot, ToolkitConfig.Dir })
            {
                string r = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
                bool inside = chosen.StartsWith(r + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
                bool above = r.StartsWith(chosen + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
                if (inside || above || chosen.Equals(r, StringComparison.OrdinalIgnoreCase))
                    return "That folder is inside (or contains) the folder where profile saves are parked. Profile saves are moved there " +
                           "automatically, so it can't be the live save folder too.";
            }
            return null;
        }
    }
}
