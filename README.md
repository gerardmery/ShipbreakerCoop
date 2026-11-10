# ShipbreakerCoop

Co-op for Hardspace: Shipbreaker, as a BepInEx 5 plugin. Built with the
[coop-anything](https://github.com/cranebeaker/coop-anything) playbook.

**Status: prototype.** Not playable as co-op yet.
- Players see each other in the bay as puppet suits (up to 4, Steam invites and Join Game, tested between two PCs).
- The host can send their ship to everyone (F6): every player then has the identical ship, cuts included.
- Not yet shared live: cuts, moving pieces, salvage, the shift timer. Next milestone.

Details, findings and test results: [docs/NOTES.md](docs/NOTES.md). Plugin build and keys: [mod/README.md](mod/README.md).

## Layout
| Folder | What |
|---|---|
| mod/ShipbreakerCoop/Core | Engine-free networking: protocol, session, interpolation, reliable file transfer |
| mod/ShipbreakerCoop/Game | The BepInEx plugin: player puppets, Steam lobby and transport, ship sync |
| tests/CoreTests | Tests for the core without the game, plus mutation twins (`mutants.py`) |
| tools/lab | Runs two game copies on one PC and checks them (smoke test, ship sync check) |
| tools/package.py | Builds the player zip (plugin + install note; BepInEx not bundled) |
| docs | Notes, sync list, milestones, the coop-anything write-up drafts |

The game's files and any decompiled game code are not in this repository. To build, you need your own copy of
the game with BepInEx 5 installed; the project references the game's DLLs from your install.

## Test
```
dotnet run -c Release --project tests/CoreTests     # ALL OK (73 checks)
python -I tests/CoreTests/mutants.py                 # ALL TWINS CAUGHT (20)
```

## License
MIT, see [LICENSE](LICENSE). Covers this repository only, not the game.
