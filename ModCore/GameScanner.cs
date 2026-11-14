using System.Security.Cryptography;
using System.Text.Json;

namespace NH5Toolkit.ModCore
{
    /// <summary>What a file in the game folder is, compared with the stock reference.</summary>
    public enum FileState
    {
        Stock,      // same bytes as the stock copy
        Replaced,   // the game has a stock file at this path, but the bytes differ (a replacement mod)
        Added,      // not in the stock reference at all (an add-on mod, or a stock file the reference never saved)
        Missing     // in the stock reference, but gone from the game folder
    }

    public sealed record ScannedFile(string Path, FileState State, long Size, string Owner);

    public sealed record ScanResult(
        string GameDir,
        string StockDir,
        DateTime ScannedAt,
        IReadOnlyList<ScannedFile> Files)
    {
        public int Count(FileState s) => Files.Count(f => f.State == s);
    }

    /// <summary>
    /// Read-only scan of the game folder. Compares every file against a stock reference folder
    /// (for now the old NASCAR Heat 5 Mod Manager's Vanilla_Backup, a mirror of stock files) and
    /// guesses who put non-stock files there. Never writes to the game or the stock folder.
    /// </summary>
    public static class GameScanner
    {
        public const string DefaultGameDir = @"C:\Program Files (x86)\Steam\steamapps\common\NASCAR Heat 5";

        public static string OldManagerDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NASCAR Heat 5 Mod Manager");

        public static string OldManagerStockDir => Path.Combine(OldManagerDir, "NH5ModManager_Data", "Vanilla_Backup");

        /// <summary>Game folder: saved choice, then the old manager's setting, then Steam's default library.</summary>
        public static string? FindGameDir()
        {
            var saved = ToolkitConfig.GetPath("gameDir");
            if (IsGameDir(saved)) return saved;

            try
            {
                var settings = Path.Combine(OldManagerDir, "app_settings.json");
                if (File.Exists(settings))
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(settings));
                    if (doc.RootElement.TryGetProperty("GameDirectory", out var g) && IsGameDir(g.GetString()))
                        return g.GetString();
                }
            }
            catch { /* unreadable settings: fall through */ }

            return IsGameDir(DefaultGameDir) ? DefaultGameDir : null;
        }

        public static bool IsGameDir(string? dir) =>
            !string.IsNullOrWhiteSpace(dir) && File.Exists(Path.Combine(dir, "NASCARHeat5.exe"));

        public static ScanResult Scan(string gameDir, string stockDir, IProgress<(long done, long total, string file)>? progress,
                                      CancellationToken cancel)
        {
            var game = ListFiles(gameDir);
            var stock = Directory.Exists(stockDir) ? ListFiles(stockDir) : new Dictionary<string, FileInfo>(StringComparer.OrdinalIgnoreCase);
            var owners = new OwnerGuesser(gameDir);
            var cache = HashCache.Load();

            // Only same-size pairs need hashing; a size difference already proves "Replaced".
            long total = game.Where(p => stock.TryGetValue(p.Key, out var s) && s.Length == p.Value.Length)
                             .Sum(p => p.Value.Length * 2);
            long done = 0;

            var files = new List<ScannedFile>(game.Count + 16);
            try
            {
            foreach (var (rel, info) in game.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
            {
                cancel.ThrowIfCancellationRequested();
                FileState state;

                if (!stock.TryGetValue(rel, out var stockInfo))
                    state = FileState.Added;
                else if (stockInfo.Length != info.Length)
                    state = FileState.Replaced;
                else
                {
                    progress?.Report((done, total, rel));
                    string a = cache.Hash(info, cancel);
                    string b = cache.Hash(stockInfo, cancel);
                    done += info.Length * 2;
                    state = a == b ? FileState.Stock : FileState.Replaced;
                }

                files.Add(new ScannedFile(rel, state, info.Length, state == FileState.Stock ? "" : owners.Guess(rel)));
            }

            foreach (var (rel, info) in stock)
                if (!game.ContainsKey(rel))
                    files.Add(new ScannedFile(rel, FileState.Missing, info.Length, ""));
            }
            finally
            {
                cache.Save();   // keep finished hashes even when the scan is cancelled
            }
            progress?.Report((total, total, ""));
            return new ScanResult(gameDir, stockDir, DateTime.Now, files);
        }

        private static Dictionary<string, FileInfo> ListFiles(string root)
        {
            var result = new Dictionary<string, FileInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                result[Path.GetRelativePath(root, path)] = new FileInfo(path);
            return result;
        }

        /// <summary>Writes the scan next to the toolkit's config so later steps (the ledger) can start from it.</summary>
        public static string SaveReport(ScanResult result)
        {
            var path = Path.Combine(ToolkitConfig.Dir, "game_scan.json");
            Directory.CreateDirectory(ToolkitConfig.Dir);
            File.WriteAllText(path, JsonSerializer.Serialize(new
            {
                result.GameDir,
                result.StockDir,
                result.ScannedAt,
                Files = result.Files.Where(f => f.State != FileState.Stock)
                                    .Select(f => new { f.Path, State = f.State.ToString(), f.Size, f.Owner })
            }, new JsonSerializerOptions { WriteIndented = true }));
            return path;
        }
    }

    /// <summary>Best guess at which tool put a non-stock file in the game folder.</summary>
    internal sealed class OwnerGuesser
    {
        private readonly HashSet<string> _oldManagerDeployed = new(StringComparer.OrdinalIgnoreCase);

        public OwnerGuesser(string gameDir)
        {
            try
            {
                var manifest = Path.Combine(GameScanner.OldManagerDir, "DeployedManifest.json");
                if (File.Exists(manifest))
                    foreach (var p in JsonSerializer.Deserialize<string[]>(File.ReadAllText(manifest)) ?? [])
                        _oldManagerDeployed.Add(p);
            }
            catch { /* no manifest: no guesses from it */ }
        }

        public string Guess(string rel)
        {
            if (rel.StartsWith(@"H5TrackLoader\", StringComparison.OrdinalIgnoreCase)
                || rel.Equals("winhttp.dll", StringComparison.OrdinalIgnoreCase)
                || rel.Equals("doorstop_config.ini", StringComparison.OrdinalIgnoreCase))
                return "H5 Track Loader";
            if (_oldManagerDeployed.Contains(rel))
                return "Old Mod Manager (profile deploy)";
            if (rel.Equals(@"NASCARHeat5_Data\Managed\Assembly-CSharp.dll", StringComparison.OrdinalIgnoreCase))
                return "Old Mod Manager (DLL patch) or another DLL mod";
            return "Unknown";
        }
    }

    /// <summary>
    /// SHA-256 per file, cached by full path + size + last-write time, so a rescan only reads files
    /// that changed. The first scan reads every same-size pair (about 40 GB here), later ones are quick.
    /// </summary>
    public sealed class HashCache
    {
        private static string CachePath => Path.Combine(ToolkitConfig.Dir, "hash_cache.json");
        private readonly Dictionary<string, string> _map;

        private HashCache(Dictionary<string, string> map) => _map = map;

        public static HashCache Load()
        {
            try
            {
                if (File.Exists(CachePath))
                    return new HashCache(JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(CachePath))
                                         ?? new());
            }
            catch { /* corrupt cache: rebuild */ }
            return new HashCache(new());
        }

        public string Hash(FileInfo file, CancellationToken cancel)
        {
            string key = $"{file.FullName}|{file.Length}|{file.LastWriteTimeUtc.Ticks}";
            if (_map.TryGetValue(key, out var cached))
                return cached;

            using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
                                              1 << 20, FileOptions.SequentialScan);
            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[1 << 20];
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                cancel.ThrowIfCancellationRequested();
                sha.AppendData(buffer, 0, read);
            }
            return _map[key] = Convert.ToHexString(sha.GetHashAndReset());
        }

        public void Save()
        {
            Directory.CreateDirectory(ToolkitConfig.Dir);
            File.WriteAllText(CachePath, JsonSerializer.Serialize(_map));
        }
    }
}
