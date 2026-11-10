# Sync List (first pass, from dnSpy type search 2026-11-10)

## Where the code lives (Shipbreaker_Data\Managed)
| DLL | Size | Role |
|---|---|---|
| BBI.Unity.Game.dll | ~4.1 MB | **Main gameplay code** (namespace BBI.Unity.Game) |
| BBI.Unity.Core.dll / BBI.Unity.Game.Data.dll | small | Core helpers, data |
| Carbon.*.dll | various | Blackbird's in-house engine framework |
| Carbon.Simulation.dll | | Sim framework incl. legacy lockstep CommandRouter |
| Carbon.Network.dll | | Network layer (not yet inspected) |
| Steamworks.NET.Plugins | | Steamworks.NET already shipped with the game |
| Assembly-CSharp.dll | 156 KB | Almost empty; third-party plugins only |
| AntiCheatToolKit.dll | | Present; check what it guards (likely value obfuscation) |

## Systems found (type names)
### Cutter
CuttingToolController (+ CutterMode), CutterScript (+ CutterState), CutterAsset, ICutterData,
CutterModeChangedEvent, CutterObjectContainer, CutterDurabilityHandler, CutterHeatBarUIController

### Grapple
GrapplePushController, GrappleRaycastSystem, GrapplePushRaycastSystem (ECS systems),
GrappleEvent (+ GrappleState), GrapplePushChargeChangedEvent, GrappleObjectContainer,
GrappleCrosshairController, GrappleLineSettingsAsset

### Tether
Tether, TetherController (+ TetherState, CandidateState), TetherChangedEvent,
ObjectTetheredEvent, TethersAsset, ITetherData

### Splitting / cutting ship parts (ECS, event-driven)
RequestHierarchySplitEvent, HandleQueuedSplitSystem, HierarchyProcessor.QueuedSplit,
HierarchySplitCompleteEvent, RemoveSplitFromEntitySystem, NewSplitEntity, SplitFromEntity,
QueuedSplitPatch, ShadowSplitData, ShatterableComponent

### Salvage
FurnaceVolume, PreFurnaceVolume, FurnaceVFXTag (processor/barge: TODO search)

### Player
CameraFollowPlayer, DroneFollowPlayer, ElementalPlayerDeathHandlerSystem (player controller: TODO)

## Key findings
1. **Events everywhere.** Cutter, grapple, tether and splits all raise events
   (CutterModeChangedEvent, TetherChangedEvent, ObjectTetheredEvent, RequestHierarchySplitEvent,
   HierarchySplitCompleteEvent). Events are natural hook points: capture on host, replay on client.
2. **Splits are requested, then processed by an ECS system.** Hooking RequestHierarchySplitEvent
   (or what raises it) may let us send cut requests to the host and broadcast results.
   This is the most promising lead for milestone 3.
3. **Legacy lockstep netcode exists but is RTS-only.** Carbon.Simulation.Commands.CommandRouter
   (peer state, per-frame command sync, checksums, player drop) is from Homeworld: Deserts of Kharak.
   Its only commands are RTS ones (AbilityCommand, CancelProductionCommand, BroadcastMapPingCommand...).
   Not used by Shipbreaker gameplay; useful as a reference, not a shortcut.
4. **Steamworks.NET is already in the game**, so the mod can use it for Steam relay networking.
5. **A mod can start a shift on a given ship** (section 7).
6. **Ships save and load as files, with stable part IDs** (see docs/NOTES.md "Milestone 2 findings"):
   `ShipPreview.WriteToFileAsync` / `CreateFromFileAsync` + `SpawnModuleFromSaveDataAsync`; `ShipMemberID` is a
   seed-derived hash chain. Lab-tested: same file on two copies -> identical part fingerprints.

## 6. Shift system
### Ship in progress save (T2, Claude 2026-11-10)
Observed:
- Only writer: `SceneLoader.PreTearDown` (SceneLoader.cs:285-307). In a **Career** session it calls
  `PlayerProfileService.TrySavePlayerProfile` and `TrySavePlayerProfileShip(ModuleService.Instance.CurrentShipPreview)`
  when tearing down (a) from the hab to the front end (quit to menu), or (b) to the hab without the front end
  (back to the hab: end of shift / leaving the bay). Skipped while front-end actions are blocked (cutscenes).
- `PlayerProfileSaveLoadManager.TrySavePlayerProfileShip` (PlayerProfileSaveLoadManager.cs:115-131) =
  `ShipPreview.WriteToFileAsync(profile.ShipInProgressFileName())` then `SaveFile.CloudSavePlayerProfileShip`
  (cloud copy too).
- File: `SaveFile.MakePlayerProfileShipFilePath` = the profile save path with extension `.ship` (SaveFile.cs:106).
- Deleted on new profile (PlayerProfileSaveLoadManager.cs:49) and profile delete (:86).

Derived (co-op risk):
- A client who loaded the host's ship and then goes back to the hab or quits saves **the host's ship as their
  own ship in progress**, locally and to the cloud. Fix before real play with ship sync: Harmony prefix on
  `PlayerProfileService.TrySavePlayerProfileShip` that skips the save on a client while a synced ship is
  loaded (or back up the client's own `.ship` before the swap and restore it after).
- The profile save on the same path (credits/XP from the host's ship) is the same question; milestone 4.

## 7. Scene / level load flow
(T1 by the local agent, checked and corrected by Claude 2026-11-10.)

Observed:
- Hab "Start Shift": `HabScreenController.StartShift()` (HabScreenController.cs:143). If
  `ModuleService.NextShipPreview` is set and differs from the current ship, it picks the career level
  (`Main.Instance.MainSettings.CareerLevelSettings.CareerLevelAssetRef`, tutorial level if the tutorial isn't
  done) and calls `SceneLoader.Instance.TearDownAndLoadLevelRefWithShipPreviewAsync(levelRef, NextShipPreview,
  false, false)`. Otherwise `SceneLoader.Instance.UnloadHabAndResumeShiftAsync()` (resume the current ship).
- `SceneLoader.TearDownAndLoadLevelRefWithShipPreviewAsync(AssetReferenceT<LevelAsset>, ShipPreview,
  bool bLoadWithHabStart = false, bool bDoQuitFade = true)` is **public** `async void` (SceneLoader.cs:206); it
  refuses to run while another TearDownAndLoad is executing (log warning).
- `ModuleService.NextShipPreview` has a private setter, set from an event handler (ModuleService.cs:76, 239).
- A file-based variant exists but is private: `TearDownAndLoadLevelRefWithShipPreviewFileAsync_Internal`
  (SceneLoader.cs ~609, uses `ShipPreview.CreateFromFileAsync`).
- `SceneLoader.EndProcess` posts `GameStateChangedEvent.GetEvent(GameSession.GameState.Gameplay)` (SceneLoader.cs:420).

Derived (for the mod):
- Start a shift on a host's ship: `ShipPreview.CreateFromFileAsync(file)` then
  `SceneLoader.Instance.TearDownAndLoadLevelRefWithShipPreviewAsync(careerLevelRef, preview, false, false)`.
  Use: lab automation (no menu clicking) and "joiner goes to the host's ship".
- Wait for "in the bay" on `GameStateChangedEvent` with `GameState.Gameplay`.

Unknown:
- Whether the preview's spawned-data block (cut state) is used on this path, or only the generated ship.
- What `bLoadWithHabStart` / the frontend transition change; whether it works from the main menu (no profile loaded).

## Next
- [ ] Inspect Carbon.Network.dll
- [x] Find the player controller / movement: Player + PlayerMotion (docs/NOTES.md)
- [ ] Find processor, barge, salvage scoring, shift timer, save system (QWEN_TASKS T2-T4)
- [ ] Read RequestHierarchySplitEvent: who raises it, what data it carries
- [ ] Check what AntiCheatToolKit protects
- [ ] Export BBI.Unity.Game + Carbon.* to decomp\ for full-text search
