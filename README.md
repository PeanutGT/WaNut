# WaNut

<p align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="docs/images/banner-dark.png">
    <img src="docs/images/banner-light.png" alt="WaNut" width="624">
  </picture>
</p>

A Windows app for starting your own Minecraft: Java Edition server in a few clicks: no command line, no Java setup, no config files.

> Status: early release (v0.2.5). Works, but expect rough edges.

## Features
- Create a Vanilla, Paper, Fabric, Forge or NeoForge server (Minecraft 1.16.5 and newer) from a simple wizard
- Start from a modpack: a Modrinth `.mrpack` or a CurseForge server pack sets up the loader, mods and configs for you
- Import a world, and a Fabric world's mods are installed with it
- Java is downloaded automatically for the Minecraft version you pick
- Live console, player list and safe shutdown that never corrupts your world
- Game settings, whitelist and OP list without editing files; "More settings" and an Advanced mode for the rest
- Automatic world backup before every start, with restore and cleanup rules
- Play with friends over LAN or a virtual LAN (Radmin VPN, ZeroTier, Tailscale, Hamachi), with the firewall rule added for you
- English and Traditional Chinese, light and dark colors, and a pixel peanut to keep you company
- Updates itself from GitHub Releases
See [docs/blueprint.md](docs/blueprint.md) for the full design.

## Meet the peanut
A small pixel peanut keeps you company while your server runs.

<p align="center">
  <img src="docs/images/guide-hello.gif" alt="Peanut waving hello" width="96">
  <img src="docs/images/starting.gif" alt="Server starting" width="96">
  <img src="docs/images/online.gif" alt="Server online" width="96">
  <img src="docs/images/friends.gif" alt="Friends joining" width="96">
  <img src="docs/images/backup-done.gif" alt="Backup done" width="96">
  <img src="docs/images/downloading.gif" alt="Downloading" width="96">
</p>

## Installing
Download `WaNut-win-Setup.exe` from the [latest release](https://github.com/PeanutGT/WaNut/releases/latest) and run it. WaNut installs for your Windows account only, so no administrator approval is needed, and it updates itself when a new version comes out. Your servers and worlds live in `%APPDATA%\WaNut` and are kept when WaNut updates or is uninstalled. The installer is not signed yet, so Windows SmartScreen may warn the first time: choose "More info" and then "Run anyway".

## Building
Requires Windows and the .NET 10 SDK.

```
dotnet build
dotnet test
```
