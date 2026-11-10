using System;
using System.Collections.Generic;

namespace ShipbreakerCoop.Core
{
    /// <summary>
    /// Host -> clients ordered, reliable game events (cuts, joint breaks) over an unreliable transport.
    /// Every event has an epoch (the ship sync it belongs to) and a sequence number. The host keeps the epoch's log
    /// and re-sends what a client has not acked; the client delivers strictly in order, holding early arrivals.
    /// A new epoch (new ship sync) drops the old log on both sides.
    /// </summary>
    public sealed class EventLog
    {
        public const int MaxPayload = 1000;
        public const int Window = 64;
        public uint Epoch { get; private set; }
        readonly List<byte[]> mPayloads = new List<byte[]>(); // index = seq - 1
        readonly List<byte> mKinds = new List<byte>();
        public int Count => mPayloads.Count;

        public void Reset(uint epoch) { Epoch = epoch; mPayloads.Clear(); mKinds.Clear(); }

        public uint Append(byte kind, byte[] payload)
        {
            if (payload.Length > MaxPayload) throw new ArgumentException("event payload too large");
            mKinds.Add(kind); mPayloads.Add(payload);
            return (uint)mPayloads.Count;
        }

        public byte[] Packet(uint seq) => Protocol.GameEvent(Epoch, seq, mKinds[(int)seq - 1], mPayloads[(int)seq - 1]);
    }

    /// <summary>Host side, one per client: what that client has acked and when each event was last sent.</summary>
    public sealed class EventSender
    {
        public readonly ulong Peer;
        public uint Acked { get; private set; }
        uint mEpoch;
        readonly Dictionary<uint, double> mSentAt = new Dictionary<uint, double>();
        public EventSender(ulong peer) { Peer = peer; }

        public void Tick(EventLog log, double now, double rto, Action<byte[]> send)
        {
            if (log.Epoch != mEpoch) { mEpoch = log.Epoch; Acked = 0; mSentAt.Clear(); }
            uint last = (uint)Math.Min(log.Count, Acked + EventLog.Window);
            for (uint seq = Acked + 1; seq <= last; seq++)
            {
                if (mSentAt.TryGetValue(seq, out var t) && now - t < rto) continue;
                send(log.Packet(seq));
                mSentAt[seq] = now;
            }
        }

        public void OnAck(uint epoch, uint contiguous)
        {
            if (epoch != mEpoch || contiguous <= Acked) return;
            for (uint s = Acked + 1; s <= contiguous; s++) mSentAt.Remove(s);
            Acked = contiguous;
        }
    }

    /// <summary>Client side: in-order delivery, acks.</summary>
    public sealed class EventReceiver
    {
        public uint Epoch { get; private set; }
        public uint Delivered { get; private set; }
        readonly Dictionary<uint, KeyValuePair<byte, byte[]>> mEarly = new Dictionary<uint, KeyValuePair<byte, byte[]>>();
        double mNextAck;
        bool mDirty;

        /// <summary>Delivers this and any held events that are now in order. Returns how many were delivered.</summary>
        public int OnEvent(uint epoch, uint seq, byte kind, byte[] payload, Action<byte, byte[]> deliver)
        {
            if (epoch != Epoch)
            {
                // A newer epoch (new ship sync) replaces the old one; stale ones are ignored.
                if (epoch < Epoch && Epoch - epoch < 0x80000000u) return 0;
                Epoch = epoch; Delivered = 0; mEarly.Clear();
            }
            mDirty = true;
            if (seq <= Delivered) return 0;                       // duplicate: just ack again
            if (seq > Delivered + 1) { if (seq <= Delivered + 4096) mEarly[seq] = new KeyValuePair<byte, byte[]>(kind, payload); return 0; }
            int n = 0;
            deliver(kind, payload); Delivered = seq; n++;
            while (mEarly.TryGetValue(Delivered + 1, out var next))
            {
                mEarly.Remove(Delivered + 1);
                deliver(next.Key, next.Value); Delivered++; n++;
            }
            return n;
        }

        public void Tick(double now, Action<byte[]> send)
        {
            if (!mDirty || now < mNextAck) return;
            mNextAck = now + 0.05; mDirty = false;
            send(Protocol.GameEventAck(Epoch, Delivered));
        }
    }
}
