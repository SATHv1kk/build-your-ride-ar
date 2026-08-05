using UnityEngine;
using UnityEngine.XR.ARFoundation;

public class CarGestureController : MonoBehaviour
{
    public CarPlacementController placement;
    public float minScale = 0.25f;
    public float maxScale = 2.5f;
    [Tooltip("Degrees per second for Q/E or arrow-key rotation (desktop).")]
    public float keyRotateSpeed = 120f;
    [Tooltip("How quickly the car follows the finger/cursor while dragging.")]
    public float dragSmoothing = 15f;
    [Tooltip("Minimum grab radius around the car in pixels (scaled by DPI).")]
    public float grabRadiusPixels = 90f;
    [Tooltip("Speed of the auto-rotate showcase (degrees per second).")]
    public float autoRotateSpeed = 30f;

    bool dragging;
    bool touchDrag;
    bool twoFinger;
    // Decided once when the two-finger gesture starts, not re-tested per
    // frame. Re-testing meant a pinch died the instant either finger drifted
    // over the bottom bar -- and on a phone, where the bar spans the full
    // width just above the thumbs, that is most pinches. A gesture that began
    // on open screen stays owned by the car until the fingers lift.
    bool twoFingerBlocked;
    bool lastKeyRotating;
    bool lastShiftScaling;
    bool hasDragTarget;
    Vector3 dragTarget;
    ARPlane lastPlane;
    Camera cachedCamera;

    // The roster's real cars run to 116 (Porsche) and 209 (Maserati)
    // renderers. Walking all of them to rebuild a bounding box on every single
    // touch-down is a GetComponentsInChildren allocation plus a few hundred
    // bounds unions at exactly the moment the frame budget is already being
    // spent starting a gesture. The car's local bounds do not change between
    // taps, so they are measured once per placed car.
    GameObject boundsCar;
    Vector3 boundsLocalCenter;
    float boundsLocalRadius;

    bool autoRotate;
    public bool IsAutoRotating => autoRotate;

    void Awake()
    {
        if (placement == null)
            placement = GetComponent<CarPlacementController>();
    }

    void Update()
    {
        var car = placement != null ? placement.PlacedCar : null;
        if (car == null)
        {
            dragging = false;
            hasDragTarget = false;
            twoFinger = false;
            twoFingerBlocked = false;
            lastKeyRotating = false;
            boundsCar = null;
            return;
        }

        var t = car.transform;

        if (autoRotate)
            t.Rotate(Vector3.up, autoRotateSpeed * Time.deltaTime, Space.World);

        if (Input.touchCount == 2)
        {
            if (dragging) EndDrag(t);
            if (!twoFinger)
            {
                // Ownership is settled here, once, for the whole gesture.
                twoFinger = true;
                twoFingerBlocked = CarPlacementController.IsPointerOverUI(Input.GetTouch(0)) ||
                                   CarPlacementController.IsPointerOverUI(Input.GetTouch(1));
            }
            if (!twoFingerBlocked) HandleTwistAndPinch(t);
        }
        else
        {
            if (twoFinger)
            {
                twoFinger = false;
                if (!twoFingerBlocked && placement != null) placement.PersistRig();
                twoFingerBlocked = false;
            }

            if (Input.touchCount == 1)
            {
                HandleDrag(t);
            }
            else
            {
                bool shifting = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                bool rotating = Input.GetKey(KeyCode.Q) || Input.GetKey(KeyCode.E);

                if (dragging && touchDrag) EndDrag(t);
                HandleMouseDrag(t);
                HandleMouseScale(t);
                HandleKeyRotate(t);

                if (lastShiftScaling && !shifting && placement != null)
                    placement.PersistRig();
                if (lastKeyRotating && !rotating && placement != null)
                    placement.PersistRig();

                lastShiftScaling = shifting;
                lastKeyRotating = rotating;
            }
        }

        if (dragging && hasDragTarget)
            t.position = Vector3.Lerp(t.position, dragTarget, 1f - Mathf.Exp(-dragSmoothing * Time.deltaTime));
    }

    void HandleDrag(Transform car)
    {
        if (placement == null) return;
        var touch = Input.GetTouch(0);
        switch (touch.phase)
        {
            case TouchPhase.Began:
                if (!CarPlacementController.IsPointerOverUI(touch) && IsNearCar(touch.position, car))
                {
                    dragging = true;
                    touchDrag = true;
                    autoRotate = false;
                }
                break;

            case TouchPhase.Moved:
            case TouchPhase.Stationary:
                if (dragging && placement.RaycastPlane(touch.position, out Pose pose, out ARPlane plane))
                {
                    dragTarget = pose.position;
                    hasDragTarget = true;
                    if (plane != null) lastPlane = plane;
                }
                break;

            case TouchPhase.Ended:
            case TouchPhase.Canceled:
                if (dragging) EndDrag(car);
                break;
        }
    }

    void HandleMouseScale(Transform car)
    {
        if (!Input.GetKey(KeyCode.LeftShift) && !Input.GetKey(KeyCode.RightShift)) return;
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.01f)
        {
            autoRotate = false;
            float newScale = Mathf.Clamp(car.localScale.x * (1f + scroll * 0.5f), minScale, maxScale);
            car.localScale = Vector3.one * newScale;
        }
    }

    void HandleKeyRotate(Transform car)
    {
        float dir = 0f;
        if (Input.GetKey(KeyCode.Q)) dir -= 1f;
        if (Input.GetKey(KeyCode.E)) dir += 1f;
        if (dir != 0f)
        {
            autoRotate = false;
            car.Rotate(Vector3.up, dir * keyRotateSpeed * Time.deltaTime, Space.World);
        }
    }

    void HandleMouseDrag(Transform car)
    {
        if (placement == null) return;
        if (Input.GetMouseButtonDown(0) && !CarPlacementController.IsPointerOverMouse() &&
            IsNearCar(Input.mousePosition, car))
        {
            dragging = true;
            touchDrag = false;
            autoRotate = false;
        }

        if (Input.GetMouseButton(0) && dragging)
        {
            if (placement.RaycastPlane(Input.mousePosition, out Pose pose, out ARPlane plane))
            {
                dragTarget = pose.position;
                hasDragTarget = true;
                if (plane != null) lastPlane = plane;
            }
        }

        if (Input.GetMouseButtonUp(0) && dragging)
            EndDrag(car);
    }

    void EndDrag(Transform car)
    {
        dragging = false;
        hasDragTarget = false;
        if (placement == null) return;
        placement.Reanchor(new Pose(car.position, car.rotation), lastPlane);
        placement.PersistRig();
    }

    // Stored in the car's local space so moving and scaling it does not
    // invalidate the cache. Rotation does shift it slightly -- Renderer.bounds
    // is a world-space AABB, so the radius is whatever the box measured at the
    // angle it was cached at. That is fine for what this feeds: a grab test
    // that is already floored by a DPI-scaled minimum and only decides whether
    // a finger landed near enough to start dragging.
    void CacheBounds(Transform car)
    {
        boundsCar = car.gameObject;
        boundsLocalCenter = Vector3.zero;
        boundsLocalRadius = 0.5f;

        var renderers = car.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return;

        var b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            b.Encapsulate(renderers[i].bounds);

        boundsLocalCenter = car.InverseTransformPoint(b.center);
        float scale = car.lossyScale.x;
        boundsLocalRadius = Mathf.Abs(scale) > 0.0001f ? b.extents.magnitude / scale : b.extents.magnitude;
    }

    bool IsNearCar(Vector2 screenPos, Transform car)
    {
        if (cachedCamera == null) cachedCamera = Camera.main;
        if (cachedCamera == null) return false;

        if (boundsCar != car.gameObject) CacheBounds(car);

        Vector3 worldCenter = car.TransformPoint(boundsLocalCenter);
        float worldRadius = boundsLocalRadius * Mathf.Abs(car.lossyScale.x);

        Vector3 center = cachedCamera.WorldToScreenPoint(worldCenter);
        if (center.z <= 0f) return false;
        Vector3 edge = cachedCamera.WorldToScreenPoint(
            worldCenter + cachedCamera.transform.right * worldRadius);

        float screenRadius = Vector2.Distance(center, edge);
        float dpiScale = Screen.dpi > 0f ? Screen.dpi / 160f : 1f;
        float grab = Mathf.Max(screenRadius, grabRadiusPixels * dpiScale);
        return Vector2.Distance(new Vector2(center.x, center.y), screenPos) <= grab;
    }

    void HandleTwistAndPinch(Transform car)
    {
        var t0 = Input.GetTouch(0);
        var t1 = Input.GetTouch(1);
        // The over-UI test now happens once in Update, when the gesture
        // starts. Only the first frame is skipped here, because deltaPosition
        // is meaningless until both fingers have a previous position.
        if (t0.phase == TouchPhase.Began || t1.phase == TouchPhase.Began) return;

        autoRotate = false;

        Vector2 prev0 = t0.position - t0.deltaPosition;
        Vector2 prev1 = t1.position - t1.deltaPosition;
        Vector2 prevVec = prev1 - prev0;
        Vector2 curVec = t1.position - t0.position;
        if (prevVec.sqrMagnitude < 1f || curVec.sqrMagnitude < 1f) return;

        float angleDelta = Vector2.SignedAngle(prevVec, curVec);
        car.Rotate(Vector3.up, -angleDelta, Space.World);

        float ratio = curVec.magnitude / prevVec.magnitude;
        float newScale = Mathf.Clamp(car.localScale.x * ratio, minScale, maxScale);
        car.localScale = Vector3.one * newScale;
    }

    public void ToggleAutoRotate()
    {
        autoRotate = !autoRotate;
        if (!autoRotate && placement != null && placement.HasPlacedCar)
            placement.PersistRig();
    }

    public void SetAutoRotate(bool on)
    {
        if (autoRotate && !on && placement != null && placement.HasPlacedCar)
            placement.PersistRig();
        autoRotate = on;
    }
}
