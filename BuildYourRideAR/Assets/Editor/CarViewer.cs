using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class CarViewer : EditorWindow
{
    public static void Open()
    {
        var w = GetWindow<CarViewer>("Car Viewer");
        w.minSize = new Vector2(400, 500);
        w.Show();
    }

    GameObject previewRoot;
    Vector2 scroll;
    float rotation;
    bool autoRotate = true;
    double lastUpdateTime;
    readonly List<GameObject> carPrefabs = new List<GameObject>();

    void OnEnable()
    {
        rotation = 0f;
        autoRotate = true;
        lastUpdateTime = EditorApplication.timeSinceStartup;
        RefreshPrefabList();
        if (previewRoot == null)
        {
            previewRoot = new GameObject("CarViewer_Root");
            previewRoot.hideFlags = HideFlags.HideAndDontSave;
            // Keep the preview rig far from the origin so the camera does not
            // render whatever is in the open scene.
            previewRoot.transform.position = new Vector3(0f, -500f, 0f);
            var camGO = new GameObject("PreviewCam");
            camGO.transform.SetParent(previewRoot.transform);
            camGO.transform.localPosition = new Vector3(0, 1.5f, -5);
            camGO.transform.localRotation = Quaternion.Euler(15, 180, 0);
            var cam = camGO.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.12f, 0.12f, 0.13f);
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 20f;
            cam.fieldOfView = 45;
            camGO.AddComponent<PreviewRotator>();
        }
        EditorApplication.update += OnEditorUpdate;
    }

    void OnDisable()
    {
        EditorApplication.update -= OnEditorUpdate;
        if (previewRoot != null) DestroyImmediate(previewRoot);
    }

    void OnEditorUpdate()
    {
        // Time.deltaTime is 0 outside play mode; derive the delta ourselves.
        double now = EditorApplication.timeSinceStartup;
        float dt = (float)(now - lastUpdateTime);
        lastUpdateTime = now;

        if (autoRotate && previewRoot != null)
        {
            var car = FindCar();
            if (car != null)
            {
                rotation += dt * 30f;
                car.transform.localRotation = Quaternion.Euler(0, rotation, 0);
                Repaint();
            }
        }
    }

    void RefreshPrefabList()
    {
        carPrefabs.Clear();
        var guids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs" });
        foreach (var g in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(g);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab != null && prefab.GetComponentInChildren<CarCustomizer>() != null)
                carPrefabs.Add(prefab);
        }
    }

    GameObject FindCar()
    {
        if (previewRoot == null) return null;
        var t = previewRoot.transform.Find("CarPreview");
        return t != null ? t.gameObject : null;
    }

    void OnGUI()
    {
        carPrefabs.RemoveAll(p => p == null);

        if (carPrefabs.Count == 0)
        {
            EditorGUILayout.HelpBox("No car prefabs found in Assets/Prefabs/", MessageType.Info);
            if (GUILayout.Button("Refresh", GUILayout.Width(100)))
                RefreshPrefabList();
            return;
        }

        GUILayout.Space(10);

        EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        autoRotate = GUILayout.Toggle(autoRotate, "Auto-Rotate", GUI.skin.button, GUILayout.Width(120));
        if (GUILayout.Button("Refresh", GUILayout.Width(80)))
            RefreshPrefabList();
        if (GUILayout.Button("Reset View", GUILayout.Width(100)))
        {
            rotation = 0f;
            var car = FindCar();
            if (car != null) car.transform.localRotation = Quaternion.identity;
        }
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();

        GUILayout.Space(5);

        EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        var previewRect = GUILayoutUtility.GetRect(380, 380, 380, 380);
        GUI.Box(previewRect, "", "frame");
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();

        if (Event.current.type == EventType.Repaint)
        {
            var cam = previewRoot != null ? previewRoot.GetComponentInChildren<Camera>() : null;
            if (cam != null)
            {
                var car = FindCar();
                if (car != null)
                {
                    var rt = RenderTexture.GetTemporary(380, 380, 24);
                    cam.targetTexture = rt;
                    cam.Render();
                    GUI.DrawTexture(previewRect, rt);
                    cam.targetTexture = null;
                    RenderTexture.ReleaseTemporary(rt);
                }
            }
        }

        if (previewRect.Contains(Event.current.mousePosition))
        {
            if (Event.current.type == EventType.MouseDrag && !autoRotate)
            {
                rotation += Event.current.delta.x * 0.5f;
                var car = FindCar();
                if (car != null)
                    car.transform.localRotation = Quaternion.Euler(0, rotation, 0);
                Repaint();
                Event.current.Use();
            }
            if (Event.current.type == EventType.ScrollWheel)
            {
                var car = FindCar();
                if (car != null)
                {
                    float scale = Mathf.Clamp(car.transform.localScale.x - Event.current.delta.y * 0.03f, 0.3f, 3f);
                    car.transform.localScale = Vector3.one * scale;
                }
                Repaint();
                Event.current.Use();
            }
        }

        GUILayout.Space(10);

        scroll = EditorGUILayout.BeginScrollView(scroll);
        foreach (var prefab in carPrefabs)
        {
            EditorGUILayout.BeginHorizontal("box");
            var icon = AssetPreview.GetAssetPreview(prefab);
            if (icon != null)
                GUILayout.Label(icon, GUILayout.Width(64), GUILayout.Height(64));
            else
                GUILayout.Label("", GUILayout.Width(64), GUILayout.Height(64));

            EditorGUILayout.BeginVertical();
            GUILayout.Label(prefab.name, EditorStyles.boldLabel);
            GUILayout.Space(4);
            if (GUILayout.Button("Preview", GUILayout.Width(80), GUILayout.Height(24)))
                LoadCar(prefab);
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(4);
        }
        EditorGUILayout.EndScrollView();
    }

    void LoadCar(GameObject prefab)
    {
        if (prefab == null) return;
        var existing = FindCar();
        if (existing != null) DestroyImmediate(existing);

        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, previewRoot.transform);
        if (go == null) return;
        go.name = "CarPreview";

        go.transform.localRotation = Quaternion.identity;
        var bounds = ComputeBounds(go);
        float maxDim = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
        if (maxDim > 0.01f)
        {
            float target = 2.5f;
            go.transform.localScale = Vector3.one * (target / maxDim);
        }
        // Recompute after scaling and center on the (possibly offset) root.
        bounds = ComputeBounds(go);
        go.transform.position += previewRoot.transform.position - bounds.center;
        rotation = 0f;

        var lights = previewRoot.GetComponentsInChildren<Light>();
        if (lights.Length == 0)
        {
            var lightGO = new GameObject("PreviewLight");
            lightGO.transform.SetParent(previewRoot.transform);
            lightGO.transform.localPosition = new Vector3(3, 4, -2);
            var light = lightGO.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            light.shadows = LightShadows.None;

            var fill = new GameObject("FillLight");
            fill.transform.SetParent(previewRoot.transform);
            fill.transform.localPosition = new Vector3(-2, 1, -3);
            var fillLight = fill.AddComponent<Light>();
            fillLight.type = LightType.Directional;
            fillLight.intensity = 0.4f;
            fillLight.color = new Color(0.6f, 0.7f, 1f);
        }

        Repaint();
    }

    Bounds ComputeBounds(GameObject go)
    {
        var renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return new Bounds(Vector3.zero, Vector3.one);
        var b = renderers[0].bounds;
        foreach (var r in renderers)
            b.Encapsulate(r.bounds);
        return b;
    }

    class PreviewRotator : MonoBehaviour { }
}
