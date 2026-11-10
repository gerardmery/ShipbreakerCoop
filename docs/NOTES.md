# Shipbreaker Co-op: working notes

Observations (O), derived (D), guesses (G) and unknowns (U) are kept apart, as coop-anything asks.

## Game identity (2026-11-10)
- Store: Steam, AppID 1161580
- Exe SHA-256: 7fc94c77eb1ed54b956f8572663f7893bb7fc407d2c1d3450f87343f93a3670f (Shipbreaker.exe)
- Engine: Unity 2020.3.35f1, Mono, x64, HDRP
- Loader: BepInEx 5.4.23.6 (from BepInEx/LogOutput.log), HarmonyX in BepInEx/core
- Steam build id: 22302871 (appmanifest_1161580.acf)

## Milestone 1 (ghost buddy) findings

### O: player
- `BBI.Unity.Game.Player` is a MonoBehaviour with a `Rigidbody` (decomp Player.cs). Movement in
  `PlayerMotion : RigidbodyController` (FixedUpdate, PhysX forces). Position can be read from the
  Rigidbody, no ECS needed. The player also has an ECS entity (EntityBlueprintComponent) for vitality,
  respawn, god mode.
- `Player.Awake` posts `PlayerSpawnedEvent` on `Main.EventSystem`.
- No floating-origin code found (grep FloatingOrigin/OriginShift/WorldOrigin: nothing), so world
  positions are comparable between two copies in the same level.
- Levels are loaded additively through Addressables (`SceneLoader.cs:904`). The mod keys "same
  place" on the name of the scene that holds the Player GameObject.

### O: Steam
- `BBI.Unity.Game.SteamManager` = stock Steamworks.NET SteamManager: `RestartAppIfNecessary(1161580)`,
  `SteamAPI.Init()`, `SteamAPI.RunCallbacks()` every Update. So the mod can use normal `Callback<T>`.
- Shipped `Steamworks.NET.Plugins.dll` has `SteamNetworkingMessages` (Send/Receive/Accept/Close),
  `SteamMatchmaking`, `SteamNetworkingMessagesSessionRequest_t`, `LobbyCreated_t`.
- D: no Spacewar (480) needed. Everyone owns the game, so lobbies and relay run under the game's own
  AppID; friends see "Hardspace: Shipbreaker" and Join Game works natively. This deviates from the
  brief's "Spacewar" default, on purpose.

### O: save / settings paths
- All save paths go through `PlatformSettings.SaveDataPath` / `PersistentDataPath`
  (= `Application.persistentDataPath` on PC). Users: SaveFile.cs, LynxUserOptions.cs (user_config.ini),
  ErrorReporting.cs (Backtrace db), DebugSaveLoadService, ShipPreview.
- `PlatformSettings.Initialize()` is called from `Main.cs:134`. LynxUserOptions caches its ini path in
  `Initialize()` and logs `[LynxUserOptions] Initialized: <path>`: use that line to verify the redirect.
- No built-in override exists (IsSaveDataPathOverridden is only ever set false).

### O: single instance
- `Shipbreaker_Data/boot.config` contains `single-instance=` (empty value). Per the playbook (Graveyard
  Keeper, Unity 2020.3) remove the line in test copies only. U: whether the empty value enables it.

### Puppet model
- O: the rip has a full suit: `Content/Art/Structures/Kitbash/Hab/Models/PRF_Shipbreaker_BodysuitMesh_HabVersion.prefab`
  (+ helmet, backpack). U: whether it is loaded in the shipbreaking bay or only in the hab, and its
  Addressables key. M1 uses a primitive puppet (capsule + helmet + visor), built at runtime, nothing shipped.

## Design decisions (M1)
- Engine-free core (`mod/ShipbreakerCoop/Core`, no UnityEngine refs) holds protocol, session, snapshot
  interpolation and the UDP dev transport, so it is unit-tested without the game (`tests/CoreTests`).
- Host relays client states to the other clients; host assigns player ids (host = 1).
- No reliability layer yet: handshake/roster are re-sent periodically and are idempotent; 10 s silence
  drops a peer.
- Snapshot interpolation 100 ms behind the sender's clock (clock offset tracked with a min filter),
  up to 250 ms velocity extrapolation when packets run dry.

### O: lab findings (2026-11-10, two copies on one PC)
- Second copy runs fine with `single-instance=` removed from its boot.config (not tested with the line kept).
- The game ignores `-screen-*`; it applies [Video] FullScreenMode/Width/Height from its own user_config.ini.
  The lab writes Windowed 1600x900 into each copy's redirected ini.
- `CreatePrimitive` + default material renders NOTHING in HDRP (screenshot: name label, no body).
  Fixed: HDRP/Lit via Shader.Find / cloned scene material; visor HDRP/Unlit.
- Unity Analytics writes `Unity\<id>\Analytics\*` under the real persistentDataPath from native code, after
  plugins load. Lab copies switch it off (Analytics.enabled/initializeOnStartup = false); verified with a
  file watcher: 60 s host+client run, zero writes in the owner folder.
- RTT on localhost: 455 ms while the menu loads, 4 ms at steady state (240 fps both copies).
- U: in the first smoke run the owner's user_config.ini was rewritten (11:17:06, still the owner's own
  values). Not reproduced in 4 later runs with a watcher; Steam Cloud only tracks vg3_meta.dclf, and the only
  writer in BBI/Carbon used the redirected path. Cause unknown. Every smoke run hashes the folder.

## Milestone 1 status
- DONE and tested: engine-free core (42 checks, 12/12 mutation twins caught), plugin, UDP lab transport,
  lab tool, two-copy smoke test (14 checks, ALL OK x3), puppet renders (screenshot evidence).
- In-level test PASSED (2026-11-10, owner playtest, two lab copies, UDP, each on a different ship):
  client puppet "extremely smooth", follows all angles floating around the bay; host died and respawned
  with no connection loss (mod re-found the new Player); host leaving the bay -> "Host: elsewhere".
  Logs: ~14k states each way, 5-6 ms rtt, 0 bad packets, 0 exceptions.
  O: Player lives in scene 'master_game_container' in both copies, independent of the ship.
  Fixed after: status fps used smoothDeltaTime, reads 10000 while paused (timeScale 0); now unscaled.
- Steam test (2026-11-10, owner + a friend on separate PCs, two Steam accounts, owner report; logs not yet read):
  host F8 + Shift+F8 invite, friend joined; movement "as smooth" as the lab; friend leaving removed the puppet;
  friend rejoined via Join Game from the Steam friends list.
  Still to test over Steam: invite with the game closed (+connect_lobby),
  hard kill (10 s timeout), network drop and auto-rejoin, client death, alt-tab, hab<->bay, long session,
  3 players (relay).

## Milestone 2 findings (2026-11-10)

### O: ship file (decomp ShipPreview.cs, DebugSaveLoadService.cs, ModuleService.cs)
- `ShipPreview.Write`: header (end pos, version 5) + generated data (seed, construction asset ref, name, rewards,
  the whole serialized `ModuleGroup` layout) + spawned data (ECS snapshot of every `LynxSaveable` entity:
  cut meshes via MeshSaveLoadUtil, machine states). This is the game's own "ship in progress" save.
- Swap in the bay without a level reload (the debug service's path): `CurrentShipPreview.DespawnModuleAsync`,
  `ShipPreview.CreateFromFileAsync`, `SpawnModuleFromSaveDataAsync(spawnPoint)`. The spawn raises
  `ShipAllSpawnWrapperEvent(SpawnFromSave)` so `ModuleService.CurrentShipPreview` follows the new ship.
- `ShipMemberID` = FNV-1a hash chain from the seed (`FromShipPreview`, `CreateShipObjectHash`): stable
  part identity across machines for the same ship.

### O: probe in the lab (dev keys Ctrl+F5 save / Ctrl+F6 load, owner playtest)
- Save: 685 KB, 72 ms, 1817 entities in 171 chunks ('The Freddi', seed 390720898). Load: 1.1-1.8 s.
- A: save, cut a panel and a beam, load -> ship back to the saved state. B: processing after the swap pays out
  normally. C: the client's file loaded in the host copy -> same ship in both bays, puppets around it.
- Part fingerprint (count + hash of ShipMemberIDs) after loading the same file: host and client identical
  (1450 parts, ccbeb32ba3994524; client twice, host from a different starting ship).
- But a live ship (850 parts at save) and the same ship freshly loaded (1450) differ in ID'd entities.
  D: sync = host saves, then BOTH load the file; never keep the host's live ship and ship a copy.
- U: why the counts differ (saved entities vs entities spawned on load); file size of a fresh, uncut ship.

### O: first real ship sync in the lab (0.2.0 + diagnostics, owner playtest)
- Transfer works: 682-716 KB file, 0 bad packets, both copies load the same ship (name, seed) in ~1.3 s.
- Fingerprint right after a load is transient (old + new + template entities, ~1470); it settles within ~1 s
  (ShipSpawnCleanUpSystem marks the manifest's template children PendingDestruction). Settled host fingerprint ==
  live fingerprint before saving (859 parts, identical lists over two syncs): save -> reload is exact on the host.
- Client loading the host's file over a DIFFERENT ship: settles at 153 of 859 parts (strict subset: 706 missing).
  Same client loading the same file again over that (now same) ship: 855 of 859. Missing 4: 0x4e, 0x4f
  (counter-like ids, maybe runtime split pieces) + 2 hash ids. No errors or warnings in either Player.log.
- D: swapping out a different ship in the bay breaks the new spawn on the client. U: the mechanism (candidate:
  ShipSpawnCleanUpSystem takes ShipManifest query [0], or the old ship's despawn still running).
- Fix candidates: (a) client loads via a level reload (SceneLoader.TearDownAndLoadLevelRefWithShipPreviewAsync,
  SYNC_LIST section 7), the game's own path for a new ship; (b) despawn, wait until no old ShipManifest / pending
  destruction remains, then spawn; (c) load twice. Not decided.

- Run 3 (wait-for-old-ship + step trace): no hang; every step done in ~1.3 s on both sides; the wait found the old
  ship already gone (0 ms), so it was not the cause. Client 856 vs host 885: 29 missing on the client, 0 extra:
  14 consecutive counter-like ids (0x53-0x60) + 15 hash ids. Client results vary per run (153, 855, 856): the
  loss is nondeterministic. Next: dump component types per id to see what the missing objects are.
- Client hang (run 2, client F6 pressed by mistake, then host F6): client Not Responding, ~0 CPU (blocked, not
  looping), BepInEx log ended at "receiving blob" (buffered: last lines may be lost). Not reproduced in run 3.
  U: cause. The flushed step trace (Coop/sync_trace.txt) is there to catch it next time.

- Run 4 (component types per id): client 841 vs host 872, 31 missing, 0 extra. 14 missing are HierarchyRoot
  entities (Rigidbody, RoomOccupancy, no ShipMemberComponent; the counter-like ids): the physics bodies that group
  parts. The rest are structure parts, salvage parts and 3 SpaceTruckPartExtractionController interactables.
  D: the client ends with a different grouping of parts into physics bodies, varying per run (recomputed after
  load, timing-dependent?). This blocks M2 (the bodies are what gets synced). Next: read how HierarchyRoots are
  created/assigned after SpawnModuleFromSaveDataAsync (HierarchyProcessor, HandleQueuedSplitSystem,
  StructureGraphSystem) and make the client keep the saved grouping.

### O: how hierarchy roots are made (research 2026-11-10)
- Root id = graph key from one session-wide counter (ECSGraphSystem.mGraphKeyCounter, GetNextAvailableGraphKey
  ECSGraphSystem.cs:964; public settable NextAvailableGraphKey). That is why root ShipMemberIDs are small and
  consecutive.
- HierarchyProcessor.UpdateHierarchyParents creates a HierarchyParent per dirty active key it has no parent for
  (HierarchyProcessor.cs:383-420). HierarchyParent.InstantiatePrefabInternal (HierarchyParent.cs:172-220) reattaches
  to the saved root only if ModuleService.ShipSpawningFromSaveInProgress and ShipMemberID(graphKey) is in
  CurrentShipPreview.ShipMemberIDToLoadedEntitiesMap; otherwise a fresh root without ShipMemberID.
- On load, ShipSpawnSystem.LoadingFixupHierarchies (ShipSpawnSystem.cs:395-430) registers each saved root's graph
  key as active+dirty (TryAddUnique) and raises the counter past it.
- Candidate miss reasons: flag already off, key renumbered (not in map), or key already active (TryAddUnique false).
  RootDiagnostics (dev only) logs, per root: key, flag, map hit, matched/fresh, into Coop/sync_trace.txt.

- Run 5 (RootDiagnostics): host created 18 roots (keys 42, 61..98), all matched to saved entities. Client created
  only 3 (42, 61, 83), all matched; keys 84..98 were never created. Client's old ship used keys 59..103.
  D: HierarchyProcessor still held the old ship's HierarchyParents for those keys: UpdateHierarchyParents only
  creates a parent for a key it has none for, and CleanUpOldParents skips while the graph has queued changes
  (HierarchyProcessor.cs:697), which it does right after a despawn. Fix: after despawn also wait until
  HierarchyProcessor.mHierarchyParents is empty (ShipFiles.WaitForOldShipGone).

- Run 6 (wait for parents to clear): waiting does not help. After 10 s the client still held 19 old parents
  (38,60,84..100; host 2: 95,96) and again skipped keys 84..94. CleanUpOldParents runs only at the end of
  UpdateHierarchyParents (HierarchyProcessor.cs:679) and returns early while the graph has queued changes.
  Fix: during the wait, call HierarchyProcessor.CleanUpOldParents (private, via reflection) when the graph has no
  queued changes, until no parent with an inactive key is left.

- Run 7 (call CleanUpOldParents): **IN SYNC**. Host and client 862:01627d2ca9b1686c, 21 roots each, all matched
  (0 fresh). One cleanup call per side, old ship gone in 9-14 ms. 0 exceptions, 0 bad packets.
  Still left after despawn on both: 2 parents with still-active keys (host 98,99; client 106,107). Harmless
  here; would matter if the host's saved ship uses one of those keys. Watch in later runs.

- Run 8 (repeat after a cut): owner cut the antenna off on the host, F6: the antenna is gone on the client too
  (owner check). IN SYNC again: 859:229a076868c020f2 on both (862 before the cut). Ship sync = initial state
  incl. cuts. Not yet: live changes between syncs (cuts, moved pieces) - next M2 step.

- Run 9 (moved panels): owner moved panels on the host, F6: they land in about the same spot on the client
  (owner check). IN SYNC: 860:da32aa30d166807a on both; live fingerprints still equal afterwards.
  Three IN SYNC runs in a row (fresh ship, after a cut, after moving panels).

## Log
- 2026-11-10 (later): M2 step 1 (ship sync) working in the lab after 8 runs; see "Milestone 2 findings".
- 2026-11-10: triage of player/Steam/save paths (above). Built M1: core + tests + plugin + lab.
