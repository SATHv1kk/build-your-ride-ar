using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

// AR Foundation's generated plane mesh carries positions, normals and a
// session-space UV, but nothing describing how close a vertex is to the plane's
// boundary -- so a shader cannot fade the edges on its own. This fills that gap
// by writing per-vertex edge distance (in metres) into UV1 after the mesh is
// regenerated, which ARPlaneGlow reads to feather the outline.
//
// The generated mesh is a triangle fan: the last vertex is the centre and every
// preceding vertex lies on the boundary.
[RequireComponent(typeof(ARPlane))]
[RequireComponent(typeof(MeshFilter))]
public class ARPlaneFeather : MonoBehaviour
{
    ARPlane plane;
    MeshFilter meshFilter;
    bool dirty = true;

    static readonly List<Vector3> s_Vertices = new List<Vector3>();
    static readonly List<Vector2> s_EdgeUvs = new List<Vector2>();

    void Awake()
    {
        plane = GetComponent<ARPlane>();
        meshFilter = GetComponent<MeshFilter>();
    }

    void OnEnable()
    {
        dirty = true;
        if (plane != null) plane.boundaryChanged += OnBoundaryChanged;
    }

    void OnDisable()
    {
        if (plane != null) plane.boundaryChanged -= OnBoundaryChanged;
    }

    void OnBoundaryChanged(ARPlaneBoundaryChangedEventArgs args)
    {
        dirty = true;
    }

    // LateUpdate, not the boundaryChanged handler: ARPlaneMeshVisualizer
    // rebuilds the mesh from that same event, and subscriber order is not
    // guaranteed. By LateUpdate the new mesh is definitely in place.
    void LateUpdate()
    {
        if (!dirty) return;
        var mesh = meshFilter != null ? meshFilter.sharedMesh : null;
        if (mesh == null) return;

        mesh.GetVertices(s_Vertices);
        int count = s_Vertices.Count;
        // Not generated yet. Stay dirty and retry next frame rather than
        // clearing the flag, or the plane would render fully transparent until
        // its boundary happened to change again.
        if (count < 4) return;

        s_EdgeUvs.Clear();
        int boundaryCount = count - 1;

        for (int i = 0; i < boundaryCount; i++)
            s_EdgeUvs.Add(Vector2.zero);

        // The centre vertex gets its true distance to the nearest boundary
        // edge, so the fan interpolates a smooth 0-at-the-rim ramp.
        float centreDistance = float.MaxValue;
        Vector3 centre = s_Vertices[boundaryCount];
        for (int i = 0; i < boundaryCount; i++)
        {
            Vector3 a = s_Vertices[i];
            Vector3 b = s_Vertices[(i + 1) % boundaryCount];
            float d = DistanceToSegment(centre, a, b);
            if (d < centreDistance) centreDistance = d;
        }
        if (centreDistance == float.MaxValue) centreDistance = 0f;
        s_EdgeUvs.Add(new Vector2(centreDistance, 0f));

        mesh.SetUVs(1, s_EdgeUvs);
        dirty = false;
    }

    static float DistanceToSegment(Vector3 point, Vector3 a, Vector3 b)
    {
        Vector3 ab = b - a;
        float lengthSq = ab.sqrMagnitude;
        if (lengthSq < 1e-8f) return Vector3.Distance(point, a);
        float t = Mathf.Clamp01(Vector3.Dot(point - a, ab) / lengthSq);
        return Vector3.Distance(point, a + ab * t);
    }
}
