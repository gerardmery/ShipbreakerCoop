# Shipbreaker Co-op plugin (Milestone 1: ghost buddy)

Every player sees the others as a puppet suit with a name tag. Nothing else is shared yet: each player
still has their own ship, salvage and timer.

## Build
```
cd mod/ShipbreakerCoop
dotnet build -c Release            # -> bin/Release/ShipbreakerCoop.dll
dotnet build -c Release -p:GameDir="D:\path\to\Hardspace Shipbreaker"   # other install
```
Game DLLs are referenced in place from the install; nothing from the game is copied or committed.

## Play
Put `ShipbreakerCoop.dll` in `BepInEx\plugins\ShipbreakerCoop\` (BepInEx 5 x64).

| Key | Action |
|---|---|
| F8 | Host (Steam: creates a friends-only lobby) |
| Shift+F8 | Steam invite overlay |
| F9 | Join (udp transport only; on Steam use the invite or Join Game) |
| F10 | Leave |

Settings: `BepInEx\config\shipbreakercoop.cfg` (`Transport = steam | udp`, `UdpPort`, `UdpJoinAddress`, `Name`).
Steam uses the game's own AppID, so friends join through normal Steam invites. Everyone needs the same mod version.

## Test
```
dotnet run -c Release --project tests/CoreTests      # engine-free core, prints ALL OK
python -I tests/CoreTests/mutants.py                  # each guard broken on purpose must fail the suite
python -I tools/lab/lab.py provision|deploy|smoke     # two real game copies in D:\Games\ShipbreakerLab
```
Lab-only command-line flags (`-coop-savedir`, `-coop-status`, `-coop-fake circle`) are listed in
`Game/CoopOptions.cs`.

## Known limits (M1)
- Puppet is primitives (body, helmet, visor, backpack), no collision. Real suit mesh: TODO.
- Shown only when both players are in the same scene. Ships differ per player until milestone 2.
- Steam path compiled but untested (needs two accounts on two PCs).
