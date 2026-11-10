using System;

namespace ShipbreakerCoop.Core
{
    /// <summary>
    /// Reliable transfer of one byte array over an unreliable datagram transport (UDP lab, Steam unreliable).
    /// Sender: a Start packet (re-sent until acked), then chunks, lowest unacked first, at most Window in flight;
    /// a chunk is re-sent when it has not been acked within the RTO. Receiver: acks with the number of chunks
    /// received contiguously from 0 plus a 64-bit map of the chunks after that, every AckInterval and on
    /// completion. The payload is verified with its length and FNV-1a 64 hash before it is handed over.
    /// </summary>
    public static class Blob
    {
        public const int ChunkSize = 1100;  // + 13 bytes of headers: under Steam's 1200-byte unreliable limit
        public const int Window = 32;
        public const double AckInterval = 0.05;
        public const double StartResend = 0.25;
        public const double Timeout = 20;

        public static ulong Hash(byte[] data)
        {
            ulong h = 14695981039346656037UL;
            for (int i = 0; i < data.Length; i++) { h ^= data[i]; h *= 1099511628211UL; }
            return h;
        }

        public static int ChunkCount(int length) => Math.Max(1, (length + ChunkSize - 1) / ChunkSize);
    }

    public sealed class BlobSender
    {
        public readonly uint Id;
        public readonly byte Kind;
        public readonly ulong Peer;
        readonly byte[] mData;
        readonly double[] mSentAt;
        readonly bool[] mAcked;
        readonly ulong mHash;
        int mAckedCount;
        bool mStartAcked;
        double mNextStart, mLastProgress;

        public int ChunkCount => mAcked.Length;
        public bool Done => mAckedCount == ChunkCount;
        public bool Failed { get; private set; }
        public float Progress => (float)mAckedCount / ChunkCount;
        public int ChunksSent { get; private set; }

        public BlobSender(uint id, byte kind, ulong peer, byte[] data, double now)
        {
            Id = id; Kind = kind; Peer = peer; mData = data;
            int n = Blob.ChunkCount(data.Length);
            mSentAt = new double[n]; mAcked = new bool[n];
            for (int i = 0; i < n; i++) mSentAt[i] = double.NegativeInfinity;
            mHash = Blob.Hash(data);
            mLastProgress = now;
        }

        public void Tick(double now, double rto, Action<byte[], bool> send)
        {
            if (Done || Failed) return;
            if (now - mLastProgress > Blob.Timeout) { Failed = true; return; }
            if (!mStartAcked && now >= mNextStart)
            {
                mNextStart = now + Blob.StartResend;
                send(Protocol.BlobStart(Id, Kind, mData.Length, mHash), true);
            }
            // No chunks before the receiver has acked the Start: chunks that overtake it are dropped,
            // which wasted the whole first window (found by the corruption test).
            if (!mStartAcked) return;
            int inFlight = 0;
            for (int i = 0; i < mAcked.Length; i++) if (!mAcked[i] && now - mSentAt[i] < rto) inFlight++;
            for (int i = 0; i < mAcked.Length && inFlight < Blob.Window; i++)
            {
                if (mAcked[i] || now - mSentAt[i] < rto) continue;
                int off = i * Blob.ChunkSize;
                int len = Math.Min(Blob.ChunkSize, mData.Length - off);
                send(Protocol.BlobChunk(Id, (uint)i, mData, off, len), false);
                mSentAt[i] = now; inFlight++; ChunksSent++;
            }
        }

        public void OnAck(uint contiguous, ulong map, double now)
        {
            mStartAcked = true;
            int before = mAckedCount;
            int n = mAcked.Length;
            for (int i = 0; i < Math.Min((long)contiguous, n); i++) Mark(i);
            for (int b = 0; b < 64; b++)
                if ((map & (1UL << b)) != 0) { long i = (long)contiguous + b; if (i < n) Mark((int)i); }
            if (mAckedCount > before) mLastProgress = now;
        }

        void Mark(int i) { if (!mAcked[i]) { mAcked[i] = true; mAckedCount++; } }
    }

    public sealed class BlobReceiver
    {
        public readonly uint Id;
        public readonly byte Kind;
        public readonly int Length;
        readonly ulong mHash;
        readonly byte[] mBuf;
        readonly bool[] mHave;
        int mHaveCount, mContiguous;
        double mNextAck, mLastProgress, mCompletedAt = -1;
        bool mPoked;

        public bool Complete => mHaveCount == mHave.Length;
        public bool Failed { get; private set; }
        public float Progress => (float)mHaveCount / mHave.Length;

        public BlobReceiver(uint id, byte kind, int length, ulong hash, double now)
        {
            if (length < 0 || length > 64 * 1024 * 1024) throw new ArgumentException("blob too large");
            Id = id; Kind = kind; Length = length; mHash = hash;
            mBuf = new byte[length];
            mHave = new bool[Blob.ChunkCount(length)];
            mLastProgress = now;
        }

        /// <summary>Returns true when this chunk completed the blob.</summary>
        public bool OnChunk(uint index, byte[] src, int off, int len, double now)
        {
            if (Complete || index >= mHave.Length || mHave[index]) return false;
            int at = (int)index * Blob.ChunkSize;
            int expect = Math.Min(Blob.ChunkSize, Length - at);
            if (len != expect) throw new ArgumentException("bad chunk length");
            Buffer.BlockCopy(src, off, mBuf, at, len);
            mHave[index] = true; mHaveCount++; mLastProgress = now;
            while (mContiguous < mHave.Length && mHave[mContiguous]) mContiguous++;
            mNextAck = 0; // ack soon
            if (Complete) mCompletedAt = now;
            return Complete;
        }

        public bool Verify() => Complete && Blob.Hash(mBuf) == mHash;

        /// <summary>The sender is still sending something we have: ack again (our final ack was lost).</summary>
        public void Poke() { mPoked = true; mNextAck = 0; }
        public byte[] Data => mBuf;

        public void Tick(double now, Action<byte[]> send)
        {
            if (Failed) return;
            if (!Complete && now - mLastProgress > Blob.Timeout) { Failed = true; return; }
            if (now < mNextAck) return;
            // After completion: ack for 3 s, then only when the sender shows it missed the final ack.
            if (Complete && now - mCompletedAt > 3 && !mPoked) return;
            mPoked = false;
            mNextAck = now + (Complete ? 0.5 : Blob.AckInterval); // keep acking a little after completion
            ulong map = 0;
            for (int b = 0; b < 64; b++) { long i = (long)mContiguous + b; if (i < mHave.Length && mHave[i]) map |= 1UL << b; }
            send(Protocol.BlobAck(Id, (uint)mContiguous, map));
        }
    }
}
