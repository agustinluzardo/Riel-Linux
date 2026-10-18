<p align="center"><img src="docs/linux/riel-linux-social.png" width="960" alt="Riel Linux — native railway simulation for Linux"></p>

# Riel Linux

**A native Linux train simulator and editor suite for Microsoft Train Simulator content.**

Riel lets you drive routes, play activities, explore with your chosen train and operate timetable
services. It includes a graphical launcher, terminal commands, a dispatcher map and native tools
for editing routes, consists, models and textures. The simulator and editors run without Wine.

The engine descends from [Free Train Simulator](https://github.com/perpetualKid/FreeTrainSimulator)
and [Open Rails](https://github.com/openrails/openrails). The editor suite is based on
[TSRE5vc](https://github.com/GokuMK/TSRE5vc), created by **Piotr Gadecki (GokuMK)**. Their work
and their contributors make Riel possible; see [the credits](CREDITS.md). This repository maintains
Riel's Linux integration, interface, fixes and releases.

[Download stable](https://github.com/agustinluzardo/Riel-Linux/releases/latest) ·
[All releases](https://github.com/agustinluzardo/Riel-Linux/releases) ·
[Installation](docs/linux/INSTALL.md) · [Changelog](CHANGELOG.md) ·
[Report a problem](https://github.com/agustinluzardo/Riel-Linux/issues)

## Download and get started

Stable **1.0.1** improves saved-game import/export, scenery resource handling and water-trough
traversal. Launcher scans share repeated locomotive reads within each scan while still detecting
new and modified content. It includes the refreshed English launcher, selectable accent colors,
nearby-train streaming fixes and native editor suite. See the
[1.0.1 release notes](docs/releases/1.0.1.md) for validation and known limitations.
Download `riel-linux-x64.zip` from the
[stable release](https://github.com/agustinluzardo/Riel-Linux/releases/latest), extract the complete
archive and run:

```sh
cd riel-linux-x64
./riel gui
```

The ZIP includes the .NET runtime and the editors' runtime. Playing does not require the .NET SDK
or a NuGet download. You need Linux x86-64, an OpenGL 3.3 driver, SDL 2, OpenAL, fontconfig, zlib
and the desktop libraries listed in [INSTALL.md](docs/linux/INSTALL.md). On Wayland, the launcher
uses XWayland.

Add the content folder containing `ROUTES` and `GLOBAL`, choose a route and activity, or switch
to **Explore** to select a path and consist. Content can live on another disk or inside a Wine
or Proton prefix; Riel reads it from that location. Routes and trains are obtained separately
and retain their authors' licenses.

![Riel launcher with visible route and locomotive information](docs/linux/launcher-preview.png)

This image is rendered from the actual launcher interface with sample route and train data.
Your routes, activities and trains appear after you add a content folder.

In **Settings → General → Launcher appearance**, choose Amber, Blue, Teal, Green,
Violet, Rose or Orange as the accent color. Preview the change immediately, then
press **Save** to remember it for both light and dark themes.

## What Riel does

### Simulation and driving

| Feature | What it provides |
| --- | --- |
| Activities | Load MSTS activities, drive the player's train, serve station stops and share the route with AI traffic. |
| Exploration | Choose a route, path, locomotive and consist, with configurable time, season and weather. |
| Timetables | Select a timetable set, service and operating day in the launcher. |
| Rolling stock | Use diesel, electric and steam locomotives, wagons and coaches defined by your content. |
| Physics | Simulate traction, adhesion, wheel slip, brakes, couplers, resistance, gradients and curves according to vehicle data and settings. |
| Operations | Couple and uncouple vehicles, operate switches, inspect cars, control doors and use distributed power where the train supports it. |
| Signaling | Read track databases, signals and scripts, speed limits, authorities and AI train routes. |
| Cabs and cameras | Use the stock's available 2D/3D cabs, exterior views, tracking cameras and free camera. |
| Environment | Display day/night, sky, fog, rain, snow, water, vegetation and distant mountains according to route content and settings. |
| Effects | Render smoke, steam, diesel exhaust, lights and other particle emitters defined by vehicles. |
| Sound | Play cab, engine, track and ambient sounds through OpenAL, with configurable volume and detail. |
| Saves and replay | Save sessions, resume them from the launcher and use the engine's replay functions. Older-save compatibility depends on the version. |
| Evaluation | Record speed and station stops for trip evaluation, and export physics, performance and driving data. |
| Controls | Configure keys and Ctrl/Shift/Alt combinations; use a RailDriver desk through Linux hidraw. |
| Driving aids and scripts | Configure the alerter, speed control, autopilot and TCS scripts supported by the locomotive; change cabs and manage trains in timetable mode. |
| Web server | Enable the engine's web server and configure its port in advanced settings. |

Compatibility depends on each route and vehicle's files. Functions requiring particular equipment
or scripts are available when that content supports them.

### Launcher and content management

- Browse and search routes, activities, paths, locomotives and consists.
- **Activity**, **Explore** and **Timetable** tabs with path and train information.
- Suggested departure times from the selected route's activities.
- Multiple content folders, MSTS installation discovery and locations configured in Wine/Proton.
- Content scanning at launcher startup, cached indexes and reopening after a run without repeating
  the full scan. A manual refresh is also available.
- Case-insensitive content references so files authored on Windows can be read on Linux file systems.
- A list of files or routes that could not be read, with the reason; other content remains available.
- Saved-game selection, activity testing, and access to the manual and content downloads.
- Profile settings for driving, audio, video, physics, keyboard, data logging, evaluation and
  advanced options.
- A Spanish translation, language selection through the system locale, and light/dark themes.
- Computer checks, detailed error messages, logs and native crash reports.
- Checks and installation of `main` updates that passed automated tests and packaging.
  **Stable** downloads are published separately in Releases.

### Dispatcher map and in-game panels

The dispatcher map shows tracks, trains, stations, platforms, signals and switches. Pan and zoom,
fit the whole route or follow the player's train. Selecting a train shows its speed, direction,
control mode, consist and next stop; signals show their state and stations show their platforms.

In a local session, you can request signal and switch changes. The simulator rejects changes to
occupied or reserved switches. Local dispatcher controls are disabled in multiplayer.

In-game panels include help, activity information, track monitor, driving, train/car operations,
next station, compass, train forces, distributed power and signaling diagnostics. Availability
depends on the session and vehicle.

| Default key | Action |
| --- | --- |
| F1 | Help and controls. |
| F2 | Save the session. |
| F3 | System information and the active renderer. |
| F4 | Track monitor. |
| F5 | Driving panel. |
| F9 | Car operations. |
| Ctrl + Alt + F9 | Train operations. |
| Ctrl + 9 | Dispatcher map. |
| Escape | Pause menu. |
| Alt + F4 | Quit. |

Bindings can be changed in **Settings → Keyboard**.

### Native editor suite: TSRE5vc

All four tools are integrated into **Tools** and use **TSRE5vc by Piotr Gadecki (GokuMK)**,
the evolution of his original [TSRE5](https://github.com/GokuMK/TSRE5) project. Riel adds launcher
integration, Linux packaging, a settings profile, content-path adaptation and stability fixes.
It retains the original credits, copyright headers and license notices.

| Tool | Purpose |
| --- | --- |
| Riel Route Editor | Create and edit MSTS/OR routes, terrain, tracks and objects using TSRE5vc's tools. Opens the route selected in the launcher. |
| Riel Consist Editor | Create, inspect and modify consists using rolling stock from the selected content folder. |
| Riel Shape Viewer | Inspect `.s` models, materials and textures. |
| Riel ACE Converter | Preview and convert ACE textures using TSRE5vc's tool; opens without selecting a route. |

The editors use OpenGL 3.3 and their own profile. Integration handles mixed-case references,
split `GLOBAL`/`Global` directories, X/Y Tracks and ACE/DDS textures. Texture loading uses a
bounded worker pool; logging is synchronized and truncated DDS files are rejected before decoding.
The editors write content files when you save changes.

Each release attaches `riel-route-editor-source.tar.gz` with the exact TSRE5vc source and patches
used to build its binaries. See [route-editor.md](docs/route-editor.md).

### Graphics, performance and diagnostics

- MonoGame DesktopGL rendering with configurable display/resolution, windowed, fullscreen and
  borderless modes.
- VSync, supported antialiasing, viewing distance, model detail, instancing, ambient brightness
  and dynamic shadows with configurable cascades, resolution and blur.
- Graphics uploads on the OpenGL-owning thread, resource queue servicing and visual prioritization
  of nearby AI trains.
- **Experimental Vulkan through Mesa Zink**, enabled in Settings and applied after restarting
  the simulator. The engine emits OpenGL, which Zink executes over Vulkan.
- F3 identifies the real renderer: **OpenGL over Vulkan (Zink)** when active. Requesting Vulkan
  does not prove that the driver selected it.
- System/performance information and available CPU/GPU temperatures; unavailable or ambiguous
  sensors show `n/a`.
- `riel doctor` checks binaries, shaders, sound/window libraries, the display session and content.
  RailDriver is reported as optional hardware.
- Logs, startup stages and native crash reports accessible from the launcher.
- Traces for AI trains, signals, road crossings, AI route resolution, sound, lights, loading and
  particles, plus editor rendering diagnostics.
- Engine and Riel tests, shader verification, real main-window checks for both editors, and
  memory checks for DDS decoding and concurrent logging.

Experimental Vulkan applies to the simulator. The editors keep their normal OpenGL path.
See [ARCHITECTURE.md](docs/linux/ARCHITECTURE.md) and [QA.md](docs/linux/QA.md).

## Terminal commands

For the portable download, replace `riel` with `./riel`. Route, activity, path and consist names
accept a unique case-insensitive prefix. Quote names containing spaces.

```sh
riel gui
riel content add "MSTS" "/mnt/data/games/Train Simulator"
riel routes
riel activities "Marias Pass"
riel play "Marias Pass" "Coal Train"
riel paths "Marias Pass"
riel consists
riel explore "Marias Pass" "Shelby-Essex" "Freight" --time 08:30 --season autumn --weather rain
```

| Command | Purpose |
| --- | --- |
| `riel content` | List content folders. |
| `riel content add <name> <path>` | Add and scan a folder. |
| `riel content remove <name>` | Remove a folder from the configuration. |
| `riel content refresh` | Rescan all folders. |
| `riel routes [folder]` | List routes. |
| `riel activities <route>` | List activities. |
| `riel paths <route>` | List player paths. |
| `riel consists [folder]` | List consists. |
| `riel play <route> <activity>` | Start an activity. |
| `riel explore <route> <path> <consist>` | Explore; accepts time, season and weather. |
| `riel start` | Repeat the last selection. |
| `riel resume` | Resume the last saved session. |
| `riel run -- <arguments>` | Pass arguments directly to the simulator. |
| `riel route-editor` | Open the route editor; accepts `--game-root` and `--route`. |
| `riel consist-editor` | Open the consist editor. |
| `riel shape-viewer` | Open the model viewer. |
| `riel ace-converter` | Open the ACE converter. |
| `riel doctor` | Check the computer and content. |
| `riel update --check` | Check the newest tested main build. |
| `riel update` | Install that build in an updatable installation. |
| `riel version` / `riel help` | Show the version / help. |

Timetable mode is selected in the launcher or through engine arguments; the CLI does not provide
a shorthand `timetable` command.

## Install on Arch or build from source

```sh
git clone https://github.com/agustinluzardo/Riel-Linux.git
cd Riel-Linux/packaging/arch
makepkg -si
```

The PKGBUILD builds and installs the simulator, launcher and TSRE5vc suite, desktop entries,
icons, manual and RailDriver udev rule. It follows `main`; use the stable download for a fixed
release. See [INSTALL.md](docs/linux/INSTALL.md) for dependencies and manual build instructions.

## Files and troubleshooting

| Default location | Contents |
| --- | --- |
| `~/.config/riel` | Settings and profiles. |
| `~/.local/share/riel` | Saves and user data. |
| `~/.local/state/riel/Logs` | Logs, `Startup.log` and reports under `Crashes`. |
| `~/.cache/riel` | Content indexes. |
| `~/.local/share/Riel/RouteEditor` | Editor settings and assets. |
| `~/.local/state/riel/Logs/Riel Route Editor Log.txt` | Editor-suite log. |

Locations follow their corresponding XDG variables. To report a failure, attach `riel doctor`,
the version, log and affected route/activity to an
[issue](https://github.com/agustinluzardo/Riel-Linux/issues). For visual problems, include the
renderer shown by F3 and whether the whole screen, scenery or dispatcher map is affected.

## Status and compatibility

- Releases target **Linux x86-64**. The portable includes .NET; the Arch package uses the
  distribution's installed runtime.
- WPF Toolbox and TrackViewer remain Windows tools in the historical source and are not part
  of the Linux package. TSRE5vc supplies the editors distributed here.
- The multiplayer server is built and included, but the multiplayer flow is not validated on
  Linux or exposed as a game mode in the launcher.
- Some routes still have AI traffic, signal and crossing differences from Open Rails. In FCGR,
  the signal between Quilmes and Ezpeleta can appear green while the route treats it as red.
- Version 0.1.8 fixes player models being unloaded when switching cameras on long consists,
  as detected by the USA2 visibility trace. Confirmation on the user's route/GPU remains pending.
  For remaining locomotive or wagon disappearance, enable **Settings → Advanced → Train visibility
  diagnostics** and attach the latest simulator log.
- Version 0.1.6 restores full MSTS track placement using the Open Rails displacement convention.
  Tests cover actual car placement on grades; confirmation on the reported original MSTS route
  remains pending.
- Version 0.1.5 fixes a verified camera-matrix rendering race. Confirmation that it resolves
  the reported black flashes on the affected route and GPU remains pending.
- Renamed public libraries use `Riel.*`. Add-ons built against `FreeTrainSimulator.*` require
  rebuilding, and scripts importing those namespaces need adapting. Saves made before that
  change have not been fully verified.

## Credits and license

**Piotr Gadecki (GokuMK)** created TSRE5 and TSRE5vc, the basis of the Route Editor, Consist Editor,
Shape Viewer and ACE Converter. [TSRE5vc](https://github.com/GokuMK/TSRE5vc) ·
[Original TSRE5](https://github.com/GokuMK/TSRE5) · [TSRE website and manual](http://koniec.org/tsre5/).

Thanks also to **perpetualKid and the Free Train Simulator contributors**, the **Open Rails team
and contributors**, and the MSTS/OR community's route, rolling-stock, sound and texture authors.
[CREDITS.md](CREDITS.md) records provenance and the main libraries used.

Riel is distributed under **GPL-3.0-or-later**. Original headers and copyright notices are
preserved; dependencies and content retain their own licenses. See [LICENSE](LICENSE),
[CONTRIBUTING.md](CONTRIBUTING.md) and the
[historical Free Train Simulator README](docs/UPSTREAM-README.md).
