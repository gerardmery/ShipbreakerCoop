# Draft PR: game/hardspace-shipbreaker (open as draft: gh pr create --draft)

Title: Hardspace: Shipbreaker: directory entry, write-up, Steam and lab playbook notes

## What this PR adds

- [x] Directory entry (new or corrected) in `data/directory.yaml`
- [x] Game write-up in `games/hardspace-shipbreaker/`
- [x] Playbook change in `playbook/` (networking-steam-spacewar, multi-instance-testing, automated-qa, how-coop-mods-work)

## Evidence

- QA level: automated (players visible; ship sync in the lab)
- How it was tested: engine-free core suite (73 checks) with 20 mutation twins; two real game copies on one PC
  driven by a lab script: presence smoke test (14 checks incl. owner-folder hashes) and a ship-sync check
  (8 checks, identical part fingerprints) three runs in a row (fresh ship, after a cut, after moving panels),
  with the owner checking visually; Steam between two PCs and accounts for presence (invite, Join Game, invite
  with the game closed, leave, End task).
- Not covered: ship sync over Steam; live sync of changes (not built yet). `tools/test_all.py` fails on this PC
  in tools that need cmake; this PR changes no tool code.
- Sources: decompiled game code (local, not included), mod repo: https://github.com/gerardmery/ShipbreakerCoop

## Checklist

- [x] `python tools/build_directory.py` was run and DIRECTORY.md is regenerated
- [x] No personal info (real names, emails, local paths with usernames, tokens, invite links to private servers)
- [x] No game files or assets; the mod's code lives in its own repo, linked
- [ ] If an agent wrote this: the owner reviewed it before submitting

🤖 Generated with [Claude Code](https://claude.com/claude-code)
