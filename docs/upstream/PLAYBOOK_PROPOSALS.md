# Draft playbook additions (evidence: Hardspace: Shipbreaker, 2026-11-10)

## playbook/networking-steam-spacewar.md, section "A game with its own old Steamworks"

Add after the Cuphead paragraph:

> **A game whose own Steamworks.NET is new enough: skip Spacewar.** Hardspace: Shipbreaker (Unity 2020.3,
> Mono) ships a Steamworks.NET with `SteamNetworkingMessages` and lobbies, and its `SteamManager` calls
> `SteamAPI.RunCallbacks()` every frame, so a BepInEx plugin can use plain `Callback<T>` / `CallResult<T>`
> under the game's own AppID; friends join through ordinary invites. Two traps:
> - That Steamworks.NET's `SteamNetworkingMessage_t.Release()` is an instance method on the marshalled
>   struct copy, so it can't free the native message. P/Invoke `SteamAPI_SteamNetworkingMessage_t_Release`
>   from the game's own `steam_api64.dll` (check the export with ctypes first).
> - The game's `SteamManager.Initialized` getter *creates* a SteamManager when none exists. Read its
>   `s_instance` / `m_bInitialized` fields instead, so the mod never starts Steam before the game does.

## playbook/multi-instance-testing.md, section "Unity games with no portable switch"

Add:

> On a Mono game whose own code routes every save path through one settings class (Shipbreaker:
> `PlatformSettings.SaveDataPath` / `PersistentDataPath`), a BepInEx plugin can redirect saves without an
> import patch: set the properties through their setters at plugin load and again in a postfix on the
> class's `Initialize()` (the getters are tiny and may be inlined, so don't patch them). Two leaks remain
> and must be closed separately:
> - **Unity Analytics** writes `Unity\<id>\Analytics\*` under the real `persistentDataPath` from native code,
>   after plugins load. Set `Analytics.initializeOnStartup`, `Analytics.enabled`,
>   `Analytics.deviceStatsEnabled` and `PerformanceReporting.enabled` to false in lab copies. Verified with a
>   file watcher: a 60 s two-copy run then wrote nothing to the owner's folder.
> - **PlayerPrefs** live in `HKCU\Software\<company>\<product>`, shared by every copy. Export the key before a
>   run and import it after.
>
> Also: a game may ignore `-screen-*` and apply its own video settings file (Shipbreaker's
> `user_config.ini` `[Video]`). Write windowed mode into each copy's private settings, or the copies
> open fullscreen at the desktop size on the owner's main monitor.

## playbook/automated-qa.md (or animation-sync-checks.md): screenshot every new visual

> Numbers can pass while the screen is wrong. Shipbreaker's first two-copy smoke test passed every
> numeric check (puppet "visible", moving, on the right circle) while nothing was drawn: under HDRP,
> `GameObject.CreatePrimitive` gets the built-in pipeline's default material and renders nothing, while an
> IMGUI name label still drew. A lab-only `ScreenCapture.CaptureScreenshot` taken 5 s after a puppet first
> becomes visible caught it. Fix: an `HDRP/Lit` material (`Shader.Find`, or clone one from the scene).

## playbook/how-coop-mods-work.md (or a page on syncing a world from a save): stale engine state after a swap

> Loading the host's save into a running client is a fast way to give everyone the same world, and the game's own
> loader may do most of the work. Check the result object by object: Hardspace: Shipbreaker's in-place ship swap
> looked perfect on screen, but the client kept a different, run-dependent set of physics bodies. The cause was
> cleanup the game defers: the old ship's per-body bookkeeping (`HierarchyProcessor.mHierarchyParents`) is only
> pruned in an update that is skipped while other changes are queued, so ids the old and new world share reused
> stale entries. Two tools found it in a few runs: an id fingerprint on both sides (count + hash of every persistent
> id, plus a dump of the id list with each object's component types for diffing), and a flushed per-step trace
> with one line per object the loader creates (id, what it looked up, matched or fresh). Waiting did not help;
> calling the game's own cleanup once before loading did.
