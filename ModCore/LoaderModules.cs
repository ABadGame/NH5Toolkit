using System.Diagnostics;

namespace NH5Toolkit.ModCore
{
    /// <summary>
    /// The H5 Track Loader's module switch: &lt;game&gt;\H5TrackLoader\mods.txt, one "Name = 1/0" line per module.
    /// The loader (H5TrackLoader.dll with ModSwitch, built 2026-11-15 or later) reads it once at game start and skips
    /// the Install of every module set to 0. No file, or no line, = on. Older loaders ignore the file.
    /// Writing keeps every other line and comment as it is.
    /// </summary>
    public static class LoaderModules
    {
        public record Module(string Key, string Name, string What, string[] Signs);

        // Same names and order as ModSwitch.Known in the loader. Signs = loader-folder paths that show the package is installed
        // (none = built into the loader itself).
        public static readonly Module[] All =
        [
            new("IndyCar", "IndyCar", "NTT INDYCAR Series (AC Dallara IR-18) on the Xfinity slot", ["indycar\\indycar_roster.txt"]),
            new("Cup13", "NASCAR '14", "2013 Gen-6 cars as their own series on the Cup slot", ["cup13"]),
            new("Cot08", "Sprint Cup 2008", "AC Gen 5 COT, 2008 season", ["cot08"]),
            new("Cot10", "Sprint Cup 2010", "COT with the 2010 paints", ["cot10"]),
            new("Cot12", "Sprint Cup 2012", "Spoiler COT (Impala / Fusion / Camry / Charger)", ["cot12"]),
            new("Gn57", "Grand National 1957", "1957 Bel Air series on the Cup slot", ["gn57"]),
            new("CupChampionship", "Cup series in Championship", "NASCAR '14 / COT series in Championship mode", []),
            new("AcAudio", "AC engine sounds", "AC car sounds for every AC car series + the AC idle on Heat 5's cars",
                ["indycar\\fmod", "cup13\\fmod", "cot08\\fmod", "cot10\\fmod", "cot12\\fmod", "gn57\\fmod"]),
            new("WoO", "World of Outlaws midgets", "Chili Bowl midgets with WoO physics and sound", ["woo\\midget_roster_h5.json"]),
            new("DayNight", "Realistic Weather and Skies", "Day to night in a race, animated sky, Options > Display rows", ["daynight\\daynight_settings.txt"]),
            new("ClassicMenu", "Classic Menu", "Heat 4 style menu and menu video", ["menu"]),
            new("NoMotionBlur", "No Motion Blur", "Motion blur off + the Options > Display > Motion Blur row", []),
            new("Tracks", "Loader tracks", "Track slots from StreamingAssets\\...\\Tracks (*.sab + slot.txt)", []),
            new("GameFixes", "Game fixes", "Loader frame / game fixes (Frames)", []),
        ];

        public static string LoaderDir(string gameDir) => Path.Combine(gameDir, "H5TrackLoader");
        public static string FilePath(string gameDir) => Path.Combine(LoaderDir(gameDir), "mods.txt");
        public static string DllPath(string gameDir) => Path.Combine(LoaderDir(gameDir), "H5TrackLoader.dll");

        public static bool LoaderInstalled(string gameDir) => File.Exists(DllPath(gameDir));

        /// <summary>The loader's build stamp (file version yyyy.MMdd.HHmm.ss); 0.5.0.0 = built before mods.txt existed.</summary>
        public static Version? LoaderVersion(string gameDir)
        {
            try
            {
                var v = FileVersionInfo.GetVersionInfo(DllPath(gameDir)).FileVersion;
                return Version.TryParse(v, out var ver) ? ver : null;
            }
            catch { return null; }
        }

        /// <summary>Loaders older than the switch read 0.5.0.0 and ignore mods.txt.</summary>
        public static bool LoaderKnowsSwitch(string gameDir) => LoaderVersion(gameDir) is { Major: >= 2026 };

        public static bool Installed(string gameDir, Module m)
        {
            if (m.Signs.Length == 0) return true;
            var dir = LoaderDir(gameDir);
            return m.Signs.Any(s => File.Exists(Path.Combine(dir, s)) || Directory.Exists(Path.Combine(dir, s)));
        }

        /// <summary>Key -> on/off for every line mods.txt sets (missing keys are on).</summary>
        public static Dictionary<string, bool> Read(string gameDir)
        {
            var map = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            var path = FilePath(gameDir);
            if (!File.Exists(path)) return map;
            foreach (var raw in File.ReadAllLines(path))
                if (Parse(raw) is { } kv) map[kv.Key] = kv.On;
            return map;
        }

        public static bool IsOn(Dictionary<string, bool> map, string key) => !map.TryGetValue(key, out var on) || on;

        /// <summary>Sets one module on or off. Rewrites only that module's line (adds it when missing); everything else stays.</summary>
        public static void Set(string gameDir, string key, bool on)
        {
            var path = FilePath(gameDir);
            if (!Directory.Exists(LoaderDir(gameDir)))
                throw new DirectoryNotFoundException("H5TrackLoader folder not found - the loader is not installed.");
            var lines = File.Exists(path)
                ? File.ReadAllLines(path).ToList()
                : new List<string>
                {
                    "# H5 Track Loader modules: Name = 1 (on) or 0 (off). Read at game start. No line = on.",
                    "# Written by NH5Toolkit (Mod Manager > Loader Modules); hand edits are kept."
                };
            string value = $"{key} = {(on ? 1 : 0)}";
            bool done = false;
            for (int i = 0; i < lines.Count; i++)
            {
                if (Parse(lines[i]) is not { } kv || !kv.Key.Equals(key, StringComparison.OrdinalIgnoreCase)) continue;
                if (!done)
                {
                    int hash = lines[i].IndexOf('#');
                    lines[i] = hash >= 0 ? value + "   " + lines[i][hash..] : value;
                    done = true;
                }
                else
                {
                    lines.RemoveAt(i--);   // a duplicate line would win in the loader (last one read) - keep one
                }
            }
            if (!done) lines.Add(value);
            var tmp = path + ".tmp";
            File.WriteAllLines(tmp, lines);
            File.Move(tmp, path, overwrite: true);
        }

        private static (string Key, bool On)? Parse(string raw)
        {
            var line = raw;
            int hash = line.IndexOf('#');
            if (hash >= 0) line = line[..hash];
            int eq = line.IndexOf('=');
            if (eq <= 0) return null;
            var key = line[..eq].Trim();
            var v = line[(eq + 1)..].Trim().ToLowerInvariant();
            if (v is "0" or "off" or "false" or "no") return (key, false);
            if (v is "1" or "on" or "true" or "yes") return (key, true);
            return null;   // the loader keeps such a module on
        }

        public static bool GameRunning() => Process.GetProcessesByName("NASCARHeat5").Length > 0;
    }
}
