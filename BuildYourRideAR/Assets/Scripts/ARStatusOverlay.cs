using System.Text;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

// On-device telemetry panel: what the AR session is doing, what the device
// actually supports, and what the user just changed. Built for diagnosing
// behaviour on a phone, where there is no console to read.
//
// Refreshes on a timer rather than per frame, and reuses one StringBuilder, so
// leaving it open does not itself cost frames or generate garbage.
public class ARStatusOverlay : MonoBehaviour
{
    [Header("UI")]
    public GameObject panel;
    public Text body;
    public Text toastText;
    public GameObject toastPanel;
    public Button toggleButton;
    public Text toggleLabel;

    [Header("Sources")]
    public CarPlacementController placement;
    public ARSession session;
    public ARPlaneManager planeManager;
    public ARAnchorManager anchorManager;
    public AROcclusionManager occlusionManager;
    public ARLightEstimator lightEstimator;

    [Header("Behaviour")]
    public float refreshInterval = 0.2f;
    public float toastDuration = 2.5f;
    public bool startVisible = true;

    readonly StringBuilder sb = new StringBuilder(512);

    float nextRefresh;
    float toastUntil;
    string lastChange = "";

    // Frame timing is sampled every frame but only rendered on refresh.
    float smoothedDelta = 0.016f;

    CarCustomizer boundCustomizer;

    void Awake()
    {
        if (placement == null) placement = FindObjectOfType<CarPlacementController>();
        if (session == null) session = FindObjectOfType<ARSession>();
        if (planeManager == null) planeManager = FindObjectOfType<ARPlaneManager>();
        if (anchorManager == null) anchorManager = FindObjectOfType<ARAnchorManager>();
        if (occlusionManager == null) occlusionManager = FindObjectOfType<AROcclusionManager>();
        if (lightEstimator == null) lightEstimator = FindObjectOfType<ARLightEstimator>();

        if (toggleButton != null) toggleButton.onClick.AddListener(Toggle);
        if (panel != null) panel.SetActive(startVisible);
        if (toastPanel != null) toastPanel.SetActive(false);
        UpdateToggleLabel();
    }

    void OnEnable()
    {
        if (placement == null) return;
        placement.CarPlaced += OnCarBound;
        placement.CarSwapped += OnCarBound;
        placement.CarRemoved += OnCarRemoved;
    }

    void OnDisable()
    {
        if (placement != null)
        {
            placement.CarPlaced -= OnCarBound;
            placement.CarSwapped -= OnCarBound;
            placement.CarRemoved -= OnCarRemoved;
        }
        BindCustomizer(null);
    }

    void OnCarBound(GameObject car)
    {
        BindCustomizer(car != null ? car.GetComponentInChildren<CarCustomizer>() : null);
        Toast("Placed " + (car != null ? CarCustomizer.Prettify(car.name.Replace("(Clone)", "")) : "car"));
    }

    void OnCarRemoved()
    {
        BindCustomizer(null);
        Toast("Car removed");
    }

    void BindCustomizer(CarCustomizer next)
    {
        if (boundCustomizer == next) return;
        if (boundCustomizer != null) boundCustomizer.Changed -= Toast;
        boundCustomizer = next;
        if (boundCustomizer != null) boundCustomizer.Changed += Toast;
    }

    public void Toast(string message)
    {
        lastChange = message;
        toastUntil = Time.time + toastDuration;
        if (toastText != null) toastText.text = message;
        if (toastPanel != null && !toastPanel.activeSelf) toastPanel.SetActive(true);
    }

    public void Toggle()
    {
        if (panel == null) return;
        panel.SetActive(!panel.activeSelf);
        UpdateToggleLabel();
    }

    void UpdateToggleLabel()
    {
        if (toggleLabel == null) return;
        toggleLabel.text = panel != null && panel.activeSelf ? "HIDE" : "INFO";
    }

    void Update()
    {
        smoothedDelta = Mathf.Lerp(smoothedDelta, Time.unscaledDeltaTime, 1f - Mathf.Exp(-5f * Time.unscaledDeltaTime));

        if (toastPanel != null && toastPanel.activeSelf && Time.time > toastUntil)
            toastPanel.SetActive(false);

        if (panel == null || !panel.activeSelf || body == null) return;
        if (Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + refreshInterval;

        Render();
    }

    void Render()
    {
        sb.Length = 0;

        float fps = smoothedDelta > 0.0001f ? 1f / smoothedDelta : 0f;
        sb.Append("<b>PERFORMANCE</b>\n");
        Line("FPS", fps.ToString("F0") + "   (" + (smoothedDelta * 1000f).ToString("F1") + " ms)");

        sb.Append("\n<b>AR SESSION</b>\n");
        var state = ARSession.state;
        Line("State", state.ToString());
        if (state != ARSessionState.SessionTracking)
            Line("Not tracking", ARSession.notTrackingReason.ToString());

        if (planeManager != null)
        {
            int planes = planeManager.trackables.count;
            float area = 0f;
            foreach (var p in planeManager.trackables)
                area += p.size.x * p.size.y;
            Line("Planes", planes + "   (" + area.ToString("F1") + " m2)");
            Line("Detect mode", planeManager.requestedDetectionMode.ToString());
        }

        if (anchorManager != null)
            Line("Anchors", anchorManager.trackables.count.ToString());

        sb.Append("\n<b>ENVIRONMENT</b>\n");
        RenderLight();
        RenderOcclusion();

        sb.Append("\n<b>BUILD</b>\n");
        RenderCar();

        if (!string.IsNullOrEmpty(lastChange))
            Line("Last change", lastChange);

        body.text = sb.ToString();
    }

    void RenderLight()
    {
        if (lightEstimator == null)
        {
            Line("Light est.", Warn("component missing"));
            return;
        }
        if (!lightEstimator.HasEstimate)
        {
            Line("Light est.", Warn("waiting for frames"));
            return;
        }

        Line("Light est.", Ok(lightEstimator.Source ?? "active"));
        Line("Intensity", lightEstimator.TargetIntensity.ToString("F2"));
        Line("Direction", lightEstimator.HasDirection ? Ok("tracked") : Warn("ambient only"));
    }

    void RenderOcclusion()
    {
        if (occlusionManager == null)
        {
            Line("Occlusion", Warn("component missing"));
            return;
        }

        var descriptor = occlusionManager.descriptor;
        if (descriptor == null)
        {
            Line("Occlusion", Warn("no subsystem"));
            return;
        }

        // Requested vs current is the useful distinction on device: a phone
        // without Depth support silently reports Disabled while still running.
        bool supported = descriptor.environmentDepthImageSupported == Supported.Supported;
        var current = occlusionManager.currentEnvironmentDepthMode;
        Line("Depth support", supported ? Ok("yes") : Warn(descriptor.environmentDepthImageSupported.ToString()));
        Line("Depth mode", current == EnvironmentDepthMode.Disabled
            ? Warn("Disabled")
            : Ok(current.ToString()));
    }

    void RenderCar()
    {
        if (placement == null || !placement.HasPlacedCar || boundCustomizer == null)
        {
            Line("Car", "none placed");
            return;
        }

        Line("Car", CarCustomizer.Prettify(boundCustomizer.CarKey));

        for (int i = 0; i < boundCustomizer.GroupCount; i++)
        {
            if (boundCustomizer.GetGroupOptionCount(i) == 0) continue;
            Line(boundCustomizer.GetGroupName(i),
                 boundCustomizer.GetGroupOptionName(i, boundCustomizer.GetGroupIndex(i)));
        }

        Line("Finish", boundCustomizer.GetFinishName(boundCustomizer.FinishIndex));
        Line("Spoiler", boundCustomizer.SpoilerEnabled ? "On" : "Off");

        var t = placement.PlacedCar.transform;
        Line("Scale", t.localScale.x.ToString("F2") + "x");
        Line("Saved build", boundCustomizer.HasSavedBuild ? "yes" : "not yet");
    }

    void Line(string label, string value)
    {
        sb.Append(label).Append(": ").Append(value).Append('\n');
    }

    static string Ok(string s) { return "<color=#7BE38B>" + s + "</color>"; }
    static string Warn(string s) { return "<color=#F0C36B>" + s + "</color>"; }
}
