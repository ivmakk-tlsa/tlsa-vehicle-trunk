# Configuration

Vehicle Trunk creates `BepInEx\config\com.ivmakk.tlsa.vehicletrunk.cfg` the first time you run the game with the mod installed. The path is relative to your game folder. Close the game before editing the file in a text editor, then start it again to apply your changes.

## Settings

All settings belong in the `[General]` section.

| Setting | Default | Values | What it does |
|---|---|---|---|
| `TrunkWeightCapacity` | `100` | Non-negative number, including decimals | Maximum total weight the trunk can hold, using the same weight units as the inventory. Set `200` for twice the default capacity or `50` for half. Negative values are treated as `0`. |
| `Verbose` | `false` | `true` / `false` | Writes trunk activity to the BepInEx log at Debug level for troubleshooting. Debug output must also be enabled in the BepInEx logger settings for these messages to appear. |

## Change the trunk capacity

For a trunk with a weight capacity of **200**, edit the existing entries to read:

```ini
[General]
TrunkWeightCapacity = 200
Verbose = false
```

Use a period for decimal values, for example `TrunkWeightCapacity = 150.5`.

Lowering the capacity below the weight already stored **does not delete any items**. You can still remove items; adding more requires enough free capacity under the new limit. Capacity changes do not alter item weights or your survivor's carrying limit.

The trunk keeps its contents between missions and across saves and reloads. Its contents are lost when your survivor dies, regardless of the capacity setting.
