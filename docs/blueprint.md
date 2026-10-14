# WaNut — Minecraft: Java Edition Server Launcher Blueprint (Windows)

> Revision 2 (2026-10-14). Revised with Peanut from the original blueprint.
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
* **PaperMC (revised):** Paper's **Fill v3 API** (`fill.papermc.io/v3/projects/paper`). The old v2 API is being retired. Verify SHA-256.
* **Fabric:** Fabric Meta API (`meta.fabricmc.net/v2`), server launcher jar for game + loader + installer versions.
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
* A short in-app guide: "Playing with friends over Radmin VPN".

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
* Self-update from GitHub Releases (decision 8); updater library to be picked (e.g. Velopack).
* Later: `Open.Nat` (UPnP).

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
* Restore a backup from the UI (server must be stopped).

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

### Phase 2: Easy Multiplayer & Customization
* [ ] Traditional Chinese translation.
* [ ] UPnP port forwarding and built-in tunnel (playit.gg or similar).
* [ ] Modrinth / CurseForge mod and plugin browser with version/loader filtering.
* [ ] Forge / NeoForge support.

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
| `Updates` | Checks GitHub Releases and applies updates (library such as Velopack). |

### Screens (WaNut.App)
1. **Main window:** server list on the left; on the right the selected server's status badge, Start/Stop, and tabs: Console, Players, Settings, Backups, Connect (IP + Copy).
2. **Create Server wizard:** name → type (Vanilla/Paper/Fabric) → version → RAM slider → EULA checkbox → Create (progress bar while Java and the server jar download).
3. **App settings:** language (later), instances folder, backup rules, update check.

### Start a server: what happens in order
1. Check no other server is running (decision 5).
2. Back up the world, then run cleanup.
3. Make sure the right Java is installed; download it if not.
4. First time only: add the firewall rule (one UAC prompt).
5. Start `java.exe` hidden with the built arguments; show console; status "Starting".
6. On `Done (...)!` → status "Online"; apply queued commands such as keepInventory.

### Build order (each step ends with something runnable)
1. Solution skeleton, Core tests running, empty main window.
2. Create Server wizard with Vanilla: download Java + server.jar, write EULA.
3. Start/stop with live console, command box, Online status, players list.
4. Paper and Fabric support.
5. `server.properties` editor.
6. Backups with cleanup.
7. Connect tab (IP list) and firewall rule.
8. Installer and self-update; first GitHub Release.

### Development and testing
* Code lives in the WaNut repo; work goes through pull requests for Peanut to review.
* Core unit tests run on every push (GitHub Actions on a Windows runner, which also builds the WPF app).
* Real-world testing (starting an actual Minecraft server, Radmin VPN, UAC) has to happen on Peanut's Windows PC.

---

## Open Questions
* None right now.
