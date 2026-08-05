using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class DevTools
{
    public static void OpenDiagnosticsLog()
    {
        string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Diagnostics", ARDiagnosticLog.FileName));
        if (!File.Exists(path))
        {
            Debug.LogWarning("[BuildYourRide] No diagnostics log yet at " + path +
                             ". Press Play once -- the log is written automatically.");
            return;
        }
        Debug.Log("[BuildYourRide] Diagnostics log: " + path);
        EditorUtility.RevealInFinder(path);
    }


    // Builds, size and viewing angle persist in PlayerPrefs, which means a
    // scale or colour set in an earlier session follows you into this one.
    // Handy when you want to check what a first-run user actually sees.
    public static void ResetSavedBuilds()
    {
        if (!EditorUtility.DisplayDialog(
                "Reset saved builds?",
                "Clears every stored car configuration, plus the saved size and viewing angle. " +
                "The next run starts factory-fresh.\n\nThis cannot be undone.",
                "Reset", "Cancel"))
        {
            return;
        }

        ConfigStore.ResetAll();
        Debug.Log("[BuildYourRide] Saved builds cleared. Next play session starts factory-fresh.");
    }

    public static void FixNormalMaps()
    {
        var folders = new[] {
            "Assets/Models/Sedan1936_Textures",
            "Assets/Models/Maserati_Textures",
            "Assets/Models/GenericCoupe_Textures",
            "Assets/Models/PorscheGT3RS_Textures",
            "Assets/Models/Batmobile_Textures",
            "Assets/Models/Spoiler_Textures"
        };

        int fixed_ = 0;
        foreach (var folder in folders)
        {
            if (!AssetDatabase.IsValidFolder(folder)) continue;
            foreach (var guid in AssetDatabase.FindAssets("t:Texture", new[] { folder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var lower = Path.GetFileName(path).ToLowerInvariant();
                if (!IsNormalMapName(lower)) continue;

                // "as", not a hard cast -- "t:Texture" can match an asset
                // whose importer isn't a plain TextureImporter, which would
                // throw InvalidCastException here otherwise (hit for real in
                // the newer OptimizeTexturesForMobile(), which scans the
                // same folders without FixNormalMaps()'s name filter).
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) continue;
                if (importer.textureType == TextureImporterType.NormalMap) continue;

                importer.textureType = TextureImporterType.NormalMap;
                importer.SaveAndReimport();
                fixed_++;
            }
        }

        AssetDatabase.Refresh();
        Debug.Log("[BuildYourRide] Marked " + fixed_ + " texture(s) as normal maps.");
    }

    static bool IsNormalMapName(string lower)
    {
        return lower.Contains("_no") || lower.Contains("_nm") ||
               lower.Contains("_normal") || lower.Contains("_norm") ||
               lower.Contains("normalmap") || lower.Contains("bump");
    }

    // Every car's texture set (Batmobile alone ships ~93 PBR PNGs) was
    // importing with whatever Unity's default Android settings happen to
    // be -- usually uncompressed or a generic fallback, not the ASTC format
    // mobile GPUs actually want. Uncompressed textures are the single
    // biggest, lowest-risk win available for this project: they inflate
    // both APK size and the GPU memory/bandwidth spent every frame just
    // sampling them, with zero code-path risk since this only touches
    // import settings, not runtime logic. ASTC 6x6 is a good quality/size
    // balance for PBR base colour/normal/roughness/metallic maps; capping
    // at 2048 only ever downscales an oversized source, never upscales one
    // that's already smaller.
    // On the menu in its own right: this is the durable home of the texture
    // import settings, but it is otherwise only reachable through
    // CarImporter.ImportAllNewCars() inside Rebuild All, which re-extracts
    // every roster FBX to get here. Re-applying import settings needs neither.
    [MenuItem("BuildYourRide/Optimize Textures For Mobile")]
    public static void OptimizeTexturesForMobile()
    {
        var folders = new[] {
            "Assets/Models/Sedan1936_Textures",
            "Assets/Models/Maserati_Textures",
            "Assets/Models/GenericCoupe_Textures",
            "Assets/Models/PorscheGT3RS_Textures",
            "Assets/Models/Batmobile_Textures",
            "Assets/Models/Spoiler_Textures"
        };

        const TextureImporterFormat format = TextureImporterFormat.ASTC_6x6;
        const int maxSize = 2048;

        int changed = 0, skipped = 0, failed = 0;
        foreach (var folder in folders)
        {
            if (!AssetDatabase.IsValidFolder(folder)) continue;
            foreach (var guid in AssetDatabase.FindAssets("t:Texture", new[] { folder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);

                // "t:Texture" can match assets whose importer isn't a plain
                // TextureImporter (e.g. a render texture or another
                // Texture-derived asset type) -- a hard cast there throws
                // InvalidCastException and, since this runs from inside
                // CarImporter.ImportAllNewCars(), that used to propagate all
                // the way up through CarRoster.RebuildAllCars() and abort
                // the rest of Rebuild All before the roster ever got saved
                // into the scene. "as" + a per-texture try/catch means one
                // odd asset can't take the whole rebuild down with it.
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) continue;

                try
                {
                    var platformSettings = importer.GetPlatformTextureSettings("Android");
                    bool formatOk = platformSettings.overridden && platformSettings.format == format &&
                                    platformSettings.maxTextureSize == maxSize;
                    // Streaming is checked alongside the format, not folded into
                    // the same flag, so a texture that was already ASTC-compressed
                    // by an earlier run still gets streaming turned on instead of
                    // being skipped as "already optimized".
                    bool streamingOk = importer.streamingMipmaps || !importer.mipmapEnabled;

                    if (formatOk && streamingOk)
                    {
                        skipped++;
                        continue;
                    }

                    platformSettings.overridden = true;
                    platformSettings.format = format;
                    platformSettings.maxTextureSize = maxSize;
                    platformSettings.textureCompression = TextureImporterCompression.Compressed;
                    importer.SetPlatformTextureSettings(platformSettings);

                    // Compression caps what each texture costs; streaming caps
                    // what the whole roster costs at once. All five cars are
                    // hard-referenced by the scene, so every one of their ~207
                    // textures is resident from launch whether or not that car is
                    // the one placed -- roughly 200 MB of ASTC that a mid-range
                    // phone does not have to spare. With streaming on, Unity
                    // loads only the mip levels the AR camera can actually
                    // resolve and honours the per-quality-level memory budget.
                    //
                    // Guarded on mipmapEnabled because streaming has nothing to
                    // stream without a mip chain (all 209 car textures have one).
                    if (importer.mipmapEnabled) importer.streamingMipmaps = true;

                    importer.SaveAndReimport();
                    changed++;
                }
                catch (System.Exception e)
                {
                    failed++;
                    Debug.LogWarning("[BuildYourRide] Could not optimize texture '" + path + "': " +
                        e.GetType().Name + ": " + e.Message);
                }
            }
        }

        if (failed > 0)
            Debug.LogWarning("[BuildYourRide] " + failed + " texture(s) could not be optimized; see warnings above. " +
                "The rest of Rebuild All still completed.");

        AssetDatabase.Refresh();
        Debug.Log("[BuildYourRide] Texture optimization: " + changed + " texture(s) set to ASTC_6x6 / max " +
            maxSize + "px + mipmap streaming for Android" +
            (skipped > 0 ? " (" + skipped + " already optimized)" : "") + ".");
    }

    public static void CleanRoster()
    {
        var scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
        string scenePath = "Assets/Scenes/Main.unity";
        if (scene.path != scenePath)
        {
            if (!File.Exists(scenePath))
            {
                Debug.LogError("Scene not found. Run Run Full Setup first.");
                return;
            }
            scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(scenePath,
                UnityEditor.SceneManagement.OpenSceneMode.Single);
        }

        var placement = Object.FindObjectOfType<CarPlacementController>();
        if (placement == null)
        {
            Debug.LogError("CarPlacementController not found.");
            return;
        }

        var realNames = new HashSet<string> {
            "PorscheGT3RS", "Maserati", "GenericCoupe", "Batmobile", "Sedan1936"
        };

        var cleaned = new List<GameObject>();
        int removed = 0;
        if (placement.carPrefabs != null)
        {
            foreach (var c in placement.carPrefabs)
            {
                if (c == null) { removed++; continue; }
                if (realNames.Contains(c.name))
                    cleaned.Add(c);
                else
                    removed++;
            }
        }

        placement.carPrefabs = cleaned.ToArray();
        CarRoster.SaveScene(placement);
        Debug.Log("[BuildYourRide] Roster cleaned: " + cleaned.Count + " cars kept, " + removed +
                  " removed. Roster: " + string.Join(", ", cleaned.ConvertAll(c => c.name)));
    }
}
