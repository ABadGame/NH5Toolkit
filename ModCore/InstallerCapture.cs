using System.Diagnostics;

namespace NH5Toolkit.ModCore
{
    public sealed class CaptureResult
    {
        public int Changed { get; set; }
        public int Added { get; set; }
        public List<string> Deleted { get; } = new();
        /// <summary>Set when nothing was recorded (installer cancelled, failed, or changed nothing).</summary>
        public string? Error { get; set; }
    }

    /// <summary>
    /// Runs a mod's own installer program against the game and records what it did:
    ///   1. make sure the current content of every game file can be put back (it's either the stock copy in
    ///      the old manager's Vanilla_Backup, already in the FileStore, or copied there now),
    ///   2. start the installer and wait for it (and for the user to confirm it finished: a .bat often
    ///      re-launches itself as admin and exits straight away),
    ///   3. compare the game folder before and after, store every changed or new file, and write it to
    ///      the mod (Files = after, Before = before) and to the ledger as owned by the mod.
    /// After that the mod is switched off like any other: Apply puts the "before" files back.
    /// </summary>
    public static class InstallerCapture
    {
        public static CaptureResult Run(string gameDir, ModPackage mod, Ledger ledger, IProgress<string>? progress,
                                        Func<string, bool> confirmFinished, CancellationToken cancel)
        {
            if (!mod.IsInstaller || mod.InstallerDir is null || mod.InstallerExe is null)
                throw new InvalidOperationException($"{mod.Name} has no installer program.");
            string program = Path.Combine(mod.InstallerDir, mod.InstallerExe);
            if (!File.Exists(program))
                return new CaptureResult { Error = $"The manager's copy of the installer is gone ({program}). Remove the mod and add it again." };

            var cache = HashCache.Load();
            try
            {
                // 1. Snapshot + make every current file restorable.
                progress?.Report("Reading the game folder before the installer runs...");
                var before = Snapshot(gameDir, cache, cancel);
                progress?.Report("Saving the current game files so they can be put back (the first time takes a while)...");
                foreach (var (rel, sha) in before)
                {
                    cancel.ThrowIfCancellationRequested();
                    if (FileStore.Has(sha)) continue;
                    string stockCopy = Path.Combine(GameScanner.OldManagerStockDir, rel);
                    if (File.Exists(stockCopy) && cache.Hash(new FileInfo(stockCopy), cancel).Equals(sha, StringComparison.OrdinalIgnoreCase))
                        continue;   // identical to the stock copy: it can be brought back from there after the run
                    FileStore.Put(Path.Combine(gameDir, rel), cache, cancel);
                }
                cache.Save();

                // 2. Run the installer.
                progress?.Report($"Running {Path.GetFileName(program)}. Use its Install button, then close it.");
                bool isBat = program.EndsWith(".bat", StringComparison.OrdinalIgnoreCase) || program.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase);
                using (var p = Process.Start(new ProcessStartInfo
                {
                    FileName = isBat ? "cmd.exe" : program,
                    Arguments = isBat ? $"/c \"\"{program}\"\"" : "",
                    WorkingDirectory = Path.GetDirectoryName(program)!,
                    UseShellExecute = !isBat
                }))
                {
                    p?.WaitForExit();
                }
                if (!confirmFinished($"Has the {mod.Name} installer finished?\n\nClick Yes once it says it's done (and its window is closed). " +
                                     "Click No if you cancelled it or it failed."))
                    return new CaptureResult { Error = "Not recorded: the installer was cancelled or failed. Nothing was changed by the Mod Manager." };

                // 3. Compare.
                progress?.Report("Recording what the installer changed...");
                var after = Snapshot(gameDir, cache, cancel);
                var result = new CaptureResult();
                var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var was = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

                foreach (var (rel, sha) in after)
                {
                    before.TryGetValue(rel, out var old);
                    if (old is not null && old.Equals(sha, StringComparison.OrdinalIgnoreCase)) continue;
                    if (old is null) result.Added++; else result.Changed++;
                    files[rel] = FileStore.Put(Path.Combine(gameDir, rel), cache, cancel);
                    was[rel] = old;
                    if (old is not null && !FileStore.Has(old))
                        FileStore.Put(Path.Combine(GameScanner.OldManagerStockDir, rel), cache, cancel);   // stock: copy from the backup
                }
                foreach (var rel in before.Keys.Where(r => !after.ContainsKey(r)))
                    result.Deleted.Add(rel);

                if (files.Count == 0)
                {
                    result.Error = result.Deleted.Count > 0
                        ? "The installer only deleted files, which the Mod Manager can't record yet. Nothing recorded."
                        : "The installer didn't change any game file. If it said the mod was already installed, remove it with the installer first.";
                    return result;
                }

                // A file the installer changed that the manager already manages (another mod's file) now
                // belongs to this mod, keeping the deepest original so switching everything off still gets back to it.
                foreach (var (rel, sha) in files)
                {
                    ledger.Files.TryGetValue(rel, out var entry);
                    ledger.Files[rel] = new LedgerEntry { Sha = sha, Owner = mod.Id, OriginalSha = entry is not null ? entry.OriginalSha : was[rel] };
                }
                ledger.Save();

                mod.Files = files;
                mod.CapturedAt = DateTime.Now;
                mod.Before = was;
                if (result.Deleted.Count > 0)
                    mod.Notes.Add("The installer deleted: " + string.Join(", ", result.Deleted.Take(10)) +
                                  ". Switching the mod off doesn't bring those back on its own (they are saved in the store).");
                return result;
            }
            finally
            {
                cache.Save();
            }
        }

        /// <summary>
        /// Clears what an installer recorded once it's switched off and its files are back, so switching it on
        /// later runs the installer fresh (on top of whatever the other mods give it then).
        /// </summary>
        public static bool ForgetIfRemoved(ModPackage mod, Ledger ledger)
        {
            if (!mod.Captured || mod.Enabled || ledger.Files.Values.Any(e => e.Owner == mod.Id)) return false;
            mod.Files.Clear();
            mod.CapturedAt = null;
            mod.Before.Clear();
            return true;
        }

        /// <summary>Every file in the game folder and its SHA-256 (cached by size + time, so this is quick after the first run).</summary>
        private static Dictionary<string, string> Snapshot(string gameDir, HashCache cache, CancellationToken cancel)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in Directory.EnumerateFiles(gameDir, "*", SearchOption.AllDirectories))
            {
                cancel.ThrowIfCancellationRequested();
                if (path.EndsWith(".nh5tk.part", StringComparison.OrdinalIgnoreCase)) continue;
                map[Path.GetRelativePath(gameDir, path)] = cache.Hash(new FileInfo(path), cancel).ToLowerInvariant();
            }
            return map;
        }
    }
}
