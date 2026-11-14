using System.Diagnostics;
using System.Text.Json;

namespace NH5Toolkit.ModCore
{
    /// <summary>What the manager wrote to one game path, and what was there before it first did.</summary>
    public sealed class LedgerEntry
    {
        public string Sha { get; set; } = "";          // what the manager last wrote
        public string Owner { get; set; } = "";        // mod id
        public string? OriginalSha { get; set; }       // content before the manager's first write (kept in FileStore); null = no file was there
    }

    /// <summary>Every game file the manager has written, per game folder: %APPDATA%\NH5Toolkit\ledger.json.</summary>
    public sealed class Ledger
    {
        public static string LedgerPath => Path.Combine(ToolkitConfig.Dir, "ledger.json");
        public string GameDir { get; set; } = "";
        public Dictionary<string, LedgerEntry> Files { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public static Ledger Load(string gameDir)
        {
            if (File.Exists(LedgerPath))
            {
                var l = JsonSerializer.Deserialize<Ledger>(File.ReadAllText(LedgerPath))
                        ?? throw new IOException("ledger.json is empty or damaged.");
                l.Files = new Dictionary<string, LedgerEntry>(l.Files, StringComparer.OrdinalIgnoreCase);
                if (l.Files.Count > 0 && !string.Equals(Path.GetFullPath(l.GameDir), Path.GetFullPath(gameDir), StringComparison.OrdinalIgnoreCase))
                    throw new IOException($"The manager's record is for another game folder ({l.GameDir}).");
                l.GameDir = gameDir;
                return l;
            }
            return new Ledger { GameDir = gameDir };
        }

        /// <summary>Written after every single file change, so a crash mid-apply never loses track of a file.</summary>
        public void Save()
        {
            Directory.CreateDirectory(ToolkitConfig.Dir);
            string tmp = LedgerPath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tmp, LedgerPath, overwrite: true);
        }
    }

    public enum StepKind { Install, Restore, Remove, Keep }

    /// <param name="OriginalSource">For a path the manager doesn't track yet: the file to keep as "the original"
    /// (the file there now, or the stock copy); null = there was no original, so turning the mod off deletes the file.</param>
    public sealed record PlanStep(StepKind Kind, string Path, string? Sha, string Owner, string Why, string? OriginalSource = null);

    public sealed class ApplyPlan
    {
        public List<PlanStep> Steps { get; } = new();
        /// <summary>Blocking problems: Apply refuses to run while there are any.</summary>
        public List<string> Problems { get; } = new();
        /// <summary>Things worth knowing that don't block (e.g. two mods ship the same file).</summary>
        public List<string> Warnings { get; } = new();
        public IEnumerable<PlanStep> Changes => Steps.Where(s => s.Kind != StepKind.Keep);
        /// <summary>Installer mods that are on but haven't run yet: run after the file steps (InstallerCapture).</summary>
        public List<ModPackage> InstallersToRun { get; } = new();
    }

    /// <summary>
    /// Brings the game folder to "stock + every enabled mod", highest priority winning each file.
    /// Works per file: install a mod's file, put back the original when no enabled mod wants a path any
    /// more, or delete a file the manager added. Before the first overwrite of any path, the file that
    /// was there is saved to the FileStore. Only touches paths it was asked to manage.
    /// </summary>
    public static class Deployer
    {
        public static ApplyPlan Plan(string gameDir, ModLibrary library, Ledger ledger, StockIndex stock, CancellationToken cancel)
        {
            var plan = new ApplyPlan();
            var cache = HashCache.Load();
            try
            {
                // Tests on a fake game folder set NH5TK_TEST_SKIP_RUNNING_CHECK (like the installers' -SkipRunningCheck).
                bool skipRunningCheck = Environment.GetEnvironmentVariable("NH5TK_TEST_SKIP_RUNNING_CHECK") == "1"
                                        && Environment.GetEnvironmentVariable("NH5TK_DATA_DIR") is { Length: > 0 };
                foreach (var name in new[] { "NASCARHeat5", "NH5ModManager" })
                    if (!skipRunningCheck && Process.GetProcessesByName(name).Length > 0)
                        plan.Problems.Add(name == "NASCARHeat5"
                            ? "NASCAR Heat 5 is running. Close the game first."
                            : "The old NASCAR Heat 5 Mod Manager is open. Close it first, so the two don't write the same files.");

                // Who wants each path. Plain file mods first, highest priority first...
                var wanted = new Dictionary<string, (string Sha, ModPackage Mod)>(StringComparer.OrdinalIgnoreCase);
                foreach (var mod in library.EnabledByPriority().Where(m => !m.IsInstaller))
                    foreach (var (path, sha) in mod.Files)
                    {
                        if (wanted.TryGetValue(path, out var winner))
                        {
                            if (!winner.Sha.Equals(sha, StringComparison.OrdinalIgnoreCase))
                                plan.Warnings.Add($"{path}: \"{winner.Mod.Name}\" wins over \"{mod.Name}\" (higher in the list).");
                            continue;
                        }
                        wanted[path] = (sha, mod);
                        if (!FileStore.Has(sha))
                            plan.Problems.Add($"\"{mod.Name}\": its copy of {path} is missing from the store. Import the mod again.");
                    }

                // ...then installer mods on top, in the order they were installed. Their files are edits of the
                // file that was there when the installer ran, so they only fit on top of exactly that file.
                foreach (var mod in library.Mods.Where(m => m.Enabled && m.Captured).OrderBy(m => m.CapturedAt))
                    foreach (var (path, sha) in mod.Files)
                    {
                        ledger.Files.TryGetValue(path, out var entry);
                        string? under = wanted.TryGetValue(path, out var below) ? below.Sha
                                      : entry is not null ? (entry.Owner == mod.Id ? entry.OriginalSha : entry.Sha)
                                      : CurrentSha(gameDir, path, cache, cancel);
                        mod.Before.TryGetValue(path, out var expected);
                        if (!string.Equals(under, expected, StringComparison.OrdinalIgnoreCase))
                        {
                            plan.Problems.Add($"\"{mod.Name}\" was installed on top of a different {Path.GetFileName(path)} than the one your " +
                                              $"other mods give it now. Switch \"{mod.Name}\" off and Apply, then switch it on and Apply again: " +
                                              "its installer runs again on top of the new file.");
                            break;
                        }
                        wanted[path] = (sha, mod);
                        if (!FileStore.Has(sha))
                            plan.Problems.Add($"\"{mod.Name}\": its copy of {path} is missing from the store.");
                    }

                // Installer mods switched on but not run yet: Apply runs them last, on top of everything else.
                plan.InstallersToRun.AddRange(library.EnabledByPriority().Where(m => m.IsInstaller && !m.Captured));

                foreach (var path in wanted.Keys.Union(ledger.Files.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(p => p))
                {
                    cancel.ThrowIfCancellationRequested();
                    var file = new FileInfo(Path.Combine(gameDir, path));
                    string? current = file.Exists ? cache.Hash(file, cancel).ToLowerInvariant() : null;
                    ledger.Files.TryGetValue(path, out var entry);

                    // Changed behind the manager's back (another installer, the old manager, by hand): never overwrite silently.
                    if (entry is not null && !string.Equals(current, entry.Sha, StringComparison.OrdinalIgnoreCase))
                    {
                        plan.Problems.Add($"{path} was changed outside the Mod Manager since it was installed (by another installer, the old manager, or by hand).");
                        continue;
                    }

                    if (wanted.TryGetValue(path, out var w))
                    {
                        if (string.Equals(current, w.Sha, StringComparison.OrdinalIgnoreCase))
                        {
                            // Already there (e.g. put in earlier by the mod's own install.bat). Its original is
                            // the stock file for a stock path, or nothing for a file the mod added.
                            string? original = null;
                            if (entry is null && stock.IsStockPath(path))
                            {
                                original = StockCopy(path);
                                if (original is null)
                                {
                                    // No stock copy: never delete a stock path. Keeping the file itself as the
                                    // "original" means turning the mod off leaves it as it is.
                                    original = file.FullName;
                                    plan.Warnings.Add($"{path}: no stock copy found, so turning \"{w.Mod.Name}\" off will leave this file as it is.");
                                }
                            }
                            plan.Steps.Add(new PlanStep(StepKind.Keep, path, w.Sha, w.Mod.Id, "already in place", original));
                        }
                        else
                            plan.Steps.Add(new PlanStep(StepKind.Install, path, w.Sha, w.Mod.Id,
                                entry is not null ? "switch to this mod's version"
                                : current is null ? "new file"
                                : "replaces the file that's there now (it is saved first)",
                                entry is null && current is not null ? file.FullName : null));
                    }
                    else if (entry is not null)
                    {
                        if (entry.OriginalSha is null)
                            plan.Steps.Add(new PlanStep(StepKind.Remove, path, null, entry.Owner, "added by a mod that is now off"));
                        else if (!FileStore.Has(entry.OriginalSha))
                            plan.Problems.Add($"{path}: the original file is missing from the store, so it can't be put back.");
                        else
                            plan.Steps.Add(new PlanStep(StepKind.Restore, path, entry.OriginalSha, entry.Owner, "put back the file from before the mod"));
                    }
                }
            }
            finally
            {
                cache.Save();
            }
            return plan;
        }

        public static void Apply(string gameDir, ApplyPlan plan, Ledger ledger, IProgress<(int done, int total, string path)>? progress,
                                 CancellationToken cancel)
        {
            if (plan.Problems.Count > 0)
                throw new InvalidOperationException("Apply was called with unresolved problems.");

            var cache = HashCache.Load();
            var changes = plan.Steps.ToList();
            int done = 0;
            try
            {
                foreach (var step in changes)
                {
                    cancel.ThrowIfCancellationRequested();
                    progress?.Report((done, changes.Count, step.Path));
                    string target = Path.Combine(gameDir, step.Path);
                    ledger.Files.TryGetValue(step.Path, out var entry);

                    switch (step.Kind)
                    {
                        case StepKind.Keep:
                            ledger.Files[step.Path] = new LedgerEntry
                            {
                                Sha = step.Sha!, Owner = step.Owner,
                                OriginalSha = entry is not null ? entry.OriginalSha
                                            : step.OriginalSource is { } keepSrc ? FileStore.Put(keepSrc, cache, cancel)
                                            : null   // a file the mod added: turning it off deletes it
                            };
                            break;

                        case StepKind.Install:
                            string? original = entry is not null ? entry.OriginalSha
                                             : step.OriginalSource is { } src ? FileStore.Put(src, cache, cancel)   // save what was there
                                             : null;
                            WriteFromStore(step.Sha!, target, cancel);
                            ledger.Files[step.Path] = new LedgerEntry { Sha = step.Sha!, Owner = step.Owner, OriginalSha = original };
                            break;

                        case StepKind.Restore:
                            WriteFromStore(step.Sha!, target, cancel);
                            ledger.Files.Remove(step.Path);
                            break;

                        case StepKind.Remove:
                            if (File.Exists(target)) File.Delete(target);
                            RemoveEmptyFolders(Path.GetDirectoryName(target)!, gameDir);
                            ledger.Files.Remove(step.Path);
                            break;
                    }
                    ledger.Save();
                    done++;
                }
                progress?.Report((done, changes.Count, ""));
            }
            finally
            {
                cache.Save();
            }
        }

        private static string? CurrentSha(string gameDir, string rel, HashCache cache, CancellationToken cancel)
        {
            var f = new FileInfo(Path.Combine(gameDir, rel));
            return f.Exists ? cache.Hash(f, cancel).ToLowerInvariant() : null;
        }

        /// <summary>The stock copy of a game file, if the old Mod Manager's Vanilla_Backup has it.</summary>
        private static string? StockCopy(string rel)
        {
            string p = Path.Combine(GameScanner.OldManagerStockDir, rel);
            return File.Exists(p) ? p : null;
        }

        /// <summary>Copy to a temp name next to the target, check the hash, then swap in.</summary>
        private static void WriteFromStore(string sha, string target, CancellationToken cancel)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            string tmp = target + ".nh5tk.part";
            File.Copy(FileStore.PathFor(sha), tmp, overwrite: true);
            if (FileStore.Sha256(tmp, cancel) != sha)
            {
                File.Delete(tmp);
                throw new IOException($"Copy check failed for {target}. Nothing was replaced.");
            }
            File.Move(tmp, target, overwrite: true);
        }

        private static void RemoveEmptyFolders(string dir, string stopAt)
        {
            while (dir.Length > stopAt.Length && Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
            {
                Directory.Delete(dir);
                dir = Path.GetDirectoryName(dir)!;
            }
        }
    }
}
