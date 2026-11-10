# Draft: "claim a game" issue for cranebeaker/coop-anything

Post at: https://github.com/cranebeaker/coop-anything/issues/new?template=claim-a-game.yml
(Checked 2026-11-10: no issue or PR mentions Shipbreaker.)

---

**Game**

Hardspace: Shipbreaker (2022)

**Plan**

Starting fresh. No co-op mod exists (searched the coop-anything directory and its issues/PRs, 2026-11-10;
the developers called co-op a months-long investigation).

- Engine: Unity 2020.3.35f1, Mono x64, HDRP, heavy DOTS/ECS + Burst for ship parts. Steam build 22302871.
- Approach: BepInEx 5 + Harmony plugin, host-authoritative. Clients send intents (grab, cut), the host runs
  physics and cutting. Networking through the game's own Steamworks.NET (ISteamNetworkingMessages + a
  friends-only lobby on the game's AppID; no Spacewar needed), plain UDP for the two-copies-on-one-PC lab.
- Milestones: 1 ghost buddy (players see each other), 2 shared salvage (same ship, grab/tether, deliveries),
  3 cutting and hazards, 4 shared shift/progression, 5 QA and packaging.
- Status: milestone 1 works in the lab (two real game copies, automated smoke test + in-level playtest);
  Steam path not tested between two accounts yet.
- Notes: will be posted as `games/hardspace-shipbreaker/` in a draft PR.

**Your repo or branch (optional)**

(to fill in once the mod repo is public)
