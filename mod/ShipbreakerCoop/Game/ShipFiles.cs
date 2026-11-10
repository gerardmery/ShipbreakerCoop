using System;
using System.IO;
using System.Threading.Tasks;
using BBI.Unity.Game;
using HarmonyLib;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace ShipbreakerCoop.Game
{
    /// <summary>
    /// The game's own ship file round trip (decomp: DebugSaveLoadService, ShipPreview.Write/Read):
    /// ShipPreview.WriteToFileAsync writes the generated data (seed, assets, the serialized ModuleGroup) plus an
    /// ECS snapshot of every LynxSaveable entity (cuts, meshes, machine states). Loading = despawn the current
    /// ship, ShipPreview.CreateFromFileAsync, SpawnModuleFromSaveDataAsync at the bay spawn point.
    /// The spawn raises ShipAllSpawnWrapperEvent(SpawnFromSave), which makes ModuleService track the new ship.
    /// </summary>
    internal static class ShipFiles
    {
        public static ShipPreview Current => ModuleService.Instance?.CurrentShipPreview;

        /// <summary>One flushed line per step in Coop/sync_trace.txt: if the game hangs, the last line names the
        /// step (the BepInEx log is buffered and lost lines in a hang, lab 2026-11-10).</summary>
        public static void Trace(string step)
        {
            try
            {
                var dir = Path.Combine(PlatformSettings.SaveDataPath, "Coop");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "sync_trace.txt"), $"{DateTime.Now:HH:mm:ss.fff} {step}" + Environment.NewLine);
            }
            catch { }
        }

        /// <summary>In the bay with a spawn point registered (where a ship can be loaded).</summary>
        public static bool InBay => ModuleService.Instance != null && ModuleService.Instance.CurrentSpawnPoint != null;

        public static async Task<bool> SaveCurrentAsync(string path)
        {
            var ship = Current;
            if (ship == null) { Plugin.Log.LogWarning("no current ship (not in the bay?)"); return false; }
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var t0 = DateTime.UtcNow;
            bool ok = await ship.WriteToFileAsync(path);
            Plugin.Log.LogInfo($"ship saved: ok={ok} '{ship.ShipName}' seed {ship.Seed} " +
                               $"{new FileInfo(path).Length / 1024.0:F0} KB in {(DateTime.UtcNow - t0).TotalMilliseconds:F0} ms; {Fingerprint()}");
            return ok;
        }

        /// <summary>Despawns the current ship and spawns the one in the file. Returns the new ship, or null.</summary>
        public static async Task<ShipPreview> LoadIntoBayAsync(string path)
        {
            var ms = ModuleService.Instance;
            if (ms == null) { Plugin.Log.LogWarning("no ModuleService (not in the bay?)"); return null; }
            var t0 = DateTime.UtcNow;
            var old = ms.CurrentShipPreview;
            Trace($"load {Path.GetFileName(path)}: despawn '{old?.ShipName}' start; {RootDiagnostics.GraphState()}");
            RootDiagnostics.Active = true;
            if (old != null) await old.DespawnModuleAsync(true, null, null);
            Trace("despawn returned; waiting for the old ship to be gone");
            await WaitForOldShipGone();
            Trace("old ship gone; reading file");
            var preview = await ShipPreview.CreateFromFileAsync(path, false);
            Trace($"file read: {(preview != null ? preview.ShipName : "FAILED")}");
            if (preview == null) { Plugin.Log.LogError("ship file did not load: " + path); return null; }
            RigidTransform at = ms.CurrentSpawnPoint != null ? ms.CurrentSpawnPoint.ToRigidTransform() : RigidTransform.identity;
            Trace("spawn start");
            bool ok = await preview.SpawnModuleFromSaveDataAsync(at, null, null, false);
            Trace($"spawn returned ok={ok}; {RootDiagnostics.GraphState()}");
            Plugin.Log.LogInfo($"ship loaded: ok={ok} '{preview.ShipName}' seed {preview.Seed} in " +
                               $"{(DateTime.UtcNow - t0).TotalMilliseconds:F0} ms; current is new: {ms.CurrentShipPreview == preview}; {Fingerprint()}");
            return ok ? preview : null;
        }

        /// <summary>
        /// DespawnModuleAsync returns before the ECS has finished removing the old ship. Spawning the new one on top
        /// of a different, half-removed ship left the client with 153 of 859 parts (lab 2026-11-10). Wait until no
        /// ShipManifest and nothing PendingDestruction is left (max 10 s), and log what we waited for.
        /// </summary>
        static async Task WaitForOldShipGone()
        {
            var em = World.DefaultGameObjectInjectionWorld.EntityManager;
            var t0 = DateTime.UtcNow;
            int manifests = 0, pending = 0, frames = 0;
            var parents = new System.Collections.Generic.List<int>();
            var stale = new System.Collections.Generic.List<int>();
            int cleanups = 0;
            using (var qm = em.CreateEntityQuery(ComponentType.ReadOnly<ShipManifest>()))
            using (var qp = em.CreateEntityQuery(ComponentType.ReadOnly<PendingDestruction>()))
            {
                while ((DateTime.UtcNow - t0).TotalSeconds < 10)
                {
                    manifests = qm.CalculateEntityCount();
                    pending = qp.CalculateEntityCount();
                    parents = OldHierarchyParents();
                    stale = StaleParents(parents);
                    if (manifests == 0 && pending == 0 && stale.Count == 0) break;
                    // The game only runs CleanUpOldParents at the end of UpdateHierarchyParents and skips it while the
                    // graph has queued changes; after a despawn the leftovers stayed for 10+ s (lab run 6). Run it.
                    if (manifests == 0 && pending == 0 && stale.Count > 0 && !GraphHasQueuedChanges())
                    {
                        cleanups++;
                        AccessTools.Method(typeof(HierarchyProcessor), "CleanUpOldParents")?.Invoke(HierarchyProcessor.Instance, null);
                    }
                    await Task.Yield(); frames++;
                }
            }
            string left = parents.Count == 0 ? "none" : string.Join(",", parents);
            var msg = $"old ship gone after {(DateTime.UtcNow - t0).TotalMilliseconds:F0} ms / {frames} waits, {cleanups} cleanup calls " +
                      $"(manifests left {manifests}, pending destruction {pending}, hierarchy parents left: {left}; " +
                      $"stale: {(stale.Count == 0 ? "none" : string.Join(",", stale))}; graph queued changes: {GraphHasQueuedChanges()})";
            Plugin.Log.LogInfo(msg);
            Trace(msg);
        }

        /// <summary>
        /// Graph keys HierarchyProcessor still holds a HierarchyParent (physics body) for. Leftovers from the old ship
        /// are only removed by CleanUpOldParents once the structure graph has no queued changes
        /// (HierarchyProcessor.cs:697). Loading before that reused a leftover body for every key the old and new
        /// ship share, so the saved bodies were never created (lab run 5: client skipped keys 84-98).
        /// </summary>
        /// <summary>Parent keys that are no longer active graph keys: what CleanUpOldParents would remove.</summary>
        static System.Collections.Generic.List<int> StaleParents(System.Collections.Generic.List<int> parents)
        {
            var stale = new System.Collections.Generic.List<int>();
            var gs = World.DefaultGameObjectInjectionWorld.GetOrCreateSystem<StructureGraphSystem>();
            var active = gs.ActiveGraphKeys;
            foreach (var k in parents) if (!active.Contains(k)) stale.Add(k);
            return stale;
        }

        static bool GraphHasQueuedChanges()
        {
            var gs = World.DefaultGameObjectInjectionWorld.GetOrCreateSystem<StructureGraphSystem>();
            return gs.HasQueuedChanges;
        }

        static System.Collections.Generic.List<int> OldHierarchyParents()
        {
            var keys = new System.Collections.Generic.List<int>();
            var hp = HierarchyProcessor.Instance;
            if (hp == null) return keys;
            var dict = HarmonyLib.Traverse.Create(hp).Field("mHierarchyParents").GetValue<System.Collections.IDictionary>();
            if (dict != null) foreach (var k in dict.Keys) keys.Add((int)k);
            keys.Sort();
            return keys;
        }

        /// <summary>Parts with a ShipMemberID (a seed-derived hash chain, decomp ShipMemberID.cs), skipping
        /// entities queued for deletion (as the game's own save does): count + an order-independent hash.
        /// Same file loaded on two machines => same fingerprint (lab, 2026-11-10).</summary>
        public static string Fingerprint() => Fingerprint(null);

        /// <summary>Same as Fingerprint(), and writes every ShipMemberID (sorted, hex) to dumpPath when given,
        /// so two machines' part lists can be diffed.</summary>
        public static string Fingerprint(string dumpPath)
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null) return "no-world";
            var em = world.EntityManager;
            var desc = new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<ShipMemberID>() },
                None = new[] { ComponentType.ReadOnly<CleanupEntity>(), ComponentType.ReadOnly<PendingDestruction>() },
            };
            using (var q = em.CreateEntityQuery(desc))
            using (var ids = q.ToComponentDataArray<ShipMemberID>(Allocator.TempJob))
            using (var ents = q.ToEntityArray(Allocator.TempJob))
            {
                ulong sum = 0, xor = 0;
                var raw = dumpPath != null ? new ulong[ids.Length] : null;
                for (int i = 0; i < ids.Length; i++)
                {
                    ulong v = Mix(ids[i].Value.Value);
                    sum += v; xor ^= v;
                    if (raw != null) raw[i] = ids[i].Value.Value;
                }
                if (raw != null)
                {
                    // id <TAB> component types, so a diff shows what kind of object each missing id is
                    var lines = new string[raw.Length];
                    for (int i = 0; i < raw.Length; i++)
                    {
                        string types;
                        try
                        {
                            using (var ct = em.GetComponentTypes(ents[i], Allocator.Temp))
                            {
                                var names = new System.Collections.Generic.List<string>();
                                for (int k = 0; k < ct.Length; k++) names.Add(ct[k].GetManagedType()?.Name ?? "?");
                                names.Sort();
                                types = string.Join(",", names);
                            }
                        }
                        catch (Exception e) { types = "error:" + e.GetType().Name; }
                        lines[i] = raw[i].ToString("x16") + "	" + types;
                    }
                    Array.Sort(lines, StringComparer.Ordinal);
                    Directory.CreateDirectory(Path.GetDirectoryName(dumpPath));
                    File.WriteAllLines(dumpPath, lines);
                }
                return $"{ids.Length}:{sum ^ (xor * 31):x16}";
            }
        }

        static ulong Mix(ulong z)
        {
            z = (z ^ (z >> 30)) * 0xbf58476d1ce4e5b9UL;
            z = (z ^ (z >> 27)) * 0x94d049bb133111ebUL;
            return z ^ (z >> 31);
        }
    }
}
