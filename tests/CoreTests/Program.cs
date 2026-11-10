using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using ShipbreakerCoop.Core;

// Engine-free tests for the co-op core. Prints ALL OK and exits 0 only when every check passes.
static class Program
{
    static int sFails, sChecks;

    static void Check(bool ok, string what)
    {
        sChecks++;
        if (!ok) { sFails++; Console.WriteLine("  FAIL: " + what); }
    }

    static int Main()
    {
        Run("protocol round trip", ProtocolRoundTrip);
        Run("protocol rejects garbage", ProtocolGarbage);
        Run("interpolation under jitter/loss/reorder", Interpolation);
        Run("interpolation snaps on scene change", InterpolationSceneSnap);
        Run("session: 3 players over a lossy network", SessionLossy);
        Run("session: timeouts, bye, rejoin", SessionTimeouts);
        Run("session: version reject, spoof, strangers", SessionSecurity);
        Run("session: host leaves, admit filter", SessionHostLeaves);
        Run("session: real UDP on loopback", SessionUdp);
        Console.WriteLine(sFails == 0 ? $"ALL OK ({sChecks} checks)" : $"RESULT: FAIL ({sFails}/{sChecks} checks failed)");
        return sFails == 0 ? 0 : 1;
    }

    static void Run(string name, Action t)
    {
        int before = sFails;
        try { t(); }
        catch (Exception e) { sFails++; Console.WriteLine("  EXCEPTION: " + e); }
        Console.WriteLine((sFails == before ? "ok   " : "FAIL ") + name);
    }

    // ------------------------------------------------------------------ protocol

    static void ProtocolRoundTrip()
    {
        var s = new PlayerStateMsg
        {
            PlayerId = 3, Seq = 123456, SendTime = 98765.4321, Scene = "ShipbreakingBay_Ünïcode",
            Pos = new V3(1, -2, 3.5f), Rot = new Q4(0, 0.7071f, 0, 0.7071f), Look = Q4.Identity,
            Vel = new V3(-4, 5, 6), Flags = PlayerStateMsg.FlagHasPlayer,
        };
        var b = Protocol.PlayerState(s);
        Check(Protocol.TryReadHeader(b, b.Length, out var t, out var r) && t == MsgType.PlayerState, "state header");
        var s2 = Protocol.ReadPlayerState(r);
        Check(s2.PlayerId == 3 && s2.Seq == 123456 && s2.SendTime == s.SendTime && s2.Scene == s.Scene
              && s2.Pos.X == 1 && s2.Vel.Z == 6 && s2.Rot.Y == s.Rot.Y && s2.HasPlayer, "state fields");
        Check(b.Length < 120, $"state packet small ({b.Length} bytes)");

        b = Protocol.Hello(new string('x', 200), "0.1.0");
        Protocol.TryReadHeader(b, b.Length, out t, out r);
        Protocol.ReadHello(r, out var name, out var ver);
        Check(t == MsgType.Hello && name.Length == Protocol.MaxString && ver == "0.1.0", "hello truncates long names");

        b = Protocol.Roster(new[] { new RosterEntry { PlayerId = 1, Name = "A" }, new RosterEntry { PlayerId = 4, Name = "D" } });
        Protocol.TryReadHeader(b, b.Length, out t, out r);
        var ro = Protocol.ReadRoster(r);
        Check(ro.Count == 2 && ro[1].PlayerId == 4 && ro[1].Name == "D", "roster");
    }

    static void ProtocolGarbage()
    {
        var rng = new Random(1);
        var net = new FakeNet(rng);
        var host = new CoopSession(net.Endpoint(1), () => net.Now, "host", "v");
        host.StartHost();
        // Random bytes, truncations of valid packets, NaN poses: none may throw out of Tick.
        var valid = Protocol.PlayerState(new PlayerStateMsg { Scene = "s", Rot = Q4.Identity, Look = Q4.Identity });
        var nan = Protocol.PlayerState(new PlayerStateMsg { Scene = "s", Pos = new V3(float.NaN, 0, 0), Rot = Q4.Identity, Look = Q4.Identity });
        for (int i = 0; i < 2000; i++)
        {
            byte[] p;
            switch (i % 3)
            {
                case 0: p = new byte[rng.Next(0, 80)]; rng.NextBytes(p); break;
                case 1: p = valid.Take(rng.Next(0, valid.Length)).ToArray(); break;
                default: p = (byte[])nan.Clone(); break;
            }
            net.Inject(from: 99, to: 1, p);
        }
        bool threw = false;
        try { host.Tick(); } catch { threw = true; }
        Check(!threw, "garbage never throws out of Tick");
        Check(host.Remotes.Count == 0, "garbage creates no players");

        // A well-formed hello from another protocol (wrong magic) must not join.
        var foreign = Protocol.Hello("x", "v"); foreign[0] ^= 0xFF;
        net.Inject(from: 98, to: 1, foreign);
        host.Tick();
        Check(host.Remotes.Count == 0, "wrong magic ignored");
        var ok = Protocol.Hello("x", "v");
        net.Inject(from: 98, to: 1, ok);
        host.Tick();
        Check(host.Remotes.Count == 1, "same hello with right magic joins (control)");
    }

    // ------------------------------------------------------------------ interpolation

    static V3 TruePos(double t) => new V3((float)(3 * t), (float)Math.Sin(t), 0); // ~3 m/s

    static void Interpolation()
    {
        var rng = new Random(7);
        var buf = new SnapshotBuffer();
        const double remoteClockOffset = 5000; // remote clock is far ahead of ours
        double maxErr = 0, sumErr = 0; int n = 0, lost = 0;
        var inflight = new List<(double arrive, PlayerStateMsg s)>();
        uint seq = 0;
        for (double now = 0; now < 20; now += 1.0 / 60)
        {
            // sender at 20 Hz
            if ((int)(now * 60) % 3 == 0)
            {
                double sendRemote = now + remoteClockOffset;
                var p0 = TruePos(now); var p1 = TruePos(now + 0.001);
                var s = new PlayerStateMsg { Seq = ++seq, SendTime = sendRemote, Scene = "bay", Pos = p0,
                    Vel = (p1 - p0) * 1000f, Rot = Q4.Identity, Look = Q4.Identity, Flags = 1 };
                if (rng.NextDouble() < 0.15) lost++;
                else inflight.Add((now + 0.04 + rng.NextDouble() * 0.05, s)); // 40-90 ms latency, jitter reorders
            }
            foreach (var x in inflight.Where(x => x.arrive <= now).ToList()) { buf.Add(x.arrive, x.s); inflight.Remove(x); }
            if (now > 2 && buf.Sample(now, out var pose))
            {
                // What we should show: the sender's pose ~ (min latency + delay) ago.
                var expect = TruePos(now - 0.04 - buf.InterpDelay);
                double e = (pose.Pos - expect).Length;
                maxErr = Math.Max(maxErr, e); sumErr += e; n++;
            }
        }
        double mean = sumErr / n;
        Console.WriteLine($"      mean err {mean:F3} m, max {maxErr:F3} m, lost {lost} packets, offset {buf.ClockOffset:F3}");
        Check(lost > 20, "the test really dropped packets");
        Check(mean < 0.05, $"mean error small ({mean:F3} m)");
        Check(maxErr < 0.35, $"max error bounded ({maxErr:F3} m)");
        Check(Math.Abs(buf.ClockOffset + remoteClockOffset - 0.04) < 0.02, "clock offset found the fastest path");
        Check(buf.Count <= buf.Capacity, "buffer bounded");
    }

    static void InterpolationSceneSnap()
    {
        var buf = new SnapshotBuffer();
        buf.Add(0.00, new PlayerStateMsg { Seq = 1, SendTime = 0.00, Scene = "hab", Pos = new V3(0, 0, 0), Rot = Q4.Identity, Look = Q4.Identity });
        // Small distance on purpose: only the scene check (not the teleport check) can catch this.
        buf.Add(0.05, new PlayerStateMsg { Seq = 2, SendTime = 0.05, Scene = "bay", Pos = new V3(10, 0, 0), Rot = Q4.Identity, Look = Q4.Identity });
        buf.Sample(0.12, out var p); // render time 0.02: between the two
        Check(p.Pos.X == 0 || p.Pos.X == 10, $"no sweep across a scene change (x={p.Pos.X})");
        buf.Sample(0.30, out p);
        Check(p.Scene == "bay" && p.Pos.X == 10, "ends in the new scene");

        var tp = new SnapshotBuffer();
        tp.Add(0.00, new PlayerStateMsg { Seq = 1, SendTime = 0.00, Scene = "bay", Pos = new V3(0, 0, 0), Rot = Q4.Identity, Look = Q4.Identity });
        tp.Add(0.05, new PlayerStateMsg { Seq = 2, SendTime = 0.05, Scene = "bay", Pos = new V3(1000, 0, 0), Rot = Q4.Identity, Look = Q4.Identity });
        tp.Sample(0.12, out p);
        Check(p.Pos.X == 0 || p.Pos.X == 1000, $"no sweep across a teleport (x={p.Pos.X})");
    }

    // ------------------------------------------------------------------ sessions over a fake network

    static void SessionLossy()
    {
        var rng = new Random(42);
        var net = new FakeNet(rng) { Loss = 0.3, MinLatency = 0.03, Jitter = 0.08, Duplicate = 0.05 };
        var host = MakeSession(net, 1, "Host");
        var c1 = MakeSession(net, 2, "Ann");
        var c2 = MakeSession(net, 3, "Bob");
        host.StartHost(); c1.Connect(1); c2.Connect(1);

        var all = new[] { host, c1, c2 };
        net.RunFor(5, all, sendStates: true);

        Check(c1.Connected && c2.Connected, "both clients joined despite 30% loss");
        Check(c1.LocalId != c2.LocalId && c1.LocalId >= 2 && c2.LocalId >= 2, $"distinct ids ({c1.LocalId}, {c2.LocalId})");
        Check(host.Remotes.Count == 2, "host sees 2 players");
        Check(c1.Remotes.ContainsKey(CoopSession.HostId) && c1.Remotes.ContainsKey(c2.LocalId), "client 1 sees host and client 2 (relay)");
        Check(c2.Remotes.ContainsKey(c1.LocalId) && c2.Remotes[c1.LocalId].Name == "Ann", "names arrive via roster");
        Check(c1.Remotes[c2.LocalId].StatesReceived > 20, "relayed states flow client->client");
        Check(c1.RttSeconds > 0.05 && c1.RttSeconds < 0.5, $"rtt measured ({c1.RttSeconds:F3}s)");

        // Pose accuracy through the whole stack: client 2 should see client 1 where it was ~150-250 ms ago.
        c2.Remotes[c1.LocalId].Buffer.Sample(net.Now, out var pose);
        var trueNow = FakeNet.PoseOf(c1.LocalId, net.Now);
        double lag = (trueNow - pose.Pos).Length / 2.0; // poses move at 2 m/s
        Check(lag > 0.05 && lag < 0.6, $"remote pose is a short time behind ({lag:F3}s)");
    }

    static void SessionTimeouts()
    {
        var net = new FakeNet(new Random(3));
        var host = MakeSession(net, 1, "Host");
        var c1 = MakeSession(net, 2, "Ann");
        var c2 = MakeSession(net, 3, "Bob");
        host.StartHost(); c1.Connect(1); c2.Connect(1);
        net.RunFor(2, new[] { host, c1, c2 }, true);
        Check(host.Remotes.Count == 2, "setup");

        // c2 leaves cleanly: Bye
        c2.Shutdown();
        net.RunFor(3, new[] { host, c1 }, true);
        Check(host.Remotes.Count == 1, "bye removes the player on the host");
        Check(!c1.Remotes.ContainsKey(3) && c1.Remotes.Count == 1, "roster removes the player on other clients");

        // c1 goes silent (crash): dropped after the timeout
        net.RunFor(host.TimeoutSeconds + 1.5, new[] { host }, true);
        Check(host.Remotes.Count == 0, "silent client dropped after timeout");

        // c1 comes back: it noticed the host timeout too, and rejoins
        net.RunFor(host.TimeoutSeconds + 3, new[] { host, c1 }, true);
        Check(c1.Connected && host.Remotes.Count == 1, "client rejoins after both sides timed out");
    }

    static void SessionSecurity()
    {
        var net = new FakeNet(new Random(5));
        var host = MakeSession(net, 1, "Host");
        var old = new CoopSession(net.Endpoint(2), () => net.Now, "Old", "0.0.1");
        host.StartHost(); old.Connect(1);
        net.RunFor(2, new[] { host, old }, false);
        Check(old.Role == Role.Off && old.LastError != null && old.LastError.Contains("version"), "version mismatch rejected");
        Check(host.Remotes.Count == 0, "rejected player not added");

        var c = MakeSession(net, 3, "Ann");
        c.Connect(1);
        net.RunFor(2, new[] { host, c }, false);
        // c tries to send a state as the host (id 1)
        int bad = host.BadPackets;
        net.Inject(3, 1, Protocol.PlayerState(new PlayerStateMsg { PlayerId = 1, Scene = "x", Rot = Q4.Identity, Look = Q4.Identity }));
        host.Tick();
        Check(host.BadPackets == bad + 1, "spoofed player id dropped");
        // a stranger sends a state without saying hello
        net.Inject(77, 1, Protocol.PlayerState(new PlayerStateMsg { PlayerId = 9, Scene = "x", Rot = Q4.Identity, Look = Q4.Identity }));
        host.Tick();
        Check(!host.Remotes.ContainsKey(9), "stranger without hello ignored");
        // a stranger talks to the client: ignored
        net.Inject(77, 3, Protocol.Welcome(4, 1));
        c.Tick();
        Check(c.LocalId != 4, "client ignores non-host packets");

        // server full
        var extra = new List<CoopSession>();
        for (int i = 0; i < 4; i++) { var x = MakeSession(net, (ulong)(10 + i), "P" + i); x.Connect(1); extra.Add(x); }
        var list = new List<CoopSession> { host, c }; list.AddRange(extra);
        net.RunFor(3, list.ToArray(), false);
        Check(host.PlayerCount == CoopSession.MaxPlayers, $"host capped at {CoopSession.MaxPlayers} ({host.PlayerCount})");
        Check(extra.Count(x => x.LastError == "server full") == 2, "the extra joiners got 'server full'");
    }

    static void SessionHostLeaves()
    {
        var net = new FakeNet(new Random(9));
        var host = MakeSession(net, 1, "Host");
        var c = MakeSession(net, 2, "Ann");
        host.StartHost(); c.Connect(1);
        net.RunFor(2, new[] { host, c }, true);
        Check(c.Connected, "setup");

        host.Shutdown();               // host presses F10 / quits: Bye(Leaving)
        net.RunFor(3, new[] { c }, true);
        Check(c.Role == Role.Off && c.LastError == "Host left the session", $"client ends the session ({c.Role}, '{c.LastError}')");
        Check(c.Remotes.Count == 0, "host puppet removed");

        // Host re-hosts: the old client must not walk back in unless the filter admits it.
        var host2 = new CoopSession(net.Endpoint(1), () => net.Now, "Host", "0.1.0") { AdmitFilter = id => id == 3 };
        net.Track(host2, 1);
        host2.StartHost();
        var stale = MakeSession(net, 2, "Ann"); stale.Connect(1);
        var invited = MakeSession(net, 3, "Bob"); invited.Connect(1);
        net.RunFor(3, new[] { host2, stale, invited }, true);
        Check(!stale.Connected && host2.PlayerCount == 2, $"player outside the lobby is not admitted (players {host2.PlayerCount})");
        Check(invited.Connected, "lobby member is admitted");
    }

    static void SessionUdp()
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        Func<double> now = () => clock.Elapsed.TotalSeconds;
        using var ht = new UdpTransport(IPAddress.Loopback, 0);
        using var ct = new UdpTransport(IPAddress.Loopback, 0);
        var host = new CoopSession(ht, now, "Host", "v"); host.StartHost();
        var c = new CoopSession(ct, now, "Ann", "v"); c.Connect(UdpTransport.Parse("127.0.0.1:" + ht.LocalPort, 0));
        var deadline = now() + 5;
        while (now() < deadline && !(c.Connected && c.Remotes.TryGetValue(1, out var r) && r.StatesReceived > 10))
        {
            host.Tick(); c.Tick();
            host.SendLocalState(new PlayerStateMsg { Scene = "bay", Pos = new V3(1, 2, 3), Rot = Q4.Identity, Look = Q4.Identity, Flags = 1 });
            c.SendLocalState(new PlayerStateMsg { Scene = "bay", Pos = new V3(4, 5, 6), Rot = Q4.Identity, Look = Q4.Identity, Flags = 1 });
            Thread.Sleep(20);
        }
        Check(c.Connected, "UDP client connected");
        Check(c.Remotes.ContainsKey(1) && c.Remotes[1].StatesReceived > 10, "UDP host states arrive");
        Check(host.Remotes.ContainsKey(2) && host.Remotes[2].StatesReceived > 5, "UDP client states arrive");
        if (c.Remotes.ContainsKey(1) && c.Remotes[1].Buffer.Sample(now() + 1, out var p))
            Check(p.Pos.X == 1 && p.Pos.Z == 3, "UDP pose intact");
        Check(UdpTransport.Unpack(UdpTransport.Parse("10.1.2.3:27960", 0)).ToString() == "10.1.2.3:27960", "address pack/unpack");
    }

    static CoopSession MakeSession(FakeNet net, ulong addr, string name)
    {
        var s = new CoopSession(net.Endpoint(addr), () => net.Now, name, "0.1.0");
        net.Track(s, addr);
        return s;
    }
}

/// <summary>In-memory datagram network with a virtual clock: loss, latency, jitter (reorder), duplicates.</summary>
sealed class FakeNet
{
    public double Now;
    public double Loss, MinLatency = 0.02, Jitter, Duplicate;
    readonly Random mRng;
    readonly List<(double at, ulong from, ulong to, byte[] data)> mInFlight = new();
    readonly Dictionary<ulong, Queue<(ulong, byte[])>> mInbox = new();
    readonly Dictionary<CoopSession, ulong> mAddrOf = new();

    public FakeNet(Random rng) { mRng = rng; }

    public ITransport Endpoint(ulong addr) { mInbox[addr] = new Queue<(ulong, byte[])>(); return new Ep(this, addr); }
    public void Track(CoopSession s, ulong addr) => mAddrOf[s] = addr;
    public void Inject(ulong from, ulong to, byte[] data) => mInbox[to].Enqueue((from, data));

    void Send(ulong from, ulong to, byte[] data)
    {
        if (!mInbox.ContainsKey(to) || mRng.NextDouble() < Loss) return;
        int copies = mRng.NextDouble() < Duplicate ? 2 : 1;
        for (int i = 0; i < copies; i++)
            mInFlight.Add((Now + MinLatency + mRng.NextDouble() * Jitter, from, to, data));
    }

    void Deliver()
    {
        for (int i = mInFlight.Count - 1; i >= 0; i--)
            if (mInFlight[i].at <= Now) { var x = mInFlight[i]; mInFlight.RemoveAt(i); mInbox[x.to].Enqueue((x.from, x.data)); }
    }

    public static V3 PoseOf(byte id, double t) => new V3((float)(2 * t), id, 0);

    /// <summary>Runs the given sessions at 60 Hz for `seconds` of virtual time; states at 20 Hz.</summary>
    public void RunFor(double seconds, CoopSession[] sessions, bool sendStates)
    {
        double end = Now + seconds; int frame = 0;
        while (Now < end)
        {
            Now += 1.0 / 60; frame++;
            Deliver();
            foreach (var s in sessions)
            {
                s.Tick();
                if (sendStates && frame % 3 == 0)
                    s.SendLocalState(new PlayerStateMsg { Scene = "bay", Pos = PoseOf(s.LocalId, Now), Vel = new V3(2, 0, 0),
                        Rot = Q4.Identity, Look = Q4.Identity, Flags = 1 });
            }
        }
    }

    sealed class Ep : ITransport
    {
        readonly FakeNet mNet; readonly ulong mAddr;
        public Ep(FakeNet net, ulong addr) { mNet = net; mAddr = addr; }
        public string Name => "fake";
        public void Send(ulong peer, byte[] data, bool reliable) => mNet.Send(mAddr, peer, data);
        public bool TryReceive(out ulong peer, out byte[] data)
        {
            peer = 0; data = null;
            var q = mNet.mInbox[mAddr];
            if (q.Count == 0) return false;
            (peer, data) = q.Dequeue();
            return true;
        }
        public void Tick() { }
        public string Describe(ulong peer) => "fake:" + peer;
        public void Dispose() { }
    }
}
