# Hardspace: Shipbreaker (2022)

| | |
|---|---|
| Engine | Unity 2020.3.35f1, Mono, x64, HDRP; DOTS/ECS + Burst for ship parts |
| Versions tested | Steam, build 22302871, Shipbreaker.exe SHA-256 `7fc94c77eb1ed54b956f8572663f7893bb7fc407d2c1d3450f87343f93a3670f` |
| Projects | ShipbreakerCoop (BepInEx 5 plugin), repo: TODO public link |
| QA level | automated (milestone 1 only) |
| Last updated | 2026-11-10 |

## Where things stand

You can't play a shift together yet. Milestone 1 works: up to 4 players see each other as a puppet suit
that follows their position and view, smoothly, through death and respawn. Ships, salvage, cutting and the
shift timer are still separate per player.

## Existing work

None. No co-op mod, fork or official mode found (coop-anything directory, issues and PRs, 2026-11-10).
The developers said co-op would take the team months just to investigate. Single-player modding exists
(piepieonline/Hardspace-Shipbreaker-PieMods, BepInEx 5) and was used as a reference for the loader setup.

## How the game works (for co-op)

- **Code:** gameplay in `BBI.Unity.Game.dll` (namespace `BBI.Unity.Game`) plus Blackbird's `Carbon.*`
  framework. `Assembly-CSharp.dll` is nearly empty. Decompiles cleanly with dnSpyEx.
- **Netcode:** none used. `Carbon.Simulation.Commands.CommandRouter` is a lockstep RTS command system left
  over from Homeworld: Deserts of Kharak (only RTS commands); useful as a reference, not a shortcut.
- **Player:** `Player` is a MonoBehaviour with a `Rigidbody`; movement in `PlayerMotion : RigidbodyController`
  (PhysX forces in FixedUpdate). Read the pose from the Rigidbody, no ECS needed. The player GameObject
  lives in scene `master_game_container` regardless of the ship. No floating origin.
- **Ship parts:** ECS entities, split by an event pipeline (`RequestHierarchySplitEvent` ->
  `HandleQueuedSplitSystem` -> `HierarchySplitCompleteEvent`). Burst jobs can't be patched; hook the
  managed systems and events around them. Most tools (cutter, grapple, tether) raise events on
  `Main.EventSystem`, which are natural capture/replay points.
- **Steam:** stock Steamworks.NET `SteamManager` (`RestartAppIfNecessary(1161580)`, `RunCallbacks()` every
  frame). The shipped Steamworks.NET has `SteamNetworkingMessages` and lobbies, so a mod can use the game's
  own AppID: friends join through normal invites.
  - Gotcha: this Steamworks.NET's `SteamNetworkingMessage_t.Release()` is an instance method on a
    marshalled struct copy and can't free the native message. P/Invoke the flat
    `SteamAPI_SteamNetworkingMessage_t_Release(IntPtr)` from the game's `steam_api64.dll` instead (checked
    present with ctypes).
- **Saves and settings:** every path goes through `PlatformSettings.SaveDataPath` / `PersistentDataPath`
  (= `Application.persistentDataPath`, LocalLow\Blackbird Interactive\Hardspace_ Shipbreaker). Options in
  `user_config.ini` there, read by `LynxUserOptions`. The video section of that ini overrides `-screen-*`.

## What we changed

Milestone 1, ghost buddy:
- Host-authoritative session: host assigns ids, relays client poses, broadcasts the roster; unknown senders
  are answered with Bye so a dropped client rejoins at once. Ids can't be spoofed.
- Snapshot interpolation 100 ms behind the sender clock (min-filtered offset), 250 ms extrapolation, snaps
  across scene changes and teleports.
- Puppet: runtime primitives with HDRP materials (see Known issues), name tag in IMGUI.
- Lab isolation for copies on one PC: `-coop-savedir` sets the PlatformSettings paths (and again after
  `PlatformSettings.Initialize`), and Unity Analytics is switched off (it writes to the real LocalLow from
  native code after plugins load).

## QA

- Engine-free core: `dotnet run -c Release --project tests/CoreTests` -> `ALL OK (42 checks)`: protocol
  round trip and fuzz, interpolation under 15% loss/jitter/reorder (mean error 0.018 m), 3 players over a
  30% loss network, timeouts/rejoin, version reject, spoofing, real loopback UDP.
  `python -I tests/CoreTests/mutants.py` -> `ALL TWINS CAUGHT` (12 guards broken one at a time).
- Two real game copies: `python -I tools/lab/lab.py smoke` -> `ALL OK` (14 checks, 3 runs, 2026-11-10):
  join, ids, roster names, states both ways, puppets visible and moving on the right circle, 0 bad packets,
  RTT 4 ms at 240 fps, owner's save folder / PlayerPrefs / exe unchanged. In-game screenshot as evidence.
- Owner playtest in the bay (two copies, different ships): puppet smooth at all angles, host death and
  respawn kept the connection, leaving the bay hides the puppet. Logs: ~14k states each way, 0 errors.
- Not tested: Steam transport between two accounts.

## How to play

Not released yet. BepInEx 5 x64 in the game folder, the plugin in `BepInEx\plugins\ShipbreakerCoop\`;
F8 host, Shift+F8 Steam invite, F10 leave.

## Known issues

- Only presence: each player still breaks their own ship.
- The puppet is a simple primitive suit with no collision.
- Steam invites and joining are untested.

## Next steps

- Two-account Steam test.
- Milestone 2: make both copies load the same ship (seed or ship file from the host), then stable ids for
  parts, host-owned physics with ownership handover for grab/tether, receptacle deliveries.
- Swap the puppet for the game's suit mesh (`PRF_Shipbreaker_BodysuitMesh_HabVersion` in the hab content).
