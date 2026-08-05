# BuildYourRideAR — the Unity project

The app itself. For what it does and why, see the [main README](../README.md).
For how it is put together, see [docs/ARCHITECTURE.md](../docs/ARCHITECTURE.md).

---

## Requirements

| | |
|---|---|
| Unity | **2022.3.61f1** (exact version — the project is pinned to it) |
| Modules | Android Build Support, including **OpenJDK** and **Android SDK & NDK Tools** |
| Render pipeline | Built-in — *not* URP or HDRP |
| Phone | Any ARCore-capable Android device, minSdk 24. Depth occlusion needs a Depth-capable phone. |

Packages install themselves from `Packages/manifest.json` on first open — AR Foundation
5.1.5, ARCore 5.1.5, Input System 1.7.0, uGUI.

---

## Open and run

1. **Unity Hub → Open →** select this `BuildYourRideAR` folder.

   The first import takes 10–30 minutes. It is processing ~200 MB of car textures and five
   FBX models. This is normal — let it finish.

2. Open the scene `Assets/Scenes/Main.unity`.

3. Run **`BuildYourRide > Upgrade Scene`**, then save the scene.

   Compiling a script does not put it in the scene. Components are added by editor tooling,
   so a scene saved before a feature existed will run happily without it — no error, nothing
   in the console, just a feature that quietly does nothing. This command adds whatever is
   missing and is safe to run as many times as you like.

4. Press **Play**. There is no AR on desktop, so a play-mode harness takes over: it removes
   the AR camera stack, lays down a grey floor you can click, and gives you an orbit camera.

`SceneVersionCheck` prints a warning at startup naming anything still missing from the scene.
A clean console means the scene is complete.

---

## Editor controls (play mode)

| Input | Action |
|---|---|
| Click the ground | Place the car |
| Drag the car | Move it on the floor |
| Left / Right arrows | Rotate the car |
| Up / Down arrows | Tilt the camera |
| Scroll | Zoom the camera |
| Shift + scroll | Resize the car |
| Right-drag | Orbit the camera |
| Middle-drag | Pan the camera |

Shift separates the two scroll behaviours: plain scroll moves the camera, Shift + scroll
resizes the car.

**Spoiler placement helpers** (add-on spoilers only). With a car placed and the spoiler on:
`W`/`A`/`D`/`Z` move it, `Shift`+`Q`/`E` raise and lower it, `Alt` + those keys rotate it,
`Ctrl` makes everything 5× faster. Press `P` to print the current position and rotation, ready
to paste into `CarImporter.cs`. `T` starts the same kind of tuning for a car's base orientation.

---

## Building an APK

**From the terminal (recommended):**

```bash
chmod +x build.sh
./build.sh
```

Output lands at `Builds/BuildYourRideAR.apk`. Install with:

```bash
adb install -r Builds/BuildYourRideAR.apk
```

If Unity is not found automatically, pass its path: `UNITY_PATH=/path/to/Unity ./build.sh`.

`build.sh` repairs the scene wiring, applies release settings, builds, and **exits non-zero
if the build fails** — batch mode otherwise reports success on a broken build.

> **Unity must be closed.** If the editor has the project open it holds a lock and batch mode
> aborts.

**Release signing** needs two files that are deliberately not in this repository:

- `buildyourride.keystore` — the signing key
- `keystore_credentials.txt` — `storepass=… keypass=… alias=…`

Without them the build still works, just debug-signed. Never commit either file.

---

## Menu commands

Three items, under **`BuildYourRide`**:

| Command | What it does | Safe? |
|---|---|---|
| **Upgrade Scene** | Repairs scene wiring in place — strips dead script references, adds and wires any missing component. Seconds. | ✅ Idempotent, run it any time |
| **Optimize Textures For Mobile** | Compresses every car texture to ASTC 6×6, caps at 2048 px, enables mipmap streaming. | ✅ Safe |
| **Rebuild All** | Rebuilds all five car prefabs from their FBX files, then upgrades the scene. Minutes. | ⚠️ **Wipes all saved car configurations** |

Only reach for **Rebuild All** when a car prefab itself is stale — after changing a palette,
an orientation, or a spoiler position in `CarImporter.cs`.

Other commands exist in code without a menu entry, to keep the menu honest: `BuildApk`,
`QuickBuild`, `ImportAllNewCars`, `RunAll`, `ConfigureRelease`, `ResetSavedBuilds`,
`OpenDiagnosticsLog`, `CarViewer.Open`, `FixNormalMaps`, `CleanRoster`.

---

## Layout

```
Assets/
├── Scenes/Main.unity        the only scene
├── Scripts/                 19 files, ~4,580 lines — runs on the phone
├── Editor/                  10 files, ~3,700 lines — content pipeline and scene tools
├── Shaders/                 ARPlaneGlow, ShadowCatcher
├── Models/                  5 car FBXs + their texture folders
├── Prefabs/                 car prefabs, spoiler, plane visual
├── Materials/               37 generated materials
└── Textures/                UI sprites and the app icon
```

### Runtime, the ones that matter

| File | Job |
|---|---|
| `CarPlacementController.cs` | Tap → raycast → anchor → spawn. Car switching, removal, re-anchoring. |
| `CarGestureController.cs` | Drag to move, twist to rotate, pinch to scale. |
| `CarCustomizer.cs` | Lives on each car. Paint groups, spoiler, wheels, per-car persistence. |
| `CustomizePanel.cs` | Builds the colour tray at runtime from whichever car is placed. |
| `UIController.cs` | Bottom bar, hints, Android back button. |
| `ConfigStore.cs` | All saving. Immediate and deferred write paths. |
| `ARLightEstimator.cs` | Drives the scene light from ARCore's reading of the room. |
| `ShadowCatcher.cs` | Invisible quad that catches the car's shadow onto the real floor. |
| `ARDiagnosticLog.cs` | Full run log to disk. **Read this first when something is wrong.** |

### Editor, the ones that matter

| File | Job |
|---|---|
| `CarImporter.cs` | FBX → playable car. Scaling, up-axis fix, part mapping, spoiler mounting. |
| `CarPartMapper.cs` | Sorts meshes into Body / Wheels / Callipers from material names. |
| `SceneUpgrade.cs` | Idempotent scene repair. |
| `ProjectSetup.cs` | Full regeneration — materials, shaders, UI, scene graph. |
| `BatchBuild.cs` | Command-line entry points. `QuickBuild` is what `build.sh` calls. |

---

## Adding a car

1. Drop the FBX into `Assets/Models/` with its textures in `Assets/Models/<Name>_Textures/`.
2. Add one `AutoImportEntry` to `CarImporter.NewCars`:

```csharp
new AutoImportEntry {
    fbxName             = "YourCar",
    displayName         = "Your Car",
    realLengthMeters    = 4.5f,     // scales the model to real size
    orientationOverride = null,     // only if the auto up-axis fix gets it wrong
    hasBuiltInSpoiler   = false     // true if the model has its own rear wing
}
```

3. Add the prefab name to `CarRoster.cs`.
4. Run **`BuildYourRide > Rebuild All`**.

The importer handles scaling, the Z-up fix, material sorting and spoiler mounting on its own.
Cars with unusual material naming may need explicit `partSpecs` keywords — three of the five
cars needed none.

---

## Troubleshooting

**A feature is compiled but does nothing** — the scene predates it. Run `Upgrade Scene`, then
save. `SceneVersionCheck` names exactly what is missing at startup.

**Six "No active XR…Subsystem" warnings on Play** — expected. ARCore is Android-only, so no
XR subsystems exist on desktop. Warnings, not errors, and they disappear on device.

**Batch build aborts immediately** — the Unity editor has the project open and holds the lock.
Close it.

**Colours or size carried over from a previous session** — builds persist. Clear them with
`DevTools.ResetSavedBuilds`.

**No planes detected** — needs decent light and a textured floor. Move the phone slowly.
Camera permission must be granted.

**No contact shadow** — the directional light needs soft shadows on, and `shadowDistance`
must be at least 25.

**Something looks wrong on device** — pull the log and read it before guessing:

```bash
adb pull /sdcard/Android/data/com.sathvikkoti.buildyourridear/files/Diagnostics/latest.log
```

It records every touch with its full raycast result, the placed car's geometry, and an
explicit visibility verdict that separates *off-screen*, *camera inside the model*,
*near-plane clipped* and *should be visible*.

---

## Known limitations

- Car position does not survive a restart — colours, size and angle do. Cross-session world
  anchoring needs cloud anchors.
- Occlusion requires a Depth-capable phone; without it the car draws over everything.
- Draw calls are high on the detailed cars (Maserati 209, Porsche 116). LOD meshes and
  texture atlasing are the next real optimization.
- Portrait layout only, in practice.
- iOS is not buildable from a Linux machine.
