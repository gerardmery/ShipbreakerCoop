# Hardspace: Shipbreaker (2022)

| | |
|---|---|
| Engine | Unity 2020.3.35f1, Mono, x64, HDRP; DOTS/ECS + Burst for ship parts |
| Versions tested | Steam, build 22302871, Shipbreaker.exe SHA-256 `7fc94c77eb1ed54b956f8572663f7893bb7fc407d2c1d3450f87343f93a3670f` |
| Projects | ShipbreakerCoop (BepInEx 5 plugin), repo: [github.com/gerardmery/ShipbreakerCoop](https://github.com/gerardmery/ShipbreakerCoop) |
| QA level | automated (players visible; ship sync in the lab) |
| Last updated | 2026-11-10 |

## Where things stand

You can't play a shift together yet. Players see each other as puppet suits (Steam tested between two PCs),
and the host can send their ship to everyone (F6): both machines then hold the same ship, cuts and moved
panels included, verified part by part. Changes after that sync (cuts, moved pieces, salvage) are not
shared live yet.

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
  are answered with Bye so a dropped client rejoins at once. Ids can't be spoofed. The host admits only members
  of its current Steam lobby (Steam keeps old sessions open, so the transport's session-request check alone let
  a player from an earlier lobby back in without an invite).
- Snapshot interpolation 100 ms behind the sender clock (min-filtered offset), 250 ms extrapolation, snaps
  across scene changes and teleports.
- Puppet: runtime primitives with HDRP materials, name tag in IMGUI.
- Lab isolation for copies on one PC: `-coop-savedir` sets the PlatformSettings paths, Unity Analytics off.

Milestone 2, step 1, ship sync:
- The game can save a live ship to a file and swap it into the bay without a level reload
  (`ShipPreview.WriteToFileAsync`, `DespawnModuleAsync`, `CreateFromFileAsync`, `SpawnModuleFromSaveDataAsync`,
  the path of its own `DebugSaveLoadService`). The file holds the generated layout (`ModuleGroup`) and an ECS
  snapshot of every `LynxSaveable` entity: cuts, meshes, machine states. About 700 KB, saved in about 70 ms.
- Host F6: save, send the file (reliable chunked transfer over the unreliable channel), then the host loads the
  same file itself: a live ship and the same ship freshly loaded differ in their ID'd entities.
- Part identity: `ShipMemberID`, a seed-derived hash chain for structure parts; physics bodies (`HierarchyRoot`)
  use graph keys from a session-wide counter as their id.
- The bug that took 8 lab runs: loading over a different ship left the client with fewer physics bodies, a
  different number each run. The old ship's `HierarchyParent`s stay in `HierarchyProcessor` after a despawn:
  `CleanUpOldParents` only runs at the end of `UpdateHierarchyParents` and skips while the structure graph has
  queued changes. Graph keys the old and new ship share then reuse the stale parent, and the saved root is never
  re-attached. Fix: after the despawn, call `CleanUpOldParents` (private, via reflection) once the graph has no
  queued changes. Found with a per-root trace (key, from-save flag, saved-map hit, matched or fresh).
- The client never saves the host's ship as its own career "ship in progress" (Harmony prefix on
  `PlayerProfileService.TrySavePlayerProfileShip`; the game writes it in `SceneLoader.PreTearDown`, also to the cloud).

## QA

- Engine-free core: `dotnet run -c Release --project tests/CoreTests` -> `ALL OK (73 checks)`: protocol fuzz,
  interpolation under loss and jitter, sessions over a 30% loss network, version/spoof/admission checks, a 700 KB
  transfer to 2 clients at 30% loss, edge sizes, corruption, impostors, host loss, reports.
  `python -I tests/CoreTests/mutants.py` -> `ALL TWINS CAUGHT` (20 guards broken one at a time).
  The corruption test found a real inefficiency: chunks sent together with the transfer's start packet were
  dropped when they overtook it; senders now wait for the start ack.
- Two real game copies on one PC: `python -I tools/lab/lab.py smoke` -> `ALL OK` (14 checks, presence).
  `python -I tools/lab/lab.py shipsync` -> `ALL OK` (8 checks) three runs in a row: fresh ship, after cutting the
  antenna off on the host, after moving panels; identical part fingerprints (count + hash of every
  `ShipMemberID`), checked visually by the owner.
- Steam between two PCs and accounts (0.1.0): invite, Join Game, invite with the game closed, friend leaving,
  End task. The 0.1.1 fixes found there (host leaving, lobby admission) are not yet retested over Steam.

## How to play

Not released yet. BepInEx 5 x64 in the game folder, the plugin in `BepInEx\plugins\ShipbreakerCoop\`;
F8 host, Shift+F8 Steam invite, F10 leave.

## Known issues

- No live sync yet: after a ship sync, cuts, moved pieces and salvage stay per player until the next sync.
- The puppet is a simple primitive suit with no collision.
- Ship sync is lab-tested only; not yet over Steam.
- Joining does not load the joiner into the host's level.

## Next steps

- Live sync: host-owned physics for loose pieces (stream poses keyed by `ShipMemberID` / graph key), clients ask
  the host before grabbing; then cut events and receptacle deliveries.
- Joiner goes straight to the host's ship: `SceneLoader.TearDownAndLoadLevelRefWithShipPreviewAsync` is public.
- Swap the puppet for the game's suit mesh (`PRF_Shipbreaker_BodysuitMesh_HabVersion`).

