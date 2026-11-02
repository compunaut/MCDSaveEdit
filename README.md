# Personal MCDSaveEdit fork

Based on CutFlame/MCDSaveEdit commit `58fe34e`. This fork removes automatic telemetry and update lookups, modernizes the Windows build to .NET 10, protects save writes, and hardens PAK/image parsing. Changes are local to this repository; nothing is pushed to the original owner.

See [audit/SECURITY-AUDIT.md](audit/SECURITY-AUDIT.md), [audit/UPDATE-PLAN.md](audit/UPDATE-PLAN.md), and [VENDORED.md](VENDORED.md) for the original findings, implementation plan and source provenance. The audit describes the upstream baseline; current validation is recorded separately in [audit/IMPLEMENTATION-STATUS.md](audit/IMPLEMENTATION-STATUS.md).

# &nbsp;[![icon](MCDSaveEdit/Properties/icon.ico)]() Minecraft: Dungeons Save File Editor

[![GitHub](https://img.shields.io/github/license/cutflame/mcdsaveedit)](https://github.com/CutFlame/MCDSaveEdit/blob/master/LICENSE)
[![GitHub release (latest by date)](https://img.shields.io/github/v/release/cutflame/mcdsaveedit?label=latest)](https://github.com/CutFlame/MCDSaveEdit/releases/latest)
[![GitHub Release Date](https://img.shields.io/github/release-date/cutflame/mcdsaveedit)](https://github.com/CutFlame/MCDSaveEdit/releases/latest)
[![GitHub all releases](https://img.shields.io/github/downloads/cutflame/mcdsaveedit/total)](https://github.com/CutFlame/MCDSaveEdit/releases)
[![Patreon pledgers](https://img.shields.io/endpoint?url=https%3A%2F%2Fshieldsio-patreon.vercel.app%2Fapi%3Fusername%3Dcutflame%26type%3Dpatrons&style=flat)](https://patreon.com/cutflame)

Windows application for modifying [Minecraft: Dungeons](https://www.minecraft.net/en-us/about-dungeons/) save files.

#### DISCLAIMER: Please keep backups of your save files! This app does not guarantee your save file to be playable after editing!

<img src="Screenshots/screenshot.png"/>

---

### Installing and Running

This is a personal maintenance fork under development. Build from this checkout using the instructions below. Do not use the upstream 2022 release as a build of these changes. Windows runtime, real-game PAK compatibility and Steam Deck/Wine validation remain release gates.

Before replacing an existing save, the fork writes a same-directory staging file and atomically replaces the destination with a unique `.bak` backup. Keep those backups until you have verified the edited character in the game. Input saves are limited to 64 MiB, and PAK/texture allocations have documented bounds; unsupported files fail instead of allocating without limits.

---

### Troubleshooting

##### Fix Missing Images

<img src="Screenshots/GameContentLocationDialog.png"/>

If you see this popup, that means it couldn't find the game content in the default location. You need to provide the path to these `.pak` files:

<img src="Screenshots/LocatePakFiles.png"/>

##### Default location of pak files

Minecraft Launcher:

`%localappdata%\Mojang\products\dungeons\dungeons\Dungeons\Content\Paks`

Steam:

`%programfiles(x86)%\Steam\steamapps\common\MinecraftDungeons\Dungeons\Content\Paks`

Microsoft Store:

`C:\XboxGames\Minecraft Dungeons\Content\Dungeons\Content\Paks`

If you're not sure which version you have, additional information may be found on [Dokustash - stash.dokucraft.co.uk](https://stash.dokucraft.co.uk/?help=modding-dungeons)

##### Application Stopped Working

If during launch you get a popup saying that MCDSaveEdit has stopped working,
this means an internal error ocurred and could mean various issues.
MCDSaveEdit has a dependency on .NET Framework 4.8 so you could try installing that.

- [.NET Framework Offline installer](https://support.microsoft.com/en-us/topic/microsoft-net-framework-4-8-offline-installer-for-windows-9d23f658-3b97-68ab-d013-aa3c3e7495e0)

---

### How to Use

1. Go to `File > Open` in the menu bar.
2. Open the folder `%HOMEPATH%\Saved Games\Mojang Studios\Dungeons`. *Be sure to backup this folder before editing your save files.*
3. You will see a folder with a 16-digit number (E.g. 2612325419657064). Open this folder, then open the "Characters" folder.
    1. NOTE: If multiple Mojang or Microsoft accounts were used in-game you might see more than one folder. 
4. Select one of the files ending in `.dat`. There will be one for each character.
5. Click `Open` and edit your save. When ready go to `File > Save` or `File > Save As...` to save your edits.

<p></p>

##### Adding Items

<img src="Screenshots/AddItemTutorial.png"/>

<p></p>

##### Adding/Changing Enchantments

<img src="Screenshots/AddEnchantmentTutorial.png"/>

<p></p>

---

### Building this personal fork

Install the exact .NET SDK in `global.json` and build on Windows:

```powershell
dotnet restore MCDSaveEdit.sln --locked-mode
dotnet build MCDSaveEdit.sln --no-restore -c Release
dotnet test MCDSaveEditTests/MCDSaveEditTests.csproj --no-build -c Release --filter "TestCategory!=GameIntegration"
dotnet run --project MCDSaveEdit -- SKIP_GAME_CONTENT
```

Save editing needs no analytics credentials or network connection. Optional installed-game image/localization access needs your locally supplied game-content AES key in the `MCD_PAK_AES_KEY` environment variable (64 hexadecimal characters, optionally prefixed with `0x`). Never commit that value. Select the PAK directory in the folder picker; no private `Secrets.cs` or machine-specific Windows SDK path is required.

Cross-platform production-source security tests:

```sh
dotnet restore SecurityTests/SecurityTests.csproj --locked-mode
dotnet test SecurityTests/SecurityTests.csproj --no-restore -c Release
python3 -m unittest discover -s scripts -p 'test_*.py' -v
```

The GitHub workflow performs the Windows and native-artifact checks and combines them in a single **Final validation** gate. See [SECURITY.md](SECURITY.md). No workflow publishes releases or pushes changes. A GitHub fork/remote has not been configured by this local implementation.

---

### Legal Disclaimer

This project is not affiliated with Mojang Studios, Microsoft, XBox Game Studios, Double Eleven or the Minecraft brand.

"Minecraft" is a trademark of Mojang Synergies AB.

Other trademarks referenced herein are property of their respective owners.

### External Credits and Licenses

Images from the game are subject to copyright by Mojang. They are extracted at runtime from the .pak files that are installed as part of the Minecraft: Dungeons game files.

[DungeonTools](https://github.com/HellPie/DungeonTools) © Diego Russi ([AGPL 3.0](https://github.com/HellPie/DungeonTools/blob/master/LICENSE))

[Microsoft.Bcl.AsyncInterfaces](https://github.com/dotnet/corefx) © Microsoft ([MIT](https://licenses.nuget.org/MIT))

[Fody](https://github.com/Fody/Fody) © Simon Cropp ([MIT](https://github.com/Fody/Fody/blob/master/License.txt))

[Costura.Fody](https://github.com/Fody/Costura) © Simon Cropp and contributors ([MIT](https://github.com/Fody/Costura/blob/develop/LICENSE))

[FModel](https://github.com/iAmAsval/FModel) © Free Software Foundation, Inc. ([GPL 3.0](https://github.com/iAmAsval/FModel/blob/master/LICENSE))

[PakReader](https://github.com/WorkingRobot/PakReader) © Aleks Margarian ([MIT](https://github.com/WorkingRobot/PakReader/blob/master/LICENSE))

[Game-icons.net](https://game-icons.net/) © Lorc, Delapouite and contributors ([CC BY 3.0](http://creativecommons.org/licenses/by/3.0/))
