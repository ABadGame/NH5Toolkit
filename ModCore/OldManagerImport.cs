using System.Text.Json;

namespace NH5Toolkit.ModCore
{
    /// <summary>
    /// One-click import from the old NASCAR Heat 5 Mod Manager (2026-11-16): every chosen old profile (Profiles\&lt;name&gt;\ = a
    /// game-folder mirror, &lt;name&gt;.json = the ticked "[Tag] relative\path" list) becomes one mod "&lt;name&gt; (old Mod Manager)" and a
    /// toolkit profile with the same name, so the old manager's parked save for that name (shared save store) stays with it.
    /// Then the game files the old manager deployed (DeployedManifest.json) are adopted into the toolkit's ledger, owned by the
    /// imported mod of the profile that's live, with the old manager's stock copies (Vanilla_Backup) as the originals:
    /// switching that profile off later puts stock back. No game file is changed by the import.
    /// </summary>
    public static class OldManagerImport
    {
        public sealed record OldProfile(string Name, string Dir, List<string> Files, long Bytes, bool HasSave);

        public sealed class Result
        {
            public List<string> Imported { get; } = new();
            public List<string> Adopted { get; } = new();
            public List<string> Notes { get; } = new();
            public string? Live { get; set; }
        }

        private static string ProfilesDir => Path.Combine(GameScanner.OldManagerDir, "Profiles");

        public static bool Available => Directory.Exists(ProfilesDir);

        /// <summary>The profile whose save is live in the old manager's save store (state.json), else Default.</summary>
        public static string LiveProfile()
        {
            try
            {
                string state = Path.Combine(ProfilesDir, ".SaveStore", "state.json");
                if (File.Exists(state))
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(state));
                    if (doc.RootElement.TryGetProperty("ActiveProfile", out var a) && a.GetString() is { Length: > 0 } name && name != "Original")
                        return name;
                }
            }
            catch { /* default below */ }
            return "Default";
        }

        private static bool Baseline(string name) =>
            name.Equals("Default", StringComparison.OrdinalIgnoreCase) || name.Equals("Vanilla", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Vanilla_Backup", StringComparison.OrdinalIgnoreCase);

        public static List<OldProfile> List()
        {
            var list = new List<OldProfile>();
            if (!Available) return list;
            foreach (var dir in Directory.GetDirectories(ProfilesDir).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                string name = Path.GetFileName(dir);
                if (name.StartsWith('.')) continue;   // .SaveStore
                var files = new List<string>();
                string json = Path.Combine(ProfilesDir, name + ".json");
                IEnumerable<string> wanted;
                if (File.Exists(json))
                {
                    try { wanted = (JsonSerializer.Deserialize<List<string>>(File.ReadAllText(json)) ?? new()).Select(Strip); }
                    catch { wanted = Enumerable.Empty<string>(); }
                }
                else
                    wanted = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(dir, f));
                long bytes = 0;
                foreach (var rel in wanted.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (rel.StartsWith(@"Save\", StringComparison.OrdinalIgnoreCase)) continue;   // a parked save, never a mod file
                    if (!rel.StartsWith(@"NASCARHeat5_Data\", StringComparison.OrdinalIgnoreCase) && !rel.StartsWith(@"DLC\", StringComparison.OrdinalIgnoreCase)) continue;
                    if (Baseline(name) && rel.StartsWith(@"DLC\", StringComparison.OrdinalIgnoreCase)) continue;   // the old deploy skipped these
                    var fi = new FileInfo(Path.Combine(dir, rel));
                    if (!fi.Exists) continue;
                    files.Add(rel);
                    bytes += fi.Length;
                }
                list.Add(new OldProfile(name, dir, files, bytes, Directory.Exists(Path.Combine(dir, "Save"))));
            }
            return list;
        }

        // "[Asset] NASCARHeat5_Data\resources.assets" -> "NASCARHeat5_Data\resources.assets"
        private static string Strip(string entry) => entry.StartsWith('[') && entry.IndexOf("] ", StringComparison.Ordinal) is var i and > 0 ? entry[(i + 2)..] : entry;

        public static Result Import(string gameDir, IEnumerable<OldProfile> chosen, ModLibrary library, ProfileStore profiles, Ledger ledger,
                                    IProgress<string>? progress, CancellationToken cancel)
        {
            var result = new Result();
            var cache = HashCache.Load();
            bool multiplayer = OldMultiplayerSetting();
            string live = LiveProfile();
            var manifest = DeployedManifest();
            ModPackage? liveMod = null;
            try
            {
                foreach (var op in chosen)
                {
                    cancel.ThrowIfCancellationRequested();
                    bool isLive = op.Name.Equals(live, StringComparison.OrdinalIgnoreCase);
                    var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    int n = 0;
                    foreach (var rel in op.Files)
                    {
                        cancel.ThrowIfCancellationRequested();
                        progress?.Report($"Importing \"{op.Name}\": {++n} of {op.Files.Count} files ({Path.GetFileName(rel)})");
                        string src = Path.Combine(op.Dir, rel);
                        // the live profile: a deployed file changed afterwards (the old manager's own dll patches, track installers)
                        // is taken as it is in the game now, so this profile is exactly what you're running
                        string game = Path.Combine(gameDir, rel);
                        if (isLive && manifest.Contains(rel) && File.Exists(game)
                            && !cache.Hash(new FileInfo(game), cancel).Equals(cache.Hash(new FileInfo(src), cancel), StringComparison.OrdinalIgnoreCase))
                        {
                            src = game;
                            result.Notes.Add($"\"{op.Name}\": {rel} was changed in the game after the old manager deployed it; the game's current file was imported.");
                        }
                        files[rel] = FileStore.Put(src, cache, cancel);
                    }

                    ModPackage? mod = null;
                    if (files.Count > 0)
                    {
                        mod = library.Mods.FirstOrDefault(m => m.Files.Count == files.Count &&
                            files.All(f => m.Files.TryGetValue(f.Key, out var sha) && sha.Equals(f.Value, StringComparison.OrdinalIgnoreCase)));
                        if (mod is null)
                        {
                            string modName = op.Name + " (old Mod Manager)";
                            mod = new ModPackage
                            {
                                Id = library.NewId(modName), Name = modName, Format = "old-manager-profile", Source = op.Dir,
                                Imported = DateTime.Now, Files = files
                            };
                            mod.Size = files.Values.Distinct().Sum(sha => new FileInfo(FileStore.PathFor(sha)).Length);
                            library.Add(mod);
                        }
                    }

                    var prof = profiles.Find(op.Name);
                    if (prof is null)
                        profiles.Profiles.Add(prof = new ModProfile { Name = op.Name, Multiplayer = multiplayer });
                    else
                        prof.Multiplayer |= multiplayer;
                    if (mod is not null && !prof.On.Contains(mod.Id)) prof.On.Add(mod.Id);
                    if (isLive) liveMod = mod;
                    result.Imported.Add(op.Name + (mod is null ? " (no mod files, profile + save only)" : $" ({files.Count} files)"));
                }
                library.Save();

                // adopt what the old manager deployed: owned by the live profile's mod, stock copy as the original
                if (liveMod is not null)
                {
                    string stockDir = GameScanner.OldManagerStockDir;
                    foreach (var rel in manifest)
                    {
                        if (ledger.Files.ContainsKey(rel) || !liveMod.Files.TryGetValue(rel, out var want)) continue;
                        string game = Path.Combine(gameDir, rel);
                        if (!File.Exists(game) || !cache.Hash(new FileInfo(game), cancel).Equals(want, StringComparison.OrdinalIgnoreCase))
                        {
                            result.Notes.Add($"{rel}: not adopted (the game's file isn't the imported one).");
                            continue;
                        }
                        string stock = Path.Combine(stockDir, rel);
                        string? original = File.Exists(stock) ? FileStore.Put(stock, cache, cancel) : null;
                        ledger.Files[rel] = new LedgerEntry { Sha = want.ToLowerInvariant(), Owner = liveMod.Id, OriginalSha = original };
                        result.Adopted.Add(rel);
                    }
                    ledger.Save();
                    profiles.Deployed = profiles.Find(live)?.Name;
                    profiles.Selected = profiles.Deployed ?? profiles.Selected;
                    result.Live = profiles.Deployed;
                }
                else if (profiles.Find(live) is { } lp)
                {
                    profiles.Deployed = lp.Name;   // the live profile has no files (e.g. Vanilla): nothing to adopt
                    result.Live = lp.Name;
                }
                profiles.Save();
            }
            finally
            {
                cache.Save();
            }
            return result;
        }

        private static HashSet<string> DeployedManifest()
        {
            foreach (var p in new[] { Path.Combine(GameScanner.OldManagerInstallDir, "DeployedManifest.json"), Path.Combine(GameScanner.OldManagerDir, "DeployedManifest.json") })
                if (File.Exists(p))
                    try { return new HashSet<string>(JsonSerializer.Deserialize<List<string>>(File.ReadAllText(p)) ?? new(), StringComparer.OrdinalIgnoreCase); }
                    catch { }
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        private static bool OldMultiplayerSetting()
        {
            try
            {
                string s = Path.Combine(GameScanner.OldManagerDir, "app_settings.json");
                if (!File.Exists(s)) return false;
                using var doc = JsonDocument.Parse(File.ReadAllText(s));
                return doc.RootElement.TryGetProperty("EnableCustomServer", out var v) && v.ValueKind == JsonValueKind.True;
            }
            catch { return false; }
        }
    }
}
