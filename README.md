# TShockPlrExporter

TShock 6.1.0 / Terraria 1.4.5.6 plugin that exports server-side character rows from `tshock.sqlite` to native Terraria `.plr` files.

## Commands

- `/exportplr <accountName|accountId|all>`
- Permission: `plrexporter.export`

Exports are written to `tshock/PlayerExports`.

## Build

Install the .NET 9 SDK, then run:

```powershell
dotnet build -c Release
```

Copy `bin/Release/net9.0/TShockPlrExporter.dll` into the server `ServerPlugins` directory and restart TShock.

## Notes

The plugin restores a `Terraria.Player` from TShock's `tsCharacter` row and then calls Terraria's own `Player.SavePlayer` method, so the final `.plr` writer is the Terraria 1.4.5.6 server assembly loaded by TShock.

Only data stored by TShock server-side characters can be exported. Client-only state that TShock never stores in `tsCharacter` cannot be reconstructed.