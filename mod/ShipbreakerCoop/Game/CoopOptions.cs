using System;
using BepInEx.Configuration;

namespace ShipbreakerCoop.Game
{
    /// <summary>
    /// Settings from BepInEx/config/shipbreakercoop.cfg, overridden by the command line:
    ///   -coop-host                 host at start (once Steam is up, for the steam transport)
    ///   -coop-join &lt;target&gt;        join: "ip:port" (udp) or a lobby id (steam)
    ///   +connect_lobby &lt;id&gt;        what Steam passes when a friend's invite launches the game
    ///   -coop-transport udp|steam
    ///   -coop-port &lt;n&gt;             udp listen port for the host (default 27960)
    ///   -coop-name &lt;name&gt;
    ///   -coop-savedir &lt;dir&gt;        lab only: keep this copy's saves and settings in &lt;dir&gt;
    ///   -coop-status &lt;file&gt;        lab only: write a JSON status file twice a second
    ///   -coop-fake circle          lab only: send a fake player circling in front of the camera
    ///   -coop-dev                  dev keys: Ctrl+F5 save current ship to a file, Ctrl+F6 load it back
    /// </summary>
    public sealed class CoopOptions
    {
        public string Transport = "steam";
        public int UdpPort = 27960;
        public string UdpJoin = "127.0.0.1:27960";
        public string Name;
        public bool ShowHud = true;

        public bool AutoHost;
        public string Join;
        public string SaveDir;
        public string StatusFile;
        public string Fake;
        public bool Dev;

        public static CoopOptions Load(ConfigFile cfg, string[] args)
        {
            var o = new CoopOptions
            {
                Transport = cfg.Bind("Network", "Transport", "steam",
                    "steam = Steam lobby + relay (real sessions). udp = plain UDP (LAN / two copies on one PC).").Value,
                UdpPort = cfg.Bind("Network", "UdpPort", 27960, "UDP port the host listens on.").Value,
                UdpJoin = cfg.Bind("Network", "UdpJoinAddress", "127.0.0.1:27960", "Where F9 joins with the udp transport.").Value,
                Name = cfg.Bind("Player", "Name", "", "Name shown above your suit. Empty = your Steam name.").Value,
                ShowHud = cfg.Bind("UI", "ShowHud", true, "Show the small co-op status box (top left).").Value,
            };
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException(a + " needs a value");
                switch (a.ToLowerInvariant())
                {
                    case "-coop-host": o.AutoHost = true; break;
                    case "-coop-join": o.Join = Next(); break;
                    case "+connect_lobby": o.Join = Next(); o.Transport = "steam"; break;
                    case "-coop-transport": o.Transport = Next().ToLowerInvariant(); break;
                    case "-coop-port": o.UdpPort = int.Parse(Next()); break;
                    case "-coop-name": o.Name = Next(); break;
                    case "-coop-savedir": o.SaveDir = Next(); break;
                    case "-coop-status": o.StatusFile = Next(); break;
                    case "-coop-fake": o.Fake = Next(); break;
                    case "-coop-dev": o.Dev = true; break;
                }
            }
            if (o.Transport != "udp" && o.Transport != "steam") o.Transport = "steam";
            return o;
        }
    }
}
