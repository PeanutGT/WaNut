# WaNut

A Windows app for starting your own Minecraft: Java Edition server in a few clicks: no command line, no Java setup, no config files.

> Status: early development. Not ready for use yet.

## Planned features (first version)
- Create a Vanilla, Paper or Fabric server from a simple wizard
- Java is downloaded automatically for the Minecraft version you pick
- Live console, player list and safe shutdown that never corrupts your world
- Automatic world backup before every start
- Play with friends over LAN or Radmin VPN

See [docs/blueprint.md](docs/blueprint.md) for the full design.

## Installing
Download `WaNut-win-Setup.exe` from the [latest release](https://github.com/PeanutGT/WaNut/releases/latest) and run it. WaNut installs for your Windows account only, so no administrator approval is needed, and it updates itself when a new version comes out. Your servers and worlds live in `%APPDATA%\WaNut` and are kept when WaNut updates or is uninstalled.

## Building
Requires Windows and the .NET 10 SDK.

```
dotnet build
dotnet test
```
