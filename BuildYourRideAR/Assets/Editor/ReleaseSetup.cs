using System.IO;
using UnityEditor;
using UnityEngine;

public static class ReleaseSetup
{
    const string IconPath = "Assets/Textures/AppIcon.png";
    const string ScenePath = "Assets/Scenes/Main.unity";
    const string KeystoreFile = "buildyourride.keystore";
    const string CredentialsFile = "keystore_credentials.txt";

    public static void ConfigureRelease()
    {
        PlayerSettings.bundleVersion = "1.0.0";
        PlayerSettings.Android.bundleVersionCode = 2;
        PlayerSettings.SetManagedStrippingLevel(BuildTargetGroup.Android, ManagedStrippingLevel.Low);
        PlayerSettings.SetIl2CppCompilerConfiguration(BuildTargetGroup.Android, Il2CppCompilerConfiguration.Release);

        ConfigureRendering();
        GenerateIcon();
        ApplyIcon();
        ConfigureSigning();
        AssetDatabase.SaveAssets();
        Debug.Log("Release configuration applied: v" + PlayerSettings.bundleVersion +
                  " (code " + PlayerSettings.Android.bundleVersionCode + ")");
    }

    public static void BuildAab()
    {
        if (!File.Exists(ScenePath))
        {
            Debug.LogError("Scene not found at " + ScenePath + ". Run BuildYourRide/Run Full Setup first.");
            return;
        }
        Directory.CreateDirectory("Builds");
        bool prevAppBundle = EditorUserBuildSettings.buildAppBundle;
        EditorUserBuildSettings.buildAppBundle = true;
        try
        {
            var opts = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = "Builds/BuildYourRideAR.aab",
                target = BuildTarget.Android,
                options = BuildOptions.None
            };
            var report = BuildPipeline.BuildPlayer(opts);
            Debug.Log("AAB build result: " + report.summary.result +
                      ", size: " + (report.summary.totalSize / (1024 * 1024)) + " MB");
        }
        finally
        {
            EditorUserBuildSettings.buildAppBundle = prevAppBundle;
        }
    }

    // Both of these live in ProjectSettings/QualitySettings assets, so they do
    // not get reset by a rebuild -- they are re-asserted here because both are
    // easy to flip back by hand in the Settings UI without noticing, and both
    // are invisible until you are looking at the app on a phone.
    static void ConfigureRendering()
    {
        // Gamma was wrong for this app specifically. Every car is PBR
        // (metallic/smoothness maps straight off the source FBXs) and the
        // lighting is driven at runtime by ARCore's Environmental HDR estimate.
        // Both assume light adds up linearly; in Gamma the shader maths happens
        // in sRGB, so metallic paint reads flat and the estimated light does not
        // fall off correctly across the body.
        //
        // Safe on this build config: Linear on Android needs GLES3 or Vulkan
        // and API 18+. The graphics API list is GLES3 then Vulkan with
        // auto-select off (no GLES2 fallback to break), and minSdk is 24.
        if (PlayerSettings.colorSpace != ColorSpace.Linear)
        {
            // Assignment triggers a full asset reimport, hence the guard.
            PlayerSettings.colorSpace = ColorSpace.Linear;
            Debug.Log("Colour space set to Linear (was Gamma). Unity will reimport assets.");
        }

        // The five roster cars are hard-referenced by the scene, so all ~207 of
        // their textures are resident from launch regardless of which car is
        // placed. Streaming is what keeps that bounded: only the mip levels the
        // camera can resolve are loaded, against the per-quality-level budget.
        if (!QualitySettings.streamingMipmapsActive)
        {
            QualitySettings.streamingMipmapsActive = true;
            Debug.LogWarning("Mipmap streaming was off for quality level '" +
                QualitySettings.names[QualitySettings.GetQualityLevel()] +
                "'. Enabled. Check the other levels in Project Settings > Quality -- " +
                "this property only ever applies to the active one.");
        }
    }

    static void ConfigureSigning()
    {
        string ksPath = Path.GetFullPath(KeystoreFile);
        string credPath = Path.GetFullPath(CredentialsFile);
        if (!File.Exists(ksPath) || !File.Exists(credPath))
        {
            Debug.LogWarning("Release keystore not found (" + KeystoreFile + " + " + CredentialsFile +
                             " in the project root). Builds will be debug-signed and cannot be " +
                             "uploaded to the Play Store.");
            return;
        }

        string storePass = null, keyPass = null, alias = null;
        foreach (var line in File.ReadAllLines(credPath))
        {
            var kv = line.Split(new[] { '=' }, 2);
            if (kv.Length != 2) continue;
            switch (kv[0].Trim())
            {
                case "storepass": storePass = kv[1].Trim(); break;
                case "keypass": keyPass = kv[1].Trim(); break;
                case "alias": alias = kv[1].Trim(); break;
            }
        }

        if (string.IsNullOrEmpty(storePass) || string.IsNullOrEmpty(alias))
        {
            Debug.LogWarning("Keystore credentials file is missing storepass/alias; " +
                             "builds will be debug-signed.");
            return;
        }

        PlayerSettings.Android.useCustomKeystore = true;
        PlayerSettings.Android.keystoreName = ksPath;
        PlayerSettings.Android.keystorePass = storePass;
        PlayerSettings.Android.keyaliasName = alias;
        PlayerSettings.Android.keyaliasPass = string.IsNullOrEmpty(keyPass) ? storePass : keyPass;
        Debug.Log("Release signing configured with keystore: " + ksPath);
    }

    static void ApplyIcon()
    {
        var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
        if (icon == null)
        {
            Debug.LogWarning("App icon missing at " + IconPath + "; default Unity icon will be used.");
            return;
        }
        PlayerSettings.SetIconsForTargetGroup(BuildTargetGroup.Unknown, new[] { icon });
    }

    static void GenerateIcon()
    {
        const int S = 512;
        var px = new Color[S * S];

        // Rounded-square background with a vertical gradient.
        const float bgRadius = 90f;
        var bgTop = new Color(0.137f, 0.149f, 0.180f);
        var bgBottom = new Color(0.070f, 0.078f, 0.098f);
        for (int y = 0; y < S; y++)
        {
            var bg = Color.Lerp(bgBottom, bgTop, y / (float)(S - 1));
            for (int x = 0; x < S; x++)
            {
                float dx = Mathf.Max(0f, bgRadius - Mathf.Min(x, S - 1 - x));
                float dy = Mathf.Max(0f, bgRadius - Mathf.Min(y, S - 1 - y));
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(bgRadius - d + 1f);
                px[y * S + x] = new Color(bg.r, bg.g, bg.b, a);
            }
        }

        var body = new Color(0.906f, 0.918f, 0.933f);
        var accent = new Color(0.290f, 0.435f, 0.882f);
        var tyre = new Color(0.180f, 0.190f, 0.210f);
        var hole = bgBottom;

        // Ground accent line, car silhouette, wheels.
        RoundedRect(px, S, 84f, 122f, 428f, 138f, 8f, accent);
        RoundedRect(px, S, 80f, 172f, 432f, 262f, 30f, body);
        RoundedRect(px, S, 158f, 240f, 354f, 322f, 42f, body);
        Circle(px, S, 168f, 172f, 54f, hole);
        Circle(px, S, 344f, 172f, 54f, hole);
        Circle(px, S, 168f, 172f, 38f, tyre);
        Circle(px, S, 344f, 172f, 38f, tyre);
        Circle(px, S, 168f, 172f, 15f, body);
        Circle(px, S, 344f, 172f, 15f, body);

        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        tex.SetPixels(px);
        tex.Apply();
        var png = tex.EncodeToPNG();
        Object.DestroyImmediate(tex);
        File.WriteAllBytes(IconPath, png);
        AssetDatabase.ImportAsset(IconPath);
        var importer = (TextureImporter)AssetImporter.GetAtPath(IconPath);
        if (importer != null)
        {
            importer.textureType = TextureImporterType.Default;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
        }
    }

    static void Blend(Color[] px, int i, Color c, float a)
    {
        var d = px[i];
        a = Mathf.Clamp01(a);
        px[i] = new Color(
            Mathf.Lerp(d.r, c.r, a),
            Mathf.Lerp(d.g, c.g, a),
            Mathf.Lerp(d.b, c.b, a),
            Mathf.Max(d.a, a));
    }

    static void Circle(Color[] px, int size, float cx, float cy, float r, Color c)
    {
        int x0 = Mathf.Max(0, (int)(cx - r) - 2), x1 = Mathf.Min(size - 1, (int)(cx + r) + 2);
        int y0 = Mathf.Max(0, (int)(cy - r) - 2), y1 = Mathf.Min(size - 1, (int)(cy + r) + 2);
        for (int y = y0; y <= y1; y++)
        for (int x = x0; x <= x1; x++)
        {
            float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
            float a = Mathf.Clamp01(r - d + 0.5f);
            if (a > 0f) Blend(px, y * size + x, c, a);
        }
    }

    static void RoundedRect(Color[] px, int size, float xMin, float yMin, float xMax, float yMax, float r, Color c)
    {
        float cx = (xMin + xMax) * 0.5f, cy = (yMin + yMax) * 0.5f;
        float hw = (xMax - xMin) * 0.5f - r, hh = (yMax - yMin) * 0.5f - r;
        int x0 = Mathf.Max(0, (int)xMin - 2), x1 = Mathf.Min(size - 1, (int)xMax + 2);
        int y0 = Mathf.Max(0, (int)yMin - 2), y1 = Mathf.Min(size - 1, (int)yMax + 2);
        for (int y = y0; y <= y1; y++)
        for (int x = x0; x <= x1; x++)
        {
            float qx = Mathf.Max(Mathf.Abs(x - cx) - hw, 0f);
            float qy = Mathf.Max(Mathf.Abs(y - cy) - hh, 0f);
            float d = Mathf.Sqrt(qx * qx + qy * qy) - r;
            float a = Mathf.Clamp01(0.5f - d);
            if (a > 0f) Blend(px, y * size + x, c, a);
        }
    }
}
