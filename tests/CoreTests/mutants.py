"""Mutation twins for the core tests: break one guard at a time and require the suite to FAIL.

    python -I tests/CoreTests/mutants.py

A twin that still passes means a test that guards nothing. Exit 0 only when every twin is caught.
"""
import os
import shutil
import subprocess
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
CORE = os.path.normpath(os.path.join(HERE, "..", "..", "mod", "ShipbreakerCoop", "Core"))

# (name, file, original text, mutated text)
MUTANTS = [
    ("no relay", "Session.cs",
     "foreach (var other in mPeersByAddr.Values) if (other != peer) Send(other.Addr, data, false);", ""),
    ("no spoof check", "Session.cs",
     "if (s.PlayerId != peer.Id) { BadPackets++; return; }", ""),
    ("no host timeout", "Session.cs",
     "if (now - p.LastHeard > TimeoutSeconds)", "if (false)"),
    ("no bye to strangers", "Session.cs",
     "if (peer == null) { Send(from, Protocol.Bye(ByeReason.NotJoined), false); return; }", "if (peer == null) return;"),
    ("bye reason ignored", "Session.cs",
     "if (Protocol.ReadBye(r) == ByeReason.NotJoined)", "if (true)"),
    ("rejoin bye ends session", "Session.cs",
     "if (Protocol.ReadBye(r) == ByeReason.NotJoined)", "if (false)"),
    ("no admit filter", "Session.cs",
     "if (peer == null && AdmitFilter != null && !AdmitFilter(from)) { BadPackets++; return; }", ""),
    ("blob hash not checked", "Session.cs",
     "if (rcv.Verify())", "if (true)"),
    ("report id not checked", "Session.cs",
     "if (rfrom != peer.Id) { BadPackets++; return; }", ""),
    ("blob chunks never re-sent", "Blob.cs",
     "if (mAcked[i] || now - mSentAt[i] < rto) continue;", "if (mAcked[i] || mSentAt[i] > double.NegativeInfinity) continue;"),
    ("blob duplicates counted", "Blob.cs",
     "if (Complete || index >= mHave.Length || mHave[index]) return false;", "if (Complete || index >= mHave.Length) return false;"),
    ("blob ack map ignored", "Blob.cs",
     "if ((map & (1UL << b)) != 0)", "if (false)"),
    ("events delivered early", "EventChannel.cs",
     "if (seq > Delivered + 1) { if (seq <= Delivered + 4096) mEarly[seq] = new KeyValuePair<byte, byte[]>(kind, payload); return 0; }", ""),
    ("events never re-sent", "EventChannel.cs",
     "if (mSentAt.TryGetValue(seq, out var t) && now - t < rto) continue;", "if (mSentAt.ContainsKey(seq)) continue;"),
    ("stale epoch accepted", "EventChannel.cs",
     "if (epoch < Epoch && Epoch - epoch < 0x80000000u) return 0;", ""),
    ("client trusts anyone", "Session.cs",
     "if (from != mHostAddr) { BadPackets++; return; }", ""),
    ("no version check", "Session.cs",
     "if (ver != ModVersion)", "if (false)"),
    ("no player cap", "Session.cs",
     "for (byte id = 2; id <= MaxPlayers; id++)", "for (byte id = 2; id <= 200; id++)"),
    ("no interpolation delay", "SnapshotBuffer.cs",
     "double t = localNow - mOffset - InterpDelay;", "double t = localNow - mOffset;"),
    ("offset not min-filtered", "SnapshotBuffer.cs",
     "else if (sample < mOffset) mOffset = sample;", "else if (false) mOffset = sample;"),
    ("sweep across scene change", "SnapshotBuffer.cs",
     "if (a.Scene != b.Scene ||", "if ("),
    ("NaN accepted", "Protocol.cs",
     'throw new InvalidDataException("non-finite value in PlayerState");', ""),
    ("no magic check", "Protocol.cs",
     "if ((ushort)(data[0] | (data[1] << 8)) != Magic) return false;", ""),
]


def run_suite(core_dir):
    r = subprocess.run(["dotnet", "run", "-c", "Release", f"-p:CoreDir={core_dir}"],
                       cwd=HERE, capture_output=True, text=True, timeout=600)
    return r.returncode, r.stdout + r.stderr


def main():
    code, out = run_suite(CORE)
    if code != 0:
        print("baseline suite fails; fix that first\n" + out)
        return 2
    escaped = []
    for name, fname, orig, mut in MUTANTS:
        tmp = tempfile.mkdtemp(prefix="sbcoop_mut_")
        try:
            for f in os.listdir(CORE):
                if f.endswith(".cs"):
                    shutil.copy(os.path.join(CORE, f), tmp)
            path = os.path.join(tmp, fname)
            src = open(path, encoding="utf-8").read()
            if src.count(orig) != 1:
                print(f"SETUP ERROR {name}: pattern found {src.count(orig)} times")
                escaped.append(name)
                continue
            open(path, "w", encoding="utf-8").write(src.replace(orig, mut))
            code, out = run_suite(tmp)
            caught = code != 0
            print(("caught   " if caught else "ESCAPED  ") + name)
            if not caught:
                escaped.append(name)
        finally:
            shutil.rmtree(tmp, ignore_errors=True)
    print("ALL TWINS CAUGHT" if not escaped else f"RESULT: FAIL, escaped: {escaped}")
    return 0 if not escaped else 1


if __name__ == "__main__":
    sys.exit(main())
