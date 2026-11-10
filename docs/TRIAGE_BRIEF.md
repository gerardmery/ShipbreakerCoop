# Shipbreaker Co-op: Triage Brief

Hand this to the coding agent with:
> Read https://github.com/cranebeaker/coop-anything and follow AGENTS.md.
> I want to play Hardspace: Shipbreaker in co-op with my friends.
> Project folder: D:\CGI\Projects\ShipbreakerCoop. Read docs/TRIAGE_BRIEF.md first.

## Known facts
- Install: C:\Program Files (x86)\Steam\steamapps\common\Hardspace Shipbreaker
- Engine: Unity, **Mono** build (MonoBleedingEdge present), so BepInEx 5 x64 + Harmony works
- Heavy DOTS/ECS + Burst (Shipbreaker_BurstDebugInformation_DoNotShip present).
  Burst jobs cannot be Harmony-patched; hook the managed systems around them.
- Unity version: **2020.3.35f1** (LTS). Matching Editor for opening the rip: 2020.3.35f1 via Unity Hub archive
- DOTS package versions: TODO (check Unity.Entities.dll / Unity.Physics.dll versions in Shipbreaker_Data/Managed)
- Existing mod to learn from: piepieonline/Hardspace-Shipbreaker-PieMods (Modded Ship Loader, BepInEx 5)
- No existing co-op mod. This is coop-anything **Path 2: build from zero**.
- Dev quote: co-op "would likely take our entire team months just to investigate if it's even possible."

## Reference material (local only, never commit)
- reference/ripped_project/: AssetRipper 2.0 Premium export
  (Script Level 2, Decompiled scripts, Prefab Outlining on, Dummy shaders, Streaming Assets extracted)
- decomp/: dnSpyEx output of Shipbreaker_Data/Managed/Assembly-CSharp.dll and game assemblies

## Phase 1: Triage (map what to sync)
Find and document in docs/SYNC_LIST.md:
1. Player controller and camera (movement in zero-g, tether)
2. Tools: cutter (+ splitsaw/demo charges), grapple, scanner, thrusters
3. Ship part entities: how parts are spawned, split by cuts, and moved (ECS components/systems)
4. Salvage receptacles: furnace, processor, barge; payout logic
5. Hazards: decompression, fuel/reactor/electrical, explosions
6. Shift system: timer, debt, save/load, hab/menu flow
7. Scene/level load flow (hab > shipbreaking bay)

## Milestones
1. **Ghost buddy**: second player visible as a puppet; positions over Steam relay
2. **Shared salvage**: piece transforms + ownership, grapple/tether, receptacle deliveries
3. **Cutting and hazards**: cut events/splits, decompression, explosions
4. **Progression**: shared shift timer, debt/payout model, host-only saves
5. **QA and ship**: coop-anything bots, chaos relay, desync tools; package; PR findings back

## Design defaults
- Host-authoritative: host runs physics and cutting; clients send intents (cut here, grab this)
- Steam relay networking via Spacewar (no port forwarding)
- Remote player model loaded from the player's own install at runtime, never shipped

## Later step: Two-client test setup (do at the start of Milestone 1)
Daily testing runs two copies on one PC; real-network checks use a second PC at each milestone.

### A. Two copies on one PC (daily)
1. Copy the install to a second folder (e.g. D:\Games\Shipbreaker_Client2\); separate BepInEx + mod build in each
2. Add steam_appid.txt containing `1161580` next to the second copy's Shipbreaker.exe
3. Launch both windowed, one per monitor (host on the main monitor, client on the second):
   `Shipbreaker.exe -screen-fullscreen 0 -screen-width 1600 -screen-height 900`
4. Mod sets `Application.runInBackground = true` so the unfocused copy keeps simulating
5. Mod has a dev transport: plain UDP over 127.0.0.1 (Steam relay only for real sessions)
6. Mod redirects the client's save folder via a launch flag (e.g. `-coopclient`) so saves don't collide
7. Lower graphics settings in both copies
- Day-one check: confirm the game allows a second copy to run; if not, patch out the single-instance check
- Drive client 2 with coop-anything tools: bots (scripted input), inspect/bridge (side-by-side state), chaos (fake lag/loss), gameslot/gamepool (launch/manage copies)

### B. Real second PC (at each milestone)
- Friend on their own PC and own copy, joining via Steam relay
- Or a laptop on a second Steam account
- Catches real-network issues the one-PC setup hides
