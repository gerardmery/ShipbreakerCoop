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

## Next
- [ ] Inspect Carbon.Network.dll
- [ ] Find the player controller / movement (search: Movement, Locomotion, Thruster, Character)
- [ ] Find processor, barge, salvage scoring, shift timer, save system
- [ ] Read RequestHierarchySplitEvent: who raises it, what data it carries
- [ ] Check what AntiCheatToolKit protects
- [ ] Export BBI.Unity.Game + Carbon.* to decomp\ for full-text search
