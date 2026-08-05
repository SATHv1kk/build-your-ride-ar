using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.ARFoundation;

// Drives the scene's directional light and ambient probe from ARCore's
// estimate of the real room lighting, so the car picks up the colour and
// direction of the light the phone is actually standing in.
//
// Deliberately no [RequireComponent(ARCameraManager)]: EditorSimulation strips
// the AR camera stack for desktop play mode, and a hard dependency would block
// that teardown.
public class ARLightEstimator : MonoBehaviour
{
    public Light mainLight;

    [Tooltip("Seconds for the light to catch up to a new estimate. Raw estimates " +
             "flicker frame to frame; smoothing keeps the car from strobing.")]
    public float smoothing = 0.35f;

    [Tooltip("Clamps to keep a dim room from turning the car black.")]
    public float minIntensity = 0.35f;
    public float maxIntensity = 2.0f;

    ARCameraManager cameraManager;
    Quaternion targetRotation;
    Color targetColor = Color.white;
    float targetIntensity = 1f;
    bool hasRotation;

    public bool HasEstimate { get; private set; }

    // Surfaced for the status overlay so the live estimate is inspectable
    // on device rather than only visible as a lighting change.
    public bool HasDirection { get { return hasRotation; } }
    public float TargetIntensity { get { return targetIntensity; } }
    public Color TargetColor { get { return targetColor; } }
    public string Source { get; private set; }

    void Awake()
    {
        cameraManager = GetComponent<ARCameraManager>();
        if (mainLight == null)
            mainLight = FindObjectOfType<Light>();
        if (mainLight != null)
        {
            targetRotation = mainLight.transform.rotation;
            targetColor = mainLight.color;
            targetIntensity = mainLight.intensity;
        }
    }

    void OnEnable()
    {
        if (cameraManager == null) return;
        // Environmental HDR on ARCore: main light direction/intensity plus an
        // ambient spherical harmonics probe for the surrounding bounce light.
        cameraManager.requestedLightEstimation =
            LightEstimation.AmbientSphericalHarmonics |
            LightEstimation.MainLightDirection |
            LightEstimation.MainLightIntensity |
            LightEstimation.AmbientIntensity |
            LightEstimation.AmbientColor;
        cameraManager.frameReceived += OnFrameReceived;
    }

    void OnDisable()
    {
        if (cameraManager != null)
            cameraManager.frameReceived -= OnFrameReceived;
    }

    void OnFrameReceived(ARCameraFrameEventArgs args)
    {
        var light = args.lightEstimation;

        if (light.mainLightDirection.HasValue)
        {
            var dir = light.mainLightDirection.Value;
            if (dir.sqrMagnitude > 0.0001f)
            {
                targetRotation = Quaternion.LookRotation(dir);
                hasRotation = true;
            }
        }

        if (light.mainLightColor.HasValue)
            targetColor = light.mainLightColor.Value;
        else if (light.colorCorrection.HasValue)
            targetColor = light.colorCorrection.Value;
        else if (light.averageColorTemperature.HasValue)
            // mainLight.colorTemperature only has a visible effect when
            // Light.useColorTemperature is enabled, which it isn't here -- so
            // this must feed the same targetColor the smoothing in Update()
            // actually reads, not the Light component directly.
            targetColor = Mathf.CorrelatedColorTemperatureToRGB(light.averageColorTemperature.Value);

        // Lumens is the Environmental HDR signal; averageBrightness is the
        // simpler fallback ARCore reports when HDR is unavailable.
        if (light.mainLightIntensityLumens.HasValue)
        {
            targetIntensity = light.mainLightIntensityLumens.Value / 1000f;
            Source = "HDR";
        }
        else if (light.averageBrightness.HasValue)
        {
            targetIntensity = light.averageBrightness.Value * 2f;
            Source = "Ambient";
        }

        targetIntensity = Mathf.Clamp(targetIntensity, minIntensity, maxIntensity);

        if (light.ambientSphericalHarmonics.HasValue)
        {
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.ambientProbe = light.ambientSphericalHarmonics.Value;
        }
        else if (light.averageBrightness.HasValue)
        {
            RenderSettings.ambientMode = AmbientMode.Flat;
            float b = Mathf.Clamp(light.averageBrightness.Value, 0.15f, 0.9f);
            RenderSettings.ambientLight = targetColor * b;
        }

        HasEstimate = true;
    }

    void Update()
    {
        if (mainLight == null || !HasEstimate) return;

        float s = Mathf.Max(smoothing, 0.001f);
        float t = 1f - Mathf.Exp(-Time.deltaTime / s);

        if (hasRotation)
            mainLight.transform.rotation = Quaternion.Slerp(mainLight.transform.rotation, targetRotation, t);

        mainLight.color = Color.Lerp(mainLight.color, targetColor, t);
        mainLight.intensity = Mathf.Lerp(mainLight.intensity, targetIntensity, t);
    }
}
