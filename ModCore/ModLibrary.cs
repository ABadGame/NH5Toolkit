using System.Text.Json;
using System.Text.Json.Serialization;

namespace NH5Toolkit.ModCore
{
    /// <summary>
    /// A mod in the manager's library. Its files are stored by content (FileStore), so the package is only
    /// a list of game-relative paths and their SHA-256. Whatever the mod came as (an install.bat package,
    /// a replace-files folder, a loader track, an old manager profile), it ends up as one of these.
    /// </summary>
    public sealed class ModPackage
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Version { get; set; } = "";
        /// <summary>What the mod came as: kit-package, folder, loose-files, loader-track, old-manager-profile, adopted.</summary>
        public string Format { get; set; } = "";
        public string Source { get; set; } = "";
        public DateTime Imported { get; set; }
        public bool Enabled { get; set; }
        /// <summary>Lower number wins when two enabled mods ship the same file.</summary>
        public int Priority { get; set; }
        /// <summary>Game-relative path (backslashes) -> SHA-256, lower-case hex.</summary>
        public Dictionary<string, string> Files { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> Notes { get; set; } = new();

        // ----- mods that bring their own installer program (.exe or .bat) -----

        /// <summary>The manager's own copy of the installer folder (%APPDATA%\NH5Toolkit\Installers\&lt;id&gt;).</summary>
        public string? InstallerDir { get; set; }
        /// <summary>The program to run, relative to InstallerDir.</summary>
        public string? InstallerExe { get; set; }
        /// <summary>
        /// For each file the installer changed: what was there before it ran (SHA-256, in the FileStore),
        /// or null for a file it added. Its edits are only valid on top of exactly these files.
        /// </summary>
        public Dictionary<string, string?> Before { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        /// <summary>When the installer ran (installers stack in this order).</summary>
        public DateTime? CapturedAt { get; set; }

        [JsonIgnore] public bool IsInstaller => InstallerExe is not null;
        /// <summary>An installer mod whose program has run and whose changes are recorded in Files.</summary>
        [JsonIgnore] public bool Captured => IsInstaller && Files.Count > 0;

        [JsonIgnore] public long Size { get; set; }

        /// <summary>"Add-on" when every file is new to the game, "Replacement" when every file is a stock one, else both.</summary>
        public string KindFor(StockIndex stock)
        {
            int replaces = Files.Keys.Count(stock.IsStockPath);
            return replaces == 0 ? "Add-on" : replaces == Files.Count ? "Replacement" : "Adds + replaces";
        }
    }

    /// <summary>The installed-mods list, kept in %APPDATA%\NH5Toolkit\library.json.</summary>
    public sealed class ModLibrary
    {
        private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
        public static string LibraryPath => Path.Combine(ToolkitConfig.Dir, "library.json");

        public List<ModPackage> Mods { get; set; } = new();

        public static ModLibrary Load()
        {
            try
            {
                if (File.Exists(LibraryPath))
                {
                    var lib = JsonSerializer.Deserialize<ModLibrary>(File.ReadAllText(LibraryPath)) ?? new();
                    foreach (var m in lib.Mods)
                    {
                        m.Files = new Dictionary<string, string>(m.Files, StringComparer.OrdinalIgnoreCase);
                        m.Before = new Dictionary<string, string?>(m.Before, StringComparer.OrdinalIgnoreCase);
                    }
                    return lib;
                }
            }
            catch (Exception ex)
            {
                // Never silently start empty over a library that exists: that would orphan every mod.
                throw new IOException($"library.json could not be read ({ex.Message}). Fix or remove {LibraryPath}.", ex);
            }
            return new ModLibrary();
        }

        public void Save()
        {
            Directory.CreateDirectory(ToolkitConfig.Dir);
            string tmp = LibraryPath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
            File.Move(tmp, LibraryPath, overwrite: true);
        }

        /// <summary>Enabled mods, highest priority first: the order Apply resolves conflicts in.</summary>
        public IEnumerable<ModPackage> EnabledByPriority() => Mods.Where(m => m.Enabled).OrderBy(m => m.Priority);

        public string NewId(string name)
        {
            string baseId = new string(name.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
            if (baseId.Length == 0) baseId = "mod";
            string id = baseId;
            for (int i = 2; Mods.Any(m => m.Id == id); i++) id = $"{baseId}-{i}";
            return id;
        }

        public void Add(ModPackage mod)
        {
            mod.Priority = Mods.Count == 0 ? 0 : Mods.Max(m => m.Priority) + 1;
            Mods.Add(mod);
        }

        /// <summary>Moves a mod up (-1) or down (+1) in priority and renumbers 0..n-1.</summary>
        public void Move(ModPackage mod, int delta)
        {
            var ordered = Mods.OrderBy(m => m.Priority).ToList();
            int i = ordered.IndexOf(mod), j = i + delta;
            if (i < 0 || j < 0 || j >= ordered.Count) return;
            (ordered[i], ordered[j]) = (ordered[j], ordered[i]);
            for (int k = 0; k < ordered.Count; k++) ordered[k].Priority = k;
        }
    }

    /// <summary>
    /// Content-addressed file store (%APPDATA%\NH5Toolkit\Store\ab\abcdef...). Holds every mod file once,
    /// however many mods ship it, plus the originals the manager replaced, so Apply can always put them back.
    /// </summary>
    public static class FileStore
    {
        public static string Root => Path.Combine(ToolkitConfig.Dir, "Store");

        public static string PathFor(string sha) => Path.Combine(Root, sha[..2], sha);

        public static bool Has(string sha) => File.Exists(PathFor(sha));

        /// <summary>Copies a file in (if its content isn't there yet) and returns its SHA-256.</summary>
        public static string Put(string file, HashCache cache, CancellationToken cancel)
        {
            string sha = cache.Hash(new FileInfo(file), cancel).ToLowerInvariant();
            if (!Has(sha))
            {
                string dest = PathFor(sha);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                string tmp = dest + ".part";
                File.Copy(file, tmp, overwrite: true);
                if (Sha256(tmp, cancel) != sha)
                {
                    File.Delete(tmp);
                    throw new IOException($"Copy check failed while storing {file}.");
                }
                File.Move(tmp, dest, overwrite: true);
            }
            return sha;
        }

        public static string Sha256(string file, CancellationToken cancel)
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.SequentialScan);
            using var sha = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
            var buffer = new byte[1 << 20];
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                cancel.ThrowIfCancellationRequested();
                sha.AppendData(buffer, 0, read);
            }
            return Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
        }
    }

    /// <summary>
    /// Which paths are stock game files, and by which name (for placing loose replacement files).
    /// Built from the old Mod Manager's Vanilla_Backup when present, plus the game folder's own layout.
    /// </summary>
    public sealed class StockIndex
    {
        private readonly HashSet<string> _paths = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<string>> _byName = new(StringComparer.OrdinalIgnoreCase);

        public static StockIndex Build(string gameDir)
        {
            var index = new StockIndex();
            if (Directory.Exists(GameScanner.OldManagerStockDir))
                foreach (var f in Directory.EnumerateFiles(GameScanner.OldManagerStockDir, "*", SearchOption.AllDirectories))
                    index.AddPath(Path.GetRelativePath(GameScanner.OldManagerStockDir, f));

            // Files under NASCARHeat5_Data / DLC that no mod tool claims are stock too (the backup doesn't cover everything).
            foreach (var f in Directory.EnumerateFiles(gameDir, "*", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(gameDir, f);
                if (rel.StartsWith(@"H5TrackLoader\", StringComparison.OrdinalIgnoreCase)) continue;
                if (rel.EndsWith(".bak", StringComparison.OrdinalIgnoreCase)) continue;
                if (rel.Contains(@"\Tracks\", StringComparison.OrdinalIgnoreCase) && rel.Contains("_ui\\", StringComparison.OrdinalIgnoreCase)) continue;
                if (!rel.StartsWith(@"NASCARHeat5_Data\", StringComparison.OrdinalIgnoreCase) && !rel.StartsWith(@"DLC\", StringComparison.OrdinalIgnoreCase)) continue;
                if (!index._paths.Contains(rel) && Directory.Exists(GameScanner.OldManagerStockDir)) continue; // backup exists: trust it for NASCARHeat5_Data
                index.AddPath(rel);
            }
            return index;
        }

        private void AddPath(string rel)
        {
            if (!_paths.Add(rel)) return;
            string name = Path.GetFileName(rel);
            if (!_byName.TryGetValue(name, out var list)) _byName[name] = list = new();
            list.Add(rel);
        }

        public bool IsStockPath(string rel) => _paths.Contains(rel);

        /// <summary>Where a loose file with this name belongs, when exactly one stock file has the name.</summary>
        public string? PlaceByName(string fileName) =>
            _byName.TryGetValue(fileName, out var list) && list.Count == 1 ? list[0] : null;

        public int Count => _paths.Count;
    }
}
