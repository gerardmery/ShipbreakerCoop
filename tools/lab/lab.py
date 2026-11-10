"""Two-copy lab for Shipbreaker co-op (coop-anything multi-instance rules).

    python -I tools/lab/lab.py provision        # full copies of the install into the lab folder
    python -I tools/lab/lab.py deploy           # copy the built plugin into every copy
    python -I tools/lab/lab.py smoke            # launch host + client, check the ghost pipe, stop them
    python -I tools/lab/lab.py up | down | status

Rules it keeps:
- The owner's install is only read. Copies are full copies (different volume), so no file is shared.
- The owner's save folder, its Player.log and the PlayerPrefs registry key are hashed before and after
  a run; the registry key is exported before and restored after (Unity writes window size there).
- Every copy gets -coop-savedir and -logFile inside its own folder.
- Only processes this tool started are killed, and only while pid + image path + creation time match.
- It refuses to start while a Shipbreaker.exe it does not own is running (the owner may be playing),
  unless --allow-foreign.
"""
import argparse
import hashlib
import json
import os
import shutil
import subprocess
import sys
import time

GAME_DIR = r"C:\Program Files (x86)\Steam\steamapps\common\Hardspace Shipbreaker"
LAB_DIR = r"D:\Games\ShipbreakerLab"
EXE = "Shipbreaker.exe"
APPID = "1161580"
COPIES = ["host", "client"]
OWNER_SAVE_DIR = os.path.join(os.environ["USERPROFILE"], "AppData", "LocalLow", "Blackbird Interactive", "Hardspace_ Shipbreaker")
PREFS_KEY = r"HKCU\Software\Blackbird Interactive\Hardspace: Shipbreaker"
HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.normpath(os.path.join(HERE, "..", ".."))
PLUGIN_DLL = os.path.join(REPO, "mod", "ShipbreakerCoop", "bin", "Release", "ShipbreakerCoop.dll")
STATE = os.path.join(LAB_DIR, "lab_state.json")
UDP_PORT = 27960


def log(*a):
    print("[lab]", *a, flush=True)


# ------------------------------------------------------------------ helpers

def sha256(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def tree_hash(root):
    """Hash of every file (relative path, size, content) under root. Missing root -> 'absent'."""
    if not os.path.isdir(root):
        return "absent"
    h = hashlib.sha256()
    for dp, dns, fns in os.walk(root):
        dns.sort()
        for fn in sorted(fns):
            p = os.path.join(dp, fn)
            h.update(os.path.relpath(p, root).encode())
            try:
                h.update(sha256(p).encode())
            except OSError as e:
                h.update(str(e).encode())
    return h.hexdigest()


def reg_export(path):
    if os.path.exists(path):
        os.remove(path)
    r = subprocess.run(["reg", "export", PREFS_KEY, path, "/y"], capture_output=True, text=True)
    return r.returncode == 0


def reg_hash():
    tmp = os.path.join(LAB_DIR, "_prefs_check.reg")
    ok = reg_export(tmp)
    h = sha256(tmp) if ok else "absent"
    if ok:
        os.remove(tmp)
    return h


def processes():
    """Running Shipbreaker.exe processes: [{pid, path, created}]."""
    ps = ("Get-CimInstance Win32_Process -Filter \"Name='%s'\" | "
          "Select ProcessId,ExecutablePath,@{n='Created';e={$_.CreationDate.ToString('o')}} | ConvertTo-Json -Compress") % EXE
    r = subprocess.run(["powershell", "-NoProfile", "-Command", ps], capture_output=True, text=True)
    out = r.stdout.strip()
    if not out:
        return []
    data = json.loads(out)
    if isinstance(data, dict):
        data = [data]
    return [{"pid": d["ProcessId"], "path": d["ExecutablePath"] or "", "created": d["Created"]} for d in data]


def load_state():
    if os.path.exists(STATE):
        with open(STATE) as f:
            return json.load(f)
    return {"procs": []}


def save_state(s):
    with open(STATE, "w") as f:
        json.dump(s, f, indent=1)


def copy_dir(name):
    return os.path.join(LAB_DIR, name)


def guard_write(path):
    p = os.path.normcase(os.path.abspath(path))
    if not p.startswith(os.path.normcase(os.path.abspath(LAB_DIR)) + os.sep):
        raise SystemExit(f"refusing to write outside the lab: {path}")


# ------------------------------------------------------------------ commands

def provision(args):
    os.makedirs(LAB_DIR, exist_ok=True)
    src_exe_hash = sha256(os.path.join(GAME_DIR, EXE))
    for name in COPIES:
        dst = copy_dir(name)
        guard_write(os.path.join(dst, "x"))
        if os.path.exists(os.path.join(dst, EXE)) and not args.force:
            log(f"{name}: exists, skipping copy (use --force to refresh)")
        else:
            log(f"{name}: copying install (~3.4 GB) ...")
            # robocopy: exit codes < 8 are success. Skip the owner's runtime output.
            r = subprocess.run(["robocopy", GAME_DIR, dst, "/E", "/NFL", "/NDL", "/NJH", "/NP", "/R:1", "/W:1",
                                "/XF", "LogOutput.log", "/XD", os.path.join(GAME_DIR, "BepInEx", "cache"),
                                os.path.join(GAME_DIR, "Logs")])
            if r.returncode >= 8:
                raise SystemExit(f"robocopy failed ({r.returncode})")
        # Private edits, only inside the copy.
        boot = os.path.join(dst, "Shipbreaker_Data", "boot.config")
        guard_write(boot)
        lines = open(boot, encoding="utf-8").read().splitlines()
        kept = [l for l in lines if not l.startswith("single-instance")]
        open(boot, "w", encoding="utf-8", newline="\n").write("\n".join(kept) + "\n")
        open(os.path.join(dst, "steam_appid.txt"), "w").write(APPID)
        cfg = os.path.join(dst, "config.ini")
        txt = open(cfg, encoding="utf-8").read().replace("SkipSplashScreen=false", "SkipSplashScreen=true")
        open(cfg, "w", encoding="utf-8").write(txt)
        os.makedirs(os.path.join(dst, "CoopSave"), exist_ok=True)
        h = sha256(os.path.join(dst, EXE))
        log(f"{name}: ready, exe sha256 {'matches' if h == src_exe_hash else 'DIFFERS'} the install, "
            f"single-instance line {'removed' if len(kept) < len(lines) else 'not present'}")


def deploy(args):
    if not os.path.exists(PLUGIN_DLL):
        raise SystemExit("plugin not built: dotnet build -c Release in mod/ShipbreakerCoop")
    for name in COPIES:
        d = os.path.join(copy_dir(name), "BepInEx", "plugins", "ShipbreakerCoop")
        guard_write(os.path.join(d, "x"))
        os.makedirs(d, exist_ok=True)
        shutil.copy2(PLUGIN_DLL, d)
        log(f"{name}: plugin deployed ({sha256(PLUGIN_DLL)[:12]})")


def launch_args(name, fake):
    d = copy_dir(name)
    a = ["-screen-fullscreen", "0", "-screen-width", "1600", "-screen-height", "900",
         "-logFile", os.path.join(d, "Player.log"),
         "-coop-transport", "udp", "-coop-savedir", os.path.join(d, "CoopSave"),
         "-coop-status", os.path.join(d, "coop_status.json"), "-coop-name", name.capitalize()]
    if name == "host":
        a += ["-coop-host", "-coop-port", str(UDP_PORT)]
    else:
        a += ["-coop-join", f"127.0.0.1:{UDP_PORT}"]
    if fake:
        a += ["-coop-fake", "circle"]
    return a


def set_windowed(d):
    """The game ignores -screen-* and applies [Video] from its own user_config.ini, which for a lab copy
    lives in CoopSave (redirected). Seen: a fresh copy defaults to FullScreenWindow at the desktop size."""
    ini = os.path.join(d, "CoopSave", "user_config.ini")
    guard_write(ini)
    want = {"FullScreenMode": "Windowed", "Width": "1600", "Height": "900"}
    if not os.path.exists(ini):
        # First launch: the game fills in every other key around this section.
        open(ini, "w", encoding="utf-8").write("[Video]\n" + "".join(f"{k}={v}\n" for k, v in want.items()))
        return
    out, section = [], ""
    for line in open(ini, encoding="utf-8").read().splitlines():
        s = line.strip()
        if s.startswith("["):
            section = s
        key = s.split("=", 1)[0] if "=" in s and not s.startswith(";") else None
        if section == "[Video]" and key in want:
            line = f"{key}={want[key]}"
        out.append(line)
    open(ini, "w", encoding="utf-8").write("\n".join(out) + "\n")


def wait_log(path, text, timeout, proc_alive):
    end = time.time() + timeout
    while time.time() < end:
        if os.path.exists(path):
            try:
                if text in open(path, encoding="utf-8", errors="replace").read():
                    return True
            except OSError:
                pass
        if not proc_alive():
            return False
        time.sleep(1)
    return False


def up(args, fake=True):
    st = load_state()
    if st["procs"]:
        raise SystemExit("lab already has recorded processes; run 'down' first")
    foreign = [p for p in processes() if not os.path.normcase(p["path"]).startswith(os.path.normcase(LAB_DIR))]
    if foreign and not args.allow_foreign:
        raise SystemExit(f"Shipbreaker is running outside the lab ({foreign}); the owner may be playing. --allow-foreign to override")
    flags = 0x00000008 | 0x00000200  # DETACHED_PROCESS | CREATE_NEW_PROCESS_GROUP
    for name in COPIES:
        d = copy_dir(name)
        for f in ("coop_status.json", "coop_shot.png", "Player.log", os.path.join("BepInEx", "LogOutput.log")):
            p = os.path.join(d, f)
            guard_write(p)
            if os.path.exists(p):
                os.remove(p)
        set_windowed(d)
        exe = os.path.join(d, EXE)
        proc = subprocess.Popen([exe] + launch_args(name, fake), cwd=d, creationflags=flags | 0x01000000)  # BREAKAWAY
        time.sleep(2)
        rec = next((p for p in processes() if p["pid"] == proc.pid), None)
        if rec is None:
            raise SystemExit(f"{name}: exited at once (code {proc.poll()}); single-instance check or missing Steam?")
        st["procs"].append({"name": name, **rec})
        save_state(st)
        log(f"{name}: started pid {proc.pid}")
        bep = os.path.join(d, "BepInEx", "LogOutput.log")
        ok = wait_log(bep, " loaded (transport ", 180, lambda: proc.poll() is None)
        log(f"{name}: plugin {'loaded' if ok else 'NOT loaded (see ' + bep + ')'}")
        if not ok:
            return False
    return True


def down(args):
    st = load_state()
    running = {p["pid"]: p for p in processes()}
    for rec in st["procs"]:
        cur = running.get(rec["pid"])
        if cur and cur["path"] == rec["path"] and cur["created"] == rec["created"]:
            subprocess.run(["taskkill", "/PID", str(rec["pid"]), "/T", "/F"], capture_output=True)
            log(f"{rec['name']}: stopped pid {rec['pid']}")
        else:
            log(f"{rec['name']}: pid {rec['pid']} already gone (or reused; not touched)")
    st["procs"] = []
    save_state(st)


def read_status(name):
    p = os.path.join(copy_dir(name), "coop_status.json")
    try:
        with open(p) as f:
            return json.load(f)
    except (OSError, ValueError):
        return None


def status(args):
    for name in COPIES:
        print(name, json.dumps(read_status(name), indent=1))


def dist(a, b):
    return sum((x - y) ** 2 for x, y in zip(a, b)) ** 0.5


def smoke(args):
    """Both copies send a fake player circling (r=1.5 m, ~4.7 m/s). Pass when, on each side, the other
    player joined, states flow, the puppet is visible and moves, and the client's puppet of the host sits
    on the host's circle."""
    before = {"saves": tree_hash(OWNER_SAVE_DIR), "prefs": reg_hash()}
    backup = os.path.join(LAB_DIR, "prefs_backup.reg")
    had_prefs = reg_export(backup)
    log(f"owner saves {before['saves'][:12]}, prefs {before['prefs'][:12]} (backed up: {had_prefs})")
    checks, ok = [], False
    try:
        if not up(args, fake=True):
            checks.append(("both copies load the plugin", False))
            return finish(checks, before, backup, had_prefs)
        checks.append(("both copies load the plugin", True))
        end = time.time() + args.timeout
        samples = []
        while time.time() < end:
            h, c = read_status("host"), read_status("client")
            if h and c and h.get("connected") and c.get("connected") and h["remotes"] and c["remotes"]:
                samples.append((h, c))
                if len(samples) >= 40:  # ~20 s connected: past the menu load, steady state
                    break
            time.sleep(0.5)
        if not samples:
            h, c = read_status("host"), read_status("client")
            log(f"no connection. host={h} client={c}")
            checks.append(("client connects to host", False))
        else:
            h, c = samples[-1]
            hr, cr = h["remotes"][0], c["remotes"][0]
            checks.append(("client connects to host", True))
            checks.append(("2 players on both sides", h["players"] == 2 and c["players"] == 2))
            checks.append(("client got player id 2", c["localId"] == 2))
            checks.append(("names via roster", hr["name"] == "Client" and cr["name"] == "Host"))
            checks.append(("states flow both ways (>40 each)", hr["states"] > 40 and cr["states"] > 40))
            checks.append(("puppets visible on both sides", hr["visible"] and cr["visible"]))
            moved = [s[1]["remotes"][0]["pos"] for s in samples if s[1]["remotes"][0]["pos"]]
            span = max((dist(moved[0], p) for p in moved), default=0)
            checks.append((f"client's puppet of host moves ({span:.2f} m)", span > 0.5))
            # The host's fake circles 4 m in front of the host camera with r = 1.5 m: the puppet must sit
            # on that circle. The host camera moves too slowly in the menu to matter between samples.
            errs = []
            for hs, cs in samples:
                hp, pp = hs["local"]["pos"], cs["remotes"][0]["pos"]
                if pp:
                    errs.append(dist(hp, pp))
            worst = max(errs) if errs else 99
            checks.append((f"puppet within 3.2 m of host's fake (circle diameter; worst {worst:.2f} m)", worst < 3.2))
            checks.append(("no bad packets", h["badPackets"] == 0 and c["badPackets"] == 0))
            # RTT includes one frame on each side, so judge it against the frame times we saw.
            tail = samples[-10:]
            rtts = sorted(s[1]["rtt"] for s in tail)
            rtt = rtts[len(rtts) // 2]
            hf = sorted(s[0].get("fps", 0) for s in tail)[len(tail) // 2]
            cf = sorted(s[1].get("fps", 0) for s in tail)[len(tail) // 2]
            frames = (1 / max(hf, 1) + 1 / max(cf, 1))
            log(f"steady state: rtt median {rtt * 1000:.0f} ms, host {hf:.0f} fps, client {cf:.0f} fps "
                f"(two frames = {frames * 1000:.0f} ms)")
            bound = frames * 1.5 + 0.05  # one frame per side, 50% frame jitter, 50 ms slack
            checks.append((f"rtt ({rtt * 1000:.0f} ms) within frame-time bound ({bound * 1000:.0f} ms)", 0 <= rtt <= bound))
    finally:
        down(args)
    return finish(checks, before, backup, had_prefs)


def finish(checks, before, backup, had_prefs):
    if had_prefs:
        r = subprocess.run(["reg", "import", backup], capture_output=True, text=True)
        log(f"owner prefs restored from backup ({'ok' if r.returncode == 0 else r.stderr.strip()})")
    after = {"saves": tree_hash(OWNER_SAVE_DIR), "prefs": reg_hash()}
    checks.append(("owner save folder unchanged", after["saves"] == before["saves"]))
    checks.append(("owner PlayerPrefs unchanged (after restore)", after["prefs"] == before["prefs"]))
    checks.append(("owner install exe unchanged", sha256(os.path.join(GAME_DIR, EXE)) == IDENTITY_EXE_SHA))
    fails = 0
    for what, good in checks:
        print(("  ok    " if good else "  FAIL  ") + what)
        fails += 0 if good else 1
    print("ALL OK" if fails == 0 else f"RESULT: FAIL ({fails} of {len(checks)})")
    return fails == 0


IDENTITY_EXE_SHA = "7fc94c77eb1ed54b956f8572663f7893bb7fc407d2c1d3450f87343f93a3670f"


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("cmd", choices=["provision", "deploy", "up", "down", "status", "smoke"])
    ap.add_argument("--force", action="store_true")
    ap.add_argument("--allow-foreign", action="store_true")
    ap.add_argument("--timeout", type=float, default=120)
    ap.add_argument("--no-fake", action="store_true", help="up: real players only (no circling fake)")
    a = ap.parse_args()
    if a.cmd == "provision":
        provision(a)
    elif a.cmd == "deploy":
        deploy(a)
    elif a.cmd == "up":
        sys.exit(0 if up(a, fake=not a.no_fake) else 1)
    elif a.cmd == "down":
        down(a)
    elif a.cmd == "status":
        status(a)
    elif a.cmd == "smoke":
        sys.exit(0 if smoke(a) else 1)


if __name__ == "__main__":
    main()
