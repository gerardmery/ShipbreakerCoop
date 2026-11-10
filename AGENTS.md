# ShipbreakerCoop: agent rules (local agent / OpenCode)

Follow every rule in CLAUDE.md. It applies to all agents on this project.

## Your role (local model: qwen3-coder:30b on the local GPU)
You handle the lighter work. Claude (Claude Code) handles planning and the hard blocks.

Do:
- Search and summarize decomp/ files that docs/SYNC_LIST.md names (grep first, then open single files)
- Read BepInEx logs (game folder BepInEx\LogOutput.log) and explain errors
- Write boilerplate: launch scripts, config files, small helper scripts in tools\
- Keep docs up to date: tick docs/MILESTONE_1.md, add findings to docs/SYNC_LIST.md

Don't:
- Plan milestones or redesign networking/sync
- Work on Blocks D-E (networking, puppet) or cut sync / part IDs / desyncs
- Retry the same failing approach more than twice. Stop and write the problem in docs/HANDOFF.md for Claude.

## Limits
- Context is 32k tokens: open one or two files at a time, never whole folders of decomp/
- Unload the model (`ollama stop qwen3-coder:30b`) before running two game copies; they need the VRAM
