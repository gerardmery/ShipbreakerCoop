"""Build the player package: dist/ShipbreakerCoop-<version>.zip

    python -I tools/package.py

Contains only our plugin and an install note. BepInEx is not bundled: players get it from its official
release page. Prints the zip's SHA-256 so both players can check they have the same build.
"""
import hashlib
import os
import re
import zipfile

REPO = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
DLL = os.path.join(REPO, "mod", "ShipbreakerCoop", "bin", "Release", "ShipbreakerCoop.dll")
PLUGIN_CS = os.path.join(REPO, "mod", "ShipbreakerCoop", "Game", "Plugin.cs")
BEPINEX = "5.4.23.6"  # what the host runs (BepInEx/LogOutput.log)

INSTALL = """Shipbreaker Co-op {v}  (milestone 1: you can see each other; each player still has their own ship)

You need: Hardspace: Shipbreaker on Steam, and to be Steam friends with the host.

1. Install BepInEx {b} for Windows x64 (NOT BepInEx 6):
   https://github.com/BepInEx/BepInEx/releases  ->  BepInEx_win_x64_{b}.zip
   Extract it into the game folder, next to Shipbreaker.exe
   (Steam: right-click the game > Manage > Browse local files).
   Start the game once to the main menu and quit, so BepInEx creates its folders.
2. Copy the BepInEx folder from this zip over the game's BepInEx folder.
   You should now have: BepInEx\\plugins\\ShipbreakerCoop\\ShipbreakerCoop.dll
3. Start the game. A small "Co-op: off" box appears top left.
4. Accept the host's Steam invite (or right-click the host in your friends list > Join Game).
   The box shows "Co-op player 2 (steam)" once you are in.

Keys: F10 leave. (F8 hosts, Shift+F8 opens the invite overlay while hosting.)
Both players must have the same mod version: the host refuses other versions.

If it doesn't connect, send the host your BepInEx\\LogOutput.log.
To remove: delete BepInEx\\plugins\\ShipbreakerCoop (or winhttp.dll to disable BepInEx entirely).
"""


def main():
    version = re.search(r'Version = "([^"]+)"', open(PLUGIN_CS, encoding="utf-8").read()).group(1)
    if not os.path.exists(DLL):
        raise SystemExit("build first: dotnet build -c Release in mod/ShipbreakerCoop")
    os.makedirs(os.path.join(REPO, "dist"), exist_ok=True)
    out = os.path.join(REPO, "dist", f"ShipbreakerCoop-{version}.zip")
    with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as z:
        z.write(DLL, "BepInEx/plugins/ShipbreakerCoop/ShipbreakerCoop.dll")
        z.writestr("INSTALL.txt", INSTALL.format(v=version, b=BEPINEX).replace("\n", "\r\n"))
    h = hashlib.sha256(open(out, "rb").read()).hexdigest()
    print(out)
    print("sha256", h)


if __name__ == "__main__":
    main()
