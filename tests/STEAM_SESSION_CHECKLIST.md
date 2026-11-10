# Steam session checklist (milestone 1, two accounts on two PCs)

Ticked = done in a real session. Source: owner report unless a log is named. Mod 0.1.0 (sections ticked so far), zip sha256 7f47d7d1…1043. Fix: 0.1.1.

## 1. Ways of joining
- [x] Invite while the friend's game is running (host F8, Shift+F8, friend accepts), 2026-11-10
- [x] Join Game from the Steam friends list, 2026-11-10
- [x] Invite while the friend's game is closed: game launched, mod auto-joined at the main menu ("Host: elsewhere" until the friend enters the bay; M1 does not load the joiner into the host's level), 2026-11-10

## 2. Leaving and dropping out
- [x] Friend quits normally: suit vanishes on host, 2026-11-10
- [x] Friend closed the game with Task Manager "End task" (Processes tab): suit gone after a few seconds, rejoined via Steam, 2026-11-10.
      Host log says "left", not "timed out": End task closes politely, so the game still sent its goodbye.
- [ ] Real crash: Task Manager > Details > Shipbreaker.exe > End task. Host log must say "timed out" (~10 s)
- [~] Host leaves: suit disappeared, but the friend stayed in a dead lobby retrying forever (0.1.0 bug; also: re-hosting let him straight back in without an invite). Fixed in 0.1.1, retest

## 3. Network blips
- [ ] Friend's Wi-Fi off ~5 s: carries on by itself
- [ ] Friend's Wi-Fi off ~20 s: friend's copy rejoins on its own

## 4. Normal play
- [x] Movement smooth over Steam, 2026-11-10
- [ ] Client dies and respawns (host side done in the lab)
- [ ] Alt-tab / minimize for a minute: suit keeps moving on the other screen
- [ ] One player goes to the hab: "elsewhere", suit returns on coming back
- [ ] 30+ minute session: ping at start and end ___ / ___ ms, any fps change

## 5. Three players
- [ ] Third player joins; the two clients see each other (host relays)

## 6. Ship sync over Steam (0.2.0+, deferred to the next big Steam session)
- [ ] Both in the bay, host F6: friend's ship replaced, both boxes IN SYNC; note how long "receiving" takes
- [ ] After the friend returns to the hab or quits: their own career ship in progress is unchanged
      (back up their LocalLow save folder first, or use a fresh career profile)
- [ ] Later builds: live sync items added here as they land

## Evidence to collect
- [x] Host BepInEx\LogOutput.log (0.1.0 session): local/logs/2026-11-10-steam/host-0.1.0.log. 0 errors, 6 joins / 6 clean leaves, 3 Steam session requests (sessions are reused), puppet material came from a particle material (fixed in 0.1.1)
- [x] Friend BepInEx\LogOutput.log (last launch only): closed-game invite confirmed; re-host admission gap confirmed from the client side; local/logs/2026-11-10-steam/SUMMARY.md
