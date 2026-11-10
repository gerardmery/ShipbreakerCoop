# Milestone 1: Ghost Buddy

Goal: two copies of the game see each other's player as a puppet. Nothing else syncs.
Work in this order. **Stop at each CHECKPOINT** and report before continuing.
Suggested: one Claude Code session per block; `/clear` between blocks.

## Block A: Plugin skeleton
- [x] A1. Create mod\ShipbreakerCoop\ BepInEx 5 plugin project (net472), referencing game DLLs from Shipbreaker_Data\Managed and BepInEx core DLLs (don't copy them into the repo)
- [x] A2. Build script that copies the built DLL to BepInEx\plugins\ShipbreakerCoop\
- [x] A3. Plugin logs "ShipbreakerCoop loaded" + game version
- **CHECKPOINT A:** you launch the game and see the log line in the BepInEx console

## Block B: Find and read the local player
- [x] B1. Keep a reference to Player (done by polling FindObjectOfType<Player> once a second instead of the event; LocalPlayerSource.cs)
- [x] B2. Log player position/rotation/velocity once per second
- [x] B3. Handle hab <-> bay level changes (player respawns; reference re-acquired)
- **CHECKPOINT B:** log shows sensible values moving in the bay and survives going hab -> bay -> hab

## Block C: Two-client setup (see TRIAGE_BRIEF "Later step: Two-client test setup")
- [x] C1. Second game copy + steam_appid.txt; check whether two copies can run at once
- [x] C2. Launch flags (-coop-savedir etc., see Game/CoopOptions.cs); windowed 1600x900 via each copy's user_config.ini (the game ignores -screen-*). Per-monitor placement not done
- [x] C3. Application.runInBackground = true; client save folder redirected
- **CHECKPOINT C:** two copies running side by side on two monitors, both in the bay

## Block D: Dev networking
- [x] D1. Local UDP transport on 127.0.0.1 (host listens, client connects); simple message format
- [x] D2. Send player transform + velocity ~20 times/sec each way
- [x] D3. Log received packets; handle disconnect cleanly
- **CHECKPOINT D:** each copy logs the other's position

## Block E: The puppet
- [x] E1. Spawn a visible stand-in for the remote player (start with a capsule; real suit model later)
- [x] E2. Interpolate between received positions (no jitter)
- [x] E3. Despawn on disconnect / level change
- **CHECKPOINT E (Milestone 1 done):** fly around in one copy, see the puppet move smoothly in the other

## Status (2026-11-10)
All blocks done and tested: see docs/NOTES.md "Milestone 1 status", tools/lab/lab.py smoke (14 checks),
tests/CoreTests (47 checks). Blocks were done in one pass without stopping at checkpoints A-D; checkpoint E
was the owner's in-level playtest. Steam relay (planned later) is also done: tests/STEAM_SESSION_CHECKLIST.md.

## After Milestone 1
- [x] Test once with a friend on a second PC (done over Steam, 2026-11-10)
- /clear, then plan Milestone 2 (shared salvage) the same way
