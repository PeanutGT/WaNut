# WaNut — Minecraft: Java Edition Server Launcher Blueprint (Windows)

> Revision 3 (2026-10-14). Revised with Peanut from the original blueprint; revision 3 adds the positioning decisions (14–20).
> Repository: https://github.com/PeanutGT/WaNut

## Project Vision
Build an intuitive, zero-friction Windows desktop application that enables casual players to create, customize, launch, and share a **Minecraft: Java Edition** server on their PC without touching command lines, system environment variables, or router configuration pages.

## Decisions Log

| # | Topic | Decision |
| :-- | :-- | :-- |
| 1 | App name | **WaNut** (working name). Data folder `%APPDATA%\WaNut\`. |
| 2 | UI language | English first. All UI text lives in resource files from day one, so Traditional Chinese (zh-TW) is a translation file, not a code change. |
| 3 | Playing with friends | Phase 1 targets LAN and virtual-LAN tools (Radmin VPN, ZeroTier, Tailscale). Built-in tunnel (playit.gg) and UPnP move later. |
| 4 | File location | `%APPDATA%\WaNut\` by default. Settings let the user move the `instances/` folder (e.g. to drive D:). "Open folder" button on every server. |
| 5 | Concurrent servers | One running server at a time. Multiple servers may exist; only one runs. Revisit later. |
| 6 | Forge / NeoForge | Later phase. Phase 1: Vanilla, Paper, Fabric. |
| 7 | Backups | Phase 1: automatic world backup before each start, with automatic cleanup (keep last N, delete older than X days). |
| 8 | Self-update | Yes. The launcher checks GitHub Releases of PeanutGT/WaNut and updates itself. |
| 9 | Git attribution | Commits and PRs carry no Claude co-author line, so Claude does not appear among the repository's contributors. |
| 10 | Backup defaults | Keep the latest **5** backups; delete backups older than **30 days**. Both adjustable in settings. |
| 11 | Oldest supported MC | **1.16.5**. Older versions are not offered in the version list. |
| 12 | Installer | **Setup .exe** installer (works best with self-update). |
| 13 | .NET version | **.NET 10 (LTS)** instead of .NET 8. |
| 14 | Audience | Beginners through advanced users. The feature set reaches advanced use, but the default screens stay simple. |
| 15 | Hiding advanced settings | Two layers: a **"More settings"** expander on each settings page, and a global **Advanced mode** switch in app settings that shows advanced-only items. Phase 1 builds both; Advanced mode holds only **custom JVM arguments** for now. |
| 16 | Tone | Cute and playful, with the peanut mascot appearing often. Errors (crash, failed backup) and Advanced mode sections use plain, calm wording with the fix. One set of texts. |
| 17 | Guided onboarding | After Phase 1. Phase 1 has only the Create Server wizard; a peanut-led guide comes once features are stable. |
| 18 | References | Modrinth App (clean, modern UI) and Duolingo (mascot, guidance), as direction only. |
| 19 | Modpack servers | Phase 2, with the mod browser and Forge. Modrinth `.mrpack` first; CurseForge starts as importing a server pack the user already downloaded. |
| 20 | Settings layout | Tabs first, then tiers (always visible / More settings / Advanced mode), per section 4b. Whitelist, OP list and the RAM slider are always visible; changing type or version backs up first and warns on downgrade. |
| 21 | New server whitelist | **On** by default for every new server, on every Minecraft version. The Players tab warns while the list is empty. |

---

## 1. Zero-Friction Setup (The "1-Click" Core)

### A. Automated Portable Java Runtime (JRE) Management
Different Minecraft versions require specific Java versions. **Never rely on the user's Windows `PATH` or pre-installed Java.**
* **Version source (revised):** Read the required Java major version from Mojang's own version data (`javaVersion.majorVersion` in each version's JSON). This stays correct for new versions, including the year-based numbering (26.x). Paper and Fabric servers use the same lookup for their Minecraft version.
* **Fallback table** (only if Mojang data is unavailable):
  * MC 1.16.5: Java 8 (oldest supported version, decision 11)
  * MC 1.17 – 1.20.4: Java 17 (1.17 officially asks for Java 16; Java 17 runs it fine, so Java 16 is dropped)
  * MC 1.20.5 – 1.21.x: Java 21
  * MC 26.x (year-based): Java 25 (confirmed on 26.3 against Mojang data, 2026-10-14)
* **Implementation:** Query the **Eclipse Adoptium (Temurin) API** (`api.adoptium.net/v3`) for the Windows x64 `.zip` JRE, verify its checksum, extract into `runtimes/java-<major>/`.
* **Execution:** Run `java.exe` by absolute path with a hidden console (see section 3).

### B. Automated Server Core Provisioning
Users pick server type and version from dropdowns, fetched from official APIs:
* **Vanilla:** Mojang Version Manifest (`piston-meta.mojang.com/mc/game/version_manifest_v2.json`); download `server.jar` and verify its SHA-1.
* **PaperMC (revised):** Paper's **Fill v3 API** (`fill.papermc.io/v3/projects/paper`). The old v2 API is being retired. Use the newest STABLE build (BETA only when a version has no stable build yet) and verify SHA-256.
* **Fabric:** versions from the Fabric Meta API (`meta.fabricmc.net/v2`). Meta's ready-made server jar has no checksum, so WaNut downloads the same launcher (`fabric-installer-<v>-server.jar`) from Fabric's Maven, verifies it against Maven's `.sha256`, and adds the `install.properties` (loader and game version) itself. The Vanilla `server.jar` (SHA-1 from Mojang) sits next to it.
* **Forge / NeoForge:** later phase (decision 6).
* Downloads are cached in `cache/` so a second server of the same version needs no download.

### C. EULA & Smart JVM Arguments
* **One-Click EULA:** A checkbox with a link to the Minecraft EULA before the first launch; checking it writes `eula=true` into `eula.txt`.
* **Smart RAM Slider:** Detect total physical RAM (`GlobalMemoryStatusEx`) and cap the slider at 75% of it.
* **Startup Flags (revised):**
  * Java 17 and below: Aikar's Flags.
  * Java 21–22: `-XX:+UseZGC -XX:+ZGenerational`.
  * Java 23+: `-XX:+UseZGC` only (generational is the default there; the extra flag prints a warning or fails).
  * Small heaps (under about 4 GB): consider Aikar's G1 flags even on Java 21+, to be decided during testing.

---

## 2. Playing with Friends (revised)

### Phase 1: LAN and virtual LAN
* Show the PC's IPv4 addresses with a **Copy** button, labelled by adapter (e.g. "Wi-Fi", "Radmin VPN", "ZeroTier", "Tailscale") so the user knows which one to give friends.
* Show the port (default `25565`) next to it, in the `IP:port` form players paste into Minecraft.
* **Windows Defender Firewall rule:** still needed for LAN and Radmin VPN, otherwise Windows can silently block friends. On first launch of a server, ask once for administrator approval (UAC) and add an inbound rule per bundled `java.exe` (rules apply to a program file, not a folder).
* The rule covers every network profile: Windows usually files Radmin VPN, and often the home network too, as "Public". WaNut also removes inbound block rules for its own `java.exe` (left behind when Windows' own prompt was cancelled), since a block rule wins over any allow rule. The question comes once per `java.exe` (remembered in `config.json`); the Connect tab shows the firewall state and can add the rule later. Setting `WANUT_FIREWALL_DRY_RUN=1` writes the PowerShell it would run to `firewall-dry-run.ps1` in the data folder instead of asking for UAC.
* The address list hides disconnected adapters, `169.254.x.x` addresses and virtual-machine adapters (Hyper-V/WSL, VirtualBox, VMware, Bluetooth). Radmin VPN, ZeroTier, Tailscale and Hamachi are recognised by their adapter description, because adapter names are translated on non-English Windows ("乙太網路").
* A short in-app guide, "Playing with friends over the internet", under More settings on the Connect tab. It is tool-neutral (the main tool differs by region): general steps for host and friends, then one line per common tool (Radmin VPN, ZeroTier, Tailscale, Hamachi) naming where to click. Any adapter with a usable IPv4 is listed, under its Windows name when WaNut doesn't know the tool.

### Later phases: over the internet without extra tools
| Method | How It Works on Windows | Pros | Cons |
| :--- | :--- | :--- | :--- |
| **UPnP** | Forward TCP/UDP `25565` on the router automatically. | No accounts; lowest latency. | Fails if router UPnP is off or the ISP uses CGNAT. |
| **Built-in tunnel** | Bundle a tunnel agent (e.g. **playit.gg**) and show a copyable address. | Works through routers and CGNAT; hides the host IP. | Slightly higher ping; relies on an outside service. |

---

## 3. Windows Process Control & Safe Shutdown

* **Hidden Subprocess:** Start `java.exe` with `CREATE_NO_WINDOW` and redirect `stdin`, `stdout`, `stderr` to the GUI.
* **Live State Detection** from `stdout`:
  * `Done (X.XXXs)! For help, type "help"` → status **Online**.
  * `joined the game` / `left the game` → live player list.
* **Graceful Shutdown:**
  * Intercept window close.
  * Never use `taskkill /F` or `Process.Kill()` as the primary shutdown.
  * Send `stop`, show a "Saving world..." spinner, wait for exit. Only if the server is still running after a long timeout, ask the user before force-closing.
* **Windows Job Object failsafe:** `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`, so a launcher crash leaves no zombie `java.exe`.
* **One server at a time** (decision 5): starting a second server asks to stop the first.

---

## 4. Tech Stack

**C# / .NET 10, WPF**, `CommunityToolkit.Mvvm`.
* Target framework: net10.0 (LTS). Chosen over .NET 8 because .NET 8 support ends November 2026.
* `System.Diagnostics.Process` for the server process; P/Invoke for Job Objects and memory.
* Resource files (`.resx`) for all UI text (decision 2).
* Self-update from GitHub Releases (decision 8) with **Velopack**: the Setup.exe installs per user into `%LOCALAPPDATA%\WaNut` (no UAC), and each update is checked against the SHA-256 in the release feed. Server data stays in `%APPDATA%\WaNut\`, so updating or uninstalling never touches worlds.
  * WaNut checks on start (can be turned off in App settings) and shows a peanut card in the sidebar; "Update now" stops a running server the normal way first, then downloads and restarts.
  * Releases are built by `.github/workflows/release.yml` only when a `v*` tag is pushed (e.g. `v0.1.0`). Setting `WANUT_UPDATE_SOURCE` to a folder or https URL with a Velopack feed tests updates without a published release.
* Later: `Open.Nat` (UPnP).

---

## 4b. Look, Tone and Settings Visibility

* **Visual style:** follow `design/design-decisions.md` in the project files (warm off-white palette, warm dark mode that follows the Windows theme, green only for the primary action and Online state, 16x16 pixel peanut logo and `wanut.ico`).
* **Mascot (decision 16):** the peanut appears often: empty states, Starting/Saving, success messages. Never in error messages or Advanced mode sections. This replaces "mascot sparingly" in the design notes.
* **"More settings" (decision 15):** each settings page shows the common items first and the rest under an expander, e.g. the `server.properties` editor shows gamemode, difficulty and PvP first.
* **Advanced mode (decision 15):** a switch in app settings. Items marked advanced-only are hidden until it is on. Phase 1 item: custom JVM arguments per server, added after WaNut's own flags.
* **Tabs first, then tiers (confirmed by Peanut, `product/feature-tiers.md` v2):** each server has the tabs Console, Players, Game settings, Server, Backups and Connect; App settings is its own window. Inside each tab, items are always visible, under "More settings", or Advanced mode only. "Common" means "you meet it when playing with friends", not "simple".

| Tab | Always visible | More settings | Advanced mode |
| :-- | :-- | :-- | :-- |
| Top bar | Start/Stop, status, Open folder | | Force close |
| Console | Console, command box | | Log detail level |
| Players | Online players, whitelist switch and list, OP list | Kick/ban (commands in Phase 1, buttons in Phase 3) | |
| Game settings | Gamemode, difficulty, PvP, max players, MOTD, keepInventory | View/simulation distance, spawn protection, command blocks, flight, mob spawning, hardcore, seed (before the world exists) | Raw `server.properties` editor |
| Server | RAM slider | Change type or version (automatic backup first, warning on downgrade), port, `online-mode` (warning when turned off) | Custom JVM arguments, choose Java manually, use your own server.jar |
| Backups | Backup list, restore, back up now | Keep count, delete after days | |
| Connect | IPv4 per adapter with Copy | Virtual LAN guide (Radmin VPN, ZeroTier, Tailscale, Hamachi) | |
| App settings | | Instances folder, update check | Advanced mode switch; clear download cache |

  Phase 1 Advanced mode contains only custom JVM arguments; the other advanced items come later.

---

## 5. Directory & File Structure

```text
%APPDATA%\WaNut\
├── config.json                  # App settings (language, instances path, backup rules)
├── runtimes/                    # Auto-downloaded portable JREs
│   ├── java-8/
│   ├── java-17/
│   └── java-21/
├── cache/                       # Cached server jars
└── instances/                   # Movable via settings (decision 4)
    └── survival-1.21.1/
        ├── instance.json        # MC version, loader, RAM, play time
        ├── server.jar
        ├── eula.txt
        ├── server.properties
        ├── world/
        ├── mods/                # Fabric
        └── backups/             # Timestamped .zip world snapshots
```

### Visual `server.properties` Editor
* **Gamemode:** Survival / Creative / Adventure
* **Difficulty:** Peaceful / Easy / Normal / Hard
* **PvP (`pvp`):** toggle
* **Max players, MOTD, port:** simple fields
* **Account verification (`online-mode`, revised):** on by default. Turning it off shows a clear warning: anyone can join using any name, including an admin's, and player data can be taken over.
* **Keep Inventory:** sent through `stdin` (`/gamerule keepInventory true`) after the server is online.

### Backups (decision 7)
* Before each start, zip `world/` (and the Nether/End folders) into `backups/<timestamp>.zip`.
* Cleanup rules in settings: keep the last N backups (default 5) and delete backups older than X days (default 30).
* Restore a backup from the UI (server must be stopped). Restoring backs up the current world first, so a restore can be undone. These safety copies don't count toward the keep count (they still expire by age), so a few starts after a restore can't delete the only way back.
* Backups and restores run only while the server is stopped; a running server keeps writing its region files. "Back up now" is greyed out while it runs.
* The newest backup is always kept, even when it is older than X days, so a server left alone for months still has one.
* The cleanup rules live in `config.json` and apply to every server. They are edited under "More settings" on the Backups tab.
* Backups hold the world only, not plugins, mods or config. From 26.x every dimension sits inside the level-name folder (`world/dimensions/minecraft/...`) for Vanilla, Paper and Fabric. Paper up to 1.21.x still splits off `world_nether` and `world_the_end`, so those are included when present.
* If the backup before a start fails (e.g. a full disk), WaNut asks whether to start anyway.

### Change type or version (Server tab, More settings)
* Opens a window with the type list and version list from the Create Server wizard. Only while the server is stopped.
* Order: download and verify the new files and Java first (a failed download changes nothing), then back up the world (a "change" backup, protected from the keep count like a restore copy), then swap the server files.
* New files are copied in under temporary names before any old file is deleted. The jars' unpacked folders (`libraries/`, `versions/`, `.fabric/`, Paper's `cache/`) are removed so they come back for the new version.
* Switching between Paper and Vanilla/Fabric on 1.21.x or older moves the Nether and End folders between Paper's layout (`world_nether/DIM-1`, `world_the_end/DIM1`) and the Vanilla one (`world/DIM-1`, `world/DIM1`). 26.x keeps them inside the world for every type, so nothing moves there.
* The world, `server.properties`, whitelist, OP list, mods and plugins stay. Game settings are re-saved for the new version, since some moved between `server.properties` and game rules (e.g. pvp in 1.21.9).
* Going to an older Minecraft version while a world exists shows a warning and needs an extra tick, because older versions can't always read newer worlds.

---

## 6. Phased Roadmap (revised)

### Phase 1: MVP
* [ ] English UI built on resource files; "Create Server" wizard (name, MC version, Vanilla/Paper/Fabric, RAM slider).
* [ ] Adoptium JRE downloader (Java version from Mojang data); Mojang / Paper (v3) / Fabric jar fetcher with checksum checks and cache.
* [ ] `eula.txt` creation and basic `server.properties` editor.
* [ ] Process controller: live console, command box, Online status, player list, graceful `stop`, Job Object failsafe.
* [ ] LAN / virtual-LAN address panel with Copy button, and Windows Firewall rule.
* [ ] Automatic backup before start, with cleanup rules.
* [ ] Self-update from GitHub Releases.
* [ ] Visual style (warm palette, dark mode, icon, peanut mascot).
* [ ] "More settings" expanders; Advanced mode switch with custom JVM arguments.

### Phase 2: Easy Multiplayer & Customization
* [ ] Traditional Chinese translation.
* [ ] Peanut-led guided onboarding (decision 17).
* [ ] UPnP port forwarding and built-in tunnel (playit.gg or similar).
* [ ] Modrinth / CurseForge mod and plugin browser with version/loader filtering.
* [ ] Forge / NeoForge support.
* [ ] Modpack server install (decision 19):
  * **Modrinth `.mrpack`:** read `modrinth.index.json`, keep only files whose `env.server` is not `unsupported`, verify each file's sha1/sha512, apply `overrides/` then `server-overrides/`, and pick the loader and Minecraft version from `dependencies`. Forge / NeoForge packs need Forge support first.
  * **CurseForge:** import a server pack zip the user already downloaded. Automatic download is hard because the API needs a key and some authors block third-party downloads.

### Phase 3: Quality of Life & Safety
* [ ] Scheduled backups and backup restore improvements.
* [ ] Visual Player Manager (kick, ban, OP, whitelist, avatars).
* [ ] Smart Crash Analyzer (duplicate mods, missing Fabric API, port in use) with plain-language fixes.
* [ ] Several servers running at the same time (decision 5, revisit).

---

## 7. Implementation Plan

### Solution layout
```text
WaNut/
├── WaNut.sln
├── src/
│   ├── WaNut.Core/        # All logic, no UI: downloads, Java, instances, properties, backups, process control
│   └── WaNut.App/         # WPF UI (MVVM): windows, view models, resource files for text
├── tests/
│   └── WaNut.Core.Tests/  # Unit tests for Core (version mapping, properties parser, log parser, backup cleanup)
└── installer/             # Setup .exe and update packaging
```
Keeping logic in `WaNut.Core` and the screens in `WaNut.App` means each feature can be tested without the UI, and a future UI change does not touch the logic.

### Core modules (WaNut.Core)
| Module | Job |
| :-- | :-- |
| `Paths` | Resolves `%APPDATA%\WaNut\` and the (movable) instances folder. |
| `Download` | One HTTP downloader with progress, checksum check (SHA-1/SHA-256) and the `cache/` folder. |
| `Versions` | Fetches version lists from Mojang, Paper Fill v3, Fabric Meta; filters to 1.16.5+. |
| `Java` | Looks up the needed Java version from Mojang data, downloads Temurin JRE from Adoptium, finds `java.exe`. |
| `Instances` | Creates a server folder, writes `instance.json`, `eula.txt`, default `server.properties`. |
| `Properties` | Reads and writes `server.properties` while keeping comments and unknown keys. |
| `Jvm` | Builds the start arguments (RAM, Aikar or ZGC flags by Java version). |
| `Process` | Starts `java.exe` hidden, streams console lines, sends commands, graceful `stop`, Job Object failsafe. |
| `LogParser` | Detects Online, player join/leave from console lines. |
| `Backups` | Zips worlds before start, applies keep-5 / 30-day cleanup, restores. |
| `Network` | Lists IPv4 addresses per adapter (Wi-Fi, Radmin VPN...), adds the firewall rule (with UAC). |
| `Updates` | Checks GitHub Releases and applies updates (Velopack). |

### Screens (WaNut.App)
1. **Main window:** server list on the left; on the right the selected server's status badge, Start/Stop, Open folder, and tabs: Console, Players, Game settings, Server, Backups, Connect (section 4b).
2. **Create Server wizard:** name → type (Vanilla/Paper/Fabric) → version → RAM slider → EULA checkbox → Create (progress bar while Java and the server jar download).
3. **App settings:** language (later), instances folder, backup rules, update check, Advanced mode switch.

### Start a server: what happens in order
1. Check no other server is running (decision 5).
2. Back up the world, then run cleanup.
3. Make sure the right Java is installed; download it if not.
4. First time only: add the firewall rule (one UAC prompt).
5. Start `java.exe` hidden with the built arguments; show console; status "Starting".
6. On `Done (...)!` → status "Online"; apply queued commands such as keepInventory.

### Build order (each step ends with something runnable)
1. ✓ Solution skeleton, Core tests running, empty main window.
2. ✓ Create Server wizard with Vanilla: download Java + server.jar, write EULA.
3. ✓ Start/stop with live console, command box, Online status, players list.
4. ✓ Paper and Fabric support.
5. ✓ Visual style: warm palette, dark mode, app icon, peanut mascot in empty and Starting/Saving states (section 4b).
6. ✓ Game settings tab (`server.properties` editor with "More settings"; a default `server.properties` is written when a server is created) and Players tab additions (whitelist switch and list, OP list).
7. ✓ Server tab (RAM slider, port, `online-mode`) and the App settings window with the Advanced mode switch; custom JVM arguments as the first advanced-only item.
8. ✓ Backups tab with cleanup; then "Change type or version" in the Server tab, which needs a backup first.
9. ✓ Connect tab (IP list) and firewall rule.
10. ✓ Installer and self-update; first GitHub Release (v0.1.0).
11. First-run setup window: desktop shortcut (the installer only adds a Start menu shortcut), servers folder, update check.
12. Move the servers folder later from App settings (More settings), carrying existing servers across.

### Development and testing
* Code lives in the WaNut repo; work goes through pull requests for Peanut to review.
* Core unit tests run on every push (GitHub Actions on a Windows runner, which also builds the WPF app).
* Real-world testing (starting an actual Minecraft server, Radmin VPN, UAC) has to happen on Peanut's Windows PC.

---

## Open Questions
* Content of the guided onboarding (decision 17, after Phase 1).
