using UnityEngine;

// Keeps an invisible quad under the placed car that renders nothing except the
// shadow falling on it. Without this the car has no contact with the real floor
// and reads as a sticker pasted over the camera feed.
public class ShadowCatcher : MonoBehaviour
{
    public CarPlacementController placement;
    public Material shadowMaterial;

    [Tooltip("Quad size as a multiple of the car's footprint. Above 1 so soft " +
             "shadows have room to fall off instead of being clipped.")]
    public float footprintPadding = 1.8f;

    GameObject quad;

    void Awake()
    {
        if (placement == null)
            placement = FindObjectOfType<CarPlacementController>();
    }

    void OnEnable()
    {
        if (placement == null) return;
        placement.CarPlaced += Attach;
        placement.CarSwapped += Attach;
        placement.CarRemoved += Clear;
        if (placement.HasPlacedCar) Attach(placement.PlacedCar);
    }

    void OnDisable()
    {
        if (placement == null) return;
        placement.CarPlaced -= Attach;
        placement.CarSwapped -= Attach;
        placement.CarRemoved -= Clear;
    }

    void Attach(GameObject car)
    {
        Clear();
        if (car == null || shadowMaterial == null) return;

        var bounds = ComputeBounds(car);
        if (bounds.size.sqrMagnitude < 0.0001f) return;

        quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = "ShadowCatcher";
        Destroy(quad.GetComponent<Collider>());

        var mr = quad.GetComponent<MeshRenderer>();
        mr.sharedMaterial = shadowMaterial;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = true;
        // The quad is a ground decal, not part of the car's silhouette; keeping
        // it out of probe/reflection passes avoids it darkening the car itself.
        mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

        var t = quad.transform;
        t.SetParent(car.transform, true);
        t.rotation = Quaternion.Euler(90f, 0f, 0f);
        t.position = new Vector3(bounds.center.x, bounds.min.y + 0.004f, bounds.center.z);

        // Size in world units, then undo the car's scale so the quad keeps the
        // footprint we measured rather than being scaled twice.
        float side = Mathf.Max(bounds.size.x, bounds.size.z) * footprintPadding;
        float carScale = car.transform.lossyScale.x;
        if (Mathf.Abs(carScale) < 0.0001f) carScale = 1f;
        t.localScale = Vector3.one * (side / carScale);
    }

    void Clear()
    {
        if (quad == null) return;
        Destroy(quad);
        quad = null;
    }

    static Bounds ComputeBounds(GameObject go)
    {
        var renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
        var b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            b.Encapsulate(renderers[i].bounds);
        return b;
    }
}
