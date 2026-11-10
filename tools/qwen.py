"""Run one task on the local agent (OpenCode + qwen3-coder via Ollama) in an isolated git worktree.

    python -I tools/qwen.py T2                 # a task from docs/QWEN_TASKS.md
    python -I tools/qwen.py --prompt "..."     # a free-form task (allowed files: docs/HANDOFF.md only)
    python -I tools/qwen.py --cleanup T2       # remove that task's worktree and branch after review

Why a worktree: on 2026-11-10 the agent overwrote docs/NOTES.md. Here it works on branch qwen/<task> in
.qwen/<task>/ (git-ignored); the main working tree is never touched. decomp/ is linked in read-only by a
junction. When it exits, this script prints every changed file, flags files outside the task's allow-list,
saves the transcript to local/qwen-runs/, and exits 0 (clean) / 1 (out-of-scope edits) / 2 (agent failed).
Claude (or you) then reviews `git diff main...qwen/<task>` and merges what is right.
"""
import argparse
import datetime
import os
import re
import subprocess
import sys

REPO = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
OPENCODE = os.path.join(os.environ["APPDATA"], "ai.opencode.desktop", "cli", "2.2.7-x64", "opencode-cli.exe")
OLLAMA = os.path.join(os.environ["LOCALAPPDATA"], "Programs", "Ollama", "ollama.exe")
MODEL = "ollama/qwen3-coder:30b"
TASKS = os.path.join(REPO, "docs", "QWEN_TASKS.md")

# Files each task may change (docs/HANDOFF.md and the task list tick are always allowed).
ALLOWED = {
    "T2": ["docs/SYNC_LIST.md"],
    "T3": ["docs/SYNC_LIST.md"],
    "T4": ["docs/SYNC_LIST.md"],
    "T5": ["docs/SYNC_LIST.md"],
    "T6": ["docs/SYNC_LIST.md"],
    "T7": ["tools/collect_logs.py"],
}
ALWAYS = ["docs/HANDOFF.md", "docs/QWEN_TASKS.md"]

PREAMBLE = """You are the local agent on the ShipbreakerCoop project. First read AGENTS.md and CLAUDE.md.
Rules for this run (they override anything else):
- Do exactly one task, below. Edit only these files: {allowed}. Edit sections in place; NEVER rewrite a whole file.
- decomp/ is read-only reference code: grep first, then open one file at a time.
- Every finding needs a source as decomp/<path>.cs:<line>, sorted into Observed / Guess / Unknown.
- Do not run the game, git commands, or anything that installs or deletes. Do not commit.
- When done: tick the task in docs/QWEN_TASKS.md and add one line to docs/HANDOFF.md (task id, files changed).
- If you fail twice at the same step, write the problem in docs/HANDOFF.md and stop.

TASK {task}:
{body}
"""


def git(*args, cwd=REPO, check=True):
    r = subprocess.run(["git", *args], cwd=cwd, capture_output=True, text=True)
    if check and r.returncode != 0:
        raise SystemExit(f"git {' '.join(args)} failed: {r.stderr.strip()}")
    return r.stdout.strip()


def task_body(task):
    text = open(TASKS, encoding="utf-8").read()
    m = re.search(rf"^## {task}\..*?(?=^## |\Z)", text, re.S | re.M)
    if not m:
        raise SystemExit(f"{task} not found in docs/QWEN_TASKS.md")
    return m.group(0).strip()


def worktree_path(task):
    return os.path.join(REPO, ".qwen", task)


def setup_worktree(task):
    wt = worktree_path(task)
    branch = f"qwen/{task}"
    if os.path.exists(wt):
        raise SystemExit(f"{wt} exists: review it, then run --cleanup {task}")
    git("worktree", "add", "-b", branch, wt, "HEAD")
    # decomp/ is git-ignored, so link it in (a junction needs no admin rights).
    subprocess.run(["cmd", "/c", "mklink", "/J", os.path.join(wt, "decomp"), os.path.join(REPO, "decomp")],
                   capture_output=True, check=True)
    return wt, branch


def cleanup(task):
    wt = worktree_path(task)
    junction = os.path.join(wt, "decomp")
    if os.path.exists(junction):
        os.rmdir(junction)  # removes the junction only, never the real decomp/
    git("worktree", "remove", "--force", wt, check=False)
    git("branch", "-D", f"qwen/{task}", check=False)
    print(f"removed worktree and branch for {task}")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("task", nargs="?")
    ap.add_argument("--prompt")
    ap.add_argument("--cleanup", metavar="TASK")
    ap.add_argument("--timeout-min", type=float, default=30)
    ap.add_argument("--unload", action="store_true", help="unload the model from VRAM afterwards")
    a = ap.parse_args()

    if a.cleanup:
        cleanup(a.cleanup)
        return 0
    if not (a.task or a.prompt):
        ap.error("give a task id (T2...) or --prompt")
    if not os.path.exists(OPENCODE):
        raise SystemExit(f"opencode-cli not found at {OPENCODE}")

    task = a.task or "adhoc-" + datetime.datetime.now().strftime("%H%M%S")
    allowed = ALLOWED.get(task, []) + ALWAYS
    body = task_body(task) if a.task else a.prompt
    prompt = PREAMBLE.format(allowed=", ".join(allowed), task=task, body=body)

    wt, branch = setup_worktree(task)
    os.makedirs(os.path.join(REPO, "local", "qwen-runs"), exist_ok=True)
    log = os.path.join(REPO, "local", "qwen-runs", f"{datetime.datetime.now():%Y-%m-%d_%H%M}-{task}.log")
    print(f"[qwen] {task} on branch {branch} in {wt}\n[qwen] transcript: {log}", flush=True)

    t0 = datetime.datetime.now()
    try:
        with open(log, "w", encoding="utf-8") as out:
            r = subprocess.run([OPENCODE, "run", "-m", MODEL, "--auto", "--archived", "--title", f"ShipbreakerCoop {task}", prompt],
                               cwd=wt, stdout=out, stderr=subprocess.STDOUT, timeout=a.timeout_min * 60)
        code = r.returncode
    except subprocess.TimeoutExpired:
        code = "timeout"
    mins = (datetime.datetime.now() - t0).total_seconds() / 60

    changed = [l[3:] for l in git("status", "--porcelain", cwd=wt).splitlines() if l.strip()]
    changed = [c for c in changed if c.rstrip("/") != "decomp"]
    outside = [c for c in changed if c.replace("\\", "/") not in allowed]
    print(f"[qwen] agent exit: {code} after {mins:.1f} min")
    print(f"[qwen] changed: {changed or 'nothing'}")
    if outside:
        print(f"[qwen] OUT OF SCOPE: {outside}")
    with open(log, encoding="utf-8", errors="replace") as f:
        tail = f.read()[-1500:]
    print("[qwen] transcript tail:\n" + tail)
    print(f"[qwen] review: git diff main -- with: git -C .qwen/{task} diff ; merge what is right; then tools/qwen.py --cleanup {task}")

    if a.unload and os.path.exists(OLLAMA):
        subprocess.run([OLLAMA, "stop", MODEL.split("/", 1)[1]], capture_output=True)
        print("[qwen] model unloaded")
    if code not in (0,):
        return 2
    return 1 if outside else 0


if __name__ == "__main__":
    sys.exit(main())
