using System;
using System.Collections.Generic;
using System.IO;

namespace ShipbreakerCoop.Core
{
    public enum Role { Off, Host, Client }

    public sealed class RemotePlayer
    {
        public byte Id;
        public string Name = "?";
        public readonly SnapshotBuffer Buffer = new SnapshotBuffer();
        public double LastStateTime = -1;   // local time of the last state received
        public int StatesReceived;
    }

    /// <summary>
    /// Host-authoritative session, engine-free. The host assigns player ids (host = 1),
    /// relays every client's PlayerState to the other clients and broadcasts the roster.
    /// Nothing here is reliable: handshake and roster are re-sent until they land.
    /// </summary>
    public sealed class CoopSession : IDisposable
    {
        public const byte HostId = 1;
        public const int MaxPlayers = 4;
        public double TimeoutSeconds = 10;
        public double HelloInterval = 1.0;
        public double RosterInterval = 2.0;
        public double PingInterval = 1.0;

        public Role Role { get; private set; } = Role.Off;
        public byte LocalId { get; private set; }
        public bool Connected => Role == Role.Host || (Role == Role.Client && LocalId != 0);
        public string LocalName;
        public string ModVersion;
        public double RttSeconds { get; private set; } = -1;
        public string LastError { get; private set; }
        public IReadOnlyDictionary<byte, RemotePlayer> Remotes => mRemotes;
        public ITransport Transport => mTransport;
        public int PacketsIn { get; private set; }
        public int PacketsOut { get; private set; }
        public int BadPackets { get; private set; }

        public event Action<string> Log;

        /// <summary>Host: who may join. Checked on every Hello (Steam keeps old sessions open, so the
        /// transport's session-request check alone let a player from an earlier lobby straight back in).
        /// Null = anyone who can reach us (UDP lab).</summary>
        public Func<ulong, bool> AdmitFilter;

        readonly ITransport mTransport;
        readonly Func<double> mClock;
        readonly Dictionary<byte, RemotePlayer> mRemotes = new Dictionary<byte, RemotePlayer>();

        // host side
        sealed class Peer { public ulong Addr; public byte Id; public string Name; public double LastHeard; }
        readonly Dictionary<ulong, Peer> mPeersByAddr = new Dictionary<ulong, Peer>();
        double mNextRoster;

        // client side
        ulong mHostAddr;
        double mHostLastHeard, mNextHello;

        double mNextPing;
        uint mSeq;

        public CoopSession(ITransport transport, Func<double> clock, string localName, string modVersion)
        {
            mTransport = transport; mClock = clock; LocalName = localName; ModVersion = modVersion;
        }

        public void StartHost()
        {
            Role = Role.Host; LocalId = HostId;
            Info($"hosting on {mTransport.Name} as '{LocalName}'");
        }

        public void Connect(ulong hostAddr)
        {
            Role = Role.Client; LocalId = 0; mHostAddr = hostAddr; mNextHello = 0;
            mHostLastHeard = mClock();
            Info($"joining {mTransport.Describe(hostAddr)} over {mTransport.Name}");
        }

        /// <summary>Number of players connected, including us.</summary>
        public int PlayerCount => Role == Role.Host ? 1 + mPeersByAddr.Count : (Connected ? 1 + mRemotes.Count : 0);

        public void SendLocalState(PlayerStateMsg s)
        {
            if (!Connected) return;
            s.PlayerId = LocalId; s.Seq = ++mSeq; s.SendTime = mClock();
            var bytes = Protocol.PlayerState(s);
            if (Role == Role.Host) foreach (var p in mPeersByAddr.Values) Send(p.Addr, bytes, false);
            else Send(mHostAddr, bytes, false);
        }

        public void Tick()
        {
            double now = mClock();
            mTransport.Tick();
            while (mTransport.TryReceive(out var from, out var data))
            {
                PacketsIn++;
                try { Handle(from, data, now); }
                catch (Exception e) when (e is EndOfStreamException || e is InvalidDataException || e is ArgumentException)
                { BadPackets++; }
            }

            if (Role == Role.Client)
            {
                if (LocalId == 0 && now >= mNextHello)
                {
                    mNextHello = now + HelloInterval;
                    Send(mHostAddr, Protocol.Hello(LocalName, ModVersion), true);
                }
                if (now - mHostLastHeard > TimeoutSeconds && LocalId != 0)
                {
                    Info("host timed out, rejoining");
                    LocalId = 0; mRemotes.Clear(); mHostLastHeard = now;
                }
                if (Connected && now >= mNextPing) { mNextPing = now + PingInterval; Send(mHostAddr, Protocol.Ping(now), false); }
            }
            else if (Role == Role.Host)
            {
                List<Peer> dead = null;
                foreach (var p in mPeersByAddr.Values)
                    if (now - p.LastHeard > TimeoutSeconds) (dead ?? (dead = new List<Peer>())).Add(p);
                if (dead != null) foreach (var p in dead) DropPeer(p, "timed out");
                if (now >= mNextRoster) { mNextRoster = now + RosterInterval; BroadcastRoster(); }
                if (now >= mNextPing)
                {
                    mNextPing = now + PingInterval;
                    var ping = Protocol.Ping(now);
                    foreach (var p in mPeersByAddr.Values) Send(p.Addr, ping, false);
                }
            }
        }

        void Handle(ulong from, byte[] data, double now)
        {
            if (!Protocol.TryReadHeader(data, data.Length, out var type, out var r)) { BadPackets++; return; }

            if (Role == Role.Host)
            {
                mPeersByAddr.TryGetValue(from, out var peer);
                if (peer != null) peer.LastHeard = now;
                switch (type)
                {
                    case MsgType.Hello:
                        Protocol.ReadHello(r, out var name, out var ver);
                        if (peer == null && AdmitFilter != null && !AdmitFilter(from)) { BadPackets++; return; }
                        if (ver != ModVersion) { Send(from, Protocol.Reject($"mod version {ver} != host {ModVersion}"), true); return; }
                        if (peer == null)
                        {
                            byte id = FreeId();
                            if (id == 0) { Send(from, Protocol.Reject("server full"), true); return; }
                            peer = new Peer { Addr = from, Id = id, Name = name, LastHeard = now };
                            mPeersByAddr[from] = peer;
                            GetRemote(id).Name = name;
                            Info($"player {id} '{name}' joined from {mTransport.Describe(from)}");
                            mNextRoster = 0;
                        }
                        Send(from, Protocol.Welcome(peer.Id, HostId), true);
                        break;
                    case MsgType.PlayerState:
                        // A client we dropped (timeout) still thinks it is in: tell it to say hello again.
                        if (peer == null) { Send(from, Protocol.Bye(ByeReason.NotJoined), false); return; }
                        var s = Protocol.ReadPlayerState(r);
                        if (s.PlayerId != peer.Id) { BadPackets++; return; } // no spoofing other players
                        Accept(s, now);
                        foreach (var other in mPeersByAddr.Values) if (other != peer) Send(other.Addr, data, false);
                        break;
                    case MsgType.Ping: Send(from, Protocol.Pong(Protocol.ReadTime(r)), false); break;
                    case MsgType.Pong: if (peer != null) RttSeconds = now - Protocol.ReadTime(r); break;
                    case MsgType.Bye: if (peer != null) DropPeer(peer, "left"); break;
                }
            }
            else if (Role == Role.Client)
            {
                if (from != mHostAddr) { BadPackets++; return; } // clients only listen to the host
                mHostLastHeard = now;
                switch (type)
                {
                    case MsgType.Welcome:
                        Protocol.ReadWelcome(r, out var yourId, out _);
                        if (LocalId != yourId) { LocalId = yourId; Info($"joined as player {yourId}"); }
                        break;
                    case MsgType.Reject:
                        LastError = Protocol.ReadReject(r);
                        Info($"host rejected us: {LastError}");
                        Role = Role.Off;
                        break;
                    case MsgType.Roster:
                        var roster = Protocol.ReadRoster(r);
                        var keep = new HashSet<byte>();
                        foreach (var e in roster)
                        {
                            if (e.PlayerId == LocalId) continue;
                            keep.Add(e.PlayerId);
                            GetRemote(e.PlayerId).Name = e.Name;
                        }
                        foreach (var id in new List<byte>(mRemotes.Keys))
                            if (!keep.Contains(id)) { mRemotes.Remove(id); Info($"player {id} left"); }
                        break;
                    case MsgType.PlayerState:
                        if (LocalId == 0) return;
                        var s = Protocol.ReadPlayerState(r);
                        if (s.PlayerId == LocalId) return;
                        Accept(s, now);
                        break;
                    case MsgType.Ping: Send(from, Protocol.Pong(Protocol.ReadTime(r)), false); break;
                    case MsgType.Pong: RttSeconds = now - Protocol.ReadTime(r); break;
                    case MsgType.Bye:
                        if (Protocol.ReadBye(r) == ByeReason.NotJoined)
                        {
                            // The host dropped us (timeout) but we still thought we were in: rejoin.
                            if (LocalId != 0) Info("host no longer knows us, rejoining");
                            LocalId = 0; mRemotes.Clear();
                        }
                        else EndAsClient("Host left the session");
                        break;
                }
            }
        }

        /// <summary>Client: the session is over for good (host left). LastError says why.</summary>
        public void EndAsClient(string why)
        {
            if (Role != Role.Client) return;
            Info(why);
            LastError = why;
            Role = Role.Off; LocalId = 0; mRemotes.Clear();
        }

        void Accept(in PlayerStateMsg s, double now)
        {
            var rp = GetRemote(s.PlayerId);
            rp.Buffer.Add(now, s);
            rp.LastStateTime = now;
            rp.StatesReceived++;
        }

        RemotePlayer GetRemote(byte id)
        {
            if (!mRemotes.TryGetValue(id, out var rp)) { rp = new RemotePlayer { Id = id }; mRemotes[id] = rp; }
            return rp;
        }

        byte FreeId()
        {
            for (byte id = 2; id <= MaxPlayers; id++)
            {
                bool used = false;
                foreach (var p in mPeersByAddr.Values) if (p.Id == id) { used = true; break; }
                if (!used) return id;
            }
            return 0;
        }

        void DropPeer(Peer p, string why)
        {
            mPeersByAddr.Remove(p.Addr);
            mRemotes.Remove(p.Id);
            Info($"player {p.Id} '{p.Name}' {why}");
            mNextRoster = 0;
        }

        void BroadcastRoster()
        {
            if (mPeersByAddr.Count == 0) return;
            var list = new List<RosterEntry> { new RosterEntry { PlayerId = HostId, Name = LocalName } };
            foreach (var p in mPeersByAddr.Values) list.Add(new RosterEntry { PlayerId = p.Id, Name = p.Name });
            var bytes = Protocol.Roster(list);
            foreach (var p in mPeersByAddr.Values) Send(p.Addr, bytes, true);
        }

        void Send(ulong to, byte[] data, bool reliable) { PacketsOut++; mTransport.Send(to, data, reliable); }
        void Info(string s) => Log?.Invoke(s);

        /// <summary>Says goodbye (best effort) and stops.</summary>
        public void Shutdown()
        {
            var bye = Protocol.Bye();
            if (Role == Role.Host) foreach (var p in mPeersByAddr.Values) Send(p.Addr, bye, true);
            else if (Role == Role.Client) Send(mHostAddr, bye, true);
            Role = Role.Off; LocalId = 0; mRemotes.Clear(); mPeersByAddr.Clear();
        }

        public void Dispose() { Shutdown(); mTransport.Dispose(); }
    }
}
