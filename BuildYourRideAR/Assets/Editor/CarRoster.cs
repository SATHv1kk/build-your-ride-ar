using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class CarRoster
{
    const string ScenePath = "Assets/Scenes/Main.unity";

    // Not on the menu -- called automatically by ProjectSetup.BuildApk()
    // before every build, so there's no separate rebuild step to remember.
    public static void RebuildAllCars()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogError("[BuildYourRide] Stop Play mode before rebuilding the roster -- " +
                            "scene editing isn't allowed while playing.");
            return;
        }

        ProjectSetup.CreateMaterials();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // Must run before car import: CarImporter.Import() auto-assigns
        // whatever sits at Assets/Prefabs/Spoiler.prefab to every car.
        SpoilerImporter.Import();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        CarImporter.ImportAllNewCars();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        if (!OpenScene()) return;

        var placement = Object.FindObjectOfType<CarPlacementController>();
        if (placement == null)
        {
            Debug.LogError("CarPlacementController not found.");
            return;
        }

        var roster = new List<GameObject>();
        AddIfPresent(roster, "Assets/Prefabs/PorscheGT3RS.prefab");
        AddIfPresent(roster, "Assets/Prefabs/Maserati.prefab");
        AddIfPresent(roster, "Assets/Prefabs/GenericCoupe.prefab");
        AddIfPresent(roster, "Assets/Prefabs/Batmobile.prefab");
        AddIfPresent(roster, "Assets/Prefabs/Sedan1936.prefab");

        if (roster.Count == 0)
        {
            Debug.LogError("No car prefabs were produced; leaving roster untouched.");
            return;
        }

        placement.carPrefabs = roster.ToArray();
        SaveScene(placement);

        Debug.LogWarning("[BuildYourRide] Clearing all saved car configurations (PlayerPrefs.DeleteAll) " +
                          "because the roster was just rebuilt -- old saved builds pointed at car keys " +
                          "that may no longer exist. This wipes every locally saved colour/scale/yaw in " +
                          "this Editor, not just this roster's.");
        ConfigStore.ResetAll();

        var names = new List<string>();
        foreach (var c in roster) names.Add(c.name);
        Debug.Log("=== Roster rebuilt: " + string.Join(", ", names.ToArray()) + " ===");

        ReportGroups(roster);
    }

    static void AddIfPresent(List<GameObject> list, string path)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab != null) list.Add(prefab);
    }

    static void ReportGroups(List<GameObject> roster)
    {
        foreach (var prefab in roster)
        {
            var customizer = prefab.GetComponentInChildren<CarCustomizer>();
            if (customizer == null)
            {
                Debug.LogWarning(prefab.name + ": no CarCustomizer.");
                continue;
            }

            var groups = customizer.partGroups;
            if (groups == null || groups.Length == 0)
            {
                Debug.LogWarning(prefab.name + ": no part groups matched, so its colour tray will be empty.");
                continue;
            }

            var sb = new System.Text.StringBuilder(prefab.name + " parts:");
            foreach (var g in groups)
            {
                string first = g.options != null && g.options.Length > 0 && g.options[0] != null
                    ? g.options[0].name
                    : "none";
                sb.Append("\n  ").Append(g.displayName)
                  .Append("  slots=").Append(g.targets != null ? g.targets.Length : 0)
                  .Append("  options=").Append(g.options != null ? g.options.Length : 0)
                  .Append("  default=").Append(first);
            }
            Debug.Log(sb.ToString());
        }
    }

    public static bool OpenScene()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path == ScenePath) return true;

        if (!System.IO.File.Exists(ScenePath))
        {
            Debug.LogError("Scene not found. Run Run Full Setup first.");
            return false;
        }
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        return true;
    }

    public static void SaveScene(Object dirtyTarget)
    {
        if (dirtyTarget != null) EditorUtility.SetDirty(dirtyTarget);
        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
    }
}
