using System.Collections.Generic;
using UnityEngine;

// Compiling a new script does not put it in the scene. Components are added by
// editor tooling, so a scene saved before a feature existed simply runs without
// it -- no error, nothing in the console, just a feature that appears not to
// work. This says so out loud at startup instead.
public static class SceneVersionCheck
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Check()
    {
        var missing = new List<string>();

        // First, because it is the only entry here whose absence takes a
        // whole input modality with it: no CarGestureController means no
        // drag-to-move, no twist-to-rotate and no pinch-to-scale on device,
        // and nothing else in the app reports that. It went missing for
        // several versions precisely because this list did not mention it.
        if (Object.FindObjectOfType<CarGestureController>() == null)
            missing.Add("touch gestures -- drag/twist/pinch (CarGestureController)");
        if (Object.FindObjectOfType<CarPlacementController>() == null)
            missing.Add("tap to place (CarPlacementController)");
        if (Object.FindObjectOfType<CustomizePanel>() == null)
            missing.Add("colour tray (CustomizePanel)");
        if (Object.FindObjectOfType<ARStatusOverlay>() == null)
            missing.Add("live status overlay (ARStatusOverlay)");
        if (Object.FindObjectOfType<ShadowCatcher>() == null)
            missing.Add("contact shadow (ShadowCatcher)");
        if (Object.FindObjectOfType<ARLightEstimator>() == null)
            missing.Add("light estimation (ARLightEstimator)");

        if (missing.Count == 0) return;

        var sb = new System.Text.StringBuilder();
        sb.Append("[BuildYourRide] This scene is out of date -- ")
          .Append(missing.Count)
          .Append(" feature(s) are compiled but not present in the scene:\n");
        foreach (var m in missing)
            sb.Append("  - ").Append(m).Append('\n');
        sb.Append("Fix: menu BuildYourRide > Upgrade Scene to v3 (fast, in place), then save and press Play again.");

        Debug.LogWarning(sb.ToString());
    }
}
