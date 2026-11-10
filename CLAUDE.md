# ShipbreakerCoop: project rules

Co-op mod for Hardspace: Shipbreaker using the coop-anything playbook.

## Read first
- docs/TRIAGE_BRIEF.md (facts, milestones, test setup)
- docs/SYNC_LIST.md (what's in the code and where)
- docs/MILESTONE_1.md (current task list; tick items as they finish)

## Rules
- Game root: C:\Program Files (x86)\Steam\steamapps\common\Hardspace Shipbreaker
- Only ever write to the game folder's BepInEx\plugins\ShipbreakerCoop\. Never modify other game files.
- Never commit reference/, decomp/ or decomp.zip (game files and code).
- decomp/ is ~6,000 files. Read the specific files SYNC_LIST.md names; search with grep before opening files. Don't read broadly.
- Unity 2020.3.35f1, Mono, BepInEx 5 + HarmonyX, target net472. Early DOTS (ComponentSystem/JobComponentSystem): copy the game's own patterns, not modern DOTS docs.
- Gameplay physics is Unity Rigidbody (PhysX). Events go through Main.EventSystem.
- Stop at each checkpoint in docs/MILESTONE_1.md and report to the user before continuing.
- After each work session, update docs/SYNC_LIST.md with new findings and tick docs/MILESTONE_1.md.
