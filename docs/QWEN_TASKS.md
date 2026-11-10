# Tasks for the local agent (qwen3-coder:30b)

Written by Claude, 2026-11-10. Do them in order; each fits in 32k context if you grep first and open one file
at a time. Claude is building M2 "ship sync" in mod/ and tests/ meanwhile, so stay out of those.

## Rules for every task
- NEVER rewrite a whole file. Edit or append the one section a task names. (On 2026-11-10 docs/NOTES.md was
  replaced with 18 lines and had to be restored from git.) Never touch docs/NOTES.md at all; that is Claude's.
- Put findings where the task says, not in new files at the repo root.
- Only write the files a task names. Do NOT edit mod/, tests/, tools/lab/, or anything in the game folder.
- Do not launch the game or the lab copies. Do not commit; Claude reviews and commits.
- Every fact gets a source: `decomp/<path>.cs:<line>`. Put each finding in one of three lists:
  **Observed** (you read it in code), **Guess** (you think so, not proven), **Unknown**.
- Write `unknown` rather than guessing. Two failed attempts at the same thing: stop and write the problem in
  docs/HANDOFF.md (what you tried, what happened).
- When a task is done, tick it here and add one line to docs/HANDOFF.md: task id, files changed.

## T1. How a shift starts (most useful: unblocks lab automation and "joiner follows host")
- [x] DONE 2026-11-10, reviewed by Claude. Result in docs/SYNC_LIST.md section 7. Review notes: main finding
  right; "set NextShipPreview" was wrong (private setter); no file:line sources were given, add them next time.
- Trace from "player picks a ship / starts a shift" to the level load. Start: `grep -n "public .*Task" decomp/BBI.Unity.Game/BBI/Unity/Game/SceneLoader.cs`,
  then `JobBoardUtils.cs`. Look for whoever calls `TearDownAndLoadLevel...WithShipPreview...`.
- Answer: (a) the call chain with method names and parameters; (b) is there one method we could call from a
  mod to start a shift in the bay with a given ShipPreview or ship file? its exact signature; (c) events
  posted when the bay finishes loading (names); (d) what `bLoadWithHabStart` and `bTransitionThroughFrontend` do.
- Output: docs/SYNC_LIST.md, new section "## 7. Scene / level load flow".

## T2. When the game writes the "ship in progress" file
- [x] Done by Claude 2026-11-10 (SYNC_LIST.md section 6). Two local-agent runs failed: one rewrote SYNC_LIST.md in the main tree, one answered a different question. `PlayerProfileSaveLoadManager.cs:99-130` writes `profile.ShipInProgressFileName()`. Find every caller of
  that save (grep the method name), i.e. when it happens: shift end, quit, autosave timer, leaving the bay?
- Also: `PlayerProfile.cs:169` (where the file lives) and `SaveFile.cs:540` (what loading it does).
- Why it matters: in co-op a client must not save the host's ship into the friend's career.
- Output: docs/SYNC_LIST.md section "## 6. Shift system" (create it), subsection "Ship in progress save".

## T3. Salvage receptacles and payout
- [ ] Start: `FurnaceVolume.cs`, `PreFurnaceVolume.cs`. Then grep class names containing `Processor`, `Barge`,
  `Receptacle`, `Salvage` in decomp/BBI.Unity.Game. For each receptacle: the class, the method that accepts an
  object, how the value is credited (method or event name), and the event posted.
- Output: docs/SYNC_LIST.md section "### Salvage" (replace the "TODO search" line).

## T4. Shift timer and end of shift
- [ ] grep `Shift`, `Workshift`, `Timer`, `Oxygen` class names; find where the shift clock counts down, what
  ends a shift, and the events posted. Keep it to names, files and lines.
- Output: docs/SYNC_LIST.md section "## 6. Shift system", subsection "Timer".

## T5. Grab, grapple and tether: who posts the events and what they carry
- [ ] `ObjectTetheredEvent`, `TetherChangedEvent`, `GrappleEvent`: for each, the fields, and every place it is
  posted (grep `new <Event>(` and `<Event>.GetEvent(`). Research only, no design.
- Output: docs/SYNC_LIST.md section "### Grapple" / "### Tether", add "Posted by:" and "Fields:" lines.

## T6. What AntiCheatToolKit guards
- [ ] grep `Obscured` and `CodeStage` in decomp/. List which fields/values use obscured types (file:line).
- Output: docs/SYNC_LIST.md, "Key findings", one new numbered item. Tick the "Check what AntiCheatToolKit
  protects" item under "Next".

## T7. Log collector script (boilerplate)
- [ ] Write `tools/collect_logs.py` (Python 3 standard library only). It copies, for each of
  `D:\Games\ShipbreakerLab\host` and `\client`: `BepInEx\LogOutput.log`, `Player.log`, `coop_status.json`,
  `coop_shot.png` (skip missing files) into `local\logs\<YYYY-MM-DD_HHMM>-lab\<host|client>\`.
- Must only read from the lab folder and only write under `local\logs\`. Never delete anything.
- Print the destination folder and the files copied. Run it once and paste the output into HANDOFF.md.

## T8. Friend's session log (when it arrives)
- [x] (Done by Claude, 2026-11-10; skip.) When `local\logs\2026-11-10-steam\friend-0.1.0.log` exists: list every line from `[Shipbreaker Co-op]`,
  any line with `Exception` or `Error`, and compare with `host-0.1.0.log`: does each join/leave on one side
  have a matching line on the other? Note anything one-sided.
- Output: `local\logs\2026-11-10-steam\SUMMARY.md` (local/ is private and not in git).

## Before the owner runs two game copies
Unload the model first: `ollama stop qwen3-coder:30b` (the copies need the VRAM).
