# Vehicle Trunk

A mod for [*The Last Stand: Aftermath*](https://www.nexusmods.com/thelaststandaftermath) that adds a storage trunk to your vehicle. Walk up to your vehicle and open the trunk like a stash: move loot in and out so you do not carry the weight. The trunk has its own weight limit, keeps its contents between missions and across save and reload, and empties when your survivor dies.

Over-encumbrance is a heavy debuff, so a trunk lets you keep the loot you find without carrying all of it. It reuses the game's own stash screen, so it looks and works like the persistent stashes already in the game.

You can open the trunk while holding a weapon or carrying a fuel can. If you have a supply bag on your back, hand it in first, then open the trunk.

Customize the trunk's weight limit with `TrunkWeightCapacity` (default **100**) in `BepInEx\config\com.ivmakk.tlsa.vehicletrunk.cfg`. Lowering the limit never deletes stored items: you can still take them out, and adding more requires enough free capacity. See [CONFIG.md](CONFIG.md) for all settings, editing instructions, and an example.

## Install

1. Install [BepInEx 6 (IL2CPP)](https://www.nexusmods.com/thelaststandaftermath/mods/1) for The Last Stand: Aftermath. Start the game once so BepInEx finishes setup, then quit.
2. Extract this mod's zip into the game folder (the folder with the game .exe). The DLL lands in `BepInEx\plugins`. Full path examples:
   - Steam: `C:\Program Files (x86)\Steam\steamapps\common\The Last Stand Aftermath\BepInEx\plugins\VehicleTrunk.dll`
   - Epic: `C:\Program Files\Epic Games\The Last Stand Aftermath\BepInEx\plugins\VehicleTrunk.dll`

Not working? Open `BepInEx\LogOutput.log` and look for the `VehicleTrunk loaded` line.

## Uninstall

Delete `VehicleTrunk.dll` from the `BepInEx\plugins` folder. Empty the trunk first for a clean uninstall. Trunk items left behind are kept in the save but become inaccessible without the mod. They come back if you reinstall it.

## Build

This is a BepInEx 6 IL2CPP plugin. It compiles against the game's IL2CPP interop assemblies, so a working game install with BepInEx 6 set up is required. Those assemblies are game-derived and are not part of this repo.

```
dotnet build src/VehicleTrunk.csproj -c Release
```

`Directory.Build.props` sets `GameDir` to the default Steam install path. If the game lives elsewhere, override it without editing the file: set a `GameDir` environment variable, or pass `-p:GameDir=...` on the build. The output DLL is at `src\bin\Release\VehicleTrunk.dll`.

## Package

Add `-p:Package=true` to a Release build to also produce the ready-to-install zip at `dist\VehicleTrunk-<version>.zip`, laid out as `BepInEx\plugins\VehicleTrunk.dll` so a user extracts it at the game root. A plain build skips this step.

```
dotnet build src/VehicleTrunk.csproj -c Release -p:Package=true
```

## License

Licensed under the GNU General Public License v3.0. Copyright (C) 2026 ivmakk. See [LICENSE](LICENSE).

You may reuse and modify this mod, but you must keep it open under the same license and give credit. Do not reupload it without credit.
