using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

public class PlacementReticle : MonoBehaviour
{
    public ARRaycastManager raycastManager;
    public CarPlacementController placement;
    public GameObject visual;

    static readonly List<ARRaycastHit> s_Hits = new List<ARRaycastHit>();

    void Update()
    {
        if (placement == null || raycastManager == null || visual == null) return;

        if (placement.HasPlacedCar || placement.useEditorMode ||
            !raycastManager.enabled || raycastManager.subsystem == null)
        {
            if (visual.activeSelf) visual.SetActive(false);
            return;
        }

        var center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        if (raycastManager.Raycast(center, s_Hits, TrackableType.PlaneWithinPolygon))
        {
            var pose = s_Hits[0].pose;
            transform.SetPositionAndRotation(pose.position + pose.up * 0.005f, pose.rotation);
            if (!visual.activeSelf) visual.SetActive(true);
        }
        else if (visual.activeSelf)
        {
            visual.SetActive(false);
        }
    }
}
