# Riel Linux changelog

Stable versions and their assets are available in
[Releases](https://github.com/agustinluzardo/Riel-Linux/releases). `main-<commit>` builds are
prereleases that passed automated tests and packaging.

## 1.0.0

- Refreshes the English launcher with restored route/train details, clearer Play and Tools
  buttons, and seven selectable accent colors for light and dark themes.
- Prioritizes selected-save metadata, waits for complete resume details, corrects timetable
  resume dispatch, and propagates cancellation through content conversion.
- Fixes texture sampler indexing, deferred night-texture selection, instanced model root
  transforms and roof queries across tiles. Rejects referenced cyclic model hierarchies
  before graphics allocation while preserving legacy truncated-parent fallback behavior.
- Corrects text-input/modal focus handling and refreshes location labels when their inputs change.
- Fixes tunnel traversal across section boundaries, crossing eligibility leaking between
  crossings, and split-platform axis comparisons.
- Includes the previous train-streaming, camera snapshot, track-placement and native editor fixes.
- Preserves graphics defaults, LOD/culling policy, resource lifetimes, physics formulas and
  simulation update frequencies. Publication uses the existing managed/native test and
  portable-package gates; custom-route FPS and GPU confirmation remain pending.
- See the [1.0.0 release notes](docs/releases/1.0.0.md) for downloads, validation and known limitations.

## 0.1.9

- Extends the 0.1.8 loading fix to complete nearby trains. A train stays loaded while
  any of its cars is within the streaming radius of the camera or any player car,
  so switching between the ends of the player consist does not discard adjacent models.
- Uses tiled horizontal squared distance for selection. Trains outside both the camera
  and player-consist vicinity continue to unload; nearby uncoupled cars remain available.
- Adds production-loader regressions for adjacent trains across tiles, traffic near the
  player's rear, free cameras, complete-consist retention and eventual model unloading.
- Extends opt-in train visibility diagnostics to other vehicles near the camera.
- Nearby trains can retain more models in memory. Normal rendering range and model
  detail selection are preserved. Confirmation on the user's original route remains pending.

## 0.1.8

- Keeps the complete player train loaded when switching cameras between the ends of a
  long consist. Previously, the opposite end could be unloaded on a curve and nearby
  vehicles then disappeared while their models were loaded again.
- Reproduces the USA2/autotrnsetout loader failure with positions captured by the 0.1.7
  visibility trace, and checks repeated camera changes through the production loader.
- Detached cars and other trains continue to stream by distance. Normal shape culling,
  detail selection, camera snapshots and MSTS track placement are preserved.
- Confirmation with the original route on the user's NVIDIA GPU remains pending.

## 0.1.7

- Uses the prepared frame's camera position for scenery-shader fog, specular lighting
  and distance fading, completing the camera snapshot introduced in 0.1.5.
- Prevents blended materials from fading out when the updater moves the live camera
  across a tile while the previous frame is being drawn.
- Adds opt-in **Train visibility diagnostics** in Settings → Advanced. It records actual
  model detail selection, culling, mesh submission and vehicle transforms in the simulator log.
- Preserves the three-dimensional MSTS track placement from 0.1.6. Confirmation that this
  fixes all reported intermittent vehicle disappearances on the original MSTS route is pending.

## 0.1.6

- Applies all three MSTS placement angles (yaw, pitch and roll) to imported endpoints and
  runtime positions, matching Open Rails' three-dimensional track displacement.
- Fixes track sections being flattened at their starting height, which made wheels jump
  between elevations and raised or steeply tilted cars at section joins.
- Uses a cached section orientation for traversal and a placed three-dimensional curve
  basis for snapping, including zero-height PAT points and horizontal diagnostics.
- Repairs positions of track items reconstructed from section distances with the same geometry.
- Computes runtime endpoints from placement angles even when an older route model contains
  flattened endpoints; the version update also refreshes available source-backed content.
- Finds signal shapes by each head's global TDB identity, avoiding wrong-head selection and
  out-of-range local reference lookups after reference changes or multi-head merging.
- Adds regressions for import, traveller movement, graded curves, tile boundaries, cached
  endpoints and real wheel-based car placement in both directions, plus signal-shape lookup.
- Includes the previous rendering and startup fixes. Native editor code is unchanged.
- The reported original MSTS route and NVIDIA hardware still need a user-side confirmation.

## 0.1.5

- Keeps each prepared frame's camera view and projection matrices instead of reading a
  camera that the updater may already be moving for the next frame.
- Uses the same captured camera for opaque and blended render passes, shadow direction
  and distant mountains, including changes of field of view or projection.
- Adds regression tests for concurrent frame preparation, tile-sized camera movement,
  projection changes, distant mountains and the loading screen.
- Includes the startup crash fixes from 0.1.4. The native editors are unchanged.
- Fixes a verified rendering race; confirmation that it resolves the reported black
  flashes on the user's route and GPU remains pending.

## 0.1.4

- Fixes `SunMoonPos.SolarAngle` startup crashes when an ENV file has no recognized sun,
  including an empty satellite light/type block. Uses the existing astronomical fallback
  and preserves configured sunrise/sunset times.
- Fixes `SignalEnvironment.InsertNode` startup crashes when an unplaced speedpost keeps
  a signal index that becomes invalid after multi-head signals are merged.
- Prevents unplaced speedposts from reusing another signal's speed restriction; records
  unsuccessful placement in the log while retaining correctly placed signals and posts.
- Adds regression tests using the production sky calculation, ENV parser, signal scanner,
  head merging and track-circuit insertion.
- Includes the editor and Vulkan updates from 0.1.3, with expanded English documentation
  and explicit TSRE5vc credits to Piotr Gadecki (GokuMK).
- Reported black flashes during gameplay remain under investigation.

## 0.1.3

### Editor suite

- Integrates native Route Editor, Consist Editor, Shape Viewer and ACE Converter, based on
  **TSRE5vc by Piotr Gadecki (GokuMK)**, with their runtime, icons and corresponding source.
- Opens the route and content folder selected in the launcher and uses its own settings profile.
- Fixes memory corruption caused by concurrent writes to the editor log.
- Validates DDS dimensions and pixel data before decoding; truncated files are rejected.
- Resolves mixed-case references, split `GLOBAL`/`Global` directories, X/Y Tracks, route models
  and textures.
- Bounds directory caching and texture workers while preserving model identity and texture context.
- Fixes OpenGL startup, resources, consist selection and ACE preview zoom.
- Checks real main windows, models and GPU-uploaded textures; verifies decoders and concurrent
  logging with sanitizers.

### Simulator and launcher

- Activates experimental Vulkan from Settings in the native environment before context creation;
  the OpenGL renderer runs through Mesa Zink.
- F3 reports the active renderer and available CPU/GPU temperatures.
- Expands settings and diagnostics and adds access to the editor suite.
- Rescans content at launcher startup and avoids repeating the scan after a session.
- Keeps track-monitor signals and limits within the visible range.

### Known status

- Zink remains experimental and does not change the editors' renderer.
- Multiplayer has no validated flow from the Linux launcher.
- Black flashes during gameplay remain under investigation.
- In FCGR, the signal between Quilmes and Ezpeleta can appear green while being treated as red.
- Add-ons built against `FreeTrainSimulator.*` require adaptation to `Riel.*`.
  Older-save compatibility has not been fully verified.

## 0.1.2

- Fixes emitter ring-buffer indexing and restores the 80-byte particle vertex stride.
- Recovers dynamic particle buffers invalidated by DesktopGL.
- Fixes the DesktopGL billboard shader so smoke, steam and other emitters do not collapse
  into zero-area polygons.
- Adds particle and diesel exhaust diagnostics in Settings.

## 0.1.1

- Applies an intermediate OpenGL particle vertex-layout fix.
- The complete billboard correction follows in 0.1.2.

## 0.1.0

- First stable Riel Linux release, with the .NET runtime included.
- Renames public libraries to `Riel.*` and adapts the launcher and updates.
- Hardens activity testing to record startup failures and continue the batch.
- Improves nearby AI train visual loading and adds diagnostics.
- Adds F3 system information and Linux temperature reporting.
