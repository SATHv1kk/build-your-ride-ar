using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

[RequireComponent(typeof(ARRaycastManager))]
[RequireComponent(typeof(ARAnchorManager))]
[RequireComponent(typeof(ARPlaneManager))]
public class CarPlacementController : MonoBehaviour
{
    public GameObject[] carPrefabs;
    public bool useEditorMode;

    ARRaycastManager raycastManager;
    ARAnchorManager anchorManager;
    ARPlaneManager planeManager;

    static readonly List<ARRaycastHit> s_Hits = new List<ARRaycastHit>();

    int carIndex;
    ARAnchor currentAnchor;
    bool planesHidden;
    Camera cachedCamera;

    public GameObject PlacedCar { get; private set; }
    public bool HasPlacedCar => PlacedCar != null;

    public string CurrentCarName
    {
        get
        {
            if (carPrefabs == null || carPrefabs.Length == 0) return "car";
            int idx = NextValidIndex(carIndex);
            return carPrefabs[idx] != null ? carPrefabs[idx].name : "car";
        }
    }

    public event Action<GameObject> CarPlaced;
    public event Action<GameObject> CarSwapped;
    public event Action CarRemoved;

    void Awake()
    {
        raycastManager = GetComponent<ARRaycastManager>();
        anchorManager = GetComponent<ARAnchorManager>();
        planeManager = GetComponent<ARPlaneManager>();

        // Unity synthesises mouse events from touches by default, so on a
        // phone every tap arrives twice: once through Input.GetTouch and again
        // through Input.GetMouseButton. Both this class and
        // CarGestureController branch on touchCount to pick one path, but the
        // touch-ended frame lands in the mouse branch, which is how a lifted
        // finger could start a mouse drag it never asked for. Desktop mouse
        // input is unaffected -- this only suppresses the synthetic events.
        Input.simulateMouseWithTouches = false;
    }

    void Start()
    {
        if (carPrefabs == null || carPrefabs.Length == 0) return;
        carIndex = NextValidIndex(IndexOf(ConfigStore.LastCar));
    }

    int IndexOf(string carName)
    {
        if (string.IsNullOrEmpty(carName) || carPrefabs == null) return 0;
        for (int i = 0; i < carPrefabs.Length; i++)
        {
            if (carPrefabs[i] != null && carPrefabs[i].name == carName) return i;
        }
        return 0;
    }

    int NextValidIndex(int start)
    {
        if (carPrefabs == null || carPrefabs.Length == 0) return 0;
        int count = carPrefabs.Length;
        for (int i = 0; i < count; i++)
        {
            int idx = (start + i) % count;
            if (carPrefabs[idx] != null) return idx;
        }
        return start % count;
    }

    void OnEnable()
    {
        if (planeManager != null)
            planeManager.planesChanged += OnPlanesChanged;
    }

    void OnDisable()
    {
        if (planeManager != null)
            planeManager.planesChanged -= OnPlanesChanged;
    }

    void Update()
    {
        if (HasPlacedCar || carPrefabs == null || carPrefabs.Length == 0) return;
        if (!useEditorMode && (raycastManager == null || planeManager == null)) return;

        if (Input.touchCount == 1)
        {
            var touch = Input.GetTouch(0);
            if (touch.phase == TouchPhase.Began && !IsPointerOverUI(touch))
            {
                if (RaycastPlane(touch.position, out Pose pose, out ARPlane plane))
                    Place(pose, plane);
            }
        }
        else if (Input.GetMouseButtonDown(0))
        {
            if (!IsPointerOverMouse())
            {
                if (RaycastPlane(Input.mousePosition, out Pose pose, out ARPlane plane))
                    Place(pose, plane);
            }
        }
    }

    public bool RaycastPlane(Vector2 screenPos, out Pose pose, out ARPlane plane)
    {
        pose = default;
        plane = null;

        if (useEditorMode)
        {
            if (cachedCamera == null) cachedCamera = Camera.main;
            if (cachedCamera == null) return false;
            var ray = cachedCamera.ScreenPointToRay(screenPos);
            if (Physics.Raycast(ray, out RaycastHit hit, 100f, ~0, QueryTriggerInteraction.Ignore))
            {
                pose = new Pose(hit.point, Quaternion.identity);
                return true;
            }
            return false;
        }

        if (raycastManager == null) return false;
        if (!raycastManager.Raycast(screenPos, s_Hits, TrackableType.PlaneWithinPolygon)) return false;
        pose = s_Hits[0].pose;
        if (planeManager != null)
            plane = planeManager.GetPlane(s_Hits[0].trackableId);
        return true;
    }

    void Place(Pose pose, ARPlane plane)
    {
        carIndex = NextValidIndex(carIndex);
        if (carPrefabs[carIndex] == null) return;
        currentAnchor = CreateAnchor(pose, plane);
        PlacedCar = Instantiate(carPrefabs[carIndex], pose.position, pose.rotation);
        if (currentAnchor != null)
            PlacedCar.transform.SetParent(currentAnchor.transform, true);
        FaceCamera(PlacedCar.transform);
        RestoreRig(PlacedCar.transform);
        SetPlanesHidden(true);
        ConfigStore.LastCar = carPrefabs[carIndex].name;
        ConfigStore.Save();
        CarPlaced?.Invoke(PlacedCar);
    }

    // Size and viewing angle are how the user framed the car last time. World
    // position cannot come back with them: a new AR session has a new origin,
    // so the car is always re-placed by tapping.
    void RestoreRig(Transform car)
    {
        float scale = ConfigStore.Scale;
        if (scale > 0.05f) car.localScale = Vector3.one * scale;

        float yaw = ConfigStore.Yaw;
        if (!float.IsNaN(yaw)) car.Rotate(Vector3.up, yaw, Space.World);
    }

    // Yaw is stored relative to where the camera was looking, because absolute
    // world yaw is meaningless once the session origin changes.
    public void PersistRig()
    {
        if (!HasPlacedCar) return;
        var car = PlacedCar.transform;
        ConfigStore.Scale = car.localScale.x;

        if (cachedCamera == null) cachedCamera = Camera.main;
        if (cachedCamera != null)
        {
            Vector3 toCamera = cachedCamera.transform.position - car.position;
            toCamera.y = 0f;
            if (toCamera.sqrMagnitude > 0.001f)
            {
                float facing = Quaternion.LookRotation(toCamera.normalized, Vector3.up).eulerAngles.y;
                ConfigStore.Yaw = Mathf.DeltaAngle(facing, car.rotation.eulerAngles.y);
            }
        }
        // Deferred: this fires at the end of every drag, pinch and twist, and
        // a user framing the car settles a gesture many times in a row.
        ConfigStore.SaveDeferred();
    }

    public void NextCar()
    {
        if (carPrefabs == null || carPrefabs.Length == 0) return;
        carIndex = NextValidIndex(carIndex + 1);
        if (carPrefabs[carIndex] == null) return;
        ConfigStore.LastCar = carPrefabs[carIndex].name;
        ConfigStore.Save();
        if (!HasPlacedCar) return;

        var t = PlacedCar.transform;
        Vector3 pos = t.position;
        Quaternion rot = t.rotation;
        Vector3 scale = t.localScale;

        Color? paintColor = null;
        int wheels = 0;
        bool spoiler = false;
        var oldCustomizer = PlacedCar.GetComponentInChildren<CarCustomizer>();
        if (oldCustomizer != null)
        {
            // The actual rendered colour, not a raw option index -- an
            // index means nothing across two cars with differently-ordered
            // palettes. See CarCustomizer.ApplyState for why this matters.
            if (oldCustomizer.GroupCount > 0)
                paintColor = oldCustomizer.GetGroupOptionColor(0, oldCustomizer.PaintIndex);
            spoiler = oldCustomizer.SpoilerEnabled;
            wheels = oldCustomizer.WheelIndex;
        }

        Destroy(PlacedCar);
        Transform parent = currentAnchor != null ? currentAnchor.transform : null;
        PlacedCar = Instantiate(carPrefabs[carIndex], pos, rot, parent);
        PlacedCar.transform.localScale = scale;

        // A car the user has already built comes back the way they left it.
        // Only a factory-fresh car inherits the previous car's colours, so
        // switching back and forth does not overwrite either build.
        var customizer = PlacedCar.GetComponentInChildren<CarCustomizer>();
        if (customizer != null && !customizer.HasSavedBuild)
            customizer.ApplyState(paintColor, spoiler, wheels);

        CarSwapped?.Invoke(PlacedCar);
    }

    public void RemoveCar()
    {
        if (!HasPlacedCar) return;
        Destroy(PlacedCar);
        if (currentAnchor != null) Destroy(currentAnchor.gameObject);
        PlacedCar = null;
        currentAnchor = null;
        SetPlanesHidden(false);
        CarRemoved?.Invoke();
    }

    public void Reanchor(Pose pose, ARPlane plane)
    {
        if (!HasPlacedCar) return;
        var oldAnchor = currentAnchor;
        currentAnchor = CreateAnchor(pose, plane);
        if (currentAnchor != null)
            PlacedCar.transform.SetParent(currentAnchor.transform, true);
        else
            currentAnchor = oldAnchor;
        // Only destroy the old anchor if the new one succeeded; otherwise the car
        // would lose its parent and any drift protection the old anchor gave it.
        if (currentAnchor != oldAnchor && oldAnchor != null)
            Destroy(oldAnchor.gameObject);
    }

    // True when the live anchor is attached to a detected plane. A free-floating
    // fallback anchor is materially worse for drift, so this is worth knowing.
    public bool AnchorIsPlaneAttached { get; private set; }

    // Why the last anchor ended up the way it did. Empty on the happy path.
    public string LastAnchorNote { get; private set; }

    public ARAnchor CurrentAnchor { get { return currentAnchor; } }

    ARAnchor CreateAnchor(Pose pose, ARPlane plane)
    {
        // No anchor subsystem in editor simulation; the car is moved directly.
        if (useEditorMode || anchorManager == null) return null;

        AnchorIsPlaneAttached = false;
        LastAnchorNote = string.Empty;

        ARAnchor anchor = null;
        if (plane == null)
        {
            LastAnchorNote = "no plane supplied";
        }
        else if (plane.trackableId == TrackableId.invalidId)
        {
            LastAnchorNote = "plane has invalid trackableId (likely subsumed by a larger plane)";
        }
        else
        {
            try
            {
                anchor = anchorManager.AttachAnchor(plane, pose);
                if (anchor == null) LastAnchorNote = "AttachAnchor returned null";
                else AnchorIsPlaneAttached = true;
            }
            catch (Exception e)
            {
                // Previously swallowed silently, which hid every downgrade to a
                // free-floating anchor -- the exact condition that drifts.
                LastAnchorNote = "AttachAnchor threw: " + e.Message;
            }
        }

        if (anchor == null)
        {
            var go = new GameObject("CarAnchor (free-floating)");
            go.transform.SetPositionAndRotation(pose.position, pose.rotation);
            anchor = go.AddComponent<ARAnchor>();
            Debug.LogWarning("[Anchor] Fell back to a free-floating anchor: " +
                             (string.IsNullOrEmpty(LastAnchorNote) ? "unknown reason" : LastAnchorNote) +
                             ". This anchor is more prone to drift than a plane-attached one.");
        }

        return anchor;
    }

    void FaceCamera(Transform car)
    {
        if (cachedCamera == null) cachedCamera = Camera.main;
        if (cachedCamera == null) return;
        Vector3 toCamera = cachedCamera.transform.position - car.position;
        toCamera.y = 0f;
        if (toCamera.sqrMagnitude < 0.001f) return;
        car.rotation = Quaternion.LookRotation(toCamera.normalized, Vector3.up);
    }

    void SetPlanesHidden(bool hidden)
    {
        planesHidden = hidden;
        if (planeManager == null) return;
        foreach (var plane in planeManager.trackables)
            plane.gameObject.SetActive(!hidden);
    }

    void OnPlanesChanged(ARPlanesChangedEventArgs args)
    {
        if (!planesHidden) return;
        foreach (var plane in args.added)
            plane.gameObject.SetActive(false);
    }

    public static bool IsPointerOverUI(Touch touch)
    {
        return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject(touch.fingerId);
    }

    public static bool IsPointerOverMouse()
    {
        return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
    }
}
