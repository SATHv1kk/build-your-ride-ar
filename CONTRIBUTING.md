# Contributing

Thanks for your interest in this project.

## What this repository is

A Unity AR car configurator, rebuilt from the ground up after an earlier university 3D Vision
module project's own retrospective identified two core problems: anchoring drift and a "pasted
onto the screen" visual feel. See the README's "About" section and
[`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) for the full story.

## Where to start

The README's ["Not solved yet"](README.md#not-solved-yet) section is the current honest list of
open problems:

1. **The car doesn't remember its place in the world** across sessions — needs cloud anchors
2. **Occlusion needs a Depth-capable phone** — no fallback for phones without the ARCore Depth API
3. **Draw calls are high on the detailed cars** (209 on the Maserati, 116 on the Porsche) — LOD
   meshes and texture atlasing are the next real win
4. **Portrait only**, in practice

## Adding a car

See the README's ["Adding a car"](BuildYourRideAR/README.md#adding-a-car) section — the import
pipeline (`CarImporter.cs`, `CarPartMapper.cs`) is designed so this is a config entry, not a new
importer.

## Development setup

- Unity **2022.3.61f1** (exact version — the project is pinned to it)
- Android Build Support module (OpenJDK + Android SDK & NDK Tools)
- Open [`BuildYourRideAR/`](BuildYourRideAR/) in Unity Hub, open `Assets/Scenes/Main.unity`, run
  **`BuildYourRide > Upgrade Scene`**, then Play — no phone needed, a play-mode harness
  substitutes a floor and orbit camera when no AR hardware is present

Full setup and troubleshooting: [`BuildYourRideAR/README.md`](BuildYourRideAR/README.md).

## Pull requests

1. Fork and branch from `main`
2. Run **`BuildYourRide > Upgrade Scene`** before committing scene changes — it's idempotent and
   keeps scene wiring correct
3. Keep the change focused — one concern per PR
4. Never commit `buildyourride.keystore` or `keystore_credentials.txt`

## Licensing

This project's own code is licensed under the **MIT License**. By contributing, you agree that
your contributions will be licensed under the same terms. Note the 3D car models themselves are
third-party community assets, not covered by this license — see the README.

## Code of Conduct

Participation is governed by the [Code of Conduct](CODE_OF_CONDUCT.md).
