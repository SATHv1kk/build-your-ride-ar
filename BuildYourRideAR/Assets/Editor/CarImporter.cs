using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
// Generic car importer. Add one AutoImport entry per car you want processed
// when the roster is rebuilt (CarRoster.RebuildAllCars). The 5-car roster
// is defined in NewCars below. The same code path handles every car: copy
// the FBX into Assets/Models/{Name}.fbx and its textures into a subfolder,
// then the importer handles scaling, texture extraction, part-mapping and
// roster registration.
public static class CarImporter
{
    const string ModelsFolder = "Assets/Models";
    const string PrefabsFolder = "Assets/Prefabs";
    const string ScenePath = "Assets/Scenes/Main.unity";

    public struct AutoImportEntry
    {
        public string name;
        public string fbxName;     // relative to ModelsFolder
        public string textureFolder; // relative to ModelsFolder
        public string prefabName;  // saved as Assets/Prefabs/{prefabName}.prefab
        public float realLengthMeters;

        // Optional: per-car part-mapping specs. When null the importer auto-
        // detects material names via common keywords (body/paint, wheel/rim,
        // calliper/caliper, carbon/grille/trim, glass/window).
        public CarPartMapper.GroupSpec[] partSpecs;

        // Explicit Z-up correction override. When non-null, the auto-detection
        // is skipped and this rotation is applied unconditionally. Use when a
        // model's bounds fool the heuristic (e.g. a tall SUV).
        public Vector3? orientationOverride;

        // True when the source model already has its own spoiler/wing mesh
        // (e.g. the 992 GT3 RS's fixed rear wing), so the universal add-on
        // Spoiler.prefab should not be assigned -- stacking a second spoiler
        // on top would clip through the model's own.
        public bool hasBuiltInSpoiler;

        // Manual spoiler position override (local space of the car prefab root).
        // When null, the importer auto-calculates from bounds. When set, this
        // exact localPosition is baked into the prefab's CarCustomizer so the
        // spoiler lands precisely where you want it regardless of model quirks.
        // X=center, Y=height, Z=rear (negative = farther rear).
        public Vector3? spoilerLocalPosition;

        // Manual spoiler rotation override (local space). When null, defaults
        // to Quaternion.identity. Set per-car when the spoiler model faces
        // the wrong direction after instantiation.
        public Quaternion? spoilerLocalRotation;

        // When true the car ships with zero part groups and no spoiler --
        // it appears exactly as the FBX author modeled it, with no paint
        // or customization UI available.
        public bool noCustomization;

        // Manual correction added on top of the auto-computed base position
        // (-bounds.center.x, -bounds.min.y, -bounds.center.z), applied after
        // scaling and orientation. The auto-centering gets X/Z right but can
        // be off on Y (the model's true "floor" not matching its bounding
        // box, e.g. wheels sitting slightly inside the chassis mesh) --
        // found by eye in Play mode and baked in here rather than left as a
        // live Inspector edit, which does not survive a prefab reimport.
        public Vector3? positionOffset;
    }

    // Real-world lengths. Maserati/BMW/Porsche are manufacturer specs; the
    // Sedan and Coupe are generic/unbranded models with no exact spec
    // available, so those are reasonable estimates for their class -- correct
    // later if the in-app scale looks off.
    // 1936 American Sedan (generic):     ~5,000 mm (typical full-size 1930s sedan)
    // Maserati Quattroporte:              5,262 mm (manufacturer spec)
    // Generic Sport Coupe:                ~4,500 mm (typical compact sports coupe)
    // BMW M4 F82:                         4,671 mm (manufacturer spec)
    // Porsche 911 GT3 RS (992):           4,601 mm (manufacturer spec)
    // Vitara Brezza 2022:                  3,995 mm
    public static readonly AutoImportEntry[] NewCars =
    {
        new AutoImportEntry
        {
            name = "Porsche 911 GT3 RS (992)",
            fbxName = "PorscheGT3RS.fbx",
            textureFolder = "PorscheGT3RS_Textures",
            prefabName = "PorscheGT3RS",
            realLengthMeters = 4.601f,
            partSpecs = null,
            hasBuiltInSpoiler = true
        },
        new AutoImportEntry
        {
            name = "Maserati Quattroporte",
            fbxName = "Maserati.fbx",
            textureFolder = "Maserati_Textures",
            prefabName = "Maserati",
            realLengthMeters = 5.262f,
            // Found by hand in Play mode (Alt+WASD/QE spoiler controls),
            // logged via LogSpoilerTransform and baked in here so it
            // survives reimports instead of resetting every time.
            spoilerLocalPosition = new Vector3(0.015f, 1.181f, 2.322f),
            spoilerLocalRotation = Quaternion.Euler(90.0f, 0.0f, 0.0f),
            partSpecs = new[]
            {
                new CarPartMapper.GroupSpec
                {
                    displayName = "Body",
                    keywords = new[] { "paint", "body", "carbody", "car_body", "colour", "color", "colored", "coloured", "ext" },
                    exclude = new[] { "glass", "window", "interior", "wheel", "tyre", "tire", "light", "taillight", "tail_light", "backlight", "brakelight", "indicator", "blinker", "lamp", "reverse", "calliper", "caliper", "engine", "carbon", "grille", "badge", "plate", "int_", "leather", "carpet", "seat", "dash", "belt", "speaker", "brake", "disc", "rotor", "rim", "alloy", "trim" },
                    palette = ProjectSetup.MaseratiPaintSwatches,
                    applyFinish = true,
                    includeOriginal = false
                },
                new CarPartMapper.GroupSpec
                {
                    displayName = "Wheels",
                    keywords = new[] { "wheel", "rim", "alloy" },
                    exclude = new[] { "tyre", "tire", "calliper", "caliper", "disc", "brake", "rotor", "trim", "rubber" },
                    palette = ProjectSetup.RimSwatches,
                    applyFinish = false
                },
                new CarPartMapper.GroupSpec
                {
                    displayName = "Callipers",
                    keywords = new[] { "calliper", "caliper" },
                    palette = ProjectSetup.CalliperSwatches,
                    applyFinish = false
                }
            }
        },
        new AutoImportEntry
        {
            name = "Coupe",
            fbxName = "GenericCoupe.fbx",
            textureFolder = "GenericCoupe_Textures",
            prefabName = "GenericCoupe",
            realLengthMeters = 4.500f,
            // Found by hand in Play mode (Alt+WASD/QE spoiler controls),
            // logged via LogSpoilerTransform and baked in here so it
            // survives reimports instead of resetting every time.
            spoilerLocalPosition = new Vector3(0.000f, 1.128f, 1.998f),
            spoilerLocalRotation = Quaternion.Euler(90.0f, 0.0f, 0.0f),
            partSpecs = null
        },
        new AutoImportEntry
        {
            // v4.9: swapped for a different source model (Assets/Models/
            // Batmobile.fbx + Batmobile_Textures/ replaced wholesale --
            // "the-batmobile-from-the-batman-movie.zip" in new cars/,
            // ~93 PBR textures, Batmovil_004_* material set).
            // orientationOverride and positionOffset below are the values
            // found by hand in Play mode (Inspector Transform on the Model
            // child: rotation Y=-52.198, position Y=-0.3, no X/Z needed on
            // either) -- baked in here rather than left as a live edit,
            // which does not survive a prefab reimport. If either needs
            // touching up further, use OrientationTuner.cs (T to tune,
            // I/K/J/L/U/O to rotate) rather than guessing blind.
            name = "Batmobile (The Batman)",
            fbxName = "Batmobile.fbx",
            textureFolder = "Batmobile_Textures",
            prefabName = "Batmobile",
            // Inherited estimate from the old model -- not verified against
            // this one. No official spec for the film car; correct if the
            // in-app scale looks off.
            realLengthMeters = 4.600f,
            orientationOverride = new Vector3(0f, -52.198f, 0f),
            positionOffset = new Vector3(0f, -0.3f, 0f),
            // Carried over from the old Tumbler (which had fixed rear fins).
            // Unverified for this model -- if DetectBuiltInSpoiler() finds
            // nothing on it at runtime, HasSpoiler will correctly report
            // false either way (no built-in match AND no add-on prefab
            // assigned because of this flag), so it's a safe default rather
            // than an assumption that forces a specific outcome. If this
            // model turns out to want the universal add-on spoiler instead,
            // remove this flag.
            hasBuiltInSpoiler = true,
            // Fixed black only, no rim colour choice: a single-entry Body
            // palette (so it's still "a choice" mechanically, just with
            // nothing else to pick) with includeOriginal = false so the
            // FBX's own material never sneaks back in as a second option,
            // and no Rims spec at all -- CarPartMapper simply never builds
            // a Rims group when one isn't listed here.
            partSpecs = new[]
            {
                new CarPartMapper.GroupSpec
                {
                    displayName = "Body",
                    keywords = new[] { "paint", "body", "carbody", "car_body", "colour", "color", "colored", "coloured", "ext" },
                    exclude = new[] { "glass", "window", "interior", "wheel", "tyre", "tire", "light", "taillight", "tail_light", "backlight", "brakelight", "indicator", "blinker", "lamp", "reverse", "calliper", "caliper", "engine", "carbon", "grille", "badge", "plate", "int_", "leather", "carpet", "seat", "dash", "belt", "speaker", "brake", "disc", "rotor", "rim", "alloy", "trim" },
                    palette = ProjectSetup.BatmobileBodySwatches,
                    applyFinish = true,
                    includeOriginal = false
                },
                new CarPartMapper.GroupSpec
                {
                    displayName = "Callipers",
                    keywords = new[] { "calliper", "caliper" },
                    palette = ProjectSetup.CalliperSwatches,
                    applyFinish = false
                }
            }
        },
        new AutoImportEntry
        {
            name = "Sedan 1936",
            fbxName = "Sedan1936.fbx",
            textureFolder = "Sedan1936_Textures",
            prefabName = "Sedan1936",
            realLengthMeters = 5.000f,
            noCustomization = true
        }
    };

    // Fallback specs used when a car has no explicit partSpecs. These match
    // the naming conventions common on Sketchfab/CGTrader models.
    static readonly CarPartMapper.GroupSpec[] DefaultSpecs =
    {
        new CarPartMapper.GroupSpec
        {
            displayName = "Body",
            keywords = new[] { "paint", "body", "carbody", "car_body", "colour", "color", "colored", "coloured", "ext" },
            exclude = new[] { "glass", "window", "interior", "wheel", "tyre", "tire", "light", "calliper", "caliper", "engine", "carbon", "grille", "badge", "plate", "int_", "leather", "carpet", "seat", "dash", "belt", "speaker", "brake", "disc", "rotor", "rim", "alloy", "trim" },
            palette = ProjectSetup.PaintSwatches,
            applyFinish = true
        },
        new CarPartMapper.GroupSpec
        {
            displayName = "Wheels",
            keywords = new[] { "wheel", "rim", "alloy" },
            exclude = new[] { "tyre", "tire", "calliper", "caliper", "disc", "brake", "rotor", "trim", "rubber" },
            palette = ProjectSetup.RimSwatches,
            applyFinish = false
        },
        new CarPartMapper.GroupSpec
        {
            displayName = "Callipers",
            keywords = new[] { "calliper", "caliper" },
            palette = ProjectSetup.CalliperSwatches,
            applyFinish = false
        },

    };

    public static void ImportAllNewCars()
    {
        ProjectSetup.CreateMaterials();
        ProjectSetup.CreateSwatchSprite();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        int imported = 0;
        var prefabs = new List<GameObject>();
        var failed = new List<string>();

        // Each car imported in isolation: an exception partway through one
        // car (a corrupt FBX, a missing texture folder, anything) used to
        // abort this whole foreach immediately, silently skipping every
        // car listed after it in NewCars -- Porsche is first, so a Porsche
        // failure meant Maserati/Coupe/Batmobile/Sedan1936 never even got
        // attempted in that run. Now one bad car can't take the others
        // down with it, and the real exception is logged instead of just
        // vanishing.
        foreach (var entry in NewCars)
        {
            GameObject prefab = null;
            try
            {
                prefab = Import(entry);
            }
            catch (System.Exception e)
            {
                Debug.LogError("[CarImporter] " + entry.name + ": import threw an exception, skipping this car -- " +
                    e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace);
                failed.Add(entry.name);
                continue;
            }

            if (prefab != null)
            {
                prefabs.Add(prefab);
                imported++;
            }
            else
            {
                failed.Add(entry.name);
            }
        }

        if (failed.Count > 0)
            Debug.LogWarning("[CarImporter] " + failed.Count + " car(s) did not import: " + string.Join(", ", failed) +
                ". Check the errors above for why -- they will be missing from the roster.");

        if (imported == 0)
        {
            Debug.LogError("No cars were imported. Check that the FBX files are present in Assets/Models/.");
            return;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        RegisterInScene(prefabs.ToArray());
        DevTools.FixNormalMaps();
        DevTools.OptimizeTexturesForMobile();
        Debug.Log("=== Imported " + imported + " car(s). Press Play to test. ===");
    }

    // Individual entries so a single car can be re-imported without
    // re-processing the others. Kept as code, not on the menu -- BuildYourRide
    // stays a single "Rebuild All" item; call these directly (C# Console, a
    // temp script, or by temporarily re-adding [MenuItem(...)]) if needed.
    public static void ImportPorscheGT3RS()
    {
        DoSingleImport(0);
    }

    public static void ImportMaserati()
    {
        DoSingleImport(1);
    }

    public static void ImportGenericCoupe()
    {
        DoSingleImport(2);
    }

    public static void ImportBatmobile()
    {
        DoSingleImport(3);
    }

    public static void ImportSedan1936()
    {
        DoSingleImport(4);
    }

    static void DoSingleImport(int index)
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogError("[BuildYourRide] Stop Play mode before reimporting -- scene editing isn't allowed while playing.");
            return;
        }
        if (index < 0 || index >= NewCars.Length) return;
        ProjectSetup.CreateMaterials();
        ProjectSetup.CreateSwatchSprite();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        var prefab = Import(NewCars[index]);
        if (prefab == null) return;

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        RegisterInScene(new[] { prefab });
    }

    static GameObject Import(AutoImportEntry entry)
    {
        string fbxPath = Path.Combine(ModelsFolder, entry.fbxName);
        if (AssetImporter.GetAtPath(fbxPath) == null)
        {
            Debug.LogError("[CarImporter] " + entry.name + ": FBX not found at " + fbxPath);
            return null;
        }

        Debug.Log("[CarImporter] Importing " + entry.name + "...");

        AssetDatabase.ImportAsset(fbxPath, ImportAssetOptions.ForceUpdate);

        var importer = (ModelImporter)AssetImporter.GetAtPath(fbxPath);
        if (importer == null)
        {
            Debug.LogError("[CarImporter] " + entry.name + ": FBX importer not available for " + fbxPath);
            return null;
        }

        string texFolder = Path.Combine(ModelsFolder, entry.textureFolder);
        if (!AssetDatabase.IsValidFolder(texFolder))
            AssetDatabase.CreateFolder(ModelsFolder, entry.textureFolder);
        importer.ExtractTextures(texFolder);
        importer.isReadable = false;

        importer.bakeAxisConversion = true;
        importer.globalScale = 1f;
        importer.useFileScale = true;
        importer.SaveAndReimport();

        var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
        if (fbx == null)
        {
            Debug.LogError("[CarImporter] " + entry.name + ": Failed to load FBX at " + fbxPath);
            return null;
        }

        var root = new GameObject(entry.prefabName);
        var model = (GameObject)Object.Instantiate(fbx, root.transform);
        model.name = "Model";
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.identity;

        // Detect Z-up models (common in 3ds Max, Zmodeler3, GTA mod exports):
        // a car standing upright will have Y extent much larger than X or Z.
        //
        // This always runs, even when the car has an orientationOverride: a
        // pure Y-axis override only changes heading (which way the car
        // faces) and can never fix a Z-up model on its own, because rotating
        // around Y never changes which local axis points up. So the up-axis
        // correction and the override compose instead of one replacing the
        // other; the override becomes an additional heading adjustment
        // layered on top of it.
        var preBounds = ComputeBounds(root);
        float maxHorizontal = Mathf.Max(preBounds.size.x, preBounds.size.z);
        if (preBounds.size.y > maxHorizontal * 1.25f)
        {
            // +90, not -90: GenericCoupe hit this exact branch and came out
            // upside down at -90. Confirmed by comparing against Brezza's
            // orientationOverride, which had the same -90 and the same bug.
            model.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Debug.Log("[CarImporter] " + entry.name + ": detected Z-up orientation, corrected.");
        }

        if (entry.orientationOverride.HasValue)
        {
            model.transform.localRotation =
                Quaternion.Euler(entry.orientationOverride.Value) * model.transform.localRotation;
            Debug.Log("[CarImporter] " + entry.name + ": applying orientation override " + entry.orientationOverride.Value);
        }

        // Scale to 1:1 real-world size.
        var bounds = ComputeBounds(root);
        float length = Mathf.Max(bounds.size.x, bounds.size.z);
        if (length > 0.01f)
        {
            float s = entry.realLengthMeters / length;
            if (Mathf.Abs(1f - s) > 0.02f)
            {
                model.transform.localScale *= s;
                Debug.Log("[CarImporter] " + entry.name + " rescaled by " + s.ToString("F3"));
            }
        }

        bounds = ComputeBounds(root);
        model.transform.localPosition = new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z);

        if (entry.positionOffset.HasValue)
        {
            model.transform.localPosition += entry.positionOffset.Value;
            Debug.Log("[CarImporter] " + entry.name + ": applying position offset " + entry.positionOffset.Value);
        }

        // Auto-detect part groups via material name matching.
        CarCustomizer.PartGroup[] groups;

        if (entry.noCustomization)
        {
            groups = new CarCustomizer.PartGroup[0];
            Debug.Log("[CarImporter] " + entry.name + ": noCustomization — zero part groups.");
        }
        else
        {
            var specs = entry.partSpecs ?? DefaultSpecs;

            // If a car's own partSpecs give Body a specific palette/
            // includeOriginal (e.g. Batmobile's fixed-black-only), the
            // fallback path needs to honour that too -- it used to always
            // fall back to the generic PaintSwatches, silently ignoring a
            // car-specific restriction whenever keyword matching missed
            // (which, per the roster notes, is exactly what happens for the
            // Batmobile: its Body has always gone through this fallback).
            var bodyPalette = ProjectSetup.PaintSwatches;
            bool bodyIncludeOriginal = true;
            foreach (var s in specs)
            {
                if (s.displayName != "Body") continue;
                if (s.palette != null) bodyPalette = s.palette;
                bodyIncludeOriginal = s.includeOriginal;
                break;
            }

            int matchedSlots;
            groups = CarPartMapper.Build(root, specs, out matchedSlots);
            if (groups.Length == 0)
            {
                var fallback = CarPartMapper.AllBodyRenderersFallback(root, bodyPalette, bodyIncludeOriginal);
                if (fallback != null) groups = new[] { fallback };
            }
            else
            {
                bool hasBody = false;
                foreach (var g in groups)
                {
                    if (g.displayName == "Body") { hasBody = true; break; }
                }
                if (!hasBody)
                {
                    var bodyFallback = CarPartMapper.AllBodyRenderersFallback(root, bodyPalette, bodyIncludeOriginal);
                    if (bodyFallback != null)
                    {
                        var list = new System.Collections.Generic.List<CarCustomizer.PartGroup>(groups);
                        list.Insert(0, bodyFallback);
                        groups = list.ToArray();
                        Debug.Log("[CarImporter] " + entry.name + ": added fallback Body group (no Body-named material matched).");
                    }
                }
            }
        }

        var customizer = root.AddComponent<CarCustomizer>();
        customizer.partGroups = groups;

        if (groups.Length > 0 && groups[0].displayName == "Body")
        {
            int targetIndex = -1;
            var opts = groups[0].options;
            for (int i = 0; i < opts.Length; i++)
            {
                // Maserati's own palette (MaseratiPaintSwatches) names its
                // black "Paint_Black"; the shared PaintSwatches used by
                // Coupe/Batmobile names its black "Paint_JetBlack" -- check
                // both rather than hardcoding one name per car.
                if (opts[i] != null && (opts[i].name == "Paint_Black" || opts[i].name == "Paint_JetBlack"))
                {
                    targetIndex = i;
                    break;
                }
            }

            if ((entry.prefabName == "Maserati" || entry.prefabName == "GenericCoupe") && targetIndex >= 0)
            {
                groups[0].defaultOptionIndex = targetIndex;
                Debug.Log("[CarImporter] " + entry.prefabName + ": default body colour forced to Black.");
            }
        }

        foreach (var g in groups)
        {
            if (g.displayName != "Wheels") continue;
            var opts = g.options;
            for (int i = 0; i < opts.Length; i++)
            {
                if (opts[i] != null && opts[i].name == "RimBlack")
                {
                    g.defaultOptionIndex = i;
                    Debug.Log("[CarImporter] " + entry.prefabName + ": Rims default forced to Black.");
                    break;
                }
            }
        }

        if (!entry.hasBuiltInSpoiler && !entry.noCustomization)
        {
            var spoiler = AssetDatabase.LoadAssetAtPath<GameObject>(Path.Combine(PrefabsFolder, "Spoiler.prefab"));
            if (spoiler != null) customizer.spoilerPrefab = spoiler;
        }
        if (entry.spoilerLocalPosition.HasValue)
            customizer.spoilerLocalPosition = entry.spoilerLocalPosition.Value;
        if (entry.spoilerLocalRotation.HasValue)
            customizer.spoilerLocalRotation = entry.spoilerLocalRotation.Value;
        customizer.wheelSetOptions = new GameObject[0];

        if (entry.prefabName == "Maserati")
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>())
            {
                var slots = r.sharedMaterials;
                for (int i = 0; i < slots.Length; i++)
                {
                    if (slots[i] == null) continue;
                    string n = slots[i].name.ToLowerInvariant();
                    if (n.Contains("glass") || n.Contains("window") || n.Contains("windscreen"))
                    {
                        slots[i].color = Color.black;
                    }
                }
            }
            Debug.Log("[CarImporter] Maserati: glass/window materials set to pitch black.");
        }

        // Which renderer/material actually landed in each group, logged
        // before root is destroyed (target.renderer references go dead
        // right after). A part that shouldn't be here (e.g. a taillight
        // caught by Body because its material name doesn't contain any of
        // the expected exclude keywords) is visible immediately in the
        // Console after import, instead of having to guess another keyword
        // blind and re-import to check.
        foreach (var g in groups)
        {
            var detail = new System.Text.StringBuilder("[CarImporter] " + entry.name + " " + g.displayName + " targets:");
            foreach (var t in g.targets)
            {
                string matName = "?";
                if (t.renderer != null)
                {
                    var slots = t.renderer.sharedMaterials;
                    if (t.materialIndex >= 0 && t.materialIndex < slots.Length && slots[t.materialIndex] != null)
                        matName = slots[t.materialIndex].name;
                }
                detail.Append("  ").Append(t.renderer != null ? t.renderer.name : "?").Append("/").Append(matName);
            }
            Debug.Log(detail.ToString());
        }

        string prefabPath = Path.Combine(PrefabsFolder, entry.prefabName + ".prefab");
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        Object.DestroyImmediate(root);

        if (prefab == null)
        {
            Debug.LogError("[CarImporter] " + entry.name + ": Failed to save prefab at " + prefabPath);
            return null;
        }

        var summary = new System.Text.StringBuilder();
        foreach (var g in groups)
            summary.Append(" ").Append(g.displayName).Append("(")
                   .Append(g.targets.Length).Append(" slots, ")
                   .Append(g.options.Length).Append(" options)");
        Debug.Log("[CarImporter] " + entry.name + " ready. Groups:" + summary);
        return prefab;
    }

    static void RegisterInScene(GameObject[] newPrefabs)
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != ScenePath)
        {
            if (!File.Exists(ScenePath))
            {
                Debug.LogError("[CarImporter] Scene not found at " + ScenePath +
                               ". Run BuildYourRide > Run Full Setup first.");
                return;
            }
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        var placement = Object.FindObjectOfType<CarPlacementController>();
        if (placement == null)
        {
            Debug.LogError("[CarImporter] CarPlacementController not found in " + ScenePath);
            return;
        }

        // Merge into the existing roster, deduplicating by name, keeping
        // whichever entry (existing or new) comes first. Only matters for the
        // standalone single-car reimport helpers (ImportBMWM4 etc.) -- when
        // this runs via CarRoster.RebuildAllCars(), that method immediately
        // overwrites placement.carPrefabs with its own explicit roster list,
        // so this merge is moot on the main rebuild path.
        var roster = new List<GameObject>();
        var seen = new HashSet<string>();

        if (placement.carPrefabs != null)
        {
            foreach (var c in placement.carPrefabs)
            {
                if (c == null || seen.Contains(c.name)) continue;
                roster.Add(c);
                seen.Add(c.name);
            }
        }

        foreach (var prefab in newPrefabs)
        {
            if (prefab == null || seen.Contains(prefab.name)) continue;
            roster.Add(prefab);
            seen.Add(prefab.name);
        }

        placement.carPrefabs = roster.ToArray();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[CarImporter] Roster now: " + string.Join(", ", seen));
    }

    static Bounds ComputeBounds(GameObject go)
    {
        var renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return new Bounds(Vector3.zero, Vector3.one);
        var b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            b.Encapsulate(renderers[i].bounds);
        return b;
    }
}
