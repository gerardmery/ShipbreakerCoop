# Setup Checklist (Phase 0)

Game root: C:\Program Files (x86)\Steam\steamapps\common\Hardspace Shipbreaker

## 1. BepInEx 5 (mod loader)
- [x] Download the latest BepInEx 5.4.x **win_x64** zip from github.com/BepInEx/BepInEx/releases (not BepInEx 6)
- [x] Extract into the game root (next to Shipbreaker.exe): BepInEx\, winhttp.dll, doorstop_config.ini
- [x] Launch the game to the main menu, then quit
- [x] Confirm BepInEx\config\ and BepInEx\LogOutput.log were created
- [x] BepInEx\config\BepInEx.cfg: [Logging.Console] Enabled = true

## 2. UnityExplorer (runtime inspector)
- [x] Download the BepInEx 5 **Mono** build of UnityExplorer
- [x] Extract into BepInEx\plugins\
- [x] Launch; F7 toggles the panel. Check the player, cutter, grapple objects in the hierarchy
- Note: ECS entities (ship parts) won't show fully here; an entity inspector comes during triage

## 3. dnSpyEx (code reading)
- [x] Download dnSpy-net-win64 from github.com/dnSpyEx/dnSpy/releases into D:\CGI\Tools\dnSpyEx
- [x] Open Shipbreaker_Data\Managed\Assembly-CSharp.dll and any BBI.* / game-named DLLs
- [x] File > Export to Project (BBI.Unity.* + Carbon.*; zipped as decomp.zip) > D:\CGI\Projects\ShipbreakerCoop\decomp

## 4. Version lookups
- [x] Unity: 2020.3.35f1
- [x] Unity.Entities.dll: stamped 0.0.0.0. From code: early DOTS (ComponentSystem/JobComponentSystem, no SystemBase). Follow the game's own patterns
- [x] Unity.Physics.dll: stamped 0.0.0.0, 123 KB; gameplay physics is PhysX Rigidbody
- [x] BepInEx version installed: 5.4.23.6

## 5. Mod dev environment
- [x] Visual Studio Community 2026 (18.11.3) with .NET Framework 4.7.2 targeting pack + Unity workload
- [x] .NET SDK 10.0.403
- (The agent creates the plugin project in mod\ at the start of Milestone 1)

## 6. Agent setup
- [ ] Git for Windows and Python 3 installed (`git --version`, `python --version`)
- [ ] Claude Code installed on this PC: `irm https://claude.ai/install.ps1 | iex`, then `claude --version`
- [ ] Clone coop-anything to D:\CGI\Tools\coop-anything
- [ ] Add MCP: `claude mcp add coop-anything -- python D:\CGI\Tools\coop-anything\tools\mcp\coop_mcp.py`
- [ ] Run Claude Code from D:\CGI\Projects\ShipbreakerCoop with the prompt in TRIAGE_BRIEF.md
