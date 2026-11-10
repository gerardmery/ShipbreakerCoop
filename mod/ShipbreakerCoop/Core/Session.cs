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
        /// <summary>A blob from the host arrived complete and verified: (kind, blob id, bytes).</summary>
        public event Action<byte, uint, byte[]> BlobReceived;
        /// <summary>A report from another player: (from player id, kind, text).</summary>
        public event Action<byte, byte, string> ReportReceived;
        /// <summary>Client: a host game event, delivered in order: (epoch, kind, payload).</summary>
        public event Action<uint, byte, byte[]> GameEventReceived;

        readonly EventLog mEventLog = new EventLog();
        readonly Dictionary<ulong, EventSender> mEventSenders = new Dictionary<ulong, EventSender>();
        readonly EventReceiver mEventReceiver = new EventReceiver();
        public uint EventEpoch => Role == Role.Host ? mEventLog.Epoch : mEventReceiver.Epoch;
        public uint EventsSentOrDelivered => Role == Role.Host ? (uint)mEventLog.Count : mEventReceiver.Delivered;
        public uint MinEventAcked { get { uint m = uint.MaxValue; foreach (var e in mEventSenders.Values) m = Math.Min(m, e.Acked); return m == uint.MaxValue ? 0 : m; } }

        readonly List<BlobSender> mSenders = new List<BlobSender>();
        readonly Dictionary<uint, BlobReceiver> mReceivers = new Dictionary<uint, BlobReceiver>();
        uint mNextBlobId = (uint)Environment.TickCount;
        public IReadOnlyList<BlobSender> Senders => mSenders;
        /// <summary>Client: progress of the newest incoming blob (0..1), or -1 when none.</summary>
        public float IncomingProgress { get; private set; } = -1;

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

        /// <summary>Host: start sending a blob to every client. Returns its id.</summary>
        public uint SendBlobToClients(byte kind, byte[] data)
        {
            if (Role != Role.Host) throw new InvalidOperationException("only the host sends blobs");
            uint id = ++mNextBlobId;
            mSenders.RemoveAll(x => x.Kind == kind); // a newer blob of the same kind replaces the old one
            foreach (var p in mPeersByAddr.Values) mSenders.Add(new BlobSender(id, kind, p.Addr, data, mClock()));
            Info($"sending blob {id} kind {kind} ({data.Length} bytes) to {mPeersByAddr.Count} player(s)");
            return id;
        }

        /// <summary>Host: start a new event epoch (a new ship sync). Earlier events are dropped everywhere.</summary>
        public void ResetEvents(uint epoch) { if (Role == Role.Host) mEventLog.Reset(epoch); }

        /// <summary>Host: append an event for every client (ordered, re-sent until acked). Returns its sequence.</summary>
        public uint BroadcastEvent(byte kind, byte[] payload)
        {
            if (Role != Role.Host) throw new InvalidOperationException("only the host broadcasts events");
            return mEventLog.Append(kind, payload);
        }

        /// <summary>Client: to the host. Host: to every client.</summary>
        public void SendReport(byte kind, string text)
        {
            if (!Connected) return;
            var bytes = Protocol.Report(LocalId, kind, text);
            if (Role == Role.Host) foreach (var p in mPeersByAddr.Values) Send(p.Addr, bytes, true);
            else Send(mHostAddr, bytes, true);
        }

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

            double rto = Math.Max(0.25, RttSeconds > 0 ? RttSeconds * 2 : 0.25);
            foreach (var b in mSenders)
            {
                var snd = b;
                snd.Tick(now, rto, (bytes, reliable) => Send(snd.Peer, bytes, reliable));
            }
            if (Role == Role.Host)
            {
                foreach (var p in mPeersByAddr.Values)
                {
                    if (!mEventSenders.TryGetValue(p.Addr, out var es)) mEventSenders[p.Addr] = es = new EventSender(p.Addr);
                    var addr = p.Addr;
                    es.Tick(mEventLog, now, rto, bytes => Send(addr, bytes, false));
                }
            }
            if (Role == Role.Client) mEventReceiver.Tick(now, bytes => Send(mHostAddr, bytes, false));
            if (Role == Role.Client)
            {
                float prog = -1;
                foreach (var rc in mReceivers.Values)
                {
                    rc.Tick(now, bytes => Send(mHostAddr, bytes, false));
                    if (!rc.Complete) prog = rc.Progress;
                }
                IncomingProgress = prog;
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
                    case MsgType.GameEventAck:
                        if (peer == null) return;
                        Protocol.ReadGameEventAck(r, out var eepoch, out var edone);
                        if (mEventSenders.TryGetValue(from, out var esnd)) esnd.OnAck(eepoch, edone);
                        break;
                    case MsgType.BlobAck:
                        if (peer == null) return;
                        Protocol.ReadBlobAck(r, out var aid, out var contig, out var map);
                        foreach (var b in mSenders) if (b.Peer == from && b.Id == aid) b.OnAck(contig, map, now);
                        break;
                    case MsgType.Report:
                        if (peer == null) return;
                        Protocol.ReadReport(r, out var rfrom, out var rkind, out var rtext);
                        if (rfrom != peer.Id) { BadPackets++; return; }
                        ReportReceived?.Invoke(rfrom, rkind, rtext);
                        foreach (var other in mPeersByAddr.Values) if (other != peer) Send(other.Addr, data, true);
                        break;
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
                    case MsgType.BlobStart:
                    {
                        Protocol.ReadBlobStart(r, out var bid, out var bkind, out var blen, out var bhash);
                        if (mReceivers.TryGetValue(bid, out var known)) { known.Poke(); break; }
                        mReceivers[bid] = new BlobReceiver(bid, bkind, blen, bhash, now);
                        foreach (var old in new List<uint>(mReceivers.Keys))   // keep only the newest blob per kind
                            if (old != bid && mReceivers[old].Kind == bkind) mReceivers.Remove(old);
                        Info($"receiving blob {bid} kind {bkind} ({blen} bytes)");
                        break;
                    }
                    case MsgType.BlobChunk:
                    {
                        var chunk = Protocol.ReadBlobChunk(r, out var cid, out var cidx);
                        if (!mReceivers.TryGetValue(cid, out var rcv)) break; // start not seen yet: the sender re-sends it
                        if (rcv.Complete) { rcv.Poke(); break; }
                        if (rcv.OnChunk(cidx, chunk, 0, chunk.Length, now))
                        {
                            if (rcv.Verify()) { Info($"blob {cid} complete"); BlobReceived?.Invoke(rcv.Kind, cid, rcv.Data); }
                            else { Info($"blob {cid} failed its hash check"); mReceivers.Remove(cid); }
                        }
                        break;
                    }
                    case MsgType.GameEvent:
                    {
                        var payload = Protocol.ReadGameEvent(r, out var gepoch, out var gseq, out var gkind);
                        mEventReceiver.OnEvent(gepoch, gseq, gkind, payload, (k, p) => GameEventReceived?.Invoke(gepoch, k, p));
                        break;
                    }
                    case MsgType.Report:
                        Protocol.ReadReport(r, out var hfrom, out var hkind, out var htext);
                        ReportReceived?.Invoke(hfrom, hkind, htext);
                        break;
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
            mSenders.RemoveAll(b => b.Peer == p.Addr);
            mEventSenders.Remove(p.Addr);
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
