using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using ShipbreakerCoop.Core;
using UnityEngine;

namespace ShipbreakerCoop.Game
{
    /// <summary>
    /// Milestone 1, ghost buddy: every player sees the others as a puppet suit.
    /// F8 host, F9 join (udp: the configured address; steam: use a Steam invite / Join Game),
    /// Shift+F8 Steam invite overlay, F10 leave.
    /// </summary>
    [BepInPlugin(Guid, "Shipbreaker Co-op", Version)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "shipbreakercoop";
        public const string Version = "0.1.1";
        public const float SendRate = 20f;

        internal static ManualLogSource Log;

        CoopOptions mOpt;
        Harmony mHarmony;
        CoopSession mSession;
        SteamLobby mLobby;
        readonly LocalPlayerSource mLocal = new LocalPlayerSource();
        readonly PuppetManager mPuppets = new PuppetManager();
        readonly Stopwatch mClock = Stopwatch.StartNew();
        double mNextSend, mNextStatus;
        string mPending;       // "host" or "join:<target>" waiting for Steam to come up
        string mLastError = "";
        GUIStyle mLabel, mBox;

        double Now => mClock.Elapsed.TotalSeconds;

        void Awake()
        {
            Log = Logger;
            mOpt = CoopOptions.Load(Config, Environment.GetCommandLineArgs());
            mHarmony = new Harmony(Guid);
            if (!string.IsNullOrEmpty(mOpt.SaveDir)) SaveRedirect.Apply(mOpt.SaveDir, mHarmony);
            mLocal.Fake = mOpt.Fake;
            Application.runInBackground = true;
            if (mOpt.AutoHost) mPending = "host";
            else if (!string.IsNullOrEmpty(mOpt.Join)) mPending = "join:" + mOpt.Join;
            Log.LogInfo($"Shipbreaker Co-op {Version} loaded (transport {mOpt.Transport}" +
                        (mPending != null ? $", pending {mPending}" : "") + ")");
        }

        // ------------------------------------------------------------------ start / stop

        /// <summary>The game's SteamManager.Initialized getter creates a SteamManager if none exists,
        /// so read its fields instead of poking it before the game made it.</summary>
        static bool SteamReady()
        {
            var t = typeof(BBI.Unity.Game.SteamManager);
            var inst = AccessTools.Field(t, "s_instance").GetValue(null);
            return inst != null && (bool)AccessTools.Field(t, "m_bInitialized").GetValue(inst);
        }

        string LocalName()
        {
            if (!string.IsNullOrEmpty(mOpt.Name)) return mOpt.Name;
            if (mOpt.Transport == "steam" && SteamReady()) return SteamLobby.MyName;
            return "Player";
        }

        void TryStartPending()
        {
            if (mPending == null || mSession != null) return;
            if (mOpt.Transport == "steam" && !SteamReady()) return; // wait for the game to init Steam
            var p = mPending; mPending = null;
            try
            {
                if (p == "host") StartHost();
                else StartJoin(p.Substring(5));
            }
            catch (Exception e) { Fail("could not start: " + e.Message); Log.LogError(e); }
        }

        void StartHost()
        {
            Stop();
            if (mOpt.Transport == "udp")
            {
                var t = new UdpTransport(IPAddress.Any, mOpt.UdpPort);
                NewSession(t).StartHost();
            }
            else
            {
                var t = new SteamTransport();
                mLobby = new SteamLobby(Version);
                t.AcceptFilter = id => mLobby.IsMember(id);
                var s = NewSession(t);
                s.AdmitFilter = id => mLobby != null && mLobby.IsMember(id);
                mLobby.Hosted += () => s.StartHost();
                mLobby.JoinRequested += id => { mPending = "join:" + id; Stop(); };
                mLobby.Host();
            }
        }

        void StartJoin(string target)
        {
            Stop();
            if (mOpt.Transport == "udp")
            {
                var t = new UdpTransport(IPAddress.Any, 0);
                NewSession(t).Connect(UdpTransport.Parse(target, mOpt.UdpPort));
            }
            else
            {
                if (!ulong.TryParse(target, out var lobbyId)) { Fail($"'{target}' is not a Steam lobby id"); return; }
                var t = new SteamTransport();
                mLobby = new SteamLobby(Version);
                ulong host = 0;
                t.AcceptFilter = id => id == host;
                var s = NewSession(t);
                mLobby.JoinedHost += owner => { host = owner; s.Connect(owner); };
                mLobby.HostLeft += () => s.EndAsClient("Host left the session");
                mLobby.JoinRequested += id => { mPending = "join:" + id; Stop(); };
                mLobby.Join(lobbyId);
            }
        }

        CoopSession NewSession(ITransport t)
        {
            mSession = new CoopSession(t, () => Now, LocalName(), Version);
            mSession.Log += m => Log.LogInfo(m);
            mLastError = "";
            return mSession;
        }

        void Stop()
        {
            if (mSession != null) { mSession.Dispose(); mSession = null; }
            if (mLobby != null) { mLobby.Dispose(); mLobby = null; }
            mPuppets.Clear();
        }

        void Fail(string msg) { mLastError = msg; Log.LogError(msg); }

        // ------------------------------------------------------------------ frame

        void Update()
        {
            if (!Application.runInBackground) Application.runInBackground = true;
            HandleKeys();
            TryStartPending();
            if (mSession == null) return;

            try { mSession.Tick(); }
            catch (Exception e) { Log.LogError("session tick: " + e); }
            if (mSession.Role == Role.Off && mSession.LastError != null) { Fail(mSession.LastError); Stop(); return; }

            double now = Now;
            if (now >= mNextSend)
            {
                mNextSend = now + 1.0 / SendRate;
                mSession.SendLocalState(mLocal.Capture());
            }
            mPuppets.Update(mSession, now, mLocal.Scene);

            if (mOpt.StatusFile != null && now >= mNextStatus) { mNextStatus = now + 0.5; WriteStatus(); LabScreenshot(now); }
        }

        double mPuppetSeenSince = -1;
        bool mShotTaken;

        /// <summary>Lab evidence: one in-game screenshot after a puppet has been visible for 5 s.</summary>
        void LabScreenshot(double now)
        {
            if (mShotTaken) return;
            bool any = false;
            foreach (var p in mPuppets.All) any |= p.Visible;
            if (!any) { mPuppetSeenSince = -1; return; }
            if (mPuppetSeenSince < 0) mPuppetSeenSince = now;
            if (now - mPuppetSeenSince < 5) return;
            var path = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(mOpt.StatusFile)), "coop_shot.png");
            ScreenCapture.CaptureScreenshot(path);
            mShotTaken = true;
            Log.LogInfo("lab screenshot: " + path);
        }

        void HandleKeys()
        {
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (Input.GetKeyDown(KeyCode.F8))
            {
                if (shift) mLobby?.OpenInviteOverlay();
                else { Stop(); mPending = "host"; }
            }
            if (Input.GetKeyDown(KeyCode.F9))
            {
                if (mOpt.Transport == "udp") { Stop(); mPending = "join:" + mOpt.UdpJoin; }
                else mLastError = "Steam: join through a friend's invite or Join Game in the friends list";
            }
            if (Input.GetKeyDown(KeyCode.F10)) { Stop(); mPending = null; Log.LogInfo("left the session"); }
        }

        void OnApplicationQuit() => Stop();
        void OnDestroy() => Stop();

        // ------------------------------------------------------------------ HUD

        void OnGUI()
        {
            if (mLabel == null)
            {
                mLabel = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                mLabel.normal.textColor = Color.white;
                mBox = new GUIStyle(GUI.skin.box) { fontSize = 13, alignment = TextAnchor.UpperLeft, wordWrap = false };
                mBox.normal.textColor = Color.white;
            }
            if (mSession != null) mPuppets.DrawLabels(mLabel);
            if (!mOpt.ShowHud) return;
            var text = HudText();
            var size = mBox.CalcSize(new GUIContent(text));
            GUI.Box(new Rect(8, 8, size.x + 12, size.y + 6), text, mBox);
        }

        string HudText()
        {
            var sb = new StringBuilder();
            if (mSession == null)
            {
                sb.Append(mPending != null ? $"Co-op: waiting to {mPending.Split(':')[0]} ({mOpt.Transport})" : "Co-op: off   F8 host   F9 join");
            }
            else
            {
                var s = mSession;
                string role = s.Role == Role.Host ? "hosting" : s.Connected ? $"player {s.LocalId}" : "connecting";
                sb.Append($"Co-op {role} ({s.Transport.Name})   players {s.PlayerCount}");
                if (s.RttSeconds >= 0 && s.Role == Role.Client) sb.Append($"   ping {s.RttSeconds * 1000:F0} ms");
                if (mLobby != null && mLobby.Status.Length > 0) sb.Append($"\n{mLobby.Status}" + (mLobby.IsHost ? "   Shift+F8 invite" : ""));
                foreach (var p in mPuppets.All)
                    sb.Append($"\n  {p.Name}: {(p.Visible ? "here" : "elsewhere")}");
                sb.Append("\nF10 leave");
            }
            if (mLastError.Length > 0) sb.Append("\n" + mLastError);
            return sb.ToString();
        }

        // ------------------------------------------------------------------ lab status file

        /// <summary>JSON for automated tests (tools/lab). Written to a temp file then moved, so readers never see half a file.</summary>
        void WriteStatus()
        {
            try
            {
                var c = CultureInfo.InvariantCulture;
                string F(float f) => f.ToString("R", c);
                string V(Vector3 v) => $"[{F(v.x)},{F(v.y)},{F(v.z)}]";
                string Esc(string s) => (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
                var local = mLocal.Capture();
                var sb = new StringBuilder();
                sb.Append('{');
                sb.Append($"\"t\":{Now.ToString("R", c)},\"version\":\"{Version}\",\"pid\":{Process.GetCurrentProcess().Id},");
                sb.Append($"\"role\":\"{mSession.Role}\",\"connected\":{(mSession.Connected ? "true" : "false")},\"localId\":{mSession.LocalId},");
                sb.Append($"\"players\":{mSession.PlayerCount},\"rtt\":{mSession.RttSeconds.ToString("R", c)},");
                sb.Append($"\"badPackets\":{mSession.BadPackets},\"fps\":{F(1f / Mathf.Max(Time.unscaledDeltaTime, 1e-4f))},");
                sb.Append($"\"local\":{{\"scene\":\"{Esc(local.Scene)}\",\"hasPlayer\":{(local.HasPlayer ? "true" : "false")},\"pos\":{V(local.Pos.ToUnity())}}},");
                sb.Append("\"remotes\":[");
                bool first = true;
                foreach (var kv in mSession.Remotes)
                {
                    var rp = kv.Value;
                    Puppet pup = null;
                    foreach (var p in mPuppets.All) if (p.Id == rp.Id) pup = p;
                    if (!first) sb.Append(','); first = false;
                    sb.Append($"{{\"id\":{rp.Id},\"name\":\"{Esc(rp.Name)}\",\"states\":{rp.StatesReceived},");
                    sb.Append($"\"visible\":{(pup != null && pup.Visible ? "true" : "false")},");
                    sb.Append($"\"pos\":{(pup != null ? V(pup.Position) : "null")}}}");
                }
                sb.Append("]}");
                var tmp = mOpt.StatusFile + ".tmp";
                File.WriteAllText(tmp, sb.ToString());
                if (File.Exists(mOpt.StatusFile)) File.Delete(mOpt.StatusFile);
                File.Move(tmp, mOpt.StatusFile);
            }
            catch (Exception e) { Log.LogWarning("status file: " + e.Message); }
        }
    }
}
