using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;

// Adds the v3 features to an existing Main.unity in place.
//
// "Run Full Setup" also does this, but it rebuilds every car prefab from
// scratch and re-extracts textures from all six roster FBXs, which is slow
// and resets the car list. This does only what is missing, and is safe to
// run repeatedly: every step checks before it acts.
public static class SceneUpgrade
{
    const string ScenePath = "Assets/Scenes/Main.unity";
    const string PlanePrefabPath = "Assets/Prefabs/ARPlaneViz.prefab";

    // Also called automatically by ProjectSetup.BuildApk() and RebuildAll()
    // before every build, so there's no separate step to remember -- but it is
    // on the menu in its own right because "Rebuild All" drags a full roster
    // re-import and FBX texture extraction along with it. Repairing scene
    // wiring is seconds of work and should not cost minutes.
    [MenuItem("BuildYourRide/Upgrade Scene")]
    public static void Upgrade()
    {
        // EditorSceneManager.OpenScene and prefab/asset edits below throw
        // InvalidOperationException mid-Play (scene state is owned by the
        // running game there). Fail with a clear message instead.
        if (EditorApplication.isPlaying)
        {
            Debug.LogError("[BuildYourRide] Stop Play mode before running Upgrade Scene to v3 -- " +
                            "scene editing isn't allowed while playing.");
            return;
        }

        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != ScenePath)
        {
            if (!System.IO.File.Exists(ScenePath))
            {
                Debug.LogError("Scene not found at " + ScenePath + ". Run BuildYourRide/Run Full Setup first.");
                return;
            }
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        int changes = 0;

        // Assets first: the scene wiring below references these.
        ProjectSetup.CreatePlaneMaterial();
        ProjectSetup.CreateShadowMaterial();
        ProjectSetup.CreateSwatchSprite();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        ProjectSetup.EnsureShadersAlwaysIncluded();
        Debug.Log("Plane material re-pointed at BuildYourRide/ARPlaneGlow.");
        changes++;

        changes += UpgradePlanePrefab();
        changes += UpgradeCamera();
        // Before UpgradeUI: it wires UIController.gestureController by
        // FindObjectOfType, which only finds anything once this has run.
        changes += UpgradeGestures();
        changes += UpgradeShadowCatcher();
        changes += UpgradeUI();
        changes += UpgradeEditorPreview();
        WarnAboutStalePrefabs();

        // Set directly rather than floor-only: a too-low value would clip
        // the contact shadow, but a too-high one (the live project's actual
        // shadowDistance was 150 -- a Unity quality-preset default nobody
        // had ever lowered) burns cascade rendering on 6x more range than
        // the scene ever uses.
        if (Mathf.Abs(QualitySettings.shadowDistance - 30f) > 0.01f)
        {
            QualitySettings.shadowDistance = 30f;
            Debug.Log("Set shadow distance to 30 m -- enough for the contact shadow, without wasting cascade range on an AR scene that's only ever a few metres deep.");
            changes++;
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("=== Scene upgrade complete: " + changes + " change(s). Press Play to try it. ===");
    }

    static int UpgradePlanePrefab()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlanePrefabPath);
        if (prefab == null)
        {
            Debug.LogWarning("Plane prefab missing at " + PlanePrefabPath + "; skipping plane upgrade.");
            return 0;
        }

        var root = PrefabUtility.LoadPrefabContents(PlanePrefabPath);
        int changes = 0;

        if (root.GetComponent<ARPlaneFeather>() == null)
        {
            root.AddComponent<ARPlaneFeather>();
            Debug.Log("Added ARPlaneFeather to the plane prefab (writes edge distance into UV1).");
            changes++;
        }

        var mr = root.GetComponent<MeshRenderer>();
        if (mr != null)
        {
            var planeMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/PlaneViz.mat");
            if (planeMat != null && mr.sharedMaterial != planeMat)
            {
                mr.sharedMaterial = planeMat;
                changes++;
            }
            mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        }

        PrefabUtility.SaveAsPrefabAsset(root, PlanePrefabPath);
        PrefabUtility.UnloadPrefabContents(root);
        return changes;
    }

    static int UpgradeCamera()
    {
        var cameraManager = Object.FindObjectOfType<ARCameraManager>();
        if (cameraManager == null)
        {
            Debug.LogWarning("No ARCameraManager in the scene; skipping occlusion and light estimation.");
            return 0;
        }

        var go = cameraManager.gameObject;
        int changes = 0;

        // ARCameraBackground reads the occlusion manager off its own
        // GameObject, so this has to live on the camera.
        if (go.GetComponent<AROcclusionManager>() == null)
        {
            var occlusion = go.AddComponent<AROcclusionManager>();
            occlusion.requestedEnvironmentDepthMode =
                UnityEngine.XR.ARSubsystems.EnvironmentDepthMode.Medium;
            occlusion.requestedOcclusionPreferenceMode =
                UnityEngine.XR.ARSubsystems.OcclusionPreferenceMode.PreferEnvironmentOcclusion;
            Debug.Log("Added AROcclusionManager to the AR Camera.");
            changes++;
        }

        if (go.GetComponent<ARLightEstimator>() == null)
        {
            var estimator = go.AddComponent<ARLightEstimator>();
            estimator.mainLight = FindDirectionalLight();
            Debug.Log("Added ARLightEstimator to the AR Camera.");
            changes++;
        }

        // 60m was never reachable in an app that places one car a few
        // metres away and lets the user walk around it -- pure wasted
        // culling/rendering range. ProjectSetup.BuildScene() only sets this
        // on a from-scratch scene build, not on the regular Rebuild All
        // path, so the live scene's camera needed the same fix applied
        // directly.
        var cam = go.GetComponent<Camera>();
        if (cam != null && Mathf.Abs(cam.farClipPlane - 30f) > 0.01f)
        {
            float old = cam.farClipPlane;
            cam.farClipPlane = 30f;
            EditorUtility.SetDirty(cam);
            Debug.Log("AR Camera far clip plane set to 30 m (was " + old + ").");
            changes++;
        }

        return changes;
    }

    // The one that actually mattered. v4.2 deleted and recreated
    // CarGestureController.cs, which gave it a new .meta GUID; the scene kept
    // referencing the old one, so XR Origin has carried a "Missing (Mono
    // Script)" corpse ever since -- a MonoBehaviour with a dead guid and no
    // serialized fields. Every gesture on device (one-finger drag to move,
    // two-finger twist to rotate, pinch to scale) has been silently dead since
    // then, and UIController's GetComponent<CarGestureController>() fallback
    // could never find it either, because a missing-script component is not a
    // CarGestureController.
    //
    // §376 of PROJECT_CONTEXT claims "Rebuild All runs SceneUpgrade.Upgrade()
    // which re-adds missing components" -- it never did. UpgradeUI() only ever
    // *wired* the reference via FindObjectOfType, which returns null forever
    // when the component itself is absent, and the null guard around it meant
    // the failure was swallowed without so much as a warning. This adds the
    // missing half: strip the dead component, then create a real one.
    static int UpgradeGestures()
    {
        var placement = Object.FindObjectOfType<CarPlacementController>();
        if (placement == null)
        {
            Debug.LogWarning("No CarPlacementController in the scene; skipping gesture upgrade.");
            return 0;
        }

        int changes = 0;

        // Sweep the whole scene, not just XR Origin: a dead script reference
        // anywhere is the same class of silent failure, and they are only ever
        // visible as a yellow "Missing (Mono Script)" row in the Inspector.
        foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                int removed = GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
                if (removed <= 0) continue;
                Debug.Log("Removed " + removed + " component(s) with a missing script from '" +
                          t.name + "'.");
                changes += removed;
            }
        }

        var gestures = Object.FindObjectOfType<CarGestureController>();
        if (gestures == null)
        {
            gestures = placement.gameObject.AddComponent<CarGestureController>();
            Debug.Log("Added CarGestureController -- drag to move, twist to rotate and pinch to scale " +
                      "work on device again.");
            changes++;
        }

        if (gestures.placement == null)
        {
            gestures.placement = placement;
            EditorUtility.SetDirty(gestures);
            changes++;
        }

        return changes;
    }

    static int UpgradeShadowCatcher()
    {
        if (Object.FindObjectOfType<ShadowCatcher>() != null) return 0;

        var placement = Object.FindObjectOfType<CarPlacementController>();
        if (placement == null)
        {
            Debug.LogWarning("No CarPlacementController in the scene; skipping shadow catcher.");
            return 0;
        }

        var go = new GameObject("Shadow Catcher");
        var shadow = go.AddComponent<ShadowCatcher>();
        shadow.placement = placement;
        shadow.shadowMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/ShadowCatcher.mat");
        if (shadow.shadowMaterial == null)
            Debug.LogWarning("ShadowCatcher.mat missing; the car will render without a contact shadow.");
        Debug.Log("Added Shadow Catcher to the scene.");
        return 1;
    }

    static int UpgradeUI()
    {
        var canvas = Object.FindObjectOfType<Canvas>();
        if (canvas == null)
        {
            Debug.LogWarning("No Canvas in the scene; skipping UI upgrade.");
            return 0;
        }

        var canvasGO = canvas.gameObject;
        var panelSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Textures/GlassPanel.png");
        var btnSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Textures/GlassBtn.png");
        if (panelSprite == null || btnSprite == null)
        {
            Debug.LogWarning("UI sprites missing from Assets/Textures; run Run Full Setup to regenerate them.");
            return 0;
        }

        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        var placement = Object.FindObjectOfType<CarPlacementController>();
        var ui = Object.FindObjectOfType<UIController>();
        int changes = 0;

        var customizePanel = canvasGO.GetComponent<CustomizePanel>();
        if (customizePanel == null)
        {
            customizePanel = ProjectSetup.BuildCustomizePanel(canvasGO, panelSprite, btnSprite, font);
            Debug.Log("Added the colour tray (part tabs, swatch grid, finish selector).");
            changes++;
        }

        if (canvasGO.GetComponent<ARStatusOverlay>() == null)
        {
            ProjectSetup.BuildStatusOverlay(canvasGO, panelSprite, btnSprite, font, placement);
            Debug.Log("Added the live status overlay and the INFO toggle.");
            changes++;
        }

        // The scene's UIController predates the customizePanel field, so it
        // deserialized as null even though the component now declares it.
        if (ui != null && ui.customizePanel == null && customizePanel != null)
        {
            ui.customizePanel = customizePanel;
            EditorUtility.SetDirty(ui);
            Debug.Log("Wired UIController.customizePanel -> COLOR button now opens the tray.");
            changes++;
        }

        if (ui != null && ui.gestureController == null)
        {
            ui.gestureController = Object.FindObjectOfType<CarGestureController>();
            if (ui.gestureController != null)
            {
                EditorUtility.SetDirty(ui);
                Debug.Log("Wired UIController.gestureController -> ROTATE button works.");
                changes++;
            }
        }

        // No ROTATE button by design: the auto-rotate showcase is reachable
        // via CarGestureController (Left/Right arrows in the editor
        // simulation, twist gesture on device) without a dedicated button
        // cluttering the bottom bar. A ROTATE button was briefly generated
        // by an earlier version of BuildUI() before that decision, though,
        // and this scene was rebuilt while that code was still in place --
        // reverting the generator code doesn't retroactively remove
        // something already baked into the live scene, so it has to be torn
        // down explicitly here.
        if (ui != null && ui.rotateButton != null)
        {
            Object.DestroyImmediate(ui.rotateButton.gameObject);
            ui.rotateButton = null;
            EditorUtility.SetDirty(ui);
            Debug.Log("Removed the leftover ROTATE button from the bottom bar.");
            changes++;
        }

        // v4.6 relabelled WHEELS -> RIMS; v4.15 reverts that per request,
        // keeping rim colour reachable only through this one button (no
        // longer duplicated in the colour tray -- see CustomizePanel).
        // SceneUpgrade never touches existing button labels on its own, so
        // this has to run explicitly in both directions depending on
        // whatever the live scene currently says.
        if (ui != null && ui.wheelsButton != null)
        {
            var label = ui.wheelsButton.GetComponentInChildren<Text>();
            if (label != null && label.text != "WHEELS")
            {
                label.text = "WHEELS";
                EditorUtility.SetDirty(label);
                Debug.Log("Relabelled the wheels button to WHEELS.");
                changes++;
            }
        }

        return changes;
    }

    // This command wires up the scene but deliberately does not rebuild car
    // prefabs. Redesigning CarCustomizer around part groups removed the old
    // paintRenderers/paintMaterials fields, and Unity drops serialized data for
    // fields that no longer exist -- so every car prefab authored before that
    // change now has zero part groups and an empty colour tray. Nothing in the
    // console says so, hence this check.
    static void WarnAboutStalePrefabs()
    {
        var stale = new System.Collections.Generic.List<string>();
        foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs" }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) continue;

            var customizer = prefab.GetComponentInChildren<CarCustomizer>();
            if (customizer == null) continue;
            if (customizer.partGroups == null || customizer.partGroups.Length == 0)
                stale.Add(prefab.name);
        }

        if (stale.Count == 0) return;

        Debug.LogWarning("[BuildYourRide] " + stale.Count +
            " car prefab(s) have no part groups, so the colour tray will be empty for them: " +
            string.Join(", ", stale.ToArray()) +
            "\nThese predate the part-group redesign. Scene wiring cannot fix this -- the prefabs " +
            "themselves must be regenerated via CarRoster.RebuildAllCars() (Sedan1936 / Maserati / " +
            "GenericCoupe) or, for the placeholder Coupe/SUV/Pickup, ProjectSetup.AddPlaceholderCars().");
    }

    // Play mode has no AR, so without this the plane look can only be judged on
    // a phone. Giving EditorSimulation the material lets it spawn a stand-in.
    static int UpgradeEditorPreview()
    {
        var sim = Object.FindObjectOfType<EditorSimulation>();
        if (sim == null || sim.planePreviewMaterial != null) return 0;

        sim.planePreviewMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/PlaneViz.mat");
        if (sim.planePreviewMaterial == null) return 0;

        EditorUtility.SetDirty(sim);
        Debug.Log("Editor play mode will now preview the detected-plane look.");
        return 1;
    }

    static Light FindDirectionalLight()
    {
        foreach (var light in Object.FindObjectsOfType<Light>())
        {
            if (light.type == LightType.Directional) return light;
        }
        return null;
    }
}
