using UnityEngine;

// Editor-only stand-in for a detected AR plane.
//
// EditorSimulation strips the AR stack for desktop play mode, so no real planes
// are ever produced and the plane material is invisible until you deploy. This
// builds a mesh shaped like one ARCore would generate -- an irregular triangle
// fan with the centre as the last vertex, UV0 in metres and UV1 carrying edge
// distance -- so ARPlaneGlow can be tuned without a device.
public class EditorPlanePreview : MonoBehaviour
{
    public static GameObject Create(Material material, Vector3 position, float radiusX, float radiusZ)
    {
        var go = new GameObject("PlanePreview (editor only)");
        go.transform.position = position;

        var filter = go.AddComponent<MeshFilter>();
        filter.sharedMesh = BuildFanMesh(48, radiusX, radiusZ, 0.14f);

        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

        go.AddComponent<EditorPlanePreview>();
        return go;
    }

    static Mesh BuildFanMesh(int segments, float radiusX, float radiusZ, float wobble)
    {
        var vertices = new Vector3[segments + 1];
        var uv0 = new Vector2[segments + 1];
        var uv1 = new Vector2[segments + 1];
        var normals = new Vector3[segments + 1];

        for (int i = 0; i < segments; i++)
        {
            float a = (i / (float)segments) * Mathf.PI * 2f;
            // A little wobble so the outline is irregular like a real detected
            // plane rather than a perfect ellipse.
            float r = 1f + Mathf.Sin(a * 3f) * wobble + Mathf.Sin(a * 7f) * wobble * 0.4f;
            vertices[i] = new Vector3(Mathf.Cos(a) * radiusX * r, 0f, Mathf.Sin(a) * radiusZ * r);
            uv0[i] = new Vector2(vertices[i].x, vertices[i].z);
            uv1[i] = Vector2.zero;
            normals[i] = Vector3.up;
        }

        int centre = segments;
        vertices[centre] = Vector3.zero;
        uv0[centre] = Vector2.zero;
        normals[centre] = Vector3.up;

        float centreDistance = float.MaxValue;
        for (int i = 0; i < segments; i++)
        {
            float d = DistanceToSegment(Vector3.zero, vertices[i], vertices[(i + 1) % segments]);
            if (d < centreDistance) centreDistance = d;
        }
        uv1[centre] = new Vector2(centreDistance, 0f);

        var triangles = new int[segments * 3];
        for (int i = 0; i < segments; i++)
        {
            triangles[i * 3] = centre;
            triangles[i * 3 + 1] = (i + 1) % segments;
            triangles[i * 3 + 2] = i;
        }

        var mesh = new Mesh { name = "PlanePreview" };
        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.uv = uv0;
        mesh.uv2 = uv1;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();
        return mesh;
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
