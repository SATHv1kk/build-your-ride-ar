using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.XR.ARFoundation;

// Writes a detailed run log to disk so problems can be diagnosed after the
// fact, from outside Unity, without having to reproduce them live.
//
// It self-attaches via RuntimeInitializeOnLoadMethod rather than being placed
// in the scene, so it works on any scene version including one that predates
// it. Nothing else needs wiring.
//
//   Editor: <project>/Diagnostics/latest.log
//   Device: <persistentDataPath>/Diagnostics/latest.log
//
// Captures: environment, scene inventory, every ground/collider size, every
// click with its full raycast result, placement geometry, an explicit
// visibility verdict (frustum test + per-renderer isVisible), periodic camera
// and car snapshots, customization changes, and every Unity console message.
public class ARDiagnosticLog : MonoBehaviour
{
    public const string FileName = "latest.log";

    static ARDiagnosticLog instance;

    // Shared entry points so other instruments (AnchorDriftTracker) write into
    // the same file rather than starting a competing log.
    public static void Write(string text) { if (instance != null) instance.Line(text); }
    public static void WriteRaw(string text) { if (instance != null) instance.Raw(text); }
    public static void WriteSection(string title) { if (instance != null) instance.Section(title); }
    public static void FlushNow() { if (instance != null) instance.Flush(); }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoAttach()
    {
        if (FindObjectOfType<ARDiagnosticLog>() != null) return;
        var go = new GameObject("~ARDiagnosticLog");
        DontDestroyOnLoad(go);
        go.AddComponent<ARDiagnosticLog>();
    }

    public static string LogPath
    {
        get { return Path.Combine(Directory, FileName); }
    }

    public static string Directory
    {
        get
        {
            return Application.isEditor
                ? Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Diagnostics"))
                : Path.Combine(Application.persistentDataPath, "Diagnostics");
        }
    }

    [Tooltip("Seconds between periodic state snapshots. 0 disables them.")]
    public float snapshotInterval = 2f;

    readonly StringBuilder buffer = new StringBuilder(8192);
    readonly StringBuilder snapshotBuffer = new StringBuilder(512);
    readonly CultureInfo ci = CultureInfo.InvariantCulture;

    CarPlacementController placement;
    Camera cam;
    CarCustomizer trackedCustomizer;
    float nextSnapshot;
    float nextFlush;
    int clickCount;
    bool failed;

    // Snapshot state, cached rather than re-derived every two seconds.
    // FindObjectOfType walks the whole scene; GetComponentsInChildren walks 209
    // renderers on the Maserati and allocates an array to do it; and
    // CalculateFrustumPlanes allocates a fresh Plane[6] on every call. None of
    // that changes between snapshots, and a diagnostic has no business being a
    // measurable part of what it is measuring.
    ARPlaneManager cachedPlaneManager;
    GameObject snapshotCar;
    Renderer[] snapshotRenderers;
    readonly Plane[] frustumPlanes = new Plane[6];

    void Awake()
    {
        instance = this;
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            File.WriteAllText(LogPath, string.Empty);
        }
        catch (System.Exception e)
        {
            failed = true;
            Debug.LogWarning("[Diagnostics] Could not open log file: " + e.Message);
            return;
        }

        Application.logMessageReceived += OnUnityLog;
        WriteEnvironment();
        Debug.Log("[Diagnostics] Logging to " + LogPath);
    }

    void Start()
    {
        placement = FindObjectOfType<CarPlacementController>();
        cam = Camera.main;

        if (placement != null)
        {
            placement.CarPlaced += OnCarPlaced;
            placement.CarSwapped += OnCarSwapped;
            placement.CarRemoved += OnCarRemoved;
        }

        // One frame in, so EditorSimulation has built its ground and stripped
        // the AR stack before we take inventory.
        Invoke(nameof(WriteSceneInventory), 0.5f);
    }

    void OnDestroy()
    {
        if (failed) return;
        Application.logMessageReceived -= OnUnityLog;
        if (placement != null)
        {
            placement.CarPlaced -= OnCarPlaced;
            placement.CarSwapped -= OnCarSwapped;
            placement.CarRemoved -= OnCarRemoved;
        }
        if (trackedCustomizer != null)
        {
            trackedCustomizer.Changed -= OnCustomizerChanged;
            trackedCustomizer = null;
        }
        Section("SESSION END");
        Line("Ended at " + System.DateTime.Now.ToString("HH:mm:ss", ci));
        Flush();
    }

    void OnApplicationPause(bool paused)
    {
        if (paused)
        {
            ConfigStore.Save();
            Flush();
        }
    }

    void OnApplicationQuit()
    {
        ConfigStore.Save();
        Flush();
    }

    // ---- writing ----------------------------------------------------------

    void Section(string title)
    {
        buffer.Append("\n===== ").Append(title).Append(" =====\n");
    }

    void Line(string text)
    {
        buffer.Append('[').Append(Time.time.ToString("F2", ci)).Append("s] ").Append(text).Append('\n');
    }

    void Raw(string text)
    {
        buffer.Append("    ").Append(text).Append('\n');
    }

    void Flush()
    {
        if (failed || buffer.Length == 0) return;
        try
        {
            File.AppendAllText(LogPath, buffer.ToString());
            buffer.Length = 0;
        }
        catch (System.Exception e)
        {
            failed = true;
            Debug.LogWarning("[Diagnostics] Write failed, logging disabled: " + e.Message);
        }
    }

    void OnUnityLog(string message, string stackTrace, LogType type)
    {
        if (failed) return;
        // Skip our own lines or the file becomes a hall of mirrors.
        if (message.StartsWith("[Diagnostics]")) return;

        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
        {
            Line("UNITY " + type + ": " + message);
            if (!string.IsNullOrEmpty(stackTrace))
            {
                foreach (var l in stackTrace.Split('\n'))
                {
                    if (l.Trim().Length > 0) Raw(l.Trim());
                }
            }
            Flush();
        }
        else if (type == LogType.Warning)
        {
            Line("UNITY WARNING: " + message);
        }
        else
        {
            Line("UNITY LOG: " + message);
        }
    }

    // ---- environment and inventory ----------------------------------------

    void WriteEnvironment()
    {
        Section("SESSION START");
        Line("Time            " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", ci));
        Raw("Unity           " + Application.unityVersion);
        Raw("Platform        " + Application.platform + (Application.isEditor ? " (editor)" : " (player)"));
        Raw("Device          " + SystemInfo.deviceModel + " / " + SystemInfo.operatingSystem);
        Raw("Graphics        " + SystemInfo.graphicsDeviceType + " - " + SystemInfo.graphicsDeviceName);
        Raw("Screen          " + Screen.width + " x " + Screen.height +
            "  dpi=" + Screen.dpi.ToString("F0", ci) +
            "  aspect=" + ((float)Screen.width / Mathf.Max(1, Screen.height)).ToString("F3", ci));
        Raw("Shadow distance " + QualitySettings.shadowDistance.ToString("F1", ci));
        Raw("Log path        " + LogPath);
        Flush();
    }

    void WriteSceneInventory()
    {
        if (failed) return;
        Section("SCENE INVENTORY");

        cam = Camera.main;
        if (cam == null)
        {
            Line("MainCamera: NONE FOUND  <-- nothing can render");
        }
        else
        {
            Line("Camera '" + cam.name + "'");
            Raw("position     " + V(cam.transform.position));
            Raw("euler        " + V(cam.transform.eulerAngles));
            Raw("fov          " + cam.fieldOfView.ToString("F1", ci) +
                "   aspect " + cam.aspect.ToString("F3", ci));
            Raw("clip         near " + cam.nearClipPlane.ToString("F3", ci) +
                "  far " + cam.farClipPlane.ToString("F1", ci));
            Raw("cullingMask  " + cam.cullingMask + (cam.cullingMask == -1 ? " (Everything)" : " (RESTRICTED)"));
            Raw("clearFlags   " + cam.clearFlags);
            Raw("enabled      " + cam.enabled + "   activeInHierarchy " + cam.gameObject.activeInHierarchy);
        }

        var placementController = FindObjectOfType<CarPlacementController>();
        if (placementController == null)
        {
            Line("CarPlacementController: NONE FOUND  <-- placement cannot work");
        }
        else
        {
            int prefabs = placementController.carPrefabs != null ? placementController.carPrefabs.Length : 0;
            Line("CarPlacementController: editorMode=" + placementController.useEditorMode + ", carPrefabs=" + prefabs);
            if (placementController.carPrefabs != null)
            {
                for (int i = 0; i < placementController.carPrefabs.Length; i++)
                {
                    var p = placementController.carPrefabs[i];
                    Raw("[" + i + "] " + (p != null ? p.name : "NULL ENTRY"));
                }
            }
        }

        WriteColliderInventory();
        WriteLightInventory();
        WriteFeatureInventory();
        Flush();
    }

    // "How big is the ground I am clicking on" -- every collider a placement
    // raycast could possibly hit, with its world bounds.
    void WriteColliderInventory()
    {
        var colliders = FindObjectsOfType<Collider>();
        Line("Colliders in scene: " + colliders.Length + "  (these are what clicks can hit)");
        foreach (var c in colliders)
        {
            var b = c.bounds;
            Raw(c.gameObject.name +
                "  type=" + c.GetType().Name +
                "  enabled=" + c.enabled +
                "  layer=" + LayerMask.LayerToName(c.gameObject.layer) + "(" + c.gameObject.layer + ")" +
                "  centre=" + V(b.center) +
                "  size=" + V(b.size));
        }
        if (colliders.Length == 0)
            Raw("NO COLLIDERS -- in editor mode a click has nothing to hit, so no car can be placed.");
    }

    void WriteLightInventory()
    {
        var lights = FindObjectsOfType<Light>();
        Line("Lights: " + lights.Length);
        foreach (var l in lights)
        {
            Raw(l.name + "  type=" + l.type +
                "  intensity=" + l.intensity.ToString("F2", ci) +
                "  shadows=" + l.shadows +
                "  euler=" + V(l.transform.eulerAngles) +
                "  enabled=" + l.enabled);
        }
        Raw("ambientMode=" + RenderSettings.ambientMode);
    }

    // Which v3 features are actually present, so the log alone explains a
    // "feature does nothing" report.
    void WriteFeatureInventory()
    {
        Line("v3 feature components present in scene:");
        Raw("CustomizePanel     " + Present(FindObjectOfType<CustomizePanel>()));
        Raw("ARStatusOverlay    " + Present(FindObjectOfType<ARStatusOverlay>()));
        Raw("ShadowCatcher      " + Present(FindObjectOfType<ShadowCatcher>()));
        Raw("ARLightEstimator   " + Present(FindObjectOfType<ARLightEstimator>()));
        Raw("AROcclusionManager " + Present(FindObjectOfType<AROcclusionManager>()));
        Raw("EditorSimulation   " + Present(FindObjectOfType<EditorSimulation>()));
        Raw("PlacementReticle   " + Present(FindObjectOfType<PlacementReticle>()));
        Raw("ARPlaneManager     " + Present(FindObjectOfType<ARPlaneManager>()));
    }

    static string Present(Object o)
    {
        return o != null ? "yes" : "NO  <-- run BuildYourRide > Upgrade Scene to v3";
    }

    // ---- input ------------------------------------------------------------

    void Update()
    {
        if (failed) return;

        // ConfigStore.TryFlush() used to be pumped from here. It now has its
        // own ConfigStorePump, so persistence no longer depends on this debug
        // component being present in the build.

        if (Input.GetMouseButtonDown(0))
            LogPress(Input.mousePosition, "mouse");

        for (int i = 0; i < Input.touchCount; i++)
        {
            var t = Input.GetTouch(i);
            if (t.phase == TouchPhase.Began)
                LogPress(t.position, "touch" + t.fingerId);
        }

        if (snapshotInterval > 0f && Time.time >= nextSnapshot)
        {
            nextSnapshot = Time.time + snapshotInterval;
            WriteSnapshot();
        }

        if (Time.time >= nextFlush)
        {
            nextFlush = Time.time + 1f;
            Flush();
        }
    }

    // "Where am I pressing, and did it land on anything?"
    void LogPress(Vector3 screenPos, string source)
    {
        clickCount++;
        Section("PRESS #" + clickCount + " (" + source + ")");

        bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        Line("screen=" + V2(screenPos) +
             "  viewport=" + V2(new Vector2(screenPos.x / Mathf.Max(1, Screen.width),
                                            screenPos.y / Mathf.Max(1, Screen.height))) +
             "  overUI=" + overUI);

        if (overUI)
            Raw("Pointer was over UI, so placement is intentionally skipped.");

        if (cam == null) cam = Camera.main;
        if (cam == null)
        {
            Raw("No main camera, cannot raycast.");
            return;
        }

        var ray = cam.ScreenPointToRay(screenPos);
        Raw("ray origin=" + V(ray.origin) + "  dir=" + V(ray.direction));

        // Physics raycast: what the editor path actually uses.
        RaycastHit hit;
        if (Physics.Raycast(ray, out hit, 100f, ~0, QueryTriggerInteraction.Ignore))
        {
            Raw("PHYSICS HIT  '" + hit.collider.gameObject.name + "'" +
                "  point=" + V(hit.point) +
                "  distance=" + hit.distance.ToString("F2", ci) + " m" +
                "  normal=" + V(hit.normal));
        }
        else
        {
            Raw("PHYSICS MISS -- ray hit no collider within 100 m.");
        }

        // AR raycast: what the device path uses.
        if (placement != null)
        {
            Pose pose;
            ARPlane plane;
            if (placement.RaycastPlane(screenPos, out pose, out plane))
            {
                Raw("PLACEMENT RAYCAST OK  pose=" + V(pose.position) +
                    "  plane=" + (plane != null ? plane.trackableId.ToString() : "none (editor mode)"));
            }
            else
            {
                Raw("PLACEMENT RAYCAST FAILED -- no car will be placed from this press.");
            }
            Raw("hasPlacedCar=" + placement.HasPlacedCar + " (if true, presses no longer place)");
        }

        // Deliberately not flushed here. File.AppendAllText opens, writes and
        // closes the file on the main thread, and this method runs on every
        // single touch-down -- i.e. it put a synchronous disk write at the
        // exact frame a tap or a drag begins, which is the worst possible
        // moment for input to feel like it stuttered. The 1-second periodic
        // flush in Update carries these lines out instead; errors and
        // exceptions still flush immediately, in OnUnityLog.
    }

    // ---- placement --------------------------------------------------------

    void OnCarPlaced(GameObject car) { LogCar("CAR PLACED", car); }
    void OnCarSwapped(GameObject car) { LogCar("CAR SWAPPED", car); }

    void OnCarRemoved()
    {
        Section("CAR REMOVED");
        Flush();
    }

    void LogCar(string title, GameObject car)
    {
        Section(title);
        if (car == null)
        {
            Line("car is NULL");
            Flush();
            return;
        }

        Line("name=" + car.name +
             "  active=" + car.activeInHierarchy +
             "  layer=" + LayerMask.LayerToName(car.layer) + "(" + car.layer + ")");
        Raw("position   " + V(car.transform.position));
        Raw("rotation   " + V(car.transform.eulerAngles));
        Raw("localScale " + V(car.transform.localScale));
        Raw("parent     " + (car.transform.parent != null ? car.transform.parent.name : "none (not anchored)"));

        var renderers = car.GetComponentsInChildren<Renderer>(true);
        int enabledCount = 0, visibleCount = 0, nullMat = 0;
        foreach (var r in renderers)
        {
            if (r.enabled && r.gameObject.activeInHierarchy) enabledCount++;
            if (r.isVisible) visibleCount++;
            foreach (var m in r.sharedMaterials)
            {
                if (m == null) nullMat++;
            }
        }

        Raw("renderers  total=" + renderers.Length +
            "  enabled=" + enabledCount +
            "  isVisible=" + visibleCount +
            "  nullMaterialSlots=" + nullMat);

        if (renderers.Length == 0)
        {
            Raw("NO RENDERERS -- the prefab has no visible geometry.");
            Flush();
            return;
        }

        var bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

        Raw("bounds     centre=" + V(bounds.center) + "  size=" + V(bounds.size));
        Raw("footprint  " + bounds.size.x.ToString("F2", ci) + " x " +
            bounds.size.z.ToString("F2", ci) + " m,  height " +
            bounds.size.y.ToString("F2", ci) + " m");

        LogVisibility(bounds);
        LogCustomizer(car);
        Flush();
    }

    // The direct answer to "is it visible over there".
    void LogVisibility(Bounds bounds)
    {
        if (cam == null) cam = Camera.main;
        if (cam == null) return;

        Vector3 camPos = cam.transform.position;
        float distToCentre = Vector3.Distance(camPos, bounds.center);
        bool camInside = bounds.Contains(camPos);
        float radius = bounds.extents.magnitude;

        var frustum = GeometryUtility.CalculateFrustumPlanes(cam);
        bool inFrustum = GeometryUtility.TestPlanesAABB(frustum, bounds);

        Vector3 toCentre = bounds.center - camPos;
        Vector3 nearestOnBox = bounds.ClosestPoint(camPos);
        float nearestSurface = Vector3.Distance(camPos, nearestOnBox);

        Raw("VISIBILITY");
        Raw("  camera pos          " + V(camPos));
        Raw("  dist to centre      " + distToCentre.ToString("F2", ci) + " m");
        Raw("  bounding radius     " + radius.ToString("F2", ci) + " m");
        Raw("  nearest surface     " + nearestSurface.ToString("F3", ci) + " m" +
            (nearestSurface < cam.nearClipPlane ? "  <-- CLOSER THAN NEAR CLIP, geometry is clipped" : ""));
        Raw("  camera inside car   " + camInside + (camInside ? "  <-- camera is inside the model" : ""));
        Raw("  inside frustum      " + inFrustum + (inFrustum ? "" : "  <-- OFF SCREEN"));
        Raw("  angle off centre    " +
            Vector3.Angle(cam.transform.forward, toCentre.normalized).ToString("F1", ci) + " deg" +
            "  (half-fov is " + (cam.fieldOfView * 0.5f).ToString("F1", ci) + " deg vertical)");

        if (!inFrustum)
            Raw("  VERDICT: the car exists but is outside the camera frustum.");
        else if (camInside)
            Raw("  VERDICT: camera is inside the bodywork; you are seeing backfaces or nothing.");
        else
            Raw("  VERDICT: the car should be on screen.");
    }

    void LogCustomizer(GameObject car)
    {
        var c = car.GetComponentInChildren<CarCustomizer>();
        if (c == null)
        {
            Raw("CarCustomizer: none on this prefab");
            return;
        }

        Raw("CUSTOMIZER  key=" + c.CarKey +
            "  groups=" + c.GroupCount +
            "  finishes=" + c.FinishCount +
            "  savedBuild=" + c.HasSavedBuild);

        if (c.GroupCount == 0)
            Raw("  NO PART GROUPS -- prefab predates the part-group redesign; regenerate it.");

        for (int i = 0; i < c.GroupCount; i++)
        {
            Raw("  group[" + i + "] " + c.GetGroupName(i) +
                "  options=" + c.GetGroupOptionCount(i) +
                "  selected=" + c.GetGroupIndex(i) +
                " (" + c.GetGroupOptionName(i, c.GetGroupIndex(i)) + ")");
        }

        Raw("  finish=" + c.GetFinishName(c.FinishIndex) +
            "  spoiler=" + c.SpoilerEnabled +
            "  wheelIndex=" + c.WheelIndex +
            "  hasWheelSets=" + c.HasWheelSets);

        if (trackedCustomizer != c)
        {
            if (trackedCustomizer != null) trackedCustomizer.Changed -= OnCustomizerChanged;
            trackedCustomizer = c;
            c.Changed += OnCustomizerChanged;
        }
    }

    void OnCustomizerChanged(string message)
    {
        Line("CHANGE  " + message);
    }

    // ---- periodic ---------------------------------------------------------

    void WriteSnapshot()
    {
        if (cam == null) cam = Camera.main;
        if (cam == null) return;

        var sb = snapshotBuffer;
        sb.Length = 0;
        sb.Append("SNAPSHOT fps=").Append((1f / Mathf.Max(0.0001f, Time.unscaledDeltaTime)).ToString("F0", ci));
        sb.Append("  cam=").Append(V(cam.transform.position));
        sb.Append("  camFwd=").Append(V(cam.transform.forward));

        if (placement != null && placement.HasPlacedCar)
        {
            var car = placement.PlacedCar;
            var t = car.transform;
            sb.Append("  car=").Append(V(t.position));
            sb.Append("  scale=").Append(t.localScale.x.ToString("F2", ci));
            sb.Append("  dist=").Append(Vector3.Distance(cam.transform.position, t.position).ToString("F2", ci));

            if (snapshotCar != car)
            {
                snapshotCar = car;
                snapshotRenderers = car.GetComponentsInChildren<Renderer>();
            }

            if (snapshotRenderers != null && snapshotRenderers.Length > 0)
            {
                var b = snapshotRenderers[0].bounds;
                for (int i = 1; i < snapshotRenderers.Length; i++) b.Encapsulate(snapshotRenderers[i].bounds);
                GeometryUtility.CalculateFrustumPlanes(cam, frustumPlanes);
                bool inView = GeometryUtility.TestPlanesAABB(frustumPlanes, b);
                sb.Append("  inView=").Append(inView);
                if (!inView) sb.Append("  <-- OFF SCREEN");
            }
        }
        else
        {
            sb.Append("  car=none");
            snapshotCar = null;
            snapshotRenderers = null;
        }

        if (cachedPlaneManager == null) cachedPlaneManager = FindObjectOfType<ARPlaneManager>();
        if (cachedPlaneManager != null && cachedPlaneManager.enabled)
            sb.Append("  planes=").Append(cachedPlaneManager.trackables.count);

        Line(sb.ToString());
    }

    // ---- formatting -------------------------------------------------------

    string V(Vector3 v)
    {
        return "(" + v.x.ToString("F2", ci) + ", " + v.y.ToString("F2", ci) + ", " + v.z.ToString("F2", ci) + ")";
    }

    string V2(Vector2 v)
    {
        return "(" + v.x.ToString("F1", ci) + ", " + v.y.ToString("F1", ci) + ")";
    }
}
