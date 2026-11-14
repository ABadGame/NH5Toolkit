using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NH5Toolkit.ModCore
{
    public sealed class ImportResult
    {
        public ModPackage? Mod { get; init; }
        /// <summary>Why nothing was imported (shown to the user as is).</summary>
        public string? Error { get; init; }
        /// <summary>Files in the download that were left out, with the reason.</summary>
        public List<string> Skipped { get; } = new();
    }

    /// <summary>
    /// Turns a mod download (folder or .zip) into a library mod, whatever shape it comes in:
    ///   1. a package with manifest.json + files\ (the install.bat mods: IndyCar, NASCAR '14, Classic Menu...),
    ///      imported straight from the manifest, every file hash-checked; the .bat is not run,
    ///   2. H5 Track Loader tracks (&lt;id&gt;.sab + &lt;id&gt;_ui\slot.txt),
    ///   3. a copy of the game's folders (NASCARHeat5_Data\..., optionally inside a "NASCAR Heat 5" folder),
    ///   4. loose files named like stock files (Music Bank.bank, sharedassets3.assets...), placed by name.
    /// Mods that bring their own installer program (.exe, or a .bat with no manifest) are recognised and
    /// refused with a clear message for now.
    /// </summary>
    public static class Importer
    {
        private const string TracksDir = @"NASCARHeat5_Data\StreamingAssets\PC\AssetBundles\Tracks\";
        private const string LoaderMarker = @"H5TrackLoader\installed-by-h5trackloader.txt";

        public static ImportResult Import(string source, ModLibrary library, StockIndex stock, CancellationToken cancel)
        {
            string? temp = null;
            try
            {
                string root = source;
                if (File.Exists(source))
                {
                    string ext = Path.GetExtension(source).ToLowerInvariant();
                    if (ext is ".rar" or ".7z")
                        return new ImportResult { Error = $"{Path.GetFileName(source)} is a {ext} archive. Unpack it first, then add the folder." };
                    if (ext != ".zip")
                        return new ImportResult { Error = "Add a mod folder or a .zip file." };
                    temp = Path.Combine(Path.GetTempPath(), "nh5tk_import_" + Guid.NewGuid().ToString("N"));
                    ZipFile.ExtractToDirectory(source, temp);
                    root = temp;
                }
                if (!Directory.Exists(root))
                    return new ImportResult { Error = $"Not found: {source}" };

                // Folder names keep their dots ("LagunaSeca_Heat5_0.1.2"); only a .zip loses its extension.
                string trimmed = source.TrimEnd('\\', '/');
                string name = File.Exists(trimmed) ? Path.GetFileNameWithoutExtension(trimmed) : Path.GetFileName(trimmed);
                var cache = HashCache.Load();
                try
                {
                    return FromManifest(root, name, source, library, cache, cancel)
                        ?? FromLoaderTracks(root, name, source, library, cache, cancel)
                        ?? FromInstallerProgram(root, name, source, library)
                        ?? FromGameFolders(root, name, source, library, cache, cancel)
                        ?? FromLooseFiles(root, name, source, library, stock, cache, cancel);
                }
                finally
                {
                    cache.Save();
                }
            }
            catch (InvalidDataException)
            {
                return new ImportResult { Error = "The .zip file is damaged. Download it again." };
            }
            finally
            {
                if (temp is not null)
                    try { Directory.Delete(temp, true); } catch { /* temp clean-up only */ }
            }
        }

        // ---------- 1. manifest.json + files\ (the install.bat packages) ----------

        private static ImportResult? FromManifest(string root, string fallbackName, string source, ModLibrary library,
                                                  HashCache cache, CancellationToken cancel)
        {
            var manifestPath = Directory.EnumerateFiles(root, "manifest.json", SearchOption.AllDirectories)
                .OrderBy(p => p.Count(c => c == '\\'))
                .FirstOrDefault(p => Directory.Exists(Path.Combine(Path.GetDirectoryName(p)!, "files")));
            if (manifestPath is null) return null;

            string dir = Path.GetDirectoryName(manifestPath)!;
            using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
            if (!doc.RootElement.TryGetProperty("files", out var files) || files.ValueKind != JsonValueKind.Object)
                return null;

            string name = doc.RootElement.TryGetProperty("name", out var n) ? n.GetString() ?? fallbackName : fallbackName;
            string version = doc.RootElement.TryGetProperty("version", out var v) ? v.GetString() ?? "" : "";
            var mod = NewMod(library, name.Replace('_', ' '), version, "install.bat package (manifest.json)", source);

            foreach (var f in files.EnumerateObject())
            {
                cancel.ThrowIfCancellationRequested();
                string rel = Normalize(f.Name);
                string file = Path.Combine(dir, "files", rel);
                if (!File.Exists(file))
                    return new ImportResult { Error = $"The download is incomplete: {rel} is listed in manifest.json but missing." };
                string sha = FileStore.Put(file, cache, cancel);
                if (!sha.Equals(f.Value.GetString(), StringComparison.OrdinalIgnoreCase))
                    return new ImportResult { Error = $"The download is damaged: {rel} doesn't match manifest.json. Download it again." };
                mod.Files[rel] = sha;
            }

            AddLoaderMarkerIfNeeded(mod, cache, cancel);
            mod.Notes.Add("Imported from its manifest; its install.bat / uninstall.bat are not needed with the Mod Manager. " +
                          "Don't run them as well, or the two will fight over the same files.");
            return Done(mod);
        }

        /// <summary>
        /// Mods that bring H5 Track Loader also need its marker file, or other mods' installers think
        /// winhttp.dll belongs to a different mod loader and refuse to install.
        /// </summary>
        private static void AddLoaderMarkerIfNeeded(ModPackage mod, HashCache cache, CancellationToken cancel)
        {
            if (!mod.Files.ContainsKey("winhttp.dll") || mod.Files.ContainsKey(LoaderMarker)) return;
            string tmp = Path.Combine(Path.GetTempPath(), "nh5tk_marker_" + Guid.NewGuid().ToString("N") + ".txt");
            File.WriteAllText(tmp, "H5 Track Loader: this folder, winhttp.dll and doorstop_config.ini in the game folder belong to it.\r\n");
            try { mod.Files[LoaderMarker] = FileStore.Put(tmp, cache, cancel); }
            finally { File.Delete(tmp); }
        }

        // ---------- 2. H5 Track Loader tracks ----------

        private static ImportResult? FromLoaderTracks(string root, string fallbackName, string source, ModLibrary library,
                                                      HashCache cache, CancellationToken cancel)
        {
            var slots = Directory.EnumerateFiles(root, "slot.txt", SearchOption.AllDirectories)
                .Where(p => Path.GetFileName(Path.GetDirectoryName(p)!).EndsWith("_ui", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (slots.Count == 0) return null;
            // A copy of the game folder that happens to contain tracks is handled as game folders instead.
            if (Directory.EnumerateDirectories(root, "NASCARHeat5_Data", SearchOption.AllDirectories).Any()) return null;

            var mod = NewMod(library, fallbackName.Replace('_', ' '), "", "H5 Track Loader track", source);
            foreach (var slot in slots)
            {
                string uiDir = Path.GetDirectoryName(slot)!;
                string uiName = Path.GetFileName(uiDir);
                string bundleBase = uiName[..^3];
                string bundle = ReadSlotValue(slot, "bundle") ?? bundleBase + ".sab";
                string sab = Path.Combine(Path.GetDirectoryName(uiDir)!, bundle);
                if (!File.Exists(sab))
                    return new ImportResult { Error = $"{uiName}\\slot.txt needs {bundle}, but it isn't next to the {uiName} folder." };

                mod.Files[TracksDir + bundle] = FileStore.Put(sab, cache, cancel);
                foreach (var f in Directory.EnumerateFiles(uiDir, "*", SearchOption.AllDirectories))
                    mod.Files[TracksDir + Path.GetFileNameWithoutExtension(bundle) + "_ui\\" + Path.GetRelativePath(uiDir, f)] =
                        FileStore.Put(f, cache, cancel);
            }
            mod.Notes.Add("Needs H5 Track Loader in the game (it reads Tracks\\*_ui\\slot.txt at start).");
            return Done(mod);
        }

        private static string? ReadSlotValue(string slotFile, string key)
        {
            foreach (var line in File.ReadLines(slotFile))
            {
                var m = Regex.Match(line, @"^\s*" + key + @"\s*[=:]\s*(.+?)\s*$", RegexOptions.IgnoreCase);
                if (m.Success) return m.Groups[1].Value;
            }
            return null;
        }

        // ---------- installer programs (.exe, or a .bat with no manifest) ----------

        /// <summary>
        /// A mod that edits the game itself (Coronado-style track installers patch Assembly-CSharp.dll and the
        /// .assets files in place). The manager keeps its own copy of the installer folder, and runs it during
        /// Apply while recording every file it changes (InstallerCapture), so the mod can be switched off again.
        /// </summary>
        private static ImportResult? FromInstallerProgram(string root, string fallbackName, string source, ModLibrary library)
        {
            var exe = Directory.EnumerateFiles(root, "*.exe", SearchOption.AllDirectories)
                .Where(p => Path.GetFileName(p).Contains("install", StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => p.Count(c => c == '\\'))
                .FirstOrDefault();
            var bat = Directory.EnumerateFiles(root, "*.bat", SearchOption.AllDirectories)
                .Where(p => Path.GetFileName(p).Contains("install", StringComparison.OrdinalIgnoreCase)
                            && !Path.GetFileName(p).Contains("uninstall", StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => p.Count(c => c == '\\'))
                .FirstOrDefault();
            string? program = exe ?? bat;
            if (program is null) return null;
            // A plain game-folder copy that just ships a convenience .bat is still importable as files.
            if (exe is null && Directory.EnumerateDirectories(root, "NASCARHeat5_Data", SearchOption.AllDirectories).Any()) return null;

            var mod = NewMod(library, Regex.Replace(fallbackName.Replace('_', ' '), @"\s+Installer\b", "", RegexOptions.IgnoreCase),
                             "", "installer program (" + Path.GetFileName(program) + ")", source);

            // Keep the installer's whole folder (program, its .dlls, its Files folder) so it can run again later.
            string programDir = Path.GetDirectoryName(program)!;
            string keep = Path.Combine(ToolkitConfig.Dir, "Installers", mod.Id);
            if (Directory.Exists(keep)) Directory.Delete(keep, true);
            CopyFolder(programDir, keep);
            mod.InstallerDir = keep;
            mod.InstallerExe = Path.GetRelativePath(programDir, program);

            mod.Notes.Add($"Brings its own installer ({Path.GetFileName(program)}). When you switch it on and Apply, the Mod Manager opens " +
                          "the installer for you: click its Install button, then close it. Every file it changes is saved first and recorded, " +
                          "so switching the mod off and applying puts them back.");
            return Done(mod);
        }

        private static void CopyFolder(string from, string to)
        {
            foreach (var dir in Directory.EnumerateDirectories(from, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, dir)));
            Directory.CreateDirectory(to);
            foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
                File.Copy(file, Path.Combine(to, Path.GetRelativePath(from, file)), overwrite: true);
        }

        // ---------- 3. a copy of the game's folders ----------

        private static ImportResult? FromGameFolders(string root, string fallbackName, string source, ModLibrary library,
                                                     HashCache cache, CancellationToken cancel)
        {
            string? data = Directory.EnumerateDirectories(root, "NASCARHeat5_Data", SearchOption.AllDirectories)
                .OrderBy(p => p.Count(c => c == '\\'))
                .FirstOrDefault();
            if (data is null && Directory.Exists(Path.Combine(root, "H5TrackLoader")))
                data = Path.Combine(root, "H5TrackLoader");
            if (data is null) return null;

            string gameRoot = Path.GetDirectoryName(data)!;
            var mod = NewMod(library, fallbackName.Replace('_', ' '), "", "game folder copy", source);
            var result = new ImportResult { Mod = mod };
            foreach (var f in Directory.EnumerateFiles(gameRoot, "*", SearchOption.AllDirectories))
            {
                cancel.ThrowIfCancellationRequested();
                string rel = Path.GetRelativePath(gameRoot, f);
                bool inGameFolder = rel.StartsWith(@"NASCARHeat5_Data\", StringComparison.OrdinalIgnoreCase)
                                    || rel.StartsWith(@"DLC\", StringComparison.OrdinalIgnoreCase)
                                    || rel.StartsWith(@"H5TrackLoader\", StringComparison.OrdinalIgnoreCase)
                                    || rel.Equals("winhttp.dll", StringComparison.OrdinalIgnoreCase)
                                    || rel.Equals("doorstop_config.ini", StringComparison.OrdinalIgnoreCase);
                if (!inGameFolder || Path.GetFileName(rel).Equals("desktop.ini", StringComparison.OrdinalIgnoreCase))
                {
                    result.Skipped.Add($"{rel} (not part of the game folder)");
                    continue;
                }
                mod.Files[rel] = FileStore.Put(f, cache, cancel);
            }
            AddLoaderMarkerIfNeeded(mod, cache, cancel);
            return mod.Files.Count == 0 ? new ImportResult { Error = "The NASCARHeat5_Data folder in this mod is empty." } : result;
        }

        // ---------- 4. loose files named like stock files ----------

        private static ImportResult FromLooseFiles(string root, string fallbackName, string source, ModLibrary library, StockIndex stock,
                                                   HashCache cache, CancellationToken cancel)
        {
            var mod = NewMod(library, fallbackName.Replace('_', ' '), "", "loose files (placed by name)", source);
            var result = new ImportResult { Mod = mod };
            var variants = new List<string>();
            var placedFrom = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var f in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                cancel.ThrowIfCancellationRequested();
                string fileName = Path.GetFileName(f);
                if (!IsGameDataName(fileName))
                {
                    result.Skipped.Add($"{Path.GetRelativePath(root, f)} (only game data files are placed by name)");
                    continue;
                }
                string? target = stock.PlaceByName(fileName);
                if (target is null)
                {
                    // "(Evening - Stages) sharedassets3.assets": a labelled alternative of a stock file.
                    var m = Regex.Match(fileName, @"^\((?<label>[^)]*)\)\s*(?<name>.+)$");
                    if (m.Success && stock.PlaceByName(m.Groups["name"].Value) is { } alt)
                        variants.Add($"\"{fileName}\" is an alternative {Path.GetFileName(alt)}");
                    else
                        result.Skipped.Add($"{Path.GetRelativePath(root, f)} (no stock file has this name)");
                    continue;
                }
                if (placedFrom.TryGetValue(target, out var other))
                    return new ImportResult
                    {
                        Error = $"Two files in this mod would both replace {fileName}: {Path.GetRelativePath(root, other)} and " +
                                $"{Path.GetRelativePath(root, f)}. Keep only the one you want and add the mod again."
                    };
                placedFrom[target] = f;
                mod.Files[target] = FileStore.Put(f, cache, cancel);
            }

            if (variants.Count > 0)
                mod.Notes.Add("Left out (choose one by renaming it to the plain file name, then add the mod again): " + string.Join("; ", variants) + ".");
            if (mod.Files.Count == 0)
                return new ImportResult
                {
                    Error = "Nothing in this mod matches a game file. It isn't a manifest package, an H5 Track Loader track, " +
                            "a copy of NASCARHeat5_Data, or loose files named like the game's own." +
                            (variants.Count > 0 ? " " + mod.Notes[^1] : "")
                };
            return result;
        }

        /// <summary>
        /// Loose files are only placed when they look like game data. A readme.txt or a picture must
        /// never land on top of a stock file that happens to share its name.
        /// </summary>
        private static bool IsGameDataName(string fileName)
        {
            string name = Regex.Replace(fileName, @"^\([^)]*\)\s*", "");   // "(Evening) sharedassets3.assets"
            string ext = Path.GetExtension(name).ToLowerInvariant();
            return ext is ".assets" or ".ress" or ".resource" or ".bank" or ".sab" or ".rab" or ".dll" or ".mp4" or ".binary" or ".scenario"
                   || Regex.IsMatch(name, @"^(level\d+|globalgamemanagers)$", RegexOptions.IgnoreCase);
        }

        // ---------- helpers ----------

        private static ModPackage NewMod(ModLibrary library, string name, string version, string format, string source)
        {
            // "LagunaSeca Heat5 0.1.2" -> name "LagunaSeca Heat5", version "0.1.2"
            var m = Regex.Match(name, @"^(?<name>.+?)[\s_-]+v?(?<ver>\d+(\.\d+)+)$");
            if (m.Success && version.Length == 0)
            {
                name = m.Groups["name"].Value;
                version = m.Groups["ver"].Value;
            }
            return new ModPackage
            {
                Id = library.NewId(name),
                Name = name,
                Version = version,
                Format = format,
                Source = source,
                Imported = DateTime.Now
            };
        }

        private static ImportResult Done(ModPackage mod) => new() { Mod = mod };

        private static string Normalize(string rel) => rel.Replace('/', '\\').TrimStart('\\');
    }
}
