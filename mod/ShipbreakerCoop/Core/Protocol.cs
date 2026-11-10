using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ShipbreakerCoop.Core
{
    public enum MsgType : byte
    {
        Hello = 1,       // client -> host: name, mod version
        Welcome = 2,     // host -> client: your id, host id
        Reject = 3,      // host -> client: reason
        PlayerState = 4, // anyone: one player's pose (host relays client states)
        Ping = 5,
        Pong = 6,
        Bye = 7,         // leaving
        Roster = 8,      // host -> clients: every player id + name (re-sent, idempotent)
    }

    public struct PlayerStateMsg
    {
        public byte PlayerId;
        public uint Seq;
        public double SendTime;   // sender's clock, seconds
        public string Scene;      // scene holding the sender's Player ("" = no player)
        public V3 Pos;
        public Q4 Rot;            // body rotation
        public Q4 Look;           // camera rotation
        public V3 Vel;
        public byte Flags;        // bit 0: has player
        public const byte FlagHasPlayer = 1;
        public bool HasPlayer => (Flags & FlagHasPlayer) != 0;
    }

    public struct RosterEntry
    {
        public byte PlayerId;
        public string Name;
    }

    /// <summary>
    /// Wire format: magic u16 'SB', version u8, type u8, then the body. Little endian.
    /// Strings are UTF-8 with a u8 byte length (capped at 64 bytes).
    /// </summary>
    public static class Protocol
    {
        public const ushort Magic = 0x4253; // "SB"
        public const byte Version = 1;
        public const int MaxString = 64;
        public const int HeaderSize = 4;

        // ---------- writing ----------

        public static byte[] Hello(string name, string modVersion)
        {
            var w = Begin(MsgType.Hello);
            WriteStr(w, name); WriteStr(w, modVersion);
            return End(w);
        }

        public static byte[] Welcome(byte yourId, byte hostId)
        {
            var w = Begin(MsgType.Welcome);
            w.Write(yourId); w.Write(hostId);
            return End(w);
        }

        public static byte[] Reject(string reason)
        {
            var w = Begin(MsgType.Reject);
            WriteStr(w, reason);
            return End(w);
        }

        public static byte[] Ping(double t) { var w = Begin(MsgType.Ping); w.Write(t); return End(w); }
        public static byte[] Pong(double t) { var w = Begin(MsgType.Pong); w.Write(t); return End(w); }
        public static byte[] Bye() => End(Begin(MsgType.Bye));

        public static byte[] Roster(IList<RosterEntry> entries)
        {
            var w = Begin(MsgType.Roster);
            w.Write((byte)entries.Count);
            foreach (var e in entries) { w.Write(e.PlayerId); WriteStr(w, e.Name); }
            return End(w);
        }

        public static byte[] PlayerState(in PlayerStateMsg s)
        {
            var w = Begin(MsgType.PlayerState);
            w.Write(s.PlayerId); w.Write(s.Seq); w.Write(s.SendTime);
            WriteStr(w, s.Scene);
            WriteV3(w, s.Pos); WriteQ(w, s.Rot); WriteQ(w, s.Look); WriteV3(w, s.Vel);
            w.Write(s.Flags);
            return End(w);
        }

        // ---------- reading ----------

        /// <summary>Checks header. Returns false for foreign or other-version packets.</summary>
        public static bool TryReadHeader(byte[] data, int len, out MsgType type, out BinaryReader body)
        {
            type = 0; body = null;
            if (data == null || len < HeaderSize) return false;
            if ((ushort)(data[0] | (data[1] << 8)) != Magic) return false;
            if (data[2] != Version) return false;
            type = (MsgType)data[3];
            body = new BinaryReader(new MemoryStream(data, HeaderSize, len - HeaderSize, false), Encoding.UTF8);
            return true;
        }

        public static void ReadHello(BinaryReader r, out string name, out string modVersion)
        { name = ReadStr(r); modVersion = ReadStr(r); }

        public static void ReadWelcome(BinaryReader r, out byte yourId, out byte hostId)
        { yourId = r.ReadByte(); hostId = r.ReadByte(); }

        public static string ReadReject(BinaryReader r) => ReadStr(r);
        public static double ReadTime(BinaryReader r) => r.ReadDouble();

        public static List<RosterEntry> ReadRoster(BinaryReader r)
        {
            int n = r.ReadByte();
            var list = new List<RosterEntry>(n);
            for (int i = 0; i < n; i++) list.Add(new RosterEntry { PlayerId = r.ReadByte(), Name = ReadStr(r) });
            return list;
        }

        public static PlayerStateMsg ReadPlayerState(BinaryReader r)
        {
            var s = new PlayerStateMsg
            {
                PlayerId = r.ReadByte(),
                Seq = r.ReadUInt32(),
                SendTime = r.ReadDouble(),
                Scene = ReadStr(r),
                Pos = ReadV3(r),
                Rot = ReadQ(r),
                Look = ReadQ(r),
                Vel = ReadV3(r),
                Flags = r.ReadByte(),
            };
            if (!Finite(s.Pos) || !Finite(s.Vel) || !Finite(s.Rot) || !Finite(s.Look) || double.IsNaN(s.SendTime))
                throw new InvalidDataException("non-finite value in PlayerState");
            return s;
        }

        // ---------- helpers ----------

        static BinaryWriter Begin(MsgType t)
        {
            var w = new BinaryWriter(new MemoryStream(64), Encoding.UTF8);
            w.Write(Magic); w.Write(Version); w.Write((byte)t);
            return w;
        }

        static byte[] End(BinaryWriter w) => ((MemoryStream)w.BaseStream).ToArray();

        static void WriteStr(BinaryWriter w, string s)
        {
            var b = Encoding.UTF8.GetBytes(s ?? "");
            int n = Math.Min(b.Length, MaxString);
            w.Write((byte)n); w.Write(b, 0, n);
        }

        static string ReadStr(BinaryReader r)
        {
            int n = r.ReadByte();
            var b = r.ReadBytes(n);
            if (b.Length != n) throw new EndOfStreamException();
            return Encoding.UTF8.GetString(b);
        }

        static void WriteV3(BinaryWriter w, V3 v) { w.Write(v.X); w.Write(v.Y); w.Write(v.Z); }
        static V3 ReadV3(BinaryReader r) => new V3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
        static void WriteQ(BinaryWriter w, Q4 q) { w.Write(q.X); w.Write(q.Y); w.Write(q.Z); w.Write(q.W); }
        static Q4 ReadQ(BinaryReader r) => new Q4(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());

        static bool Finite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);
        static bool Finite(V3 v) => Finite(v.X) && Finite(v.Y) && Finite(v.Z);
        static bool Finite(Q4 q) => Finite(q.X) && Finite(q.Y) && Finite(q.Z) && Finite(q.W);
    }
}
