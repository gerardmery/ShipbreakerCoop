using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using BBI.Unity.Game;
using HarmonyLib;
using ShipbreakerCoop.Core;

namespace ShipbreakerCoop.Game
{
    /// <summary>
    /// Milestone 2, step 1: everyone breaks the same ship.
    /// Host (F6): save the current ship to a file, send the file to every client (blob), then load that same file
    /// itself, because a live ship and the same ship freshly loaded differ in their ID'd entities (lab probe).
    /// Client: write the file, load it as soon as it is in the bay. Both report a fingerprint of their parts;
    /// matching fingerprints = identical starting state.
    /// The client never saves the host's ship as its own "ship in progress" (see SavePatch).
    /// </summary>
    internal sealed class ShipSync
    {
        public const byte BlobKindShip = 1;
        public const byte ReportShipFingerprint = 1;

        public enum State { Idle, Saving, Sending, Loading, WaitingForBay, Loaded, Failed }

        public State Current { get; private set; } = State.Idle;
        public uint SyncId { get; private set; }
        public string LocalFingerprint { get; private set; } = "";
        public string Detail { get; private set; } = "";
        public readonly Dictionary<byte, string> PeerFingerprints = new Dictionary<byte, string>(); // player id -> "syncId:fp"
        bool mBusy;
        string mPendingPath;

        /// <summary>The ship this client loaded from the host. Never saved as the client's own.</summary>
        static ShipPreview sForeignShip;
        public static bool IsForeign(ShipPreview p) => p != null && ReferenceEquals(p, sForeignShip);

        static string Dir => Path.Combine(PlatformSettings.SaveDataPath, "Coop");

        // ------------------------------------------------------------------ host

        public async void HostSync(CoopSession s)
        {
            if (mBusy || s == null || s.Role != Role.Host) return;
            if (!ShipFiles.InBay || ShipFiles.Current == null) { Fail("host must be in the bay with a ship"); return; }
            mBusy = true;
            try
            {
                PeerFingerprints.Clear();
                Current = State.Saving; Detail = "saving";
                var path = Path.Combine(Dir, "sync_out.ship");
                if (!await ShipFiles.SaveCurrentAsync(path)) { Fail("save failed"); return; }
                var bytes = File.ReadAllBytes(path);
                SyncId = s.SendBlobToClients(BlobKindShip, bytes);
                Current = State.Loading; Detail = $"sending {bytes.Length / 1024} KB, reloading own copy";
                var ship = await ShipFiles.LoadIntoBayAsync(path);
                if (ship == null) { Fail("reloading own ship failed"); return; }
                Finish(s, "host");
            }
            catch (Exception e) { Fail(e.GetType().Name); Plugin.Log.LogError(e); }
            finally { mBusy = false; }
        }

        // ------------------------------------------------------------------ client

        public void OnBlob(byte kind, uint id, byte[] data)
        {
            if (kind != BlobKindShip) return;
            try
            {
                Directory.CreateDirectory(Dir);
                mPendingPath = Path.Combine(Dir, $"sync_in_{id}.ship");
                File.WriteAllBytes(mPendingPath, data);
                SyncId = id;
                // Don't clear PeerFingerprints: the host's report for this sync can arrive before the file is
                // complete. InSync() only compares reports carrying the current sync id.
                Current = State.WaitingForBay; Detail = $"got host ship ({data.Length / 1024} KB)";
                Plugin.Log.LogInfo($"host ship {id} received, {data.Length} bytes");
            }
            catch (Exception e) { Fail("could not store ship: " + e.Message); }
        }

        public void Update(CoopSession s)
        {
            if (mPendingPath == null || mBusy || s == null || s.Role != Role.Client) return;
            if (!ShipFiles.InBay) { Detail = "host ship ready: enter the bay to load it"; return; }
            LoadPending(s);
        }

        async void LoadPending(CoopSession s)
        {
            mBusy = true;
            var path = mPendingPath; mPendingPath = null;
            try
            {
                Current = State.Loading; Detail = "loading host ship";
                var ship = await ShipFiles.LoadIntoBayAsync(path);
                if (ship == null) { Fail("loading the host ship failed"); return; }
                sForeignShip = ship;
                Finish(s, "client");
            }
            catch (Exception e) { Fail(e.GetType().Name); Plugin.Log.LogError(e); }
            finally { mBusy = false; }
        }

        // ------------------------------------------------------------------ both

        void Finish(CoopSession s, string who)
        {
            LocalFingerprint = ShipFiles.Fingerprint();
            Current = State.Loaded; Detail = "";
            s.SendReport(ReportShipFingerprint, $"{SyncId}:{LocalFingerprint}");
            Plugin.Log.LogInfo($"ship sync {SyncId} loaded on {who}: {LocalFingerprint}");
        }

        public void OnReport(byte from, byte kind, string text)
        {
            if (kind != ReportShipFingerprint) return;
            PeerFingerprints[from] = text;
            Plugin.Log.LogInfo($"player {from} ship fingerprint {text}");
        }

        /// <summary>null = not decided yet, true = every reported fingerprint for this sync matches ours.</summary>
        public bool? InSync(int expectedPeers)
        {
            if (Current != State.Loaded) return null;
            string mine = $"{SyncId}:{LocalFingerprint}";
            int n = 0;
            foreach (var kv in PeerFingerprints)
            {
                if (!kv.Value.StartsWith(SyncId + ":")) continue;
                if (kv.Value != mine) return false;
                n++;
            }
            return n >= expectedPeers ? true : (bool?)null;
        }

        void Fail(string why)
        {
            Current = State.Failed; Detail = why;
            Plugin.Log.LogError("ship sync: " + why);
        }

        public void Reset() { Current = State.Idle; Detail = ""; mPendingPath = null; PeerFingerprints.Clear(); }

        // ------------------------------------------------------------------ save protection

        /// <summary>
        /// The game saves ModuleService.CurrentShipPreview as the profile's ship in progress (and to the cloud)
        /// when leaving the bay or quitting a career session (decomp SceneLoader.PreTearDown, SYNC_LIST.md section 6).
        /// A client must not store the host's ship as its own.
        /// </summary>
        [HarmonyPatch(typeof(PlayerProfileService), nameof(PlayerProfileService.TrySavePlayerProfileShip))]
        internal static class SavePatch
        {
            static bool Prefix(ShipPreview shipToSave, ref Task<bool> __result)
            {
                if (!IsForeign(shipToSave)) return true;
                Plugin.Log.LogInfo("not saving the host's ship as this player's ship in progress");
                __result = Task.FromResult(false);
                return false;
            }
        }
    }
}
