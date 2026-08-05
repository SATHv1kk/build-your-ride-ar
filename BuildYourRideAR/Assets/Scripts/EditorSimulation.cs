using System.Collections;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR.ARFoundation;

public class EditorSimulation : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoAttach()
    {
        if (!Application.isEditor) return;
        if (FindObjectOfType<EditorSimulation>() != null) return;
        var xr = FindObjectOfType<XROrigin>();
        if (xr != null) xr.gameObject.AddComponent<EditorSimulation>();
    }

    [Tooltip("PlaneViz material, so the detected-plane look can be previewed " +
             "and tuned in play mode without deploying to a phone.")]
    public Material planePreviewMaterial;

    CarPlacementController placement;
    Camera cam;
    Transform pivot;
    Vector3 target;
    float dist = 4f;
    float yaw = 180f;
    float pitch = 35f;
    float targetDist;
    float targetYaw;
    float targetPitch;
    float focusHeight;
    bool active;
    bool lastArrowRotating;
    Material groundMaterial;

    void Awake()
    {
        // This component is authored into the scene (so it must not be
        // compiled out -- stripping a scene-referenced script is what left a
        // "Missing (Mono Script)" corpse where CarGestureController used to
        // be). But on device every one of its methods is a no-op, and merely
        // declaring OnGUI switches on Unity's IMGUI event loop for the whole
        // player: a per-frame layout/repaint pass and its allocations, for a
        // label that can never be drawn. Removing the component removes both.
        if (!Application.isEditor)
        {
            Destroy(this);
            return;
        }

        placement = GetComponent<CarPlacementController>();
        cam = Camera.main;
    }

    IEnumerator Start()
    {
        yield return null;

        if (Application.isEditor)
        {
            yield return null;
            Init();
        }
    }

    void Init()
    {
        if (placement == null)
        {
            enabled = false;
            return;
        }

        active = true;
        placement.useEditorMode = true;

        targetDist = dist;
        targetYaw = yaw;
        targetPitch = pitch;

        BuildGround();
        SetupCamera();
        DestroyARComponents();

        placement.CarPlaced += OnCarPlaced;
        placement.CarSwapped += OnCarSwapped;
        placement.CarRemoved += OnCarRemoved;
    }

    void OnDestroy()
    {
        if (groundMaterial != null) Destroy(groundMaterial);
        if (placement == null) return;
        placement.CarPlaced -= OnCarPlaced;
        placement.CarSwapped -= OnCarSwapped;
        placement.CarRemoved -= OnCarRemoved;
    }

    void BuildGround()
    {
        var plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
        plane.name = "SimGround";
        var t = plane.transform;
        t.localScale = new Vector3(3f, 1f, 3f);
        t.position = Vector3.zero;
        Destroy(plane.GetComponent<Collider>());

        var mr = plane.GetComponent<MeshRenderer>();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = true;
        groundMaterial = new Material(Shader.Find("Standard"))
        {
            color = new Color(0.25f, 0.27f, 0.30f)
        };
        mr.sharedMaterial = groundMaterial;

        var col = plane.AddComponent<BoxCollider>();
        col.size = new Vector3(30f, 0.01f, 30f);

        if (planePreviewMaterial != null)
        {
            // Just above the grey floor so the two do not z-fight.
            EditorPlanePreview.Create(planePreviewMaterial, new Vector3(0f, 0.002f, 0f), 3.2f, 2.4f);
        }
    }

    void SetupCamera()
    {
        if (cam == null) return;

        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.10f, 0.11f, 0.13f);
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 200f;
        cam.fieldOfView = 50f;

        var t = cam.transform;
        pivot = new GameObject("CamPivot").transform;
        pivot.position = Vector3.zero;
        pivot.rotation = Quaternion.Euler(pitch, yaw, 0f);

        // worldPositionStays: false. The default (true) makes Unity preserve the
        // camera's world rotation by back-compensating localRotation, which left
        // the camera positioned by the pivot but permanently facing world +Z --
        // so orbiting moved it around the target without ever looking at it.
        t.SetParent(pivot, false);
        t.localPosition = new Vector3(0, 0, -dist);
        t.localRotation = Quaternion.identity;
    }

    void DestroyARComponents()
    {
        if (cam == null) return;

        // ARCameraBackground subscribes to AROcclusionManager.frameReceived in its
        // OnEnable. Destroying the occlusion manager with that subscription live
        // makes its OnDisable call InvokeFrameReceived, which dereferences a
        // subsystem that never exists on desktop -- one NullReferenceException on
        // every play. Disabling the background runs its OnDisable synchronously and
        // unsubscribes; Destroy alone cannot, because Unity defers the destruction
        // itself to the end of the frame.
        var background = cam.GetComponent<ARCameraBackground>();
        if (background != null) background.enabled = false;

        // ARLightEstimator and ARCameraBackground both declare a dependency on
        // ARCameraManager, so they have to go first or Unity refuses to remove it.
        Destroy(cam.GetComponent<ARLightEstimator>());
        Destroy(cam.GetComponent<AROcclusionManager>());
        Destroy(background);
        Destroy(cam.GetComponent<ARCameraManager>());
        Destroy(cam.GetComponent<TrackedPoseDriver>());

        var pm = GetComponent<ARPlaneManager>();
        if (pm != null) pm.enabled = false;
        var rm = GetComponent<ARRaycastManager>();
        if (rm != null) rm.enabled = false;
        var am = GetComponent<ARAnchorManager>();
        if (am != null) am.enabled = false;
    }

    void Update()
    {
        if (!active || pivot == null) return;

        bool overUI = CarPlacementController.IsPointerOverMouse();

        if (!overUI)
        {
            // Plain scroll zooms the camera; Shift+scroll is reserved for
            // scaling the car (CarGestureController).
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (!shift && Mathf.Abs(scroll) > 0.01f)
            {
                // Proportional so one notch feels the same at 2 m or 20 m, but
                // the per-frame factor is capped: scroll axis magnitude varies
                // wildly between platforms, and an uncapped multiplier flung the
                // camera straight to the zoom ceiling in a couple of frames.
                float factor = Mathf.Clamp(1f - scroll * 0.75f, 0.6f, 1.6f);
                targetDist = Mathf.Clamp(targetDist * factor, 1.5f, 40f);
            }

            if (Input.GetMouseButton(1))
            {
                targetYaw += Input.GetAxis("Mouse X") * 3f;
                targetPitch = Mathf.Clamp(targetPitch - Input.GetAxis("Mouse Y") * 3f, 5f, 80f);
            }

            float pitchInput = 0f;
            if (Input.GetKey(KeyCode.UpArrow)) pitchInput += 1f;
            if (Input.GetKey(KeyCode.DownArrow)) pitchInput -= 1f;
            if (pitchInput != 0f)
                targetPitch = Mathf.Clamp(targetPitch + pitchInput * 90f * Time.deltaTime, 5f, 80f);

            if (placement != null && placement.HasPlacedCar && !Input.GetKey(KeyCode.LeftShift) && !Input.GetKey(KeyCode.RightShift))
            {
                float rotDir = 0f;
                if (Input.GetKey(KeyCode.LeftArrow)) rotDir -= 1f;
                if (Input.GetKey(KeyCode.RightArrow)) rotDir += 1f;
                bool arrowRotating = rotDir != 0f;
                if (arrowRotating)
                    placement.PlacedCar.transform.Rotate(Vector3.up, rotDir * 120f * Time.deltaTime, Space.World);
                if (lastArrowRotating && !arrowRotating)
                    placement.PersistRig();
                lastArrowRotating = arrowRotating;
            }

            if (Input.GetMouseButton(2))
            {
                var right = cam.transform.right * -Input.GetAxis("Mouse X") * dist * 0.3f;
                var up = cam.transform.up * -Input.GetAxis("Mouse Y") * dist * 0.3f;
                target += right + up;
            }
        }

        dist = Mathf.Lerp(dist, targetDist, 1f - Mathf.Exp(-15f * Time.deltaTime));
        yaw = Mathf.Lerp(yaw, targetYaw, 1f - Mathf.Exp(-15f * Time.deltaTime));
        pitch = Mathf.Lerp(pitch, targetPitch, 1f - Mathf.Exp(-15f * Time.deltaTime));

        // Follow the car's centre, not its origin: the imported cars are
        // pivoted at the base so the origin sits on the ground.
        if (placement != null && placement.HasPlacedCar && !Input.GetMouseButton(1) && !Input.GetMouseButton(2))
        {
            Vector3 follow = placement.PlacedCar.transform.position + Vector3.up * focusHeight;
            target = Vector3.Lerp(target, follow, 1f - Mathf.Exp(-3f * Time.deltaTime));
        }

        pivot.position = target;
        pivot.rotation = Quaternion.Euler(pitch, yaw, 0f);
        cam.transform.localPosition = new Vector3(0, 0, -dist);
        // Keep the camera aligned with the pivot so it always looks at the
        // target, whatever else touches the transform.
        cam.transform.localRotation = Quaternion.identity;
    }

    void OnCarPlaced(GameObject car)
    {
        FocusOn(car);
        SetLayerRecursive(car, 2);
    }

    void OnCarSwapped(GameObject car)
    {
        FocusOn(car);
        SetLayerRecursive(car, 2);
    }

    void OnCarRemoved()
    {
        target = Vector3.zero;
        targetDist = 4f;
        focusHeight = 0f;
    }

    // Frame the car from its actual size. A fixed distance cannot work here:
    // cars are ~4.9 m long and CarPlacementController turns the car to face the
    // camera, so its length axis points straight down the view direction. At
    // the old hardcoded 3 m the camera sat inside the bodywork, closer than the
    // near clip plane, and the car was simply not visible.
    void FocusOn(GameObject car)
    {
        if (car == null) return;

        var bounds = ComputeBounds(car);
        focusHeight = bounds.center.y - car.transform.position.y;
        target = bounds.center;
        targetDist = FramingDistance(bounds);

        focusInfo = string.Format(
            "{0}  |  size {1:F2} x {2:F2} x {3:F2} m  |  camera {4:F1} m  |  renderers {5}",
            car.name.Replace("(Clone)", ""),
            bounds.size.x, bounds.size.y, bounds.size.z,
            targetDist,
            car.GetComponentsInChildren<Renderer>().Length);
        Debug.Log("[EditorSimulation] Framing " + focusInfo);
    }

    float FramingDistance(Bounds bounds)
    {
        float radius = bounds.extents.magnitude;
        if (radius < 0.01f) return 4f;
        if (cam == null) return Mathf.Clamp(radius * 2.6f, 2f, 40f);

        // Fit the car's bounding sphere in whichever axis is tighter.
        float vFov = cam.fieldOfView * Mathf.Deg2Rad;
        float hFov = 2f * Mathf.Atan(Mathf.Tan(vFov * 0.5f) * Mathf.Max(cam.aspect, 0.1f));
        float d = Mathf.Max(radius / Mathf.Sin(vFov * 0.5f), radius / Mathf.Sin(hFov * 0.5f));
        return Mathf.Clamp(d * 1.15f, 2f, 40f);
    }

    static Bounds ComputeBounds(GameObject go)
    {
        var renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.one);
        var b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            b.Encapsulate(renderers[i].bounds);
        return b;
    }

    static void SetLayerRecursive(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform child in go.transform)
            SetLayerRecursive(child.gameObject, layer);
    }

    GUIStyle guiStyle;
    string focusInfo = "";

    void OnGUI()
    {
        if (!active) return;

        if (guiStyle == null)
        {
            guiStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                normal = { textColor = new Color(0.55f, 0.60f, 0.66f) }
            };
        }

        bool placed = placement != null && placement.HasPlacedCar;
        var msg = placed
            ? "W/A/D/Z move spoiler (Ctrl fast) | Shift+Q/E spoiler height | Left/Right rotate car | Shift+scroll scale | R-drag orbit | Up/Down tilt | Scroll zoom | M-drag pan"
            : "Click ground to place car | R-drag orbit | Up/Down tilt | Scroll zoom";

        GUI.Label(new Rect(14, Screen.height - 30, 900, 24), msg, guiStyle);
        if (placed && focusInfo.Length > 0)
            GUI.Label(new Rect(14, Screen.height - 48, 900, 24), focusInfo, guiStyle);
    }
}
