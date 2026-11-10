# Draft PR: game/hardspace-shipbreaker (open as draft: gh pr create --draft)

Title: Hardspace: Shipbreaker: directory entry, write-up, Steam and lab playbook notes

## What this PR adds

- [x] Directory entry (new or corrected) in `data/directory.yaml`
- [x] Game write-up in `games/hardspace-shipbreaker/`
- [x] Playbook change in `playbook/` (networking-steam-spacewar, multi-instance-testing, automated-qa)

## Evidence

- QA level: automated (milestone 1, players visible only)
- How it was tested: engine-free core suite (42 checks) with 12 mutation twins; two real game clients on one
  PC driven by a lab script (14 checks incl. owner-folder hashes, 3 passing runs) plus an in-game screenshot;
  owner playtest of two copies in the shipbreaking bay. Steam between two accounts not tested yet.
- Sources: decompiled game code (local, not included), mod repo: OWNER/ShipbreakerCoop

## Checklist

- [ ] `python tools/build_directory.py` was run and DIRECTORY.md is regenerated
- [x] No personal info (real names, emails, local paths with usernames, tokens, invite links to private servers)
- [x] No game files or assets; the mod's code lives in its own repo, linked
- [ ] If an agent wrote this: the owner reviewed it before submitting

🤖 Generated with [Claude Code](https://claude.com/claude-code)
