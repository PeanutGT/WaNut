# WaNut

Windows desktop app (C# / .NET 10, WPF) that lets casual players create and run a Minecraft: Java Edition server in a few clicks.

## Rules
- Follow every rule in @./docs/CONVENTIONS.md. It is written in Traditional Chinese; code, identifiers, comments and commit messages are in English.
- Never add AI co-author lines or AI attribution (Gemini, Claude or other) to commits or PRs.
- Never push directly to `main`; work on a branch and open a PR for Peanut to review.
- Before starting a task, use any available skill or extension that fits it.
- Build: `dotnet build` · Test: `dotnet test` · Format: `dotnet format`

## Reading the spec
`docs/blueprint.md` (about 30 KB) is the product spec and decision log. It is not loaded automatically; read only the sections your task touches, e.g. `grep -n '^#' docs/blueprint.md` and then that section. The code is the source of truth; read every file you change.
- Decisions: "Decisions Log" · Java, jars, JVM flags: section 1 · Connect/firewall: 2 · process control: 3
- Tabs and what is always visible / More settings / Advanced mode: 4b
- Backups, world import/export, delete server, change type or version: 5 · Build order and next step: 7

## Code map
- `src/WaNut.Core/` logic only, never references WPF. One folder per module: Backups, Crashes (crash cause from console + crash report, fix helpers), Downloads, Hosting (process, log parser, Job Object), Http, Instances, Java, Jvm, Modpacks (.mrpack and CurseForge server pack read + install), Mods (Modrinth; `ModJars` reads mod ids from jars), Network (adapters, firewall), Players, Properties, Settings, Updates (Velopack), Versions (Mojang, Paper, Fabric, Forge, NeoForge; `ForgeInstallerRunner` runs the loader installers), Worlds (import/export, `Nbt/`). `LauncherPaths.cs` resolves data folders.
- Version-dependent rules (properties vs game rules, world layout, presets) live in `src/WaNut.Core/Settings/GameFeatures.cs`.
- `src/WaNut.App/` WPF + MVVM (CommunityToolkit.Mvvm) + DI. Each tab is `Views/<Tab>View.xaml` + `ViewModels/<Tab>ViewModel.cs` (Players, GameSettings, ServerSettings, Backups, Mods, Connect); windows are `Views/*Window.xaml`. `Services/ServerController.cs` runs the server. UI text: `Resources/Strings.resx` and `Strings.zh-TW.resx` (keys `<Screen>_<Element>`, every key in both). Colors: brush keys from `Themes/` only.
- `tests/WaNut.Core.Tests/` xUnit, one folder per Core module. `tests/FakeMinecraftServer/` fakes server output.
- `.github/workflows/ci.yml` build + test on Windows; `release.yml` runs on `v*` tags.
