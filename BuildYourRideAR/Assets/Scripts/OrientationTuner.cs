using UnityEngine;

// Editor-only dev tool for finding the correct CarImporter.orientationOverride
// for a car by eye, instead of guessing Euler angles blind and re-importing
// each time. Rotates the placed car's "Model" child (the same transform
// orientationOverride is baked into at import time) with keyboard controls
// and prints the resulting angles ready to paste into code.
//
// Self-attaches next to CarPlacementController, the same way EditorSimulation
// and ARDiagnosticLog do, so it needs no scene wiring and never ships in a
// device build (Application.isEditor guards it out).
//
// Usage: Play in editor, place the car that needs fixing, press T to start
// tuning. I/K pitch, J/L yaw, U/O roll, hold Shift for 5x speed, R resets to
// the orientation the prefab was imported with, T again to stop. The Console
// logs the resulting angles every 0.5s while rotating -- copy the
// "orientationOverride = ..." line straight into CarImporter.cs's matching
// AutoImportEntry and run BuildYourRide > Rebuild All to bake it in.
public class OrientationTuner : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoAttach()
    {
        if (!Application.isEditor) return;
        if (FindObjectOfType<OrientationTuner>() != null) return;
        var placement = FindObjectOfType<CarPlacementController>();
        if (placement != null) placement.gameObject.AddComponent<OrientationTuner>();
    }

    const float BaseRate = 20f;   // degrees/sec
    const float FastRate = 100f;  // degrees/sec, Shift held

    CarPlacementController placement;
    Transform model;
    bool tuning;
    Quaternion importedRotation;
    float nextLog;

    void Awake()
    {
        placement = GetComponent<CarPlacementController>();
    }

    void Update()
    {
        if (placement == null || !placement.HasPlacedCar)
        {
            tuning = false;
            return;
        }

        if (Input.GetKeyDown(KeyCode.T))
        {
            tuning = !tuning;
            model = placement.PlacedCar.transform.Find("Model");
            if (model == null)
            {
                Debug.LogWarning("[OrientationTuner] Placed car has no 'Model' child; nothing to tune.");
                tuning = false;
                return;
            }

            if (tuning)
            {
                importedRotation = model.localRotation;
                Debug.Log("[OrientationTuner] Tuning ON for " +
                    placement.PlacedCar.name.Replace("(Clone)", string.Empty) +
                    ".  I/K = pitch, J/L = yaw, U/O = roll, Shift = 5x speed, R = reset, T = stop.");
            }
            else
            {
                Debug.Log("[OrientationTuner] Tuning OFF.");
            }
        }

        if (!tuning || model == null) return;

        if (Input.GetKeyDown(KeyCode.R))
        {
            model.localRotation = importedRotation;
            Debug.Log("[OrientationTuner] Reset to the orientation the prefab was imported with.");
        }

        bool fast = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        float rate = (fast ? FastRate : BaseRate) * Time.deltaTime;
        bool moved = false;

        // World-space axes so each key always tilts the same visible
        // direction on screen no matter how the model is currently rotated.
        if (Input.GetKey(KeyCode.I)) { model.Rotate(Vector3.right, -rate, Space.World); moved = true; }
        if (Input.GetKey(KeyCode.K)) { model.Rotate(Vector3.right, rate, Space.World); moved = true; }
        if (Input.GetKey(KeyCode.J)) { model.Rotate(Vector3.up, -rate, Space.World); moved = true; }
        if (Input.GetKey(KeyCode.L)) { model.Rotate(Vector3.up, rate, Space.World); moved = true; }
        if (Input.GetKey(KeyCode.U)) { model.Rotate(Vector3.forward, -rate, Space.World); moved = true; }
        if (Input.GetKey(KeyCode.O)) { model.Rotate(Vector3.forward, rate, Space.World); moved = true; }

        if (moved && Time.time >= nextLog)
        {
            nextLog = Time.time + 0.5f;
            var e = model.localEulerAngles;
            // Unity reports Euler angles in [0, 360); a rotation you think of
            // as "-10" shows up as "350". Both are the same rotation -- pick
            // whichever reads more naturally when you paste it in.
            Debug.Log("[OrientationTuner] localEulerAngles = (" +
                e.x.ToString("F1") + ", " + e.y.ToString("F1") + ", " + e.z.ToString("F1") +
                ")   ->   orientationOverride = new Vector3(" +
                e.x.ToString("F0") + "f, " + e.y.ToString("F0") + "f, " + e.z.ToString("F0") + "f)");
        }
    }
}
