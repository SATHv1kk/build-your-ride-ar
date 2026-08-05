using System.Collections.Generic;
using System.IO;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.Rendering;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.Management;

public static class ProjectSetup
{
    const string ScenePath = "Assets/Scenes/Main.unity";

    static Dictionary<string, Material> mats;

    // Ordered so the swatch tray reads as a designed palette rather than an
    // arbitrary material folder listing. CarImporter's DefaultSpecs pull the
    // same lists, so every imported car offers identical colours.
    //
    // Trimmed to the main/important colours only (v4.6) -- the previous
    // 13-entry list had near-duplicate pairs (White/PearlWhite,
    // Grey/GunmetalGrey, Black/JetBlack, Red/RacingRed, Blue/BaysideBlue)
    // plus accent hues (Yellow/Purple/DarkGreen) that just added scroll
    // without adding real choice. Keeping the nicer-looking variant of each
    // essential hue and dropping the rest.
    public static readonly KeyValuePair<string, Color>[] PaintSwatches =
    {
        new KeyValuePair<string, Color>("Paint_PearlWhite",     new Color(0.95f, 0.96f, 0.97f)),
        new KeyValuePair<string, Color>("Paint_GunmetalGrey",   new Color(0.24f, 0.26f, 0.29f)),
        new KeyValuePair<string, Color>("Paint_JetBlack",       new Color(0.020f, 0.020f, 0.025f)),
        new KeyValuePair<string, Color>("Paint_RacingRed",      new Color(0.72f, 0.03f, 0.04f)),
        new KeyValuePair<string, Color>("Paint_BaysideBlue",    new Color(0.06f, 0.28f, 0.66f))
    };

    // Limited palette for Maserati — White/Black/Blue/Red (dropped
    // Purple/Green as non-main accent colours, matching the same "main
    // colours" trim applied to PaintSwatches above; Red added back per
    // request after the initial v4.6 trim).
    public static readonly KeyValuePair<string, Color>[] MaseratiPaintSwatches =
    {
        new KeyValuePair<string, Color>("Paint_White", new Color(0.88f, 0.89f, 0.90f)),
        new KeyValuePair<string, Color>("Paint_Black", new Color(0.05f, 0.05f, 0.06f)),
        new KeyValuePair<string, Color>("Paint_Blue",  new Color(0.05f, 0.20f, 0.58f)),
        new KeyValuePair<string, Color>("Paint_RacingRed", new Color(0.72f, 0.03f, 0.04f))
    };

    // Batmobile: no real colour choice, just the one fixed black -- a single-
    // entry palette so it goes through the same CarPartMapper/CarCustomizer
    // machinery as every other car's Body group instead of a special case.
    public static readonly KeyValuePair<string, Color>[] BatmobileBodySwatches =
    {
        new KeyValuePair<string, Color>("Paint_JetBlack", new Color(0.020f, 0.020f, 0.025f))
    };

    public static readonly KeyValuePair<string, Color>[] RimSwatches =
    {
        new KeyValuePair<string, Color>("RimSilver", new Color(0.80f, 0.80f, 0.85f)),
        new KeyValuePair<string, Color>("RimBlack",  new Color(0.10f, 0.10f, 0.11f)),
        new KeyValuePair<string, Color>("RimBronze", new Color(0.48f, 0.31f, 0.14f)),
        new KeyValuePair<string, Color>("RimGold",   new Color(0.78f, 0.62f, 0.22f)),
        new KeyValuePair<string, Color>("RimWhite",  new Color(0.90f, 0.90f, 0.92f))
    };

    public static readonly KeyValuePair<string, Color>[] TrimSwatches =
    {
        new KeyValuePair<string, Color>("CarbonBlack", new Color(0.040f, 0.040f, 0.045f)),
        new KeyValuePair<string, Color>("Trim",        new Color(0.120f, 0.120f, 0.130f)),
        new KeyValuePair<string, Color>("TrimSilver",  new Color(0.720f, 0.730f, 0.760f)),
        new KeyValuePair<string, Color>("TrimBronze",  new Color(0.450f, 0.300f, 0.140f))
    };

    public static readonly KeyValuePair<string, Color>[] CalliperSwatches =
    {
        new KeyValuePair<string, Color>("CalliperRed",    new Color(0.85f, 0.06f, 0.05f)),
        new KeyValuePair<string, Color>("CalliperYellow", new Color(0.93f, 0.75f, 0.05f)),
        new KeyValuePair<string, Color>("CalliperBlue",   new Color(0.08f, 0.24f, 0.72f)),
        new KeyValuePair<string, Color>("CalliperBlack",  new Color(0.06f, 0.06f, 0.07f))
    };

    public static Material[] LoadPalette(KeyValuePair<string, Color>[] swatches)
    {
        var list = new List<Material>();
        foreach (var s in swatches)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/" + s.Key + ".mat");
            if (m != null) list.Add(m);
        }
        return list.ToArray();
    }

    public static void RunAll()
    {
        EnsureFolders();
        ConfigurePlayerSettings();
        EnableARCore();
        CreateMaterials();
        EnsureShadersAlwaysIncluded();
        CreateSpoilerPrefab();
        var planePrefab = CreatePlanePrefab();
        // The roster is the real cars. The procedural box cars are no longer
        // shipped and are not on the menu — but the code to generate them
        // remains callable directly.
        BuildScene(new GameObject[0], planePrefab);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        CarImporter.ImportAllNewCars();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("=== BuildYourRide setup complete ===");
    }

    // The three procedural box cars, kept out of the shipped roster but fully
    // regenerable from code, so deleting their prefabs costs nothing.
    public static void AddPlaceholderCars()
    {
        CreateMaterials();
        var spoiler = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Spoiler.prefab");
        if (spoiler == null) spoiler = CreateSpoilerPrefab();

        var made = new[] { CreateCoupe(spoiler), CreateSuv(spoiler), CreatePickup(spoiler) };
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        if (!CarRoster.OpenScene()) return;

        var placement = Object.FindObjectOfType<CarPlacementController>();
        if (placement == null)
        {
            Debug.LogError("CarPlacementController not found; prefabs were created but not added to the scene.");
            return;
        }

        var list = new List<GameObject>(placement.carPrefabs ?? new GameObject[0]);
        foreach (var p in made)
        {
            if (p != null && !list.Contains(p)) list.Add(p);
        }
        placement.carPrefabs = list.ToArray();
        CarRoster.SaveScene(placement);
        Debug.Log("=== Placeholder cars added. Roster is now " + list.Count + " car(s). ===");
    }

    // Plain method (not on the menu) -- called from the single Rebuild All
    // menu item if a full APK build is desired.
    [MenuItem("BuildYourRide/Rebuild All")]
    public static void RebuildAll()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogError("[BuildYourRide] Stop Play mode first.");
            return;
        }
        Debug.Log("[BuildYourRide] Rebuilding car roster and scene...");
        CarRoster.RebuildAllCars();
        SceneUpgrade.Upgrade();
        Debug.Log("=== Rebuild complete. Press Play to test. ===");
    }

    public static void BuildApk()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogError("[BuildYourRide] Stop Play mode before building -- scene editing isn't allowed while playing.");
            return;
        }

        Debug.Log("[BuildYourRide] Rebuilding car roster and scene before build...");
        CarRoster.RebuildAllCars();
        SceneUpgrade.Upgrade();

        if (!File.Exists(ScenePath))
        {
            Debug.LogError("Scene not found at " + ScenePath + ". Run BuildYourRide/Run Full Setup first.");
            return;
        }
        Directory.CreateDirectory("Builds");
        var opts = new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = "Builds/BuildYourRideAR.apk",
            target = BuildTarget.Android,
            options = BuildOptions.None
        };
        var report = UnityEditor.BuildPipeline.BuildPlayer(opts);
        Debug.Log("Build result: " + report.summary.result + ", size: " + (report.summary.totalSize / (1024 * 1024)) + " MB");
    }

    static void EnsureFolders()
    {
        foreach (var folder in new[] { "Scenes", "Scripts", "Materials", "Prefabs", "Textures" })
        {
            if (!AssetDatabase.IsValidFolder("Assets/" + folder))
                AssetDatabase.CreateFolder("Assets", folder);
        }
    }

    static void ConfigurePlayerSettings()
    {
        PlayerSettings.companyName = "SathvikKoti";
        PlayerSettings.productName = "Build Your Ride AR";
        PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, "com.sathvikkoti.buildyourridear");
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel24;
        PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
        PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.OpenGLES3, GraphicsDeviceType.Vulkan });
        PlayerSettings.SetMobileMTRendering(BuildTargetGroup.Android, true);

        // Linear belongs with the rest of the canonical player config, not only
        // in ReleaseSetup: every car is PBR and the lighting is driven by
        // ARCore's Environmental HDR estimate, both of which assume light
        // composites linearly. The graphics API list set two lines above is
        // exactly what makes this legal on Android (GLES3/Vulkan, no GLES2),
        // alongside minSdk 24 -- so this must stay below those calls.
        //
        // Guarded because assigning it kicks off a full asset reimport.
        if (PlayerSettings.colorSpace != ColorSpace.Linear)
        {
            PlayerSettings.colorSpace = ColorSpace.Linear;
            Debug.Log("Colour space set to Linear (was Gamma); Unity will reimport assets.");
        }

        var playerSettingsObj = Unsupported.GetSerializedAssetInterfaceSingleton("PlayerSettings");
        if (playerSettingsObj != null)
        {
            var playerSettings = new SerializedObject(playerSettingsObj);
            var prop = playerSettings.FindProperty("activeInputHandler");
            if (prop != null)
            {
                prop.intValue = 2;
                playerSettings.ApplyModifiedProperties();
            }
        }
        Debug.Log("Player settings configured (Android, IL2CPP/ARM64, GLES3+Vulkan, input=Both).");
    }

    static void EnableARCore()
    {
        try
        {
            XRGeneralSettingsPerBuildTarget settingsPerTarget;
            EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey, out settingsPerTarget);
            if (settingsPerTarget == null)
            {
                settingsPerTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                if (!AssetDatabase.IsValidFolder("Assets/XR"))
                    AssetDatabase.CreateFolder("Assets", "XR");
                AssetDatabase.CreateAsset(settingsPerTarget, "Assets/XR/XRGeneralSettings.asset");
                EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, settingsPerTarget, true);
            }

            var androidSettings = settingsPerTarget.SettingsForBuildTarget(BuildTargetGroup.Android);
            if (androidSettings == null)
            {
                androidSettings = ScriptableObject.CreateInstance<XRGeneralSettings>();
                androidSettings.name = "Android Settings";
                AssetDatabase.AddObjectToAsset(androidSettings, settingsPerTarget);
                settingsPerTarget.SetSettingsForBuildTarget(BuildTargetGroup.Android, androidSettings);
            }

            if (androidSettings.Manager == null)
            {
                var manager = ScriptableObject.CreateInstance<XRManagerSettings>();
                manager.name = "Android Providers";
                AssetDatabase.AddObjectToAsset(manager, settingsPerTarget);
                androidSettings.Manager = manager;
            }

            AssetDatabase.Refresh();
        bool ok = XRPackageMetadataStore.AssignLoader(androidSettings.Manager, "UnityEngine.XR.ARCore.ARCoreLoader", BuildTargetGroup.Android);
        if (!ok)
        {
            AssetDatabase.Refresh();
            ok = XRPackageMetadataStore.AssignLoader(androidSettings.Manager, "UnityEngine.XR.ARCore.ARCoreLoader", BuildTargetGroup.Android);
        }
        EditorUtility.SetDirty(settingsPerTarget);
        Debug.Log(ok
            ? "ARCore loader enabled for Android."
            : "WARNING: could not auto-enable ARCore. Enable manually: Project Settings > XR Plug-in Management > Android > ARCore.");
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("XR auto-setup failed: " + e.Message +
                "\nEnable manually: Project Settings > XR Plug-in Management > Android tab > check ARCore.");
        }
    }

    public static void CreateMaterials()
    {
        mats = new Dictionary<string, Material>();

        foreach (var swatch in PaintSwatches)
            Paint(swatch.Key, swatch.Value);

        // MaseratiPaintSwatches and BatmobileBodySwatches used to work only
        // because their material names happened to also appear in the old,
        // larger PaintSwatches list -- once that list was trimmed (v4.6),
        // those two per-car palettes were left pointing at .mat files
        // CreateMaterials() never actually owned, surviving only as leftover
        // assets from before the trim. A truly fresh checkout (no
        // Assets/Materials/ yet) would have imported Maserati and Batmobile
        // with an empty or near-empty body palette. Duplicate keys across
        // these arrays and PaintSwatches are harmless -- Paint() just
        // overwrites the same asset with the same colour again.
        foreach (var swatch in MaseratiPaintSwatches)
            Paint(swatch.Key, swatch.Value);
        foreach (var swatch in BatmobileBodySwatches)
            Paint(swatch.Key, swatch.Value);

        Std("Glass", new Color(0.06f, 0.07f, 0.10f), 0.2f, 0.9f);
        Std("Tyre", new Color(0.08f, 0.08f, 0.08f), 0.0f, 0.4f);
        Std("SpoilerBody", new Color(0.08f, 0.08f, 0.09f), 0.5f, 0.7f);

        foreach (var rim in RimSwatches)
            Std(rim.Key, rim.Value, 1.0f, 0.82f);

        foreach (var calliper in CalliperSwatches)
            Std(calliper.Key, calliper.Value, 0.35f, 0.65f);

        foreach (var trim in TrimSwatches)
            Std(trim.Key, trim.Value, 0.35f, 0.55f);

        CreateShadowMaterial();
        CreateSwatchSprite();

        Emissive("LightWhite", Color.white, new Color(1.0f, 1.0f, 0.9f) * 1.2f);
        Emissive("LightRed", new Color(0.6f, 0.02f, 0.02f), new Color(1.0f, 0.05f, 0.05f) * 0.8f);

        CreatePlaneMaterial();

        var reticleTex = CreateRingTexture();
        var reticle = new Material(Shader.Find("Unlit/Transparent"));
        reticle.mainTexture = reticleTex;
        Save("Reticle", reticle);
    }

    static void Paint(string name, Color color)
    {
        var m = new Material(Shader.Find("Standard"));
        m.color = color;
        m.SetFloat("_Metallic", 0.7f);
        m.SetFloat("_Glossiness", 0.75f);
        Save(name, m);
    }

    static void Std(string name, Color color, float metallic, float smoothness)
    {
        var m = new Material(Shader.Find("Standard"));
        m.color = color;
        m.SetFloat("_Metallic", metallic);
        m.SetFloat("_Glossiness", smoothness);
        Save(name, m);
    }

    static void Emissive(string name, Color color, Color emission)
    {
        var m = new Material(Shader.Find("Standard"));
        m.color = color;
        m.EnableKeyword("_EMISSION");
        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        m.SetColor("_EmissionColor", emission);
        Save(name, m);
    }

    static void Save(string name, Material m)
    {
        // Update the existing asset in place instead of delete+create, so the
        // GUID survives and prefabs referencing the material keep working.
        string path = "Assets/Materials/" + name + ".mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null)
        {
            existing.shader = m.shader;
            existing.CopyPropertiesFromMaterial(m);
            existing.shaderKeywords = m.shaderKeywords;
            existing.globalIlluminationFlags = m.globalIlluminationFlags;
            existing.renderQueue = m.renderQueue;
            Object.DestroyImmediate(m);
            EditorUtility.SetDirty(existing);
            // mats is only populated during a full run; SceneUpgrade calls the
            // individual creators on their own.
            if (mats != null) mats[name] = existing;
            return;
        }
        AssetDatabase.CreateAsset(m, path);
        if (mats != null) mats[name] = m;
    }

    static Texture2D CreateRingTexture()
    {
        const int size = 256;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var pixels = new Color32[size * size];
        var c = new Vector2(size / 2f - 0.5f, size / 2f - 0.5f);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), c);
                byte a = 0;
                if (d >= 96f && d <= 116f) a = 255;
                else if (d < 10f) a = 200;
                pixels[y * size + x] = new Color32(255, 255, 255, a);
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply();
        var png = tex.EncodeToPNG();
        Object.DestroyImmediate(tex);
        File.WriteAllBytes("Assets/Textures/Reticle.png", png);
        AssetDatabase.ImportAsset("Assets/Textures/Reticle.png");
        var reticleTex2D = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/Reticle.png");
        for (int i = 0; i < 10 && reticleTex2D == null; i++)
        {
            AssetDatabase.Refresh();
            reticleTex2D = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/Reticle.png");
        }
        var importer = (TextureImporter)AssetImporter.GetAtPath("Assets/Textures/Reticle.png");
        if (importer != null)
        {
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
            reticleTex2D = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/Reticle.png");
        }
        return reticleTex2D;
    }

    static GameObject CreateSpoilerPrefab()
    {
        var root = new GameObject("Spoiler");

        Box(root.transform, "Wing", new Vector3(0f, 0.28f, 0f), new Vector3(1.50f, 0.06f, 0.30f), mats["SpoilerBody"]);
        Box(root.transform, "Post_L", new Vector3(-0.52f, 0.13f, 0f), new Vector3(0.06f, 0.28f, 0.08f), mats["SpoilerBody"]);
        Box(root.transform, "Post_R", new Vector3(0.52f, 0.13f, 0f), new Vector3(0.06f, 0.28f, 0.08f), mats["SpoilerBody"]);

        return SavePrefab(root, "Spoiler");
    }

    static GameObject Box(Transform parent, string name, Vector3 center, Vector3 size, Material mat, List<Renderer> paintList = null)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = center;
        go.transform.localScale = size;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        var r = go.GetComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        if (paintList != null) paintList.Add(r);
        return go;
    }

    static void Wheel(Transform parent, string name, Vector3 center, float radius, float width, Material tyre, Material rim, List<Renderer> hubList)
    {
        var wheel = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        wheel.name = name;
        wheel.transform.SetParent(parent, false);
        wheel.transform.localPosition = center;
        wheel.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
        wheel.transform.localScale = new Vector3(radius * 2f, width * 0.5f, radius * 2f);
        Object.DestroyImmediate(wheel.GetComponent<Collider>());
        wheel.GetComponent<MeshRenderer>().sharedMaterial = tyre;

        var hub = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        hub.name = "Hub";
        hub.transform.SetParent(wheel.transform, false);
        hub.transform.localScale = new Vector3(0.6f, 1.03f, 0.6f);
        Object.DestroyImmediate(hub.GetComponent<Collider>());
        var hubRenderer = hub.GetComponent<MeshRenderer>();
        hubRenderer.sharedMaterial = rim;
        if (hubList != null) hubList.Add(hubRenderer);
    }

    // Hubs from every wheel set are collected, not just the active one, so
    // recolouring rims survives switching between standard and sport wheels.
    static GameObject WheelSet(Transform parent, string name, float radius, float width, float x, float y, float zFront, float zRear, Material rim, List<Renderer> hubList)
    {
        var set = new GameObject(name);
        set.transform.SetParent(parent, false);
        Wheel(set.transform, "Wheel_FL", new Vector3(-x, y, zFront), radius, width, mats["Tyre"], rim, hubList);
        Wheel(set.transform, "Wheel_FR", new Vector3(x, y, zFront), radius, width, mats["Tyre"], rim, hubList);
        Wheel(set.transform, "Wheel_RL", new Vector3(-x, y, zRear), radius, width, mats["Tyre"], rim, hubList);
        Wheel(set.transform, "Wheel_RR", new Vector3(x, y, zRear), radius, width, mats["Tyre"], rim, hubList);
        return set;
    }

    static CarCustomizer.PaintTarget[] Targets(List<Renderer> renderers)
    {
        var list = new List<CarCustomizer.PaintTarget>();
        if (renderers == null) return list.ToArray();
        foreach (var r in renderers)
        {
            if (r != null) list.Add(new CarCustomizer.PaintTarget(r, 0));
        }
        return list.ToArray();
    }

    static CarCustomizer AddCustomizer(GameObject root, List<Renderer> body, List<Renderer> hubs,
        GameObject spoilerPrefab, GameObject[] wheelSets)
    {
        var customizer = root.AddComponent<CarCustomizer>();
        var groups = new List<CarCustomizer.PartGroup>
        {
            new CarCustomizer.PartGroup
            {
                displayName = "Body",
                targets = Targets(body),
                options = PaintMaterials(),
                applyFinish = true
            }
        };

        if (hubs != null && hubs.Count > 0)
        {
            groups.Add(new CarCustomizer.PartGroup
            {
                displayName = "Wheels",
                targets = Targets(hubs),
                options = Palette(RimSwatches),
                // Rims keep their own metal look; the paint finish selector is
                // for bodywork only.
                applyFinish = false
            });
        }

        customizer.partGroups = groups.ToArray();
        customizer.spoilerPrefab = spoilerPrefab;
        customizer.wheelSetOptions = wheelSets;
        return customizer;
    }

    static void Lights(Transform parent, float xOffset, float y, float zFront, float zRear)
    {
        Box(parent, "Headlight_L", new Vector3(-xOffset, y, zFront), new Vector3(0.35f, 0.12f, 0.06f), mats["LightWhite"]);
        Box(parent, "Headlight_R", new Vector3(xOffset, y, zFront), new Vector3(0.35f, 0.12f, 0.06f), mats["LightWhite"]);
        Box(parent, "Taillight_L", new Vector3(-xOffset, y, zRear), new Vector3(0.35f, 0.12f, 0.06f), mats["LightRed"]);
        Box(parent, "Taillight_R", new Vector3(xOffset, y, zRear), new Vector3(0.35f, 0.12f, 0.06f), mats["LightRed"]);
    }

    static GameObject SavePrefab(GameObject root, string name)
    {
        string path = "Assets/Prefabs/" + name + ".prefab";
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        return prefab;
    }

    static GameObject CreateCoupe(GameObject spoilerPrefab)
    {
        var root = new GameObject("Coupe");
        var paint = new List<Renderer>();

        Box(root.transform, "Body", new Vector3(0f, 0.55f, 0.05f), new Vector3(1.80f, 0.55f, 4.60f), mats["Paint_BaysideBlue"], paint);
        Box(root.transform, "FrontLip", new Vector3(0f, 0.25f, 2.30f), new Vector3(1.76f, 0.22f, 0.30f), mats["Paint_BaysideBlue"], paint);
        Box(root.transform, "Cabin", new Vector3(0f, 1.02f, -0.15f), new Vector3(1.60f, 0.44f, 2.10f), mats["Glass"]);
        Lights(root.transform, 0.55f, 0.62f, 2.36f, -2.26f);

        var hubs = new List<Renderer>();
        var wheelsStd = WheelSet(root.transform, "Wheels_Standard", 0.33f, 0.25f, 0.82f, 0.33f, 1.45f, -1.45f, mats["RimSilver"], hubs);
        var wheelsSport = WheelSet(root.transform, "Wheels_Sport", 0.35f, 0.30f, 0.84f, 0.35f, 1.45f, -1.45f, mats["RimSilver"], hubs);

        AddCustomizer(root, paint, hubs, spoilerPrefab, new[] { wheelsStd, wheelsSport });

        return SavePrefab(root, "Coupe");
    }

    static GameObject CreateSuv(GameObject spoilerPrefab)
    {
        var root = new GameObject("SUV");
        var paint = new List<Renderer>();

        Box(root.transform, "Body", new Vector3(0f, 0.75f, 0f), new Vector3(1.95f, 0.85f, 4.80f), mats["Paint_BaysideBlue"], paint);
        Box(root.transform, "Cabin", new Vector3(0f, 1.45f, -0.10f), new Vector3(1.80f, 0.55f, 2.60f), mats["Glass"]);
        Box(root.transform, "RoofRails", new Vector3(0f, 1.76f, -0.10f), new Vector3(1.60f, 0.06f, 2.30f), mats["Trim"]);
        Lights(root.transform, 0.62f, 0.85f, 2.41f, -2.41f);

        var hubs = new List<Renderer>();
        var wheelsStd = WheelSet(root.transform, "Wheels_Standard", 0.40f, 0.28f, 0.86f, 0.40f, 1.55f, -1.55f, mats["RimSilver"], hubs);
        var wheelsOffroad = WheelSet(root.transform, "Wheels_Offroad", 0.44f, 0.34f, 0.88f, 0.44f, 1.55f, -1.55f, mats["RimSilver"], hubs);

        AddCustomizer(root, paint, hubs, spoilerPrefab, new[] { wheelsStd, wheelsOffroad });

        return SavePrefab(root, "SUV");
    }

    static GameObject CreatePickup(GameObject spoilerPrefab)
    {
        var root = new GameObject("Pickup");
        var paint = new List<Renderer>();

        Box(root.transform, "Body", new Vector3(0f, 0.65f, 0f), new Vector3(1.95f, 0.75f, 5.30f), mats["Paint_BaysideBlue"], paint);
        Box(root.transform, "Cab", new Vector3(0f, 1.40f, 0.55f), new Vector3(1.80f, 0.60f, 1.60f), mats["Glass"]);
        Box(root.transform, "BedSide_L", new Vector3(-0.90f, 1.15f, -1.60f), new Vector3(0.12f, 0.35f, 2.00f), mats["Paint_BaysideBlue"], paint);
        Box(root.transform, "BedSide_R", new Vector3(0.90f, 1.15f, -1.60f), new Vector3(0.12f, 0.35f, 2.00f), mats["Paint_BaysideBlue"], paint);
        Box(root.transform, "Tailgate", new Vector3(0f, 1.15f, -2.59f), new Vector3(1.90f, 0.35f, 0.12f), mats["Paint_BaysideBlue"], paint);
        Lights(root.transform, 0.62f, 0.75f, 2.66f, -2.66f);

        var hubs = new List<Renderer>();
        var wheelsStd = WheelSet(root.transform, "Wheels_Standard", 0.42f, 0.30f, 0.86f, 0.42f, 1.70f, -1.70f, mats["RimSilver"], hubs);
        var wheelsOffroad = WheelSet(root.transform, "Wheels_Offroad", 0.46f, 0.36f, 0.88f, 0.46f, 1.70f, -1.70f, mats["RimSilver"], hubs);

        AddCustomizer(root, paint, hubs, spoilerPrefab, new[] { wheelsStd, wheelsOffroad });

        return SavePrefab(root, "Pickup");
    }

    static Material[] PaintMaterials()
    {
        return Palette(PaintSwatches);
    }

    static Material[] Palette(KeyValuePair<string, Color>[] swatches)
    {
        var list = new List<Material>();
        foreach (var s in swatches)
        {
            Material m;
            if (mats.TryGetValue(s.Key, out m) && m != null) list.Add(m);
        }
        return list.ToArray();
    }

    // The old plane visual used "Legacy Shaders/Transparent/Diffuse", a
    // built-in shader that was rendering magenta on device -- the signature of
    // a shader that did not survive into the build. A project shader is
    // compiled with the project, so it cannot go missing, and it lets the
    // planes look like soft light instead of flat paint.
    public static void CreatePlaneMaterial()
    {
        var shader = Shader.Find("BuildYourRide/ARPlaneGlow");
        if (shader == null)
        {
            Debug.LogWarning("ARPlaneGlow shader not found; falling back to an unlit transparent plane.");
            var fallback = new Material(Shader.Find("Unlit/Transparent"));
            Save("PlaneViz", fallback);
            return;
        }

        var m = new Material(shader);
        m.SetColor("_ColorNear", new Color(0.35f, 0.80f, 1.00f));
        m.SetColor("_ColorFar", new Color(0.55f, 0.45f, 1.00f));
        m.SetColor("_RimColor", new Color(0.70f, 0.92f, 1.00f));
        m.SetColor("_PulseColor", new Color(0.80f, 0.95f, 1.00f));
        m.SetFloat("_Alpha", 0.10f);
        m.SetFloat("_RimAlpha", 0.35f);
        m.SetFloat("_GridAlpha", 0.16f);
        m.SetFloat("_PulseAlpha", 0.18f);
        m.SetFloat("_EdgeFeather", 0.30f);
        m.SetFloat("_RimWidth", 0.07f);
        m.SetFloat("_GridSpacing", 0.25f);
        m.SetFloat("_GradientScale", 2.0f);
        m.SetFloat("_PulseSpeed", 0.35f);
        m.SetFloat("_PulseFreq", 0.30f);
        m.SetFloat("_PulseWidth", 0.35f);
        m.SetFloat("_BreatheSpeed", 1.4f);
        m.SetFloat("_BreatheDepth", 0.18f);
        Save("PlaneViz", m);
    }

    // Belt and braces against shader stripping: both project shaders are also
    // registered as always-included so they ship even if a future refactor
    // stops referencing them from a scene material.
    public static void EnsureShadersAlwaysIncluded()
    {
        var graphicsSettings = Unsupported.GetSerializedAssetInterfaceSingleton("GraphicsSettings");
        if (graphicsSettings == null) return;

        var so = new SerializedObject(graphicsSettings);
        var list = so.FindProperty("m_AlwaysIncludedShaders");
        if (list == null) return;

        foreach (var name in new[] { "BuildYourRide/ARPlaneGlow", "BuildYourRide/ShadowCatcher" })
        {
            var shader = Shader.Find(name);
            if (shader == null) continue;

            bool present = false;
            for (int i = 0; i < list.arraySize; i++)
            {
                if (list.GetArrayElementAtIndex(i).objectReferenceValue == shader)
                {
                    present = true;
                    break;
                }
            }
            if (present) continue;

            list.InsertArrayElementAtIndex(list.arraySize);
            list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = shader;
            Debug.Log("Added " + name + " to Always Included Shaders.");
        }

        so.ApplyModifiedProperties();
    }

    public static void CreateShadowMaterial()
    {
        var shader = Shader.Find("BuildYourRide/ShadowCatcher");
        if (shader == null)
        {
            Debug.LogWarning("ShadowCatcher shader not found; the car will render without a contact shadow.");
            return;
        }
        var m = new Material(shader);
        m.SetFloat("_ShadowStrength", 0.55f);
        m.SetFloat("_FadeRadius", 0.5f);
        Save("ShadowCatcher", m);
    }

    // A soft-edged filled circle, used for the colour chips in the tray.
    public static void CreateSwatchSprite()
    {
        const int size = 128;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var pixels = new Color32[size * size];
        float c = size / 2f - 0.5f;
        float r = size / 2f - 1f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), new Vector2(c, c));
                byte a = (byte)(Mathf.Clamp01(r - d) * 255f);
                pixels[y * size + x] = new Color32(255, 255, 255, a);
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply();
        SaveSprite(tex, "Swatch", Vector4.zero);
    }

    static GameObject CreatePlanePrefab()
    {
        var go = new GameObject("ARPlaneViz");
        go.AddComponent<ARPlane>();
        go.AddComponent<MeshFilter>();
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mats["PlaneViz"];
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;
        // Planes are a UI affordance, not scene geometry: keeping them out of
        // probe and reflection passes stops them tinting the car.
        mr.lightProbeUsage = LightProbeUsage.Off;
        mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
        go.AddComponent<ARPlaneMeshVisualizer>();
        go.AddComponent<ARPlaneFeather>();
        return SavePrefab(go, "ARPlaneViz");
    }

    static void BuildScene(GameObject[] carPrefabs, GameObject planePrefab)
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.45f, 0.45f, 0.47f);

        var lightGO = new GameObject("Directional Light");
        var light = lightGO.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.1f;
        light.shadows = LightShadows.Soft;
        lightGO.transform.rotation = Quaternion.Euler(55f, -35f, 0f);

        var sessionGO = new GameObject("AR Session");
        sessionGO.AddComponent<ARSession>();
        sessionGO.AddComponent<ARInputManager>();

        var originGO = new GameObject("XR Origin");
        var origin = originGO.AddComponent<XROrigin>();

        var offsetGO = new GameObject("Camera Offset");
        offsetGO.transform.SetParent(originGO.transform, false);

        var camGO = new GameObject("AR Camera");
        camGO.transform.SetParent(offsetGO.transform, false);
        camGO.tag = "MainCamera";
        var cam = camGO.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;
        cam.nearClipPlane = 0.1f;
        // This is a room-scale app -- the car is placed a few metres away
        // and the user walks around it, never a wide outdoor scene. 60m
        // meant every frame culled and rendered against a far plane 10-20x
        // larger than anything the app ever places, for zero visual gain.
        cam.farClipPlane = 30f;
        camGO.AddComponent<ARCameraManager>();
        camGO.AddComponent<ARCameraBackground>();

        // ARCameraBackground reads the occlusion manager off its own
        // GameObject, so depth must live on the camera, not the XR Origin.
        var occlusion = camGO.AddComponent<AROcclusionManager>();
        occlusion.requestedEnvironmentDepthMode =
            UnityEngine.XR.ARSubsystems.EnvironmentDepthMode.Medium;
        occlusion.requestedOcclusionPreferenceMode =
            UnityEngine.XR.ARSubsystems.OcclusionPreferenceMode.PreferEnvironmentOcclusion;

        var lightEstimator = camGO.AddComponent<ARLightEstimator>();
        lightEstimator.mainLight = light;

        var tpd = camGO.AddComponent<TrackedPoseDriver>();
        var posAction = new InputAction("Position", binding: "<XRHMD>/centerEyePosition", expectedControlType: "Vector3");
        posAction.AddBinding("<HandheldARInputDevice>/devicePosition");
        var rotAction = new InputAction("Rotation", binding: "<XRHMD>/centerEyeRotation", expectedControlType: "Quaternion");
        rotAction.AddBinding("<HandheldARInputDevice>/deviceRotation");
        tpd.positionInput = new InputActionProperty(posAction);
        tpd.rotationInput = new InputActionProperty(rotAction);

        origin.Camera = cam;
        origin.CameraFloorOffsetObject = offsetGO;

        var planeManager = originGO.AddComponent<ARPlaneManager>();
        planeManager.planePrefab = planePrefab;
        planeManager.requestedDetectionMode = UnityEngine.XR.ARSubsystems.PlaneDetectionMode.Horizontal;
        var raycastManager = originGO.AddComponent<ARRaycastManager>();
        originGO.AddComponent<ARAnchorManager>();

        var placement = originGO.AddComponent<CarPlacementController>();
        placement.carPrefabs = carPrefabs;

        var gestures = originGO.AddComponent<CarGestureController>();
        gestures.placement = placement;

        var editorSim = originGO.AddComponent<EditorSimulation>();
        editorSim.planePreviewMaterial = mats["PlaneViz"];

        var reticleGO = new GameObject("Placement Reticle");
        var reticle = reticleGO.AddComponent<PlacementReticle>();
        var visual = GameObject.CreatePrimitive(PrimitiveType.Quad);
        visual.name = "Visual";
        Object.DestroyImmediate(visual.GetComponent<Collider>());
        visual.transform.SetParent(reticleGO.transform, false);
        visual.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        visual.transform.localScale = Vector3.one * 0.5f;
        var visualRenderer = visual.GetComponent<MeshRenderer>();
        visualRenderer.sharedMaterial = mats["Reticle"];
        visualRenderer.shadowCastingMode = ShadowCastingMode.Off;
        visualRenderer.receiveShadows = false;
        reticle.raycastManager = raycastManager;
        reticle.placement = placement;
        reticle.visual = visual;

        var shadowGO = new GameObject("Shadow Catcher");
        var shadow = shadowGO.AddComponent<ShadowCatcher>();
        shadow.placement = placement;
        Material shadowMat;
        if (mats.TryGetValue("ShadowCatcher", out shadowMat))
            shadow.shadowMaterial = shadowMat;

        // The contact shadow only ever needs to reach a car a few metres
        // away, but Mathf.Max only ever raised an undersized value -- it
        // never lowered Unity's own quality-preset default (150m on this
        // project), which meant every frame was computing shadow cascades
        // for 6x more range than anything in the scene could use. Setting
        // it directly instead of floor-only: 30 m comfortably covers the
        // contact shadow with margin, at a fraction of the cost.
        QualitySettings.shadowDistance = 30f;

        BuildUI(placement, planeManager);

        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        Debug.Log("Scene built and added to build settings: " + ScenePath);
    }

    static Texture2D CreateRoundedRect(int w, int h, float r, Color fill, Color border, float borderWidth)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        var pixels = new Color[w * h];
        float borderPx = borderWidth * Mathf.Min(w, h);
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float dx = Mathf.Max(0f, r - Mathf.Min(x, w - 1 - x));
                float dy = Mathf.Max(0f, r - Mathf.Min(y, h - 1 - y));
                float dist = dx > 0f || dy > 0f ? Mathf.Sqrt(dx * dx + dy * dy) : 0f;

                if (dist > r) { pixels[y * w + x] = Color.clear; continue; }

                float alpha = 1f;
                if (dist > r - 1.5f) alpha = Mathf.Clamp01(r - dist);

                if (dist > r - borderPx && borderPx > 0f)
                    pixels[y * w + x] = Color.Lerp(fill, border, Mathf.Clamp01((dist - (r - borderPx)) / borderPx)) * new Color(1, 1, 1, alpha);
                else
                    pixels[y * w + x] = fill * new Color(1, 1, 1, alpha);
            }
        }
        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    static Sprite SaveSprite(Texture2D tex, string name, Vector4 border)
    {
        string path = "Assets/Textures/" + name + ".png";
        var png = tex.EncodeToPNG();
        Object.DestroyImmediate(tex);
        File.WriteAllBytes(path, png);
        AssetDatabase.ImportAsset(path);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        if (importer != null)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spriteBorder = border;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    // Shared by BuildUI (fresh scene) and SceneUpgrade.UpgradeUI (adding a
    // button to an existing bottom bar), so both paths produce an identical
    // button instead of two independently-maintained copies drifting apart.
    public static Button MakeBarButton(Transform bar, string label, Sprite sprite, Font font)
    {
        var go = new GameObject(label + "Btn", typeof(RectTransform));
        go.transform.SetParent(bar, false);
        var img = go.AddComponent<Image>();
        img.sprite = sprite;
        img.type = Image.Type.Sliced;
        var btn = go.AddComponent<Button>();

        var txtGO = new GameObject("Text", typeof(RectTransform));
        txtGO.transform.SetParent(go.transform, false);
        var txt = txtGO.AddComponent<Text>();
        txt.text = label;
        txt.font = font;
        txt.fontStyle = FontStyle.Bold;
        txt.alignment = TextAnchor.MiddleCenter;
        txt.color = new Color(0.92f, 0.93f, 0.95f);
        txt.resizeTextForBestFit = true;
        txt.resizeTextMinSize = 20;
        txt.resizeTextMaxSize = 44;
        // The parent Image is the button's hit target; the label sitting on top
        // of it only adds a second graphic for every touch to raycast against.
        // MakeText already does this for the other labels in the UI.
        txt.raycastTarget = false;
        var txtRT = txtGO.GetComponent<RectTransform>();
        txtRT.anchorMin = Vector2.zero;
        txtRT.anchorMax = Vector2.one;
        txtRT.offsetMin = new Vector2(6f, 4f);
        txtRT.offsetMax = new Vector2(-6f, -4f);
        return btn;
    }

    static void BuildUI(CarPlacementController placement, ARPlaneManager planeManager)
    {
        var canvasGO = new GameObject("Canvas");
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080f, 2340f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGO.AddComponent<GraphicRaycaster>();

        var esGO = new GameObject("EventSystem");
        esGO.AddComponent<EventSystem>();
        esGO.AddComponent<StandaloneInputModule>();

        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        // Sprites must be persisted assets: a Sprite.Create() sprite is scene-lost
        // on save/reload, leaving every Image as a plain white rectangle.
        var btnSprite = SaveSprite(CreateRoundedRect(256, 64, 28f,
            new Color(0.08f, 0.08f, 0.10f, 0.72f),
            new Color(1f, 1f, 1f, 0.30f), 0.06f),
            "GlassBtn", new Vector4(28f, 28f, 28f, 28f));

        var accentSprite = SaveSprite(CreateRoundedRect(256, 64, 28f,
            new Color(0.12f, 0.15f, 0.35f, 0.78f),
            new Color(0.5f, 0.6f, 1.0f, 0.55f), 0.06f),
            "GlassBtnAccent", new Vector4(28f, 28f, 28f, 28f));

        var panelSprite = SaveSprite(CreateRoundedRect(128, 64, 20f,
            new Color(0.04f, 0.04f, 0.06f, 0.60f),
            new Color(1f, 1f, 1f, 0.18f), 0.04f),
            "GlassPanel", new Vector4(20f, 20f, 20f, 20f));

        var bar = new GameObject("BottomBar", typeof(RectTransform));
        bar.transform.SetParent(canvasGO.transform, false);
        var barRT = bar.GetComponent<RectTransform>();
        barRT.anchorMin = new Vector2(0.04f, 0.015f);
        barRT.anchorMax = new Vector2(0.96f, 0.100f);
        barRT.offsetMin = Vector2.zero;
        barRT.offsetMax = Vector2.zero;
        var barImg = bar.AddComponent<Image>();
        barImg.sprite = panelSprite;
        barImg.type = Image.Type.Sliced;

        var layout = bar.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 14f;
        layout.padding = new RectOffset(18, 18, 8, 8);
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = true;

        Button MakeButton(string label, Sprite sprite) => MakeBarButton(bar.transform, label, sprite, font);

        var carBtn = MakeButton("CAR", btnSprite);
        var colorBtn = MakeButton("COLOR", btnSprite);
        var spoilerBtn = MakeButton("SPOILER", btnSprite);
        var wheelsBtn = MakeButton("WHEELS", btnSprite);
        var removeBtn = MakeButton("REMOVE", accentSprite);

        var hintGO = new GameObject("HintPanel", typeof(RectTransform));
        hintGO.transform.SetParent(canvasGO.transform, false);
        var hintRT = hintGO.GetComponent<RectTransform>();
        hintRT.anchorMin = new Vector2(0.06f, 0.87f);
        hintRT.anchorMax = new Vector2(0.94f, 0.94f);
        hintRT.offsetMin = Vector2.zero;
        hintRT.offsetMax = Vector2.zero;
        var hintImg = hintGO.AddComponent<Image>();
        hintImg.sprite = panelSprite;
        hintImg.type = Image.Type.Sliced;
        hintImg.raycastTarget = false;
        var hintLayout = hintGO.AddComponent<HorizontalLayoutGroup>();
        hintLayout.padding = new RectOffset(24, 24, 10, 10);
        hintLayout.childAlignment = TextAnchor.MiddleCenter;
        hintLayout.childControlWidth = true;
        hintLayout.childControlHeight = true;

        var hintTextGO = new GameObject("HintText", typeof(RectTransform));
        hintTextGO.transform.SetParent(hintGO.transform, false);
        var hintText = hintTextGO.AddComponent<Text>();
        hintText.font = font;
        hintText.alignment = TextAnchor.MiddleCenter;
        hintText.color = new Color(0.90f, 0.91f, 0.93f);
        hintText.resizeTextForBestFit = true;
        hintText.resizeTextMinSize = 18;
        hintText.resizeTextMaxSize = 34;
        hintText.raycastTarget = false;
        var hintTextRT = hintTextGO.GetComponent<RectTransform>();
        hintTextRT.anchorMin = Vector2.zero;
        hintTextRT.anchorMax = Vector2.one;
        hintTextRT.offsetMin = Vector2.zero;
        hintTextRT.offsetMax = Vector2.zero;

        var customizePanel = BuildCustomizePanel(canvasGO, panelSprite, btnSprite, font);
        BuildStatusOverlay(canvasGO, panelSprite, btnSprite, font, placement);

        var ui = canvasGO.AddComponent<UIController>();
        ui.placement = placement;
        ui.planeManager = planeManager;
        ui.arSession = Object.FindObjectOfType<ARSession>();
        ui.customizePanel = customizePanel;
        ui.carButton = carBtn;
        ui.colorButton = colorBtn;
        ui.spoilerButton = spoilerBtn;
        ui.wheelsButton = wheelsBtn;
        ui.removeButton = removeBtn;
        ui.hintText = hintText;
        ui.hintPanel = hintGO;
        // Wired explicitly rather than left to UIController's Awake fallback:
        // the fallback silently yields null whenever the component is absent,
        // which is exactly how the gesture controller went missing for several
        // versions without anything saying so. See SceneUpgrade.UpgradeGestures.
        ui.gestureController = placement.GetComponent<CarGestureController>();
    }

    // The tray that slides in above the bottom bar: part tabs, a colour grid
    // and the finish selector. Its contents are populated at runtime from
    // whichever car is placed, so only the containers are authored here.
    public static CustomizePanel BuildCustomizePanel(GameObject canvasGO, Sprite panelSprite, Sprite tabSprite, Font font)
    {
        var panelGO = new GameObject("CustomizePanel", typeof(RectTransform));
        panelGO.transform.SetParent(canvasGO.transform, false);
        var panelRT = panelGO.GetComponent<RectTransform>();
        panelRT.anchorMin = new Vector2(0.04f, 0.115f);
        panelRT.anchorMax = new Vector2(0.96f, 0.360f);
        panelRT.offsetMin = Vector2.zero;
        panelRT.offsetMax = Vector2.zero;

        var panelImg = panelGO.AddComponent<Image>();
        panelImg.sprite = panelSprite;
        panelImg.type = Image.Type.Sliced;

        var column = panelGO.AddComponent<VerticalLayoutGroup>();
        column.padding = new RectOffset(18, 18, 16, 16);
        column.spacing = 14f;
        column.childControlWidth = true;
        column.childControlHeight = true;
        column.childForceExpandWidth = true;
        column.childForceExpandHeight = false;

        var partRow = MakeRow(panelGO.transform, "PartRow", 84f);
        var partLayout = partRow.gameObject.AddComponent<HorizontalLayoutGroup>();
        partLayout.spacing = 10f;
        partLayout.childControlWidth = true;
        partLayout.childControlHeight = true;
        partLayout.childForceExpandWidth = true;
        partLayout.childForceExpandHeight = true;

        // A grid rather than a row: thirteen colours do not fit across a phone
        // at a tappable size, so they wrap onto a second line.
        var swatchRow = MakeRow(panelGO.transform, "SwatchGrid", 250f);
        var grid = swatchRow.gameObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(104f, 104f);
        grid.spacing = new Vector2(14f, 14f);
        grid.childAlignment = TextAnchor.UpperCenter;
        grid.constraint = GridLayoutGroup.Constraint.Flexible;

        var finishRow = MakeRow(panelGO.transform, "FinishRow", 84f);
        var finishLayout = finishRow.gameObject.AddComponent<HorizontalLayoutGroup>();
        finishLayout.spacing = 10f;
        finishLayout.childControlWidth = true;
        finishLayout.childControlHeight = true;
        finishLayout.childForceExpandWidth = true;
        finishLayout.childForceExpandHeight = true;

        var swatchSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Textures/Swatch.png");
        if (swatchSprite == null)
            Debug.LogWarning("Swatch sprite missing; colour chips will render as squares.");

        var panel = canvasGO.AddComponent<CustomizePanel>();
        panel.panel = panelGO;
        panel.partRow = partRow;
        panel.swatchRow = swatchRow;
        panel.finishRow = finishRow;
        panel.swatchSprite = swatchSprite;
        panel.tabSprite = tabSprite;

        panelGO.SetActive(false);
        return panel;
    }

    // Live telemetry: an INFO toggle top-right, a readout panel down the left,
    // and a toast pill that names each change as it happens.
    public static ARStatusOverlay BuildStatusOverlay(GameObject canvasGO, Sprite panelSprite, Sprite btnSprite,
        Font font, CarPlacementController placement)
    {
        var toggleGO = new GameObject("InfoToggle", typeof(RectTransform));
        toggleGO.transform.SetParent(canvasGO.transform, false);
        var toggleRT = toggleGO.GetComponent<RectTransform>();
        toggleRT.anchorMin = new Vector2(0.78f, 0.950f);
        toggleRT.anchorMax = new Vector2(0.96f, 0.988f);
        toggleRT.offsetMin = Vector2.zero;
        toggleRT.offsetMax = Vector2.zero;
        var toggleImg = toggleGO.AddComponent<Image>();
        toggleImg.sprite = btnSprite;
        toggleImg.type = Image.Type.Sliced;
        var toggleBtn = toggleGO.AddComponent<Button>();
        var toggleLabel = MakeText(toggleGO.transform, "Label", font, TextAnchor.MiddleCenter, 16, 30);
        toggleLabel.text = "INFO";
        toggleLabel.fontStyle = FontStyle.Bold;

        var panelGO = new GameObject("StatusPanel", typeof(RectTransform));
        panelGO.transform.SetParent(canvasGO.transform, false);
        var panelRT = panelGO.GetComponent<RectTransform>();
        panelRT.anchorMin = new Vector2(0.04f, 0.400f);
        panelRT.anchorMax = new Vector2(0.66f, 0.940f);
        panelRT.offsetMin = Vector2.zero;
        panelRT.offsetMax = Vector2.zero;
        var panelImg = panelGO.AddComponent<Image>();
        panelImg.sprite = panelSprite;
        panelImg.type = Image.Type.Sliced;
        panelImg.color = new Color(1f, 1f, 1f, 0.85f);
        panelImg.raycastTarget = false;

        var bodyText = MakeText(panelGO.transform, "Body", font, TextAnchor.UpperLeft, 14, 22);
        bodyText.supportRichText = true;
        // Fixed size, not best-fit: a readout that rescales as lines come and
        // go is unreadable while you are watching a value change.
        bodyText.resizeTextForBestFit = false;
        bodyText.fontSize = 20;
        bodyText.lineSpacing = 1.05f;
        var bodyRT = bodyText.GetComponent<RectTransform>();
        bodyRT.offsetMin = new Vector2(20f, 16f);
        bodyRT.offsetMax = new Vector2(-16f, -16f);

        var toastGO = new GameObject("ToastPanel", typeof(RectTransform));
        toastGO.transform.SetParent(canvasGO.transform, false);
        var toastRT = toastGO.GetComponent<RectTransform>();
        toastRT.anchorMin = new Vector2(0.20f, 0.800f);
        toastRT.anchorMax = new Vector2(0.80f, 0.855f);
        toastRT.offsetMin = Vector2.zero;
        toastRT.offsetMax = Vector2.zero;
        var toastImg = toastGO.AddComponent<Image>();
        toastImg.sprite = panelSprite;
        toastImg.type = Image.Type.Sliced;
        toastImg.raycastTarget = false;
        var toastText = MakeText(toastGO.transform, "ToastText", font, TextAnchor.MiddleCenter, 18, 34);
        toastText.fontStyle = FontStyle.Bold;

        var overlay = canvasGO.AddComponent<ARStatusOverlay>();
        overlay.panel = panelGO;
        overlay.body = bodyText;
        overlay.toastPanel = toastGO;
        overlay.toastText = toastText;
        overlay.toggleButton = toggleBtn;
        overlay.toggleLabel = toggleLabel;
        overlay.placement = placement;

        panelGO.SetActive(true);
        toastGO.SetActive(false);
        return overlay;
    }

    static Text MakeText(Transform parent, string name, Font font, TextAnchor anchor, int minSize, int maxSize)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var txt = go.AddComponent<Text>();
        txt.font = font;
        txt.alignment = anchor;
        txt.color = new Color(0.91f, 0.93f, 0.95f);
        txt.resizeTextForBestFit = true;
        txt.resizeTextMinSize = minSize;
        txt.resizeTextMaxSize = maxSize;
        txt.raycastTarget = false;
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(8f, 4f);
        rt.offsetMax = new Vector2(-8f, -4f);
        return txt;
    }

    static RectTransform MakeRow(Transform parent, string name, float height)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var element = go.AddComponent<LayoutElement>();
        element.preferredHeight = height;
        element.flexibleHeight = 0f;
        return go.GetComponent<RectTransform>();
    }
}
