# Milestone 2: Shared salvage

Goal: both players break the same ship and see the same pieces move. Host-authoritative: the host runs physics,
cutting and salvage; clients send intents and show what the host decides.

## Step 1: Ship sync (DONE, lab-tested)
- [x] Host F6 sends its ship file; both load it; identical part fingerprints (3 lab runs: fresh, after a cut, after
      moving panels). Fix for stale physics bodies on the client (docs/NOTES.md, runs 1-9).
- [x] Client never saves the host's ship as its own ship in progress (code in place; never triggered in a test yet).
- [ ] Over Steam (deferred: tests/STEAM_SESSION_CHECKLIST.md section 6).

## Research that shapes steps 2-4 (2026-11-10, sources in the decomp)
- Cut pieces keep the original part's `ShipMemberID` unchanged (ShatterableComponent.cs:378-389,
  ShipManifestSystem.cs:226). After cuts, IDs are not unique per piece. The save system copes with a multimap.
- Every split goes through `RequestHierarchySplitEvent {StructurePart Part; SplitInfo}`, handled by
  `HierarchyProcessor.OnRequestHierarchySplit` (HierarchyProcessor.cs:209). Cutter, demo charge and mega-cut planes
  are deterministic. Explosions, impacts, arcs and integrity-empty use `UnityEngine.Random` to build the planes,
  but before this event, so capturing the event captures the result.
- Pieces come out in hull order (CuttingService.cs:72-107). Planes are world space, converted with the part's
  transform (CuttingService.cs:41-55): send them in part-local space.
- Replay risks: follow-through splits iterate a `HashSet` (HierarchyProcessor.cs:289); elemental, vaporize and
  object-limit checks are local; at most 15 splits are processed per frame.
- Physics bodies: graph keys are local to each machine and can't be matched. `Rigidbody` is on the
  HierarchyParent's GameObject (HierarchyParent.cs:15). The game re-applies isKinematic when a key is dirtied
  (HierarchyParent.cs:147-160, 257-355). Grab, grapple and tether only apply forces (HandGrab.cs:936-971,
  GrapplingHook.cs:807).
- Joint and bolt breaks are graph-edge removals, not mesh splits: `RemoveHierarchyJointsEvent`
  (BreakableJointComponent.cs:449). Scalpel goes through `VaporizationComponent`.
- No gameplay networking or replay system exists; hook points are `Main.EventSystem` events.

## Design
- **Net ids (the mod's own):** after a ship sync, every part with a unique ShipMemberID maps 1:1 on both sides. Each
  replicated split assigns its pieces ids `(split sequence, hull index)` on the host. The client assigns the same
  ids, because hull order is deterministic for the same mesh and local planes.
- **Cuts (host -> clients):** the host captures each executed split at `RequestHierarchySplitEvent` (part net id,
  planes in part-local space, gap, forces) and broadcasts it in order with a sequence number. The client fires
  the same event on its matching part, and suppresses its own local splits while in a synced session.
- **Joint breaks:** the same pattern with `RemoveHierarchyJointsEvent` (edge between two net ids).
- **Bodies:** a body's net id is the smallest net id among its parts, which both sides compute from the same
  grouping. The host streams poses (position, rotation, velocity) of awake bodies at 10-20 Hz. The client keeps
  those bodies kinematic and interpolates (the snapshot buffer from milestone 1), re-applying kinematic after
  graph updates.
- **Client actions:** cut, grab, grapple and tether become intents sent to the host ("cut part X with plane P",
  "grab body B"). The host performs them and the result comes back through the streams above.
- **Check:** after every replicated split both sides publish a fingerprint that counts duplicate ShipMemberIDs too.
  The lab check compares them within ~2 s of each split.

## Steps
- [ ] 2. Cut replication host -> client (host cuts only; client cutting blocked in a session). Lab test: an automated
      host "auto-cut" dev mode cuts N random parts with fixed planes; the check requires identical fingerprints after
      each one. Also test a real cutter cut and a demo charge by hand.
- [ ] 3. Body pose streaming (host -> client), kinematic on the client. Lab test: host pushes pieces (dev mode
      applies forces); client poses within tolerance of the host's.
- [ ] 4. Client intents: cutting first, then grab, grapple and tether (host-owned, with ownership hand-off while
      holding).
- [ ] 5. Salvage deliveries (furnace, processor, barge) decided on the host; payout per player is milestone 4.
- [ ] Big Steam session with all of the above plus the deferred checklist.
