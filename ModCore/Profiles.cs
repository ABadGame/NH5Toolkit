using System.Text.Json;

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
            foreach (var p in Profiles) p.On.Remove(id);
        }
    }
}
