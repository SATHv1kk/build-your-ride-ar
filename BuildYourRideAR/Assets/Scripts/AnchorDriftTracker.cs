using UnityEngine;
using UnityEngine.XR.ARFoundation;

// Instruments the drift the original project report named as its main unsolved
// problem. Writes into the ARDiagnosticLog file.
//
// The central measurement is the *divergence* between how far the anchor has
// moved and how far the car has moved since placement:
//
//   anchorDelta = anchor.position - anchorPositionAtPlacement
//   carDelta    = car.position    - carPositionAtPlacement
//   divergence  = |carDelta - anchorDelta|
//
// Reading it:
//   divergence ~ 0, both deltas growing  -> ARCore re-estimating the world.
//                                           The car is correctly following its
//                                           anchor. Working as intended.
//   divergence growing                   -> the car has decoupled from its
//                                           anchor. A parenting bug, not a
//                                           tracking problem.
//
// Motion is also classified as creep (continuous small refinement) versus jumps
// (sudden relocalisation), and every jump is stamped with the session and
// anchor tracking state at that instant.
public class AnchorDriftTracker : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoAttach()
    {
        if (FindObjectOfType<AnchorDriftTracker>() != null) return;
        var go = new GameObject("~AnchorDriftTracker");
        DontDestroyOnLoad(go);
        go.AddComponent<AnchorDriftTracker>();
    }

    [Tooltip("Per-frame movement above this counts as a jump rather than creep, in metres.")]
    public float jumpThreshold = 0.02f;

    [Tooltip("Divergence above this means the car is no longer following its anchor, in metres.")]
    public float divergenceAlarm = 0.05f;

    [Tooltip("Seconds between drift summaries.")]
    public float summaryInterval = 5f;

    [Tooltip("Spawn a small marker at the anchor. If the marker holds still " +
             "while the car wanders, parenting is broken rather than tracking. " +
             "Ignored in a non-development player -- see ShowMarker.")]
    public bool showAnchorMarker = true;

    // Unlike OrientationTuner, PartPicker and EditorSimulation, this tracker
    // has no Application.isEditor guard on AutoAttach -- deliberately, because
    // drift is the one problem that only reproduces on a real phone. The
    // *marker*, though, is a bright orange 6 cm sphere parented to the anchor,
    // which means it renders inside the car in the shipped app. Keep the
    // instrumentation, drop the prop.
    bool ShowMarker
    {
        get { return showAnchorMarker && (Application.isEditor || Debug.isDebugBuild); }
    }

    CarPlacementController placement;

    bool tracking;
    Vector3 carOrigin;
    Vector3 anchorOrigin;
    Vector3 lastCarPos;
    Vector3 lastAnchorPos;

    float creepDistance;      // summed small movements
    float jumpDistance;       // summed large movements
    int jumpCount;
    float maxDivergence;
    bool divergenceReported;
    float nextSummary;

    GameObject marker;

    void Start()
    {
        placement = FindObjectOfType<CarPlacementController>();
        if (placement == null) return;
        placement.CarPlaced += OnCarPlaced;
        placement.CarSwapped += OnCarPlaced;
        placement.CarRemoved += OnCarRemoved;
    }

    void OnDestroy()
    {
        if (placement == null) return;
        placement.CarPlaced -= OnCarPlaced;
        placement.CarSwapped -= OnCarPlaced;
        placement.CarRemoved -= OnCarRemoved;
    }

    void OnCarPlaced(GameObject car)
    {
        if (car == null) return;

        var anchor = placement.CurrentAnchor;

        ARDiagnosticLog.WriteSection("DRIFT TRACKING STARTED");
        ARDiagnosticLog.Write("car origin    " + V(car.transform.position));

        if (anchor == null)
        {
            ARDiagnosticLog.WriteRaw("anchor        NONE (editor mode, or anchoring unavailable)");
            ARDiagnosticLog.WriteRaw("Drift tracking is meaningless without an anchor; skipping.");
            tracking = false;
            ARDiagnosticLog.FlushNow();
            return;
        }

        ARDiagnosticLog.WriteRaw("anchor origin " + V(anchor.transform.position));
        ARDiagnosticLog.WriteRaw("anchor type   " +
            (placement.AnchorIsPlaneAttached ? "PLANE-ATTACHED (good)" : "FREE-FLOATING (drift-prone)"));
        if (!string.IsNullOrEmpty(placement.LastAnchorNote))
            ARDiagnosticLog.WriteRaw("anchor note   " + placement.LastAnchorNote);
        ARDiagnosticLog.WriteRaw("anchor state  " + anchor.trackingState);
        ARDiagnosticLog.WriteRaw("car parent    " +
            (car.transform.parent != null ? car.transform.parent.name : "NONE <-- car is not parented to the anchor"));

        LogAnchorCount();

        carOrigin = car.transform.position;
        anchorOrigin = anchor.transform.position;
        lastCarPos = carOrigin;
        lastAnchorPos = anchorOrigin;

        creepDistance = 0f;
        jumpDistance = 0f;
        jumpCount = 0;
        maxDivergence = 0f;
        divergenceReported = false;
        nextSummary = Time.time + summaryInterval;
        tracking = true;

        if (ShowMarker) CreateMarker(anchor.transform);
        ARDiagnosticLog.FlushNow();
    }

    void OnCarRemoved()
    {
        if (tracking) WriteSummary("FINAL");
        tracking = false;
        DestroyMarker();
    }

    // More than one anchor after placement means Reanchor is leaving strays
    // behind, and they compete. Zero means anchoring never happened.
    void LogAnchorCount()
    {
        var anchors = FindObjectsOfType<ARAnchor>();
        ARDiagnosticLog.WriteRaw("anchors alive " + anchors.Length +
            (anchors.Length == 0 ? "  <-- nothing is anchored"
             : anchors.Length > 1 ? "  <-- MORE THAN ONE, stale anchors may be competing" : ""));
        foreach (var a in anchors)
        {
            ARDiagnosticLog.WriteRaw("   " + a.gameObject.name +
                "  state=" + a.trackingState +
                "  pos=" + V(a.transform.position) +
                "  children=" + a.transform.childCount);
        }
    }

    void Update()
    {
        if (!tracking || placement == null) return;

        if (!placement.HasPlacedCar)
        {
            tracking = false;
            DestroyMarker();
            return;
        }

        var anchor = placement.CurrentAnchor;
        if (anchor == null) return;

        Vector3 carPos = placement.PlacedCar.transform.position;
        Vector3 anchorPos = anchor.transform.position;

        float carStep = Vector3.Distance(carPos, lastCarPos);
        float anchorStep = Vector3.Distance(anchorPos, lastAnchorPos);

        // Ignore frames where the user is dragging: that movement is intended.
        bool userDriving = Input.touchCount > 0 || Input.GetMouseButton(0);

        if (!userDriving && carStep > 0.0005f)
        {
            if (carStep >= jumpThreshold)
            {
                jumpDistance += carStep;
                jumpCount++;
                LogJump(carStep, anchorStep, anchor);
            }
            else
            {
                creepDistance += carStep;
            }
        }

        Vector3 carDelta = carPos - carOrigin;
        Vector3 anchorDelta = anchorPos - anchorOrigin;
        float divergence = Vector3.Distance(carDelta, anchorDelta);
        if (divergence > maxDivergence) maxDivergence = divergence;

        // The decisive signal: the car is no longer moving with its anchor.
        if (!userDriving && !divergenceReported && divergence > divergenceAlarm)
        {
            divergenceReported = true;
            ARDiagnosticLog.WriteSection("DIVERGENCE ALARM");
            ARDiagnosticLog.Write("Car has decoupled from its anchor by " +
                                  divergence.ToString("F3") + " m");
            ARDiagnosticLog.WriteRaw("car moved     " + carDelta.magnitude.ToString("F3") + " m " + V(carDelta));
            ARDiagnosticLog.WriteRaw("anchor moved  " + anchorDelta.magnitude.ToString("F3") + " m " + V(anchorDelta));
            ARDiagnosticLog.WriteRaw("car parent    " +
                (placement.PlacedCar.transform.parent != null
                    ? placement.PlacedCar.transform.parent.name
                    : "NONE"));
            ARDiagnosticLog.WriteRaw("VERDICT: this is a parenting problem, not a tracking problem. " +
                                     "A car following its anchor keeps divergence near zero even while both move.");
            LogAnchorCount();
            ARDiagnosticLog.FlushNow();
        }

        lastCarPos = carPos;
        lastAnchorPos = anchorPos;

        if (Time.time >= nextSummary)
        {
            nextSummary = Time.time + summaryInterval;
            WriteSummary("PERIODIC");
        }
    }

    void LogJump(float carStep, float anchorStep, ARAnchor anchor)
    {
        ARDiagnosticLog.Write("DRIFT JUMP  car moved " + carStep.ToString("F3") +
                              " m in one frame (anchor moved " + anchorStep.ToString("F3") + " m)");
        ARDiagnosticLog.WriteRaw("session      " + ARSession.state +
                                 "   notTracking=" + ARSession.notTrackingReason);
        ARDiagnosticLog.WriteRaw("anchor state " + anchor.trackingState +
                                 "   planeAttached=" + placement.AnchorIsPlaneAttached);
        ARDiagnosticLog.WriteRaw("interpretation: a sudden step like this is relocalisation, " +
                                 "not gradual SLAM refinement.");
    }

    void WriteSummary(string label)
    {
        float net = Vector3.Distance(
            placement != null && placement.HasPlacedCar
                ? placement.PlacedCar.transform.position
                : carOrigin,
            carOrigin);

        ARDiagnosticLog.Write("DRIFT SUMMARY (" + label + ")");
        ARDiagnosticLog.WriteRaw("net displacement from placement  " + net.ToString("F3") + " m");
        ARDiagnosticLog.WriteRaw("creep (continuous refinement)    " + creepDistance.ToString("F3") + " m");
        ARDiagnosticLog.WriteRaw("jumps (relocalisation)           " + jumpDistance.ToString("F3") +
                                 " m over " + jumpCount + " event(s)");
        ARDiagnosticLog.WriteRaw("max anchor/car divergence        " + maxDivergence.ToString("F3") + " m" +
            (maxDivergence > divergenceAlarm ? "  <-- car is not following its anchor" : "  (car is following its anchor)"));
        ARDiagnosticLog.WriteRaw("anchor type                      " +
            (placement != null && placement.AnchorIsPlaneAttached ? "plane-attached" : "free-floating"));
        ARDiagnosticLog.FlushNow();
    }

    // A marker parented to the anchor. If it sits still while the car wanders,
    // the parenting is broken; if both move together, the world is being
    // re-estimated and the anchor is doing its job.
    void CreateMarker(Transform anchorTransform)
    {
        DestroyMarker();
        marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        marker.name = "AnchorMarker (debug)";
        Destroy(marker.GetComponent<Collider>());
        marker.transform.SetParent(anchorTransform, false);
        marker.transform.localPosition = Vector3.zero;
        marker.transform.localScale = Vector3.one * 0.06f;

        var r = marker.GetComponent<MeshRenderer>();
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        var shader = Shader.Find("Unlit/Color");
        if (shader != null)
        {
            r.sharedMaterial = new Material(shader) { color = new Color(1f, 0.25f, 0.1f) };
        }
    }

    void DestroyMarker()
    {
        if (marker == null) return;
        // The sharedMaterial on this sphere is a runtime-created instance,
        // so it must be explicitly destroyed before the GameObject.
        var mr = marker.GetComponent<MeshRenderer>();
        if (mr != null && mr.sharedMaterial != null)
            Destroy(mr.sharedMaterial);
        Destroy(marker);
        marker = null;
    }

    static string V(Vector3 v)
    {
        return "(" + v.x.ToString("F3") + ", " + v.y.ToString("F3") + ", " + v.z.ToString("F3") + ")";
    }
}
