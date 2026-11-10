using System;
using System.Collections.Generic;

namespace ShipbreakerCoop.Core
{
    public struct PoseSample
    {
        public V3 Pos;
        public Q4 Rot;
        public Q4 Look;
        public string Scene;
        public bool HasPlayer;
        public bool Extrapolated;
    }

    /// <summary>
    /// Snapshot interpolation for one remote player. Renders InterpDelay seconds behind the
    /// sender's clock. The sender clock is mapped to ours with a min-filtered offset
    /// (local receive time - send time): the fastest packet sets it, slow drift is followed.
    /// </summary>
    public sealed class SnapshotBuffer
    {
        public double InterpDelay = 0.10;
        public double MaxExtrapolate = 0.25;
        public int Capacity = 32;

        readonly List<PlayerStateMsg> mSnaps = new List<PlayerStateMsg>();
        bool mHasOffset;
        double mOffset;          // local - remote
        uint mLatestSeq;
        bool mAny;

        public int Count => mSnaps.Count;
        public double ClockOffset => mOffset;
        public int Dropped { get; private set; }

        public void Clear() { mSnaps.Clear(); mHasOffset = false; mAny = false; }

        public void Add(double localRecvTime, in PlayerStateMsg s)
        {
            double sample = localRecvTime - s.SendTime;
            if (!mHasOffset) { mOffset = sample; mHasOffset = true; }
            else if (sample < mOffset) mOffset = sample;
            else mOffset += (sample - mOffset) * 0.02;

            // A big jump back in seq means the sender restarted: start over.
            if (mAny && s.Seq + 1000 < mLatestSeq) { mSnaps.Clear(); mAny = false; mHasOffset = true; mOffset = sample; }

            // Insert sorted by SendTime; drop exact duplicates.
            int i = mSnaps.Count;
            while (i > 0 && mSnaps[i - 1].SendTime > s.SendTime) i--;
            if (i > 0 && mSnaps[i - 1].Seq == s.Seq) { Dropped++; return; }
            mSnaps.Insert(i, s);
            if (!mAny || s.Seq > mLatestSeq) mLatestSeq = s.Seq;
            mAny = true;
            while (mSnaps.Count > Capacity) mSnaps.RemoveAt(0);
        }

        public bool Sample(double localNow, out PoseSample pose)
        {
            pose = default;
            if (mSnaps.Count == 0) return false;
            double t = localNow - mOffset - InterpDelay;

            var first = mSnaps[0];
            var last = mSnaps[mSnaps.Count - 1];
            if (t <= first.SendTime) { pose = From(first, false); return true; }
            if (t >= last.SendTime)
            {
                float dt = (float)Math.Min(t - last.SendTime, MaxExtrapolate);
                pose = From(last, dt > 0);
                pose.Pos = last.Pos + last.Vel * dt;
                return true;
            }
            for (int i = 1; i < mSnaps.Count; i++)
            {
                var b = mSnaps[i];
                if (b.SendTime < t) continue;
                var a = mSnaps[i - 1];
                double span = b.SendTime - a.SendTime;
                float k = span <= 1e-9 ? 1f : (float)((t - a.SendTime) / span);
                // A scene change or teleport between two samples: snap, don't sweep through space.
                if (a.Scene != b.Scene || (b.Pos - a.Pos).Length > 50f) { pose = From(k < 0.5f ? a : b, false); return true; }
                pose = new PoseSample
                {
                    Pos = V3.Lerp(a.Pos, b.Pos, k),
                    Rot = Q4.Nlerp(a.Rot, b.Rot, k),
                    Look = Q4.Nlerp(a.Look, b.Look, k),
                    Scene = b.Scene,
                    HasPlayer = b.HasPlayer,
                };
                // Trim samples we will never need again.
                if (i > 2) mSnaps.RemoveRange(0, i - 2);
                return true;
            }
            pose = From(last, false);
            return true;
        }

        static PoseSample From(in PlayerStateMsg s, bool extrapolated) => new PoseSample
        {
            Pos = s.Pos, Rot = s.Rot, Look = s.Look, Scene = s.Scene, HasPlayer = s.HasPlayer, Extrapolated = extrapolated,
        };
    }
}
