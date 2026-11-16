using System.Text.Json;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace NH5Toolkit.ModCore
{
    /// <summary>
    /// "Enable Multiplayer", the same patch as the old NASCAR Heat 5 Mod Manager's Custom Server option (its Form1.ApplyAssemblyPatches,
    /// Patch 2): NGUtil.GetBaseURL in Assembly-CSharp.dll points at the community server instead of the shut-down mgrsys.com servers.
    /// Path A replaces the two stock URLs in place; path B (a dll without them, e.g. already patched by another tool) rewrites the
    /// method body exactly like the old manager did.
    ///
    /// In the toolkit it is a hidden step that goes on top of everything else: a "captured installer" mod (id __multiplayer) whose
    /// one file is the patched copy of whatever Assembly-CSharp.dll the other mods leave there, with Before = that file. So Deploy
    /// installs it last, switching it off puts the file underneath back, and the Deployer's own check refuses to put it on any other dll.
    /// </summary>
    public static class MultiplayerPatch
    {
        public const string Id = "__multiplayer";
        public const string DllPath = @"NASCARHeat5_Data\Managed\Assembly-CSharp.dll";
        private const string Server2020 = "http://72.39.41.141:8000/n2020/";
        private const string Server2022 = "http://72.39.41.141:8000/n2022/";
        private const string Stock2020 = "https://n2020.mgrsys.com/";
        private const string Stock2022 = "https://n2022.mgrsys.com/";

        // v2 2026-11-16: a dll already pointing at the server maps to itself (v1 cached Cecil rewrites of such dlls)
        private static string CachePath => Path.Combine(ToolkitConfig.Dir, "multiplayer_patch_v2.json");

        /// <summary>The hidden mod for this plan: the patched copy of the dll the other enabled mods leave in the game.</summary>
        public static ModPackage Build(string gameDir, ModLibrary library, Ledger ledger, CancellationToken cancel)
        {
            var cache = HashCache.Load();
            try
            {
                string under = UnderSha(gameDir, library, ledger, cache, cancel, out string sourceFile);
                string patched = PatchedSha(under, sourceFile, gameDir, cache, cancel);
                return new ModPackage
                {
                    Id = Id,
                    Name = "Enable Multiplayer",
                    Format = "toolkit",
                    Enabled = true,
                    Priority = int.MaxValue,
                    InstallerExe = "(multiplayer patch)",   // makes it a captured installer step: applied after every other mod
                    CapturedAt = DateTime.MaxValue,
                    Files = new(StringComparer.OrdinalIgnoreCase) { [DllPath] = patched },
                    Before = new(StringComparer.OrdinalIgnoreCase) { [DllPath] = under }
                };
            }
            finally
            {
                cache.Save();
            }
        }

        /// <summary>The library plus the hidden mod, for Deployer.Plan (the library itself never stores it).</summary>
        public static ModLibrary WithPatch(ModLibrary library, ModPackage patch) =>
            new() { Mods = library.Mods.Where(m => m.Id != Id).Append(patch).ToList() };

        // the dll the Deployer would leave without the patch: same order as Deployer.Plan
        private static string UnderSha(string gameDir, ModLibrary library, Ledger ledger, HashCache cache, CancellationToken cancel, out string file)
        {
            string? sha = library.EnabledByPriority().Where(m => !m.IsInstaller).FirstOrDefault(m => m.Files.ContainsKey(DllPath))?.Files[DllPath];
            foreach (var m in library.Mods.Where(m => m.Enabled && m.Captured && m.Id != Id).OrderBy(m => m.CapturedAt))
                if (m.Files.TryGetValue(DllPath, out var s)) sha = s;
            if (sha is null && ledger.Files.TryGetValue(DllPath, out var entry))
                sha = entry.OriginalSha;   // no mod wants it: Deploy puts back the file from before the manager
            if (sha is not null)
            {
                file = FileStore.PathFor(sha);
                if (!File.Exists(file)) throw new IOException("The Assembly-CSharp.dll under Enable Multiplayer is missing from the store.");
                return sha.ToLowerInvariant();
            }
            file = Path.Combine(gameDir, DllPath);   // untouched by the manager: the game's own file
            if (!File.Exists(file)) throw new IOException("NASCARHeat5_Data\\Managed\\Assembly-CSharp.dll not found in the game folder.");
            return cache.Hash(new FileInfo(file), cancel).ToLowerInvariant();
        }

        private static string PatchedSha(string underSha, string sourceFile, string gameDir, HashCache cache, CancellationToken cancel)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try { if (File.Exists(CachePath)) map = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(CachePath)) ?? map; }
            catch { /* rebuilt below */ }
            if (map.TryGetValue(underSha, out var known) && FileStore.Has(known)) return known;

            string tmp = Path.Combine(Path.GetTempPath(), $"nh5tk_mp_{Guid.NewGuid():N}.dll");
            try
            {
                Patch(sourceFile, Path.Combine(gameDir, "NASCARHeat5_Data", "Managed"), tmp);
                string patched = FileStore.Put(tmp, cache, cancel);
                map = new Dictionary<string, string>(map, StringComparer.OrdinalIgnoreCase) { [underSha] = patched };
                Directory.CreateDirectory(ToolkitConfig.Dir);
                File.WriteAllText(CachePath, JsonSerializer.Serialize(map, new JsonSerializerOptions { WriteIndented = true }));
                return patched;
            }
            finally
            {
                try { File.Delete(tmp); } catch { }
            }
        }

        /// <summary>Writes a copy of <paramref name="dll"/> with GetBaseURL pointed at the community server.</summary>
        public static void Patch(string dll, string managedDir, string output)
        {
            var resolver = new DefaultAssemblyResolver();
            resolver.AddSearchDirectory(managedDir);
            resolver.AddSearchDirectory(Path.GetDirectoryName(dll)!);
            using var asm = AssemblyDefinition.ReadAssembly(dll, new ReaderParameters { AssemblyResolver = resolver, InMemory = true });
            var module = asm.MainModule;
            var getBaseUrl = module.Types.FirstOrDefault(t => t.Name == "NGUtil")?.Methods
                .FirstOrDefault(m => m.Name == "GetBaseURL" && m.Parameters.Count == 0 && m.HasBody)
                ?? throw new InvalidOperationException("NGUtil.GetBaseURL not found in Assembly-CSharp.dll.");

            var strings = getBaseUrl.Body.Instructions.Where(i => i.OpCode == OpCodes.Ldstr && i.Operand is string).ToList();
            if (strings.Any(i => Is(i, Stock2020) || Is(i, Stock2022)))
            {
                // path A: in place, keeps everything else another mod may have changed in the method
                foreach (var i in strings)
                {
                    if (Is(i, Stock2020)) i.Operand = Server2020;
                    else if (Is(i, Stock2022)) i.Operand = Server2022;
                }
            }
            else if (!(strings.Any(i => Is(i, Server2020)) && strings.Any(i => Is(i, Server2022))))
            {
                // path B: the old manager's rewrite (Left Shift = its LAN test server, then 2022 / 2020 by game year)
                MethodReference? Find(Func<MethodReference, bool> match)
                {
                    foreach (var t in module.GetTypes())
                        foreach (var m in t.Methods.Where(m => m.HasBody))
                            foreach (var i in m.Body.Instructions)
                                if (i.Operand is MethodReference r && match(r)) return r;
                    return null;
                }
                var global = Find(r => r.Name == "Global" && r.DeclaringType.Name == "InputSys");
                var debugKey = Find(r => r.Name == "Debug_GetKeyDown");
                var instance = Find(r => r.Name == "get_Instance" && r.DeclaringType.FullName.Contains("LazySingleton") && r.DeclaringType.FullName.Contains("GameYearManager"));
                var future = Find(r => r.Name == "IsFutureYearActive" && r.DeclaringType.Name == "GameYearManager");
                if (global is null || debugKey is null || instance is null || future is null)
                    throw new InvalidOperationException("This Assembly-CSharp.dll can't be patched for multiplayer (GetBaseURL has no stock server and the helper calls weren't found).");
                var body = getBaseUrl.Body;
                body.ExceptionHandlers.Clear();
                body.Variables.Clear();
                body.Instructions.Clear();
                body.InitLocals = false;
                body.MaxStackSize = 8;
                var il = body.GetILProcessor();
                var getInstance = il.Create(OpCodes.Call, instance);
                var n2020 = il.Create(OpCodes.Ldstr, Server2020);
                var ret = il.Create(OpCodes.Ret);
                il.Emit(OpCodes.Call, global);
                il.Emit(OpCodes.Ldc_I4_S, (sbyte)42);   // LEFT_SHIFT
                il.Emit(OpCodes.Callvirt, debugKey);
                il.Emit(OpCodes.Brfalse, getInstance);
                il.Emit(OpCodes.Ldstr, "http://192.168.1.132:8000/");
                il.Emit(OpCodes.Ret);
                il.Append(getInstance);
                il.Emit(OpCodes.Callvirt, future);
                il.Emit(OpCodes.Brfalse, n2020);
                il.Emit(OpCodes.Ldstr, Server2022);
                il.Emit(OpCodes.Br, ret);
                il.Append(n2020);
                il.Append(ret);
            }
            else
            {
                // already pointing at the community server: the file as it is (a Cecil rewrite would change its bytes and
                // make Deploy replace the dll with an identical-acting copy)
                File.Copy(dll, output, overwrite: true);
                return;
            }
            asm.Write(output);
        }

        private static bool Is(Instruction i, string url) => string.Equals(i.Operand as string, url, StringComparison.OrdinalIgnoreCase);
    }
}
