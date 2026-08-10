# Changelog

All notable changes to this project are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

There are no tagged releases yet.

## [Unreleased]

Nothing yet.

---

## [1.0.0]

The full rebuild, as it stands today — 29 C# scripts (19 runtime, 10 editor tooling), 2 custom
shaders, 5 cars, one scene.

### Added

- Plane-anchored AR placement (`ARAnchor`-parented, not world-space) — fixes the predecessor
  project's core drift bug
- Live paint customization across per-part-group material palettes (Body/Wheels/Callipers),
  applied at the material-slot level rather than the whole-mesh level
- Real-world light estimation driving the scene light from ARCore's room measurements
- Contact-shadow grounding via an invisible shadow-catcher shader
- Depth occlusion on ARCore Depth API-capable phones
- Generic FBX → playable-car import pipeline (`CarImporter.cs`, `CarPartMapper.cs`) — scaling,
  Z-up/Y-up correction, material-keyword part mapping, automatic spoiler mounting
- 5 fully playable cars at real manufacturer-spec lengths
- Per-car persistence (colors, spoiler, wheels) via `PlayerPrefs`, with immediate and
  2-second-deferred save paths
- Two custom shaders: `ARPlaneGlow` (scanning-floor visualization) and `ShadowCatcher`
- A play-mode testing harness requiring no phone — substitutes a floor, a synthetic detected
  plane, and an orbit camera when no AR hardware is present
- Structured on-device diagnostic logging, including a four-way "visibility verdict" classifier
- `build.sh` for headless/CI batch builds

### Performance

- Texture compression + on-demand streaming: 114 MB → 57 MB app size
- Deferred/coalesced disk writes instead of a write-per-tap
- Cached bounding-box hit-testing instead of walking up to 209 meshes per touch
- Frame-rate-independent smoothing, replacing a naive `Lerp` that behaved differently at 30 vs
  60 fps
- Result: 60 fps sustained on a Galaxy S23

### Fixed

- A deleted-and-recreated script file silently broke all touch gestures (drag/twist/pinch) for
  several versions — a Unity script GUID mismatch left the gesture component as an inert
  "Missing Script" placeholder
- A keyword-based mesh-hiding rule (meant to catch spoiler struts) matched suspension push-rods,
  making an entire 116-mesh car vanish when the spoiler was toggled off
- A deferred-save system existed but was never actually wired to any caller, so every colour tap
  still blocked on an immediate disk write

### Known limitations

See the README's ["Not solved yet"](README.md#not-solved-yet) section — no cross-session world
anchoring, Depth-only occlusion, high draw calls on the detailed cars, portrait-only in practice.
