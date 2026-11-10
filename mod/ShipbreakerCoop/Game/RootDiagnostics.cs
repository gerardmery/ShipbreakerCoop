using System;
using BBI.Unity.Game;
using HarmonyLib;
using Unity.Entities;
using UnityEngine;

namespace ShipbreakerCoop.Game
{
    /// <summary>
    /// Diagnostics for the client losing hierarchy roots (physics bodies) after loading the host's ship
    /// (docs/NOTES.md run 4). A root reattaches to its saved entity only while ModuleService.ShipSpawningFromSaveInProgress
    /// is true and ShipMemberID(graphKey) is in CurrentShipPreview.ShipMemberIDToLoadedEntitiesMap
    /// (decomp HierarchyParent.cs:172-220). Logs, per root created while Active: key, flag, map, matched.
    /// </summary>
    internal static class RootDiagnostics
    {
        public static bool Active;

        public static void Apply(Harmony h)
        {
            var m = AccessTools.Method(typeof(HierarchyParent), "InstantiatePrefabInternal", new[] { typeof(GameObject), typeof(int) });
            if (m == null) { Plugin.Log.LogWarning("RootDiagnostics: InstantiatePrefabInternal not found"); return; }
            h.Patch(m, prefix: new HarmonyMethod(typeof(RootDiagnostics), nameof(Prefix)),
                       postfix: new HarmonyMethod(typeof(RootDiagnostics), nameof(Postfix)));
        }

        static void Prefix(int graphKey, out string __state)
        {
            __state = null;
            if (!Active) return;
            try
            {
                var ms = ModuleService.Instance;
                var prev = ms?.CurrentShipPreview;
                bool fromSave = ms != null && ms.ShipSpawningFromSaveInProgress;
                string map = "no-preview";
                if (prev != null)
                {
                    var hm = prev.ShipMemberIDToLoadedEntitiesMap;
                    map = !hm.IsCreated ? "map-not-created"
                        : hm.ContainsKey(new ShipMemberID((ulong)(long)graphKey)) ? "key-in-map" : $"key-NOT-in-map({hm.Count()} entries)";
                }
                __state = $"fromSave={fromSave} {map} preview='{prev?.ShipName}'";
            }
            catch (Exception e) { __state = "prefix-error " + e.GetType().Name; }
        }

        static void Postfix(HierarchyParent __instance, int graphKey, string __state)
        {
            if (!Active || __state == null) return;
            try
            {
                var bp = __instance.EntityBlueprint;
                bool fresh = bp != null && Traverse.Create(bp).Field("bNoSavedEntityConfirmed").GetValue<bool>();
                string id = "no-entity";
                if (bp != null && bp.EntityManager.Exists(bp.Entity))
                    id = bp.EntityManager.HasComponent<ShipMemberID>(bp.Entity)
                        ? bp.EntityManager.GetComponentData<ShipMemberID>(bp.Entity).Value.Value.ToString("x") : "no-ShipMemberID";
                ShipFiles.Trace($"  root key {graphKey} (0x{graphKey:x}): {__state} -> {(fresh ? "FRESH (saved root not matched)" : "matched")} id={id}");
            }
            catch (Exception e) { ShipFiles.Trace($"  root key {graphKey}: postfix-error {e.GetType().Name}"); }
        }

        /// <summary>Graph counter and active keys, for before/after a load.</summary>
        public static string GraphState()
        {
            try
            {
                var gs = World.DefaultGameObjectInjectionWorld.GetOrCreateSystem<StructureGraphSystem>();
                var keys = gs.ActiveGraphKeys;
                int min = int.MaxValue, max = int.MinValue;
                for (int i = 0; i < keys.Length; i++) { min = Math.Min(min, keys[i]); max = Math.Max(max, keys[i]); }
                return $"graph: next key {gs.NextAvailableGraphKey}, {keys.Length} active" + (keys.Length > 0 ? $" ({min}..{max})" : "");
            }
            catch (Exception e) { return "graph: error " + e.GetType().Name; }
        }
    }
}
