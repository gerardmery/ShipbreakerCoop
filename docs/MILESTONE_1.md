# Milestone 1: Ghost Buddy

Goal: two copies of the game see each other's player as a puppet. Nothing else syncs.
Work in this order. **Stop at each CHECKPOINT** and report before continuing.
Suggested: one Claude Code session per block; `/clear` between blocks.

## Block A: Plugin skeleton
- [ ] A1. Create mod\ShipbreakerCoop\ BepInEx 5 plugin project (net472), referencing game DLLs from Shipbreaker_Data\Managed and BepInEx core DLLs (don't copy them into the repo)
- [ ] A2. Build script that copies the built DLL to BepInEx\plugins\ShipbreakerCoop\
- [ ] A3. Plugin logs "ShipbreakerCoop loaded" + game version
- **CHECKPOINT A:** you launch the game and see the log line in the BepInEx console

## Block B: Find and read the local player
- [ ] B1. Subscribe to PlayerSpawnedEvent via Main.EventSystem; keep a reference to Player
- [ ] B2. Log player position/rotation/velocity once per second
- [ ] B3. Handle hab <-> bay level changes (player respawns; reference re-acquired)
- **CHECKPOINT B:** log shows sensible values moving in the bay and survives going hab -> bay -> hab

## Block C: Two-client setup (see TRIAGE_BRIEF "Later step: Two-client test setup")
- [ ] C1. Second game copy + steam_appid.txt; check whether two copies can run at once
- [ ] C2. Launch flags: -coopclient, windowed sizes per monitor
- [ ] C3. Application.runInBackground = true; client save folder redirected
- **CHECKPOINT C:** two copies running side by side on two monitors, both in the bay

## Block D: Dev networking
- [ ] D1. Local UDP transport on 127.0.0.1 (host listens, client connects); simple message format
- [ ] D2. Send player transform + velocity ~20 times/sec each way
- [ ] D3. Log received packets; handle disconnect cleanly
- **CHECKPOINT D:** each copy logs the other's position

## Block E: The puppet
- [ ] E1. Spawn a visible stand-in for the remote player (start with a capsule; real suit model later)
- [ ] E2. Interpolate between received positions (no jitter)
- [ ] E3. Despawn on disconnect / level change
- **CHECKPOINT E (Milestone 1 done):** fly around in one copy, see the puppet move smoothly in the other

## After Milestone 1
- Test once with a friend on a second PC (Steam relay is a later block)
- /clear, then plan Milestone 2 (shared salvage) the same way
