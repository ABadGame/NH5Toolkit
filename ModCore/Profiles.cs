using System.Text.Json;
using System.Text.Json.Serialization;

namespace NH5Toolkit.ModCore
{
    /// <summary>
    /// A mod profile, like the old NASCAR Heat 5 Mod Manager's: a named set of which mods are on (in order: first wins a file
    /// two of them ship) and whether Enable Multiplayer is on. Selecting a profile only changes the list; Deploy puts it in the game.
    /// </summary>
    public sealed class ModProfile
    {
        public string Name { get; set; } = "";
        /// <summary>Ids of the mods that are on, highest priority first.</summary>
        public List<string> On { get; set; } = new();
        public bool Multiplayer { get; set; }
        /// <summary>
        /// 2026-11-16 (user: "i cant edit the individual files in the profiles"): files left out of a mod for this profile only,
        /// mod id -> game-relative paths. Like the old manager's per-file ticks; the mod itself and other profiles keep them.
        /// </summary>
        public Dictionary<string, List<string>> Excluded { get; set; } = new();

        public bool IsExcluded(string modId, string path) =>
            Excluded.TryGetValue(modId, out var l) && l.Contains(path, StringComparer.OrdinalIgnoreCase);

        public void SetExcluded(string modId, string path, bool excluded)
        {
            if (!Excluded.TryGetValue(modId, out var l)) Excluded[modId] = l = new();
            l.RemoveAll(x => x.Equals(path, StringComparison.OrdinalIgnoreCase));
            if (excluded) l.Add(path);
            if (l.Count == 0) Excluded.Remove(modId);
        }
    }

    /// <summary>%APPDATA%\NH5Toolkit\profiles.json: every profile, the one selected, and the one last deployed to the game.</summary>
    public sealed class ProfileStore
    {
        private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
        public static string StorePath => Path.Combine(ToolkitConfig.Dir, "profiles.json");

        public List<ModProfile> Profiles { get; set; } = new();
        public string Selected { get; set; } = "Default";
        /// <summary>The profile the game folder was last deployed with (null = never deployed by the toolkit).</summary>
        public string? Deployed { get; set; }

        public static ProfileStore Load(ModLibrary library)
        {
            ProfileStore store;
            if (File.Exists(StorePath))
            {
                try { store = JsonSerializer.Deserialize<ProfileStore>(File.ReadAllText(StorePath)) ?? new(); }
                catch (Exception ex) { throw new IOException($"profiles.json could not be read ({ex.Message}). Fix or remove {StorePath}.", ex); }
            }
            else
                store = new ProfileStore();
            if (store.Profiles.Count == 0)
            {
                // first start: "Default" = the mods that are on right now, so nothing changes
                var def = new ModProfile { Name = "Default" };
                def.On = library.EnabledByPriority().Select(m => m.Id).ToList();
                store.Profiles.Add(def);
                store.Selected = def.Name;
            }
            if (store.Find(store.Selected) is null) store.Selected = store.Profiles[0].Name;
            return store;
        }

        public void Save()
        {
            Directory.CreateDirectory(ToolkitConfig.Dir);
            string tmp = StorePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
            File.Move(tmp, StorePath, overwrite: true);
        }

        public ModProfile? Find(string name) => Profiles.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        [JsonIgnore]
        public ModProfile Current => Find(Selected) ?? Profiles[0];

        public static string? CheckName(string name, ProfileStore store, string? except = null)
        {
            name = name.Trim();
            if (name.Length == 0) return "Type a name.";
            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return "A profile name can't contain \\ / : * ? \" < > |";
            if (store.Find(name) is { } other && !other.Name.Equals(except, StringComparison.OrdinalIgnoreCase))
                return $"There is already a profile named \"{other.Name}\".";
            return null;
        }

        /// <summary>Makes the library show this profile: its mods on, in its order (others off, behind them).</summary>
        public static void ApplyToLibrary(ModProfile profile, ModLibrary library)
        {
            var known = profile.On.Where(id => library.Mods.Any(m => m.Id == id)).ToList();
            int p = 0;
            foreach (var id in known)
            {
                var m = library.Mods.First(x => x.Id == id);
                m.Enabled = true;
                m.Priority = p++;
            }
            foreach (var m in library.Mods.Where(m => !known.Contains(m.Id)).OrderBy(m => m.Priority).ToList())
            {
                m.Enabled = false;
                m.Priority = p++;
            }
        }

        /// <summary>Stores the library's current on/off + order into the profile.</summary>
        public static void CaptureFromLibrary(ModProfile profile, ModLibrary library) =>
            profile.On = library.EnabledByPriority().Select(m => m.Id).ToList();

        public void ForgetMod(string id)
        {
            foreach (var p in Profiles)
            {
                p.On.Remove(id);
                p.Excluded.Remove(id);
            }
        }

        /// <summary>
        /// The library as this profile deploys it: mods with files left out (Excluded) are copies without those files. The real
        /// library is never changed. Installer mods are always whole (their captured edits only fit together).
        /// </summary>
        public static ModLibrary Filtered(ModLibrary library, ModProfile profile)
        {
            if (profile.Excluded.Count == 0) return library;
            var mods = new List<ModPackage>();
            foreach (var m in library.Mods)
            {
                if (m.IsInstaller || !profile.Excluded.TryGetValue(m.Id, out var ex) || ex.Count == 0) { mods.Add(m); continue; }
                var skip = new HashSet<string>(ex, StringComparer.OrdinalIgnoreCase);
                mods.Add(new ModPackage
                {
                    Id = m.Id, Name = m.Name, Version = m.Version, Format = m.Format, Source = m.Source, Imported = m.Imported,
                    Enabled = m.Enabled, Priority = m.Priority, Notes = m.Notes, Size = m.Size,
                    Files = new Dictionary<string, string>(m.Files.Where(f => !skip.Contains(f.Key)), StringComparer.OrdinalIgnoreCase)
                });
            }
            return new ModLibrary { Mods = mods };
        }
    }
}
