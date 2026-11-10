using System;
using System.Net;
using System.Net.Sockets;

namespace ShipbreakerCoop.Core
{
    /// <summary>
    /// Datagram transport. A peer is a ulong: a SteamID for Steam, packed IPv4+port for UDP.
    /// Reliable is a hint (Steam honours it, the UDP dev transport ignores it).
    /// </summary>
    public interface ITransport : IDisposable
    {
        void Send(ulong peer, byte[] data, bool reliable);
        bool TryReceive(out ulong peer, out byte[] data);
        void Tick();
        string Describe(ulong peer);
        string Name { get; }
    }

    /// <summary>Plain UDP, for the two-copies-on-one-PC lab only. No encryption, no NAT help.</summary>
    public sealed class UdpTransport : ITransport
    {
        readonly Socket mSocket;
        readonly byte[] mBuf = new byte[65536];
        public int LocalPort { get; }
        public string Name => "udp";

        public UdpTransport(IPAddress bind, int port)
        {
            mSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            mSocket.Blocking = false;
            // Windows: an ICMP "port unreachable" otherwise makes the next ReceiveFrom throw (WSAECONNRESET).
            try { mSocket.IOControl(-1744830452 /* SIO_UDP_CONNRESET */, new byte[] { 0 }, null); } catch { }
            mSocket.Bind(new IPEndPoint(bind, port));
            LocalPort = ((IPEndPoint)mSocket.LocalEndPoint).Port;
        }

        public static ulong Pack(IPEndPoint ep)
        {
            var b = ep.Address.MapToIPv4().GetAddressBytes();
            uint ip = (uint)(b[0] << 24 | b[1] << 16 | b[2] << 8 | b[3]);
            return ((ulong)ip << 16) | (ushort)ep.Port;
        }

        public static IPEndPoint Unpack(ulong peer)
        {
            uint ip = (uint)(peer >> 16);
            var addr = new IPAddress(new[] { (byte)(ip >> 24), (byte)(ip >> 16), (byte)(ip >> 8), (byte)ip });
            return new IPEndPoint(addr, (int)(peer & 0xFFFF));
        }

        /// <summary>Parses "host:port" (or "port" for 127.0.0.1).</summary>
        public static ulong Parse(string s, int defaultPort)
        {
            s = s.Trim();
            string host = "127.0.0.1"; int port = defaultPort;
            int c = s.LastIndexOf(':');
            if (c >= 0) { host = s.Substring(0, c); port = int.Parse(s.Substring(c + 1)); }
            else if (int.TryParse(s, out var p)) port = p;
            else host = s;
            if (!IPAddress.TryParse(host, out var ip))
                ip = Array.Find(Dns.GetHostAddresses(host), a => a.AddressFamily == AddressFamily.InterNetwork);
            return Pack(new IPEndPoint(ip, port));
        }

        public void Send(ulong peer, byte[] data, bool reliable)
        {
            try { mSocket.SendTo(data, Unpack(peer)); }
            catch (SocketException) { /* unreachable peer: the session's timeout handles it */ }
        }

        public bool TryReceive(out ulong peer, out byte[] data)
        {
            peer = 0; data = null;
            while (mSocket.Available > 0)
            {
                EndPoint from = new IPEndPoint(IPAddress.Any, 0);
                int n;
                try { n = mSocket.ReceiveFrom(mBuf, ref from); }
                catch (SocketException) { continue; }
                peer = Pack((IPEndPoint)from);
                data = new byte[n];
                Buffer.BlockCopy(mBuf, 0, data, 0, n);
                return true;
            }
            return false;
        }

        public void Tick() { }
        public string Describe(ulong peer) => Unpack(peer).ToString();
        public void Dispose() => mSocket.Close();
    }
}
