using System.IO;
using UnityEditor;
using UnityEngine;

// Replaces the old procedurally-boxed Spoiler.prefab (three primitive cubes)
// with a real modeled spoiler. CarImporter.Import() already auto-assigns
// whatever sits at Assets/Prefabs/Spoiler.prefab to every car's
// CarCustomizer.spoilerPrefab, so overwriting that one prefab upgrades every
// car's spoiler with no per-car changes needed.
public static class SpoilerImporter
{
    const string FbxPath = "Assets/Models/Spoiler_Universal.fbx";
    const string TextureFolder = "Assets/Models/Spoiler_Textures";
    const string PrefabPath = "Assets/Prefabs/Spoiler.prefab";

    // Matches the width of the old procedural wing (Vector3(1.50f, ...)) so
    // CarCustomizer.CalcSpoilerPosition() -- unchanged -- still places it
    // sensibly across the roster.
    const float TargetWidth = 1.4f;

    public static void Import()
    {
        if (AssetImporter.GetAtPath(FbxPath) == null)
        {
            Debug.LogWarning("[SpoilerImporter] FBX not found at " + FbxPath + "; keeping existing Spoiler.prefab.");
            return;
        }

        AssetDatabase.ImportAsset(FbxPath, ImportAssetOptions.ForceUpdate);
        var importer = (ModelImporter)AssetImporter.GetAtPath(FbxPath);
        if (importer == null)
        {
            Debug.LogError("[SpoilerImporter] FBX importer not available for " + FbxPath);
            return;
        }

        if (!AssetDatabase.IsValidFolder(TextureFolder))
            AssetDatabase.CreateFolder("Assets/Models", "Spoiler_Textures");
        importer.ExtractTextures(TextureFolder);
        importer.isReadable = false;
        importer.bakeAxisConversion = true;
        importer.globalScale = 1f;
        importer.useFileScale = true;
        importer.SaveAndReimport();

        var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
        if (fbx == null)
        {
            Debug.LogError("[SpoilerImporter] Failed to load FBX at " + FbxPath);
            return;
        }

        var root = new GameObject("Spoiler");
        var model = (GameObject)Object.Instantiate(fbx, root.transform);
        model.name = "Model";
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.identity;

        // Same Z-up heuristic as CarImporter: a spoiler modeled standing on
        // its mounting edge will have Y extent much larger than X or Z.
        var preBounds = ComputeBounds(root);
        float maxHorizontal = Mathf.Max(preBounds.size.x, preBounds.size.z);
        if (preBounds.size.y > maxHorizontal * 1.25f)
        {
            // +90, not -90 -- see CarImporter.cs's identical heuristic, which
            // had the sign backwards (confirmed upside-down on GenericCoupe).
            model.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Debug.Log("[SpoilerImporter] Detected Z-up orientation, corrected.");
        }

        // Scale so the wing spans TargetWidth, matching the old placeholder.
        var bounds = ComputeBounds(root);
        if (bounds.size.x > 0.01f)
        {
            float s = TargetWidth / bounds.size.x;
            model.transform.localScale *= s;
        }

        // Recenter so the prefab's own pivot sits at the bottom-centre of its
        // bounds -- CarCustomizer.ApplySpoiler() gives the instance identity
        // local rotation and places this pivot at the car's rear-top, so the
        // mesh must hang correctly from that point.
        bounds = ComputeBounds(root);
        model.transform.localPosition = new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z);

        var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);

        if (prefab == null)
        {
            Debug.LogError("[SpoilerImporter] Failed to save prefab at " + PrefabPath);
            return;
        }

        Debug.Log("[SpoilerImporter] Spoiler.prefab replaced with the modeled carbon-fibre spoiler.");
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
