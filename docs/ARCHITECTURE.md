# Architecture

A closer look at how the app is put together, for anyone who wants more than the README.

---

## 1. The two halves

Unity projects have a split that is easy to miss: some code ships to the phone, and some
code only ever runs inside the editor to *build the content* that ships.

| | Runtime | Editor tooling |
|---|---|---|
| Where it runs | On the phone | Only inside Unity |
| Scripts | 19 | 10 |
| Lines | ~4,580 | ~3,700 |
| Job | Run the app | Turn raw FBX files into playable cars, and keep the scene correct |

The editor half is roughly as large as the runtime half. That is normal for a project
where content is generated rather than assembled by hand.

---

## 2. Placement — what happens when you tap

```
you tap the screen
   │
   ├─ raycast against detected planes      (ARRaycastManager)
   │     nothing hit → ignore the tap
   │
   ├─ create an anchor attached to that plane
   │     AttachAnchor(plane, pose)
   │     if that fails → fall back to a free-standing anchor
   │
   ├─ spawn the car prefab
   ├─ parent the car to the anchor          ← this is the important line
   ├─ turn the car to face you
   ├─ restore your saved size and angle
   ├─ hide the glowing plane visuals
   │
   └─ announce "car placed"
         ├─ UI enables its buttons and builds the colour tray
         ├─ shadow catcher spawns under the car
         └─ status overlay starts reporting
```

**Why parenting to the anchor matters.** ARCore is constantly revising its estimate of
where the room actually is. When it corrects itself, the anchor moves. Because the car is
a child of the anchor, the car moves with it and stays on the same physical spot.

The original version of this project skipped the anchor and just placed the model in world
space. That is why it drifted.

---

## 3. Measuring drift honestly

Drift is easy to misdiagnose, because "the car moved" and "ARCore corrected the world" look
identical on screen. So the app measures the difference between the two:

```
anchorMovement = where the anchor is now  −  where it started
carMovement    = where the car is now     −  where it started

divergence     = | carMovement − anchorMovement |
```

| Reading | Meaning |
|---|---|
| Divergence ≈ 0, both moving | ARCore is re-estimating and the car is correctly following. **Working as intended.** |
| Divergence growing | The car has come loose from its anchor. **A parenting bug, not a tracking problem.** |

This distinction is the whole point. Without it, a healthy correction looks like a failure.

---

## 4. How paint is applied

Each car carries a list of **part groups**:

```csharp
class PartGroup {
    string     displayName;   // "Body", "Wheels", "Callipers"
    PaintTarget[] targets;    // which surfaces this group owns
    Material[]    options;    // the palette you can pick from
}

class PaintTarget {
    Renderer renderer;
    int      materialIndex;   // ← the detail that makes this work
}
```

**`materialIndex` is the part people get wrong.** Downloaded models routinely put several
materials on a single mesh — one mesh might hold the door paint, the window glass and a
plastic trim strip all at once. Addressing the whole mesh would repaint all three.

So a target is *one material slot on one mesh*. Applying a colour reads the material array,
replaces that one index, and writes the array back.

The colour tray is built at runtime by reading whatever groups the placed car actually has.
A car with one group gets one tab; the Porsche gets three. No per-car UI code exists.

---

## 5. The import pipeline

The point of this pipeline is that adding a car should mean adding a few lines of
configuration, not writing a new importer.

```
     ┌──────────────────────────────────────────────┐
     │  one AutoImportEntry per car                 │
     │    file name, real length in metres,         │
     │    optional rotation fix, optional palette   │
     └──────────────────────────────────────────────┘
                        │
        ┌───────────────┼───────────────┐
        ▼               ▼               ▼
   scale to real   fix up-axis     map materials
     length        if Z-up         to part groups
        │               │               │
        └───────────────┼───────────────┘
                        ▼
                 mount a spoiler
                        ▼
                  save a prefab
```

### Scaling
The model is measured, then scaled so its longest axis equals the car's real length. A
Porsche ends up genuinely 4.601 m long, which is what makes walking around it feel right.

### The up-axis problem
Models exported from 3ds Max, Zmodeler and GTA mod tools often treat **Z** as up, while
Unity uses **Y**. Imported straight, those cars stand on their nose.

Detection is a proportion check: if the model is much taller than it is long, it is almost
certainly lying on the wrong axis. Cars with unusual proportions that fool the check get an
explicit override instead.

### Finding the boot for the spoiler
The spoiler must sit on the rear deck. Early versions assumed "rear" meant a fixed direction
along one axis — which was wrong for about half the models, so spoilers landed on bonnets.

The working version tests all eight corners of the car's bounding box against the direction
the car is actually facing. That works no matter how the model was authored.

---

## 6. Saving your build

All settings live in Unity's `PlayerPrefs`, keyed per car:

| Key | Holds |
|---|---|
| `BYR.lastCar` | which car you had out |
| `BYR.<car>.part<N>` | chosen colour for each part group |
| `BYR.<car>.spoiler` | wing on or off |
| `BYR.<car>.wheels` | wheel colour |
| `BYR.scale` | car size, shared across all cars |
| `BYR.yaw` | heading, **relative to where the camera was looking** |

Heading is stored relative to the camera on purpose. Absolute world rotation is meaningless
across sessions, because every new AR session starts a fresh, arbitrary origin. Storing
"45° from wherever you were looking" survives that; storing "45° from world north" does not.

### Two save paths, on purpose

| Method | Behaviour | Used for |
|---|---|---|
| `Save()` | Writes immediately. | Placing, switching cars, app pause, app quit |
| `SaveDeferred()` | Waits 2 s, coalesces, then writes once. | Every colour tap, spoiler toggle, wheel change, gesture |

The rule: if losing the write would be worse than a brief hitch, write now. Otherwise
batch it.

Pausing matters most on Android — the operating system can kill a backgrounded app without
ever calling the quit callback, so the pause handler is the last guaranteed chance to write.

---

## 7. The two shaders

**`ARPlaneGlow`** — the glowing floor you see while scanning. Soft cyan-to-indigo gradient
with feathered edges, a rim glow, a grid locked to the room, and a pulse travelling outward.

AR Foundation's generated plane mesh carries positions, normals, and a session-space UV in
metres — but nothing telling a shader how close a vertex is to the plane's edge. Without
that, a shader cannot fade out its own boundary. A helper component computes each vertex's
distance to the nearest boundary edge after every mesh rebuild and writes it into a spare
UV channel for the shader to read.

Because the main UV is in real metres, the grid stays locked to the room as planes grow,
instead of sliding around.

**`ShadowCatcher`** — draws nothing at all except the shadow that falls on it. An invisible
quad sits under the car, samples the shadow attenuation, and blends the result over the
camera feed. This is what grounds the car in the room.

Both are project shaders rather than built-ins, and both are registered as always-included.
A built-in shader that does not make it into the build renders as bright magenta on device —
which is exactly what happened to an earlier version.

---

## 8. Testing without a phone

Building an APK and sideloading it for every small change is far too slow.

A play-mode harness detects that no AR hardware is present, then:

- removes the AR camera stack
- lays down a grey floor with a collider you can click
- spawns a fake detected plane so the plane shader is visible and tunable
- reparents the camera to an orbit rig with mouse controls

The result is that the entire flow — placing, painting, spoilers, wheels, persistence — is
testable in seconds inside the editor. Only the parts that genuinely need real AR
(tracking quality, light estimation, occlusion, drift) require a device.

The harness also removes itself entirely on non-editor platforms. Simply declaring an
`OnGUI` method switches on Unity's legacy UI loop for the whole player, so leaving it in a
release build would cost frames for no reason.

---

## 9. Diagnostics

Every run writes a detailed log to disk — on device, into the app's own storage.

It records:

- device, GPU, screen and graphics API
- a full scene inventory at startup: camera settings, colliders, lights, which features are present
- every touch, with its screen position and the complete raycast result
- placement geometry and a **visibility verdict** that separates *off-screen*, *camera is
  inside the model*, *clipped by the near plane*, and *should be visible*
- periodic snapshots of frame rate, camera and car position
- every customization change
- every console message, with stack traces

The visibility verdict exists because "I placed a car and see nothing" has four completely
different causes that look the same to a user. Reading one log usually identifies the
problem without needing to reproduce it.

---

## 10. Things learned the hard way

**Compiling a script does not put it in the scene.** A scene saved before a feature existed
runs perfectly happily without it — no error, no warning, just a feature that quietly does
nothing. The fix is a startup checklist that names anything missing out loud.

**A null-guarded "wire it up" step is not a repair.** `if (x != null) wire(x)` around a
lookup silently does nothing in exactly the case you wrote it for. If a component must
exist, something has to actually *create* it.

**Prefer repairs that can be run twice.** The scene repair tool checks before it acts, so
running it again is harmless. The full regeneration wipes saved data — useful for
bootstrapping, far too blunt for routine work. Keeping these separate stopped a lot of
accidental data loss.

**Read the log before forming a theory.** More than one bug here was diagnosed from a
verbal description, "fixed", and made worse. The log had the answer each time.
