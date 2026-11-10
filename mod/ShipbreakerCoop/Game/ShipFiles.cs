using System;
using System.IO;
using System.Threading.Tasks;
using BBI.Unity.Game;
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
            if (old != null) await old.DespawnModuleAsync(true, null, null);
            var preview = await ShipPreview.CreateFromFileAsync(path, false);
            if (preview == null) { Plugin.Log.LogError("ship file did not load: " + path); return null; }
            RigidTransform at = ms.CurrentSpawnPoint != null ? ms.CurrentSpawnPoint.ToRigidTransform() : RigidTransform.identity;
            bool ok = await preview.SpawnModuleFromSaveDataAsync(at, null, null, false);
            Plugin.Log.LogInfo($"ship loaded: ok={ok} '{preview.ShipName}' seed {preview.Seed} in " +
                               $"{(DateTime.UtcNow - t0).TotalMilliseconds:F0} ms; current is new: {ms.CurrentShipPreview == preview}; {Fingerprint()}");
            return ok ? preview : null;
        }

        /// <summary>Parts with a ShipMemberID (a seed-derived hash chain, decomp ShipMemberID.cs), skipping
        /// entities queued for deletion (as the game's own save does): count + an order-independent hash.
        /// Same file loaded on two machines => same fingerprint (lab, 2026-11-10).</summary>
        public static string Fingerprint()
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
            {
                ulong sum = 0, xor = 0;
                for (int i = 0; i < ids.Length; i++)
                {
                    ulong v = Mix(ids[i].Value.Value);
                    sum += v; xor ^= v;
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
