using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;

public class UIController : MonoBehaviour
{
    public CarPlacementController placement;
    public ARPlaneManager planeManager;
    public ARSession arSession;
    public CustomizePanel customizePanel;
    public CarGestureController gestureController;
    public Button carButton;
    public Button colorButton;
    public Button spoilerButton;
    public Button wheelsButton;
    public Button removeButton;
    public Button rotateButton;
    public Text hintText;
    public GameObject hintPanel;

    CarCustomizer customizer;
    Text spoilerBtnLabel;
    Text rotateBtnLabel;
    float placedAt = -999f;

    void Awake()
    {
        // AR sessions are long-lived and hands-free; never let the screen dim.
        Screen.sleepTimeout = SleepTimeout.NeverSleep;

        if (gestureController == null && placement != null)
            gestureController = placement.GetComponent<CarGestureController>();

        if (carButton != null) carButton.onClick.AddListener(OnCarClicked);
        if (colorButton != null) colorButton.onClick.AddListener(OnColorClicked);
        if (spoilerButton != null) spoilerButton.onClick.AddListener(OnSpoilerClicked);
        if (wheelsButton != null) wheelsButton.onClick.AddListener(OnWheelsClicked);
        if (removeButton != null) removeButton.onClick.AddListener(OnRemoveClicked);
        if (rotateButton != null) rotateButton.onClick.AddListener(OnRotateClicked);
        spoilerBtnLabel = spoilerButton != null ? spoilerButton.GetComponentInChildren<Text>() : null;
        rotateBtnLabel = rotateButton != null ? rotateButton.GetComponentInChildren<Text>() : null;
        SetCustomizeButtons(false);
    }

    void OnEnable()
    {
        if (placement == null) return;
        placement.CarPlaced += OnCarPlaced;
        placement.CarSwapped += OnCarSwapped;
        placement.CarRemoved += OnCarRemoved;
    }

    void OnDisable()
    {
        if (placement == null) return;
        placement.CarPlaced -= OnCarPlaced;
        placement.CarSwapped -= OnCarSwapped;
        placement.CarRemoved -= OnCarRemoved;
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape)) OnBack();

        if (placement == null || hintText == null) return;

        if (!placement.HasPlacedCar)
        {
            if (hintPanel != null && !hintPanel.activeSelf) hintPanel.SetActive(true);

            if (placement.useEditorMode)
            {
                hintText.text = "Click to place your car";
                return;
            }

            bool arRunning = arSession != null &&
                arSession.subsystem != null &&
                arSession.subsystem.running;
            bool hasPlanes = planeManager != null && planeManager.trackables.count > 0;

            if (!arRunning && Time.time > 3f)
                hintText.text = "AR not available. Check camera permission.";
            else if (hasPlanes)
                hintText.text = "Tap to place your car";
            else
                hintText.text = "Anchor space not detected. Move your phone slowly to scan the floor.";
        }
        else if (Time.time - placedAt < 6f)
        {
            if (hintPanel != null && !hintPanel.activeSelf) hintPanel.SetActive(true);
            hintText.text = placement.useEditorMode
                ? "Drag the car to move  |  Left/Right arrows rotate  |  Shift+scroll scale"
                : "Drag the car to move  |  Twist to rotate  |  Pinch to scale";
        }
        else if (hintPanel != null && hintPanel.activeSelf)
        {
            hintPanel.SetActive(false);
        }
    }

    // Android back button. Steps back out of whatever is open instead of
    // dropping the user straight to the home screen.
    void OnBack()
    {
        if (customizePanel != null && customizePanel.IsOpen)
        {
            customizePanel.Hide();
            return;
        }
        if (placement != null && placement.HasPlacedCar)
        {
            placement.RemoveCar();
            return;
        }
        Application.Quit();
    }

    void OnCarClicked()
    {
        CloseColourTray();
        if (placement != null) placement.NextCar();
    }

    void OnColorClicked()
    {
        if (customizePanel != null && customizer != null) customizePanel.Toggle();
        else if (customizer != null) customizer.NextPaint();
    }

    void OnSpoilerClicked()
    {
        CloseColourTray();
        if (customizer == null) return;
        customizer.ToggleSpoiler();
        UpdateSpoilerLabel();
    }

    void OnWheelsClicked()
    {
        CloseColourTray();
        if (customizer != null) customizer.NextWheels();
    }

    void OnRemoveClicked()
    {
        CloseColourTray();
        if (placement != null) placement.RemoveCar();
    }

    // Only one overlay open at a time: tapping any action other than COLOR
    // itself closes the colour tray first, leaving whatever was just
    // selected visible instead of hidden behind it. Closing here (before
    // the action runs) rather than after means Bind()'s own "reopen if it
    // was open" logic on a car swap naturally sees it as already closed --
    // no separate special-casing needed there.
    void CloseColourTray()
    {
        if (customizePanel != null && customizePanel.IsOpen) customizePanel.Hide();
    }

    void OnRotateClicked()
    {
        if (gestureController != null)
        {
            gestureController.ToggleAutoRotate();
            UpdateRotateLabel();
        }
    }

    void OnCarPlaced(GameObject car)
    {
        placedAt = Time.time;
        Bind(car);
    }

    void OnCarSwapped(GameObject car)
    {
        Bind(car);
    }

    void Bind(GameObject car)
    {
        customizer = car.GetComponentInChildren<CarCustomizer>();
        bool hasCustomizer = customizer != null;

        if (colorButton != null)
            colorButton.interactable = hasCustomizer && customizer.GroupCount > 0;
        // Grey out rather than silently no-op: the placeholder cars have wheel
        // sets and the imported cars have a spoiler, but not every car has both.
        if (spoilerButton != null)
        {
            bool hasSpoiler = hasCustomizer && customizer.HasSpoiler;
            spoilerButton.interactable = hasSpoiler;
        }
        if (wheelsButton != null)
            wheelsButton.interactable = hasCustomizer && customizer.HasWheelSets;
        if (removeButton != null) removeButton.interactable = true;
        if (rotateButton != null) rotateButton.interactable = true;

        if (customizePanel != null)
        {
            bool wasOpen = customizePanel.IsOpen;
            customizePanel.Bind(customizer);
            if (wasOpen && hasCustomizer) customizePanel.Show();
        }

        UpdateSpoilerLabel();
        UpdateRotateLabel();
    }

    void OnCarRemoved()
    {
        customizer = null;
        placedAt = -999f;
        if (customizePanel != null)
        {
            customizePanel.Hide();
            customizePanel.Bind(null);
        }
        if (gestureController != null)
            gestureController.SetAutoRotate(false);
        UpdateRotateLabel();
        SetCustomizeButtons(false);
    }

    void UpdateSpoilerLabel()
    {
        if (spoilerBtnLabel == null || customizer == null) return;
        if (customizer.NoSpoilerAvailable)
            spoilerBtnLabel.text = "NO SPOILER";
        else
            spoilerBtnLabel.text = customizer.SpoilerEnabled ? "SPOILER ON" : "SPOILER OFF";
    }

    void UpdateRotateLabel()
    {
        if (rotateBtnLabel == null) return;
        bool on = gestureController != null && gestureController.IsAutoRotating;
        rotateBtnLabel.text = on ? "ROTATE ON" : "ROTATE OFF";
    }

    void SetCustomizeButtons(bool on)
    {
        if (carButton != null) carButton.interactable = true;
        if (colorButton != null) colorButton.interactable = on && customizer != null;
        if (spoilerButton != null) spoilerButton.interactable = on && customizer != null && customizer.HasSpoiler;
        if (wheelsButton != null) wheelsButton.interactable = on && customizer != null && customizer.HasWheelSets;
        if (removeButton != null) removeButton.interactable = on;
        if (rotateButton != null) rotateButton.interactable = on;
    }
}
