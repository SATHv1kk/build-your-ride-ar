using System.Collections.Generic;
using UnityEngine;

// Editor-only dev tool: lets you isolate individual renderers near the
// car's known spoiler/wing mesh by hiding/showing them one at a time, so you
// can identify which mesh is which by watching exactly what appears or
// disappears -- instead of guessing from a name like "TwiXeR_992_gt3rs_
// spoiler" and hoping it's the right part.
//
// Ordered by physical distance from the wing/spoiler, not by front/back half
// or parent/child hierarchy -- this FBX's hierarchy is flat (every part is a
// direct child of "Model"), which is exactly what made a hierarchy- or
// half-based approach useless here (see CarCustomizer.DetectBuiltInSpoiler's
// v4.8 postmortem). Proximity is the one thing that reliably works
// regardless of how the source file's hierarchy or axes are set up.
//
// Self-attaches next to CarPlacementController, the same way
// OrientationTuner/EditorSimulation do, so it needs no scene wiring and
// never ships in a device build (Application.isEditor guards it out).
//
// Usage: Play in editor, place a car, press G to scan and start picking.
// Console prints the full numbered list once (closest to the spoiler/wing
// first), then the currently selected part on every move. ] / [ step to the
// next/previous part, Space toggles the selected part's visibility, G again
// turns picking off (does not restore hidden parts -- press Space again on
// the same part, or just replace the car, to bring it back).
public class PartPicker : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoAttach()
    {
        if (!Application.isEditor) return;
        if (FindObjectOfType<PartPicker>() != null) return;
        var placement = FindObjectOfType<CarPlacementController>();
        if (placement != null) placement.gameObject.AddComponent<PartPicker>();
    }

    CarPlacementController placement;
    bool active;
    readonly List<Renderer> parts = new List<Renderer>();
    int index;

    void Awake()
    {
        placement = GetComponent<CarPlacementController>();
    }

    void Update()
    {
        if (placement == null || !placement.HasPlacedCar)
        {
            active = false;
            return;
        }

        if (Input.GetKeyDown(KeyCode.G))
        {
            active = !active;
            if (active) ScanNearSpoiler();
            else Debug.Log("[PartPicker] OFF.");
        }

        if (!active || parts.Count == 0) return;

        if (Input.GetKeyDown(KeyCode.RightBracket)) { index = (index + 1) % parts.Count; Announce(); }
        if (Input.GetKeyDown(KeyCode.LeftBracket)) { index = (index - 1 + parts.Count) % parts.Count; Announce(); }

        if (Input.GetKeyDown(KeyCode.Space))
        {
            var r = parts[index];
            if (r != null)
            {
                r.enabled = !r.enabled;
                Debug.Log("[PartPicker] [" + index + "] " + r.name + " -> " + (r.enabled ? "VISIBLE" : "HIDDEN"));
            }
        }
    }

    void ScanNearSpoiler()
    {
        parts.Clear();
        index = 0;
        var car = placement.PlacedCar;
        if (car == null) return;

        var renderers = car.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return;

        // Anchor on the mesh(es) we already know are the spoiler/wing (same
        // keywords CarCustomizer.DetectBuiltInSpoiler() uses), then order
        // every other renderer by physical distance from there -- closest
        // first. This works regardless of front/back axis conventions (which
        // turned out not to be consistent across cars -- see git history)
        // and regardless of hierarchy shape (this FBX's hierarchy is flat,
        // so "connected via parent" would just mean "everything").
        Vector3? anchor = null;
        var anchorNames = new List<string>();
        foreach (var r in renderers)
        {
            string n = r.name.ToLower();
            if (!n.Contains("spoiler") && !n.Contains("wing")) continue;
            anchor = anchor.HasValue ? (anchor.Value + r.bounds.center) : r.bounds.center;
            anchorNames.Add(r.name);
        }

        if (anchor.HasValue && anchorNames.Count > 0)
            anchor = anchor.Value / anchorNames.Count;

        if (!anchor.HasValue)
        {
            // No spoiler/wing mesh on this car at all -- fall back to the
            // car's overall centre so the tool still does something useful.
            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            anchor = bounds.center;
            Debug.Log("[PartPicker] No spoiler/wing mesh found on this car; anchoring on its overall centre instead.");
        }
        else
        {
            Debug.Log("[PartPicker] Anchored on: " + string.Join(", ", anchorNames));
        }

        Vector3 anchorPos = anchor.Value;
        float Distance(Renderer r) => (r.bounds.center - anchorPos).sqrMagnitude;

        parts.AddRange(renderers);
        parts.Sort((a, b) => Distance(a).CompareTo(Distance(b)));

        var sb = new System.Text.StringBuilder("[PartPicker] ON -- " + parts.Count + " part(s) on " +
            car.name.Replace("(Clone)", string.Empty) + ", closest to the spoiler/wing first. ] / [ = next/prev, Space = toggle visible, G = off.\n");
        for (int i = 0; i < parts.Count; i++)
            sb.Append("  [").Append(i).Append("] ").Append(parts[i] != null ? parts[i].name : "?").Append('\n');
        Debug.Log(sb.ToString());

        Announce();
    }

    void Announce()
    {
        if (index < 0 || index >= parts.Count) return;
        var r = parts[index];
        Debug.Log("[PartPicker] Selected [" + index + "] " + (r != null ? r.name : "?") +
            (r != null ? "  (currently " + (r.enabled ? "visible" : "hidden") + ")" : ""));
    }
}
