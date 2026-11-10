# Steam session checklist (milestone 1, two accounts on two PCs)

Ticked = done in a real session. Source: owner report unless a log is named. Mod 0.1.0, zip sha256 7f47d7d1…1043.

## 1. Ways of joining
- [x] Invite while the friend's game is running (host F8, Shift+F8, friend accepts), 2026-11-10
- [x] Join Game from the Steam friends list, 2026-11-10
- [x] Invite while the friend's game is closed: game launched, mod auto-joined at the main menu ("Host: elsewhere" until the friend enters the bay; M1 does not load the joiner into the host's level), 2026-11-10

## 2. Leaving and dropping out
- [x] Friend quits normally: suit vanishes on host, 2026-11-10
- [x] Friend's game killed in Task Manager: suit gone "after a few seconds" (timeout path), friend rejoined via Steam, back at the menu showing "elsewhere", 2026-11-10
- [ ] Host leaves (F10): friend sees the host disappear

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

## Evidence to collect
- [ ] Host BepInEx\LogOutput.log
- [ ] Friend BepInEx\LogOutput.log
