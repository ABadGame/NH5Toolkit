namespace NH5Toolkit.ModCore
{
    /// <summary>One mod found in the game folder: a named set of non-stock files plus what we know about it.</summary>
    public sealed class ModGroup
    {
        public required string Name { get; init; }
        public required string Source { get; init; }        // where the grouping came from (install record, backups, ...)
        public string? KindOverride { get; init; }
        public List<ScannedFile> Files { get; } = new();
        public List<string> Notes { get; } = new();

        public long Size => Files.Sum(f => f.Size);

        /// <summary>"Add-on", "Replacement", "Adds + replaces", or a fixed label such as "Loader plugin".</summary>
        public string Kind
        {
            get
            {
                if (KindOverride is not null) return KindOverride;
                bool adds = Files.Any(f => f.State == FileState.Added);
                bool replaces = Files.Any(f => f.State == FileState.Replaced);
                return adds && replaces ? "Adds + replaces" : replaces ? "Replacement" : adds ? "Add-on" : "Missing files";
            }
        }
    }

    /// <summary>
    /// Turns a flat scan into mods, read-only. Evidence is used strongest first and each file goes
    /// to the first group that claims it:
    ///   1. H5 Track Loader core files and its own install records (IndyCar's indycar_install.txt),
    ///   2. the "*.nh5&lt;track&gt;.bak" backups a track installer leaves next to every file it patched,
    ///   3. the old NASCAR Heat 5 Mod Manager's last Deploy (DeployedManifest.json), matched by hash
    ///      against its profiles to name the profile each file came from,
    ///   4. whatever is left, grouped by what the file is (track, music, movies, game data).
    /// </summary>
    public static class ModGrouper
    {
        private const string TracksDir = @"NASCARHeat5_Data\StreamingAssets\PC\AssetBundles\Tracks\";
        private const string BundlesDir = @"NASCARHeat5_Data\StreamingAssets\PC\AssetBundles\";

        public static List<ModGroup> Group(ScanResult scan, IProgress<string>? progress, CancellationToken cancel)
        {
            var groups = new List<ModGroup>();
            var left = scan.Files.Where(f => f.State != FileState.Stock)
                                 .ToDictionary(f => f.Path, StringComparer.OrdinalIgnoreCase);

            ModGroup Claim(ModGroup g, IEnumerable<string> paths)
            {
                foreach (var p in paths)
                    if (left.Remove(p, out var f))
                        g.Files.Add(f);
                if (g.Files.Count > 0) groups.Add(g);
                return g;
            }

            progress?.Report("Reading install records...");
            ClaimTrackLoader(scan.GameDir, left, Claim);

            progress?.Report("Looking for track installer backups...");
            var oldManagerFiles = OldManager.DeployedFiles();
            ClaimTrackInstaller(left, Claim, oldManagerFiles);

            progress?.Report("Matching files against the old Mod Manager's profiles...");
            ClaimOldManager(scan.GameDir, left, Claim, oldManagerFiles, cancel);

            progress?.Report("Sorting the rest...");
            ClaimLeftovers(left, Claim);

            // Most useful first: conflicts, the loader and its plugins, other mods, unrecorded files,
            // then "probably stock" and missing files at the bottom.
            static int Rank(ModGroup g) =>
                g.Notes.Any(n => n.StartsWith("CONFLICT", StringComparison.Ordinal)) ? 0
                : g.KindOverride is "Loader" ? 1
                : g.KindOverride is "Loader plugin" ? 2
                : g.KindOverride is "Probably stock" ? 5
                : g.KindOverride is "Missing files" ? 6
                : g.Source.StartsWith("Not recorded", StringComparison.Ordinal) ? 4
                : 3;
            return groups.OrderBy(Rank)
                         .ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
                         .ToList();
        }

        // ---------- 1. H5 Track Loader and its install records ----------

        private static void ClaimTrackLoader(string gameDir, Dictionary<string, ScannedFile> left,
                                             Func<ModGroup, IEnumerable<string>, ModGroup> claim)
        {
            static bool IsCore(string p) =>
                p.Equals("winhttp.dll", StringComparison.OrdinalIgnoreCase)
                || p.Equals("doorstop_config.ini", StringComparison.OrdinalIgnoreCase)
                || (p.StartsWith(@"H5TrackLoader\", StringComparison.OrdinalIgnoreCase)
                    && p.IndexOf('\\', 14) < 0                      // directly in H5TrackLoader\, not a subfolder
                    && !Path.GetFileName(p).StartsWith("indycar", StringComparison.OrdinalIgnoreCase));

            var core = claim(new ModGroup
            {
                Name = "H5 Track Loader",
                Source = "Loader files (Doorstop + H5TrackLoader folder)",
                KindOverride = "Loader"
            }, left.Keys.Where(IsCore).ToList());
            if (core.Files.Count > 0)
                core.Notes.Add("Loads add-on tracks, series and fixes in memory. It changes no stock game file.");

            // IndyCar: its own install record lists every file it put in (I|path).
            var record = Path.Combine(gameDir, "H5TrackLoader", "indycar_install.txt");
            var indy = new List<string>();
            if (File.Exists(record))
                foreach (var line in File.ReadLines(record))
                    if (line.StartsWith("I|", StringComparison.Ordinal))
                        indy.Add(line[2..].Trim());
            indy.AddRange(left.Keys.Where(p => p.StartsWith(@"H5TrackLoader\indycar", StringComparison.OrdinalIgnoreCase)));

            var indyGroup = claim(new ModGroup
            {
                Name = "IndyCar series",
                Source = File.Exists(record) ? "Install record H5TrackLoader\\indycar_install.txt" : "H5TrackLoader\\indycar folder",
                KindOverride = "Loader plugin"
            }, indy);
            if (indyGroup.Files.Count > 0)
                indyGroup.Notes.Add("Needs H5 Track Loader. Its liveries are new files in CarAppearances (no stock file replaced).");

            // Tracks added through the loader: H5TrackLoader\Tracks\<Name>\...
            foreach (var name in left.Keys
                         .Where(p => p.StartsWith(@"H5TrackLoader\Tracks\", StringComparison.OrdinalIgnoreCase))
                         .Select(p => p.Split('\\')[2]).Distinct(StringComparer.OrdinalIgnoreCase).ToList())
            {
                string prefix = $@"H5TrackLoader\Tracks\{name}\";
                claim(new ModGroup { Name = $"{name} (track)", Source = "H5TrackLoader\\Tracks folder", KindOverride = "Loader plugin" },
                      left.Keys.Where(p => p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList());
            }

            // Anything else in the loader folder (logs, settings, files from newer loader versions).
            claim(new ModGroup { Name = "H5 Track Loader (other files)", Source = "H5TrackLoader folder", KindOverride = "Loader" },
                  left.Keys.Where(p => p.StartsWith(@"H5TrackLoader\", StringComparison.OrdinalIgnoreCase)).ToList());
        }

        // ---------- 2. track installer that leaves "<file>.nh5<track>.bak" ----------

        private static void ClaimTrackInstaller(Dictionary<string, ScannedFile> left,
                                                Func<ModGroup, IEnumerable<string>, ModGroup> claim,
                                                HashSet<string> oldManagerFiles)
        {
            var backups = left.Keys
                .Select(p => (path: p, m: System.Text.RegularExpressions.Regex.Match(p, @"^(?<base>.+)\.nh5(?<tag>[a-z0-9_]+)\.bak$",
                                                                                   System.Text.RegularExpressions.RegexOptions.IgnoreCase)))
                .Where(x => x.m.Success)
                .ToList();
            if (backups.Count == 0) return;

            var tracks = backups.Select(b => b.m.Groups["tag"].Value.ToLowerInvariant()).Distinct().OrderBy(t => t).ToList();
            var patched = backups.Select(b => b.m.Groups["base"].Value).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            var paths = new List<string>(patched);
            paths.AddRange(backups.Select(b => b.path));
            foreach (var t in tracks)
            {
                paths.Add(TracksDir + t + ".sab");
                paths.Add(BundlesDir + t + "_loading.rab");
                paths.AddRange(left.Keys.Where(p => p.StartsWith(TracksDir + t + "_", StringComparison.OrdinalIgnoreCase)));
            }

            var g = claim(new ModGroup
            {
                Name = "Track pack: " + string.Join(", ", tracks),
                Source = "Backups named *.nh5<track>.bak left by its installer"
            }, paths);

            g.Notes.Add($"Adds {tracks.Count} tracks and patches {patched.Count} stock files in place. " +
                        "Each track's installer backed up the files before patching, so they stack: remove them newest first, or restore stock.");
            var shared = patched.Where(oldManagerFiles.Contains).ToList();
            if (shared.Count > 0)
                g.Notes.Add("CONFLICT: the old Mod Manager's Deploy also replaces " + string.Join(", ", shared.Select(Path.GetFileName)) +
                            ". Whichever ran last wins, and a Deploy can undo these tracks.");
            if (patched.Any(p => p.EndsWith("Assembly-CSharp.dll", StringComparison.OrdinalIgnoreCase)))
                g.Notes.Add("Patches Assembly-CSharp.dll (game code). Other code mods that patch this file can't be combined with it.");
        }

        // ---------- 3. old Mod Manager deploy, named by matching profile ----------

        private static void ClaimOldManager(string gameDir, Dictionary<string, ScannedFile> left,
                                            Func<ModGroup, IEnumerable<string>, ModGroup> claim,
                                            HashSet<string> deployed, CancellationToken cancel)
        {
            var mine = left.Keys.Where(deployed.Contains).ToList();
            if (mine.Count == 0) return;

            var cache = HashCache.Load();
            try
            {
                foreach (var bucket in mine.GroupBy(Category))
                {
                    var g = claim(new ModGroup
                    {
                        Name = bucket.Key,
                        Source = "Old Mod Manager's last Deploy (DeployedManifest.json)"
                    }, bucket.ToList());

                    foreach (var f in g.Files)
                    {
                        cancel.ThrowIfCancellationRequested();
                        var matches = OldManager.ProfilesWithSameFile(gameDir, f.Path, cache, cancel);
                        g.Notes.Add(matches.Count > 0
                            ? $"{Path.GetFileName(f.Path)}: same as profile {string.Join(", ", matches.Select(m => $"\"{m}\""))}"
                            : $"{Path.GetFileName(f.Path)}: matches NO profile, so it was changed after the last Deploy (by hand or another tool). The next Deploy will overwrite it.");
                    }
                }
            }
            finally
            {
                cache.Save();
            }
        }

        // ---------- 4. the rest ----------

        private static void ClaimLeftovers(Dictionary<string, ScannedFile> left, Func<ModGroup, IEnumerable<string>, ModGroup> claim)
        {
            // Program files next to the exe: the old manager's stock copy only covers NASCARHeat5_Data and DLC.
            var program = left.Keys.Where(p => !p.Contains('\\') && left[p].State == FileState.Added
                                               && (p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                                                   || p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))).ToList();
            var prog = claim(new ModGroup
            {
                Name = "Game program files",
                Source = "Not in the stock copy",
                KindOverride = "Probably stock"
            }, program);
            if (prog.Files.Count > 0)
                prog.Notes.Add("The old Mod Manager's stock copy never saved files outside NASCARHeat5_Data, so these can't be checked yet. " +
                               "They are most likely the game's own.");

            claim(new ModGroup { Name = "Missing stock files", Source = "In the stock copy, not in the game", KindOverride = "Missing files" },
                  left.Values.Where(f => f.State == FileState.Missing).Select(f => f.Path).ToList());

            foreach (var bucket in left.Keys.ToList().GroupBy(Category))
            {
                var g = claim(new ModGroup { Name = bucket.Key, Source = "Not recorded by any tool" }, bucket.ToList());
                g.Notes.Add("No install record, backup or manager deploy mentions these files. They were put in by hand or by a tool that keeps no record.");
            }
        }

        /// <summary>What a file is, for naming groups that have no better name.</summary>
        private static string Category(string p)
        {
            string name = Path.GetFileName(p);
            if (p.StartsWith(TracksDir, StringComparison.OrdinalIgnoreCase))
                return "Track: " + Path.GetFileNameWithoutExtension(name);
            if (p.Contains(@"\FMOD\", StringComparison.OrdinalIgnoreCase)) return "Music and sound banks";
            if (p.Contains(@"\Movies\", StringComparison.OrdinalIgnoreCase)) return "Intro and menu movies";
            if (p.Contains(@"\CarAppearances\", StringComparison.OrdinalIgnoreCase)) return "Car paint schemes";
            if (p.EndsWith("Assembly-CSharp.dll", StringComparison.OrdinalIgnoreCase)) return "Game code (Assembly-CSharp.dll)";
            if (p.StartsWith(@"DLC\", StringComparison.OrdinalIgnoreCase)) return "DLC files";
            if (p.StartsWith(@"NASCARHeat5_Data\", StringComparison.OrdinalIgnoreCase) && !p[17..].Contains('\\'))
                return "Game data (assets and levels)";
            return "Other: " + (Path.GetDirectoryName(p) is { Length: > 0 } d ? d : "game folder");
        }
    }

    /// <summary>Read-only access to the old NASCAR Heat 5 Mod Manager's records.</summary>
    internal static class OldManager
    {
        public static HashSet<string> DeployedFiles()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var manifest = Path.Combine(GameScanner.OldManagerDir, "DeployedManifest.json");
                if (File.Exists(manifest))
                    foreach (var p in System.Text.Json.JsonSerializer.Deserialize<string[]>(File.ReadAllText(manifest)) ?? [])
                        set.Add(p);
            }
            catch { /* unreadable: no evidence from it */ }
            return set;
        }

        /// <summary>Profiles holding a byte-identical copy of this game file (size first, then SHA-256).</summary>
        public static List<string> ProfilesWithSameFile(string gameDir, string rel, HashCache cache, CancellationToken cancel)
        {
            var result = new List<string>();
            var profiles = Path.Combine(GameScanner.OldManagerDir, "Profiles");
            if (!Directory.Exists(profiles)) return result;

            var game = new FileInfo(Path.Combine(gameDir, rel));
            if (!game.Exists) return result;
            string? gameHash = null;

            foreach (var dir in Directory.GetDirectories(profiles))
            {
                string name = Path.GetFileName(dir);
                if (name.StartsWith('.')) continue;
                var copy = new FileInfo(Path.Combine(dir, rel));
                if (!copy.Exists || copy.Length != game.Length) continue;
                gameHash ??= cache.Hash(game, cancel);
                if (cache.Hash(copy, cancel) == gameHash)
                    result.Add(name);
            }
            return result;
        }
    }
}
