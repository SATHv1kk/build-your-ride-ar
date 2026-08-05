using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// The colour tray. Cars differ in which parts they expose -- the Porsche has
// body, rims, callipers and trim; a placeholder box car has only body -- so the
// tabs and swatches are rebuilt for whichever car is currently placed rather
// than authored once in the scene.
public class CustomizePanel : MonoBehaviour
{
    public GameObject panel;
    public RectTransform partRow;
    public RectTransform swatchRow;
    public RectTransform finishRow;

    [Header("Sprites")]
    public Sprite swatchSprite;
    public Sprite tabSprite;

    [Header("Style")]
    public Color tabIdle = new Color(0.10f, 0.10f, 0.13f, 0.75f);
    public Color tabActive = new Color(0.26f, 0.40f, 0.85f, 0.92f);
    public Color swatchOutline = new Color(1f, 1f, 1f, 0.95f);

    Font font;
    CarCustomizer customizer;
    int activeGroup;

    readonly List<Button> partButtons = new List<Button>();
    // Parallel to partButtons: the real partGroups[] index each button
    // targets. Needed because Wheels being skipped from the tab strip means
    // a button's position in partButtons is no longer the same number as
    // its group index -- e.g. Callipers is group 2, but is the second
    // (position-1) button once Wheels (group 1) stops getting a tab.
    // RefreshPartTabs() has to compare against this, not the loop position,
    // or the active tab highlight silently stops matching anything past the
    // first skipped group.
    readonly List<int> partButtonGroups = new List<int>();
    readonly List<Button> swatchButtons = new List<Button>();
    readonly List<Button> finishButtons = new List<Button>();

    public bool IsOpen
    {
        get { return panel != null && panel.activeSelf; }
    }

    void Awake()
    {
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (panel != null) panel.SetActive(false);
    }

    public void Bind(CarCustomizer target)
    {
        customizer = target;
        activeGroup = 0;
        if (customizer == null)
        {
            Hide();
            return;
        }
        BuildPartTabs();
        BuildFinishes();
        BuildSwatches();
    }

    public void Toggle()
    {
        if (IsOpen) Hide();
        else Show();
    }

    // Contents are built by Bind and by the part tabs, so revealing the tray
    // is just a SetActive -- rebuilding here would do the work twice on a car
    // swap that happens while the tray is open.
    public void Show()
    {
        if (panel == null || customizer == null) return;
        panel.SetActive(true);
    }

    public void Hide()
    {
        if (panel != null) panel.SetActive(false);
    }

    // ---- construction ------------------------------------------------------

    void BuildPartTabs()
    {
        ClearRow(partRow, partButtons);
        partButtonGroups.Clear();
        if (partRow == null || customizer == null) return;

        int groups = customizer.GroupCount;
        int visibleGroups = 0;
        for (int i = 0; i < groups; i++)
        {
            if (customizer.GetGroupOptionCount(i) == 0) continue;
            // Rims live only in the dedicated WHEELS button on the bottom
            // bar now -- having the same choice reachable two different
            // ways (here and there) is exactly the redundant-path clutter
            // a clean interface avoids.
            if (customizer.GetGroupName(i) == "Wheels") continue;
            visibleGroups++;
        }

        // One paintable part is not a choice; hide the tab strip entirely.
        partRow.gameObject.SetActive(visibleGroups > 1);
        if (visibleGroups <= 1) return;

        for (int i = 0; i < groups; i++)
        {
            if (customizer.GetGroupOptionCount(i) == 0) continue;
            if (customizer.GetGroupName(i) == "Wheels") continue;
            int captured = i;
            var btn = MakeLabelButton(partRow, customizer.GetGroupName(i));
            btn.onClick.AddListener(delegate { SelectGroup(captured); });
            partButtons.Add(btn);
            partButtonGroups.Add(i);
        }
        RefreshPartTabs();
    }

    void BuildFinishes()
    {
        ClearRow(finishRow, finishButtons);
        if (finishRow == null || customizer == null) return;

        int count = customizer.FinishCount;
        finishRow.gameObject.SetActive(count > 1);
        if (count <= 1) return;

        for (int i = 0; i < count; i++)
        {
            int captured = i;
            var btn = MakeLabelButton(finishRow, customizer.GetFinishName(i));
            btn.onClick.AddListener(delegate { SelectFinish(captured); });
            finishButtons.Add(btn);
        }
        RefreshFinishes();
    }

    void BuildSwatches()
    {
        ClearRow(swatchRow, swatchButtons);
        if (swatchRow == null || customizer == null) return;

        int options = customizer.GetGroupOptionCount(activeGroup);
        for (int i = 0; i < options; i++)
        {
            int captured = i;
            var go = new GameObject("Swatch" + i, typeof(RectTransform));
            go.transform.SetParent(swatchRow, false);

            var img = go.AddComponent<Image>();
            img.sprite = swatchSprite;
            img.color = swatchOutline;
            img.preserveAspect = true;

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;

            // Inner disc carries the paint colour; the outer disc is the ring
            // that shows selection, so the two never fight over one image.
            var fillGO = new GameObject("Fill", typeof(RectTransform));
            fillGO.transform.SetParent(go.transform, false);
            var fill = fillGO.AddComponent<Image>();
            fill.sprite = swatchSprite;
            fill.color = customizer.GetGroupOptionColor(activeGroup, i);
            fill.raycastTarget = false;
            fill.preserveAspect = true;
            var fillRT = fillGO.GetComponent<RectTransform>();
            fillRT.anchorMin = Vector2.zero;
            fillRT.anchorMax = Vector2.one;
            fillRT.offsetMin = new Vector2(6f, 6f);
            fillRT.offsetMax = new Vector2(-6f, -6f);

            btn.onClick.AddListener(delegate { SelectSwatch(captured); });
            swatchButtons.Add(btn);
        }
        RefreshSwatches();
    }

    Button MakeLabelButton(RectTransform parent, string label)
    {
        var go = new GameObject(label + "Tab", typeof(RectTransform));
        go.transform.SetParent(parent, false);

        var img = go.AddComponent<Image>();
        img.sprite = tabSprite;
        img.type = Image.Type.Sliced;
        img.color = tabIdle;

        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;

        var txtGO = new GameObject("Text", typeof(RectTransform));
        txtGO.transform.SetParent(go.transform, false);
        var txt = txtGO.AddComponent<Text>();
        txt.text = label.ToUpper();
        txt.font = font;
        txt.fontStyle = FontStyle.Bold;
        txt.alignment = TextAnchor.MiddleCenter;
        txt.color = new Color(0.93f, 0.94f, 0.96f);
        txt.resizeTextForBestFit = true;
        txt.resizeTextMinSize = 14;
        txt.resizeTextMaxSize = 30;
        txt.raycastTarget = false;
        var txtRT = txtGO.GetComponent<RectTransform>();
        txtRT.anchorMin = Vector2.zero;
        txtRT.anchorMax = Vector2.one;
        txtRT.offsetMin = new Vector2(4f, 3f);
        txtRT.offsetMax = new Vector2(-4f, -3f);

        return btn;
    }

    static void ClearRow(RectTransform row, List<Button> tracked)
    {
        tracked.Clear();
        if (row == null) return;
        for (int i = row.childCount - 1; i >= 0; i--)
        {
            var child = row.GetChild(i);
            // Destroy is deferred to end of frame, so unparent first: otherwise
            // the layout group counts the outgoing chips alongside the incoming
            // ones and squeezes everything for a frame.
            child.SetParent(null, false);
            Destroy(child.gameObject);
        }
    }

    // ---- interaction -------------------------------------------------------

    void SelectGroup(int group)
    {
        activeGroup = group;
        RefreshPartTabs();
        BuildSwatches();
    }

    void SelectSwatch(int option)
    {
        if (customizer == null) return;
        customizer.SetGroupIndex(activeGroup, option);
        RefreshSwatches();
    }

    void SelectFinish(int index)
    {
        if (customizer == null) return;
        customizer.SetFinish(index);
        RefreshFinishes();
    }

    void RefreshPartTabs()
    {
        for (int i = 0; i < partButtons.Count; i++)
        {
            var img = partButtons[i].targetGraphic as Image;
            if (img != null) img.color = partButtonGroups[i] == activeGroup ? tabActive : tabIdle;
        }
    }

    void RefreshFinishes()
    {
        if (customizer == null) return;
        for (int i = 0; i < finishButtons.Count; i++)
        {
            var img = finishButtons[i].targetGraphic as Image;
            if (img != null) img.color = i == customizer.FinishIndex ? tabActive : tabIdle;
        }
    }

    void RefreshSwatches()
    {
        if (customizer == null) return;
        int selected = customizer.GetGroupIndex(activeGroup);
        for (int i = 0; i < swatchButtons.Count; i++)
        {
            var img = swatchButtons[i].targetGraphic as Image;
            if (img == null) continue;
            img.color = i == selected ? swatchOutline : new Color(1f, 1f, 1f, 0.25f);
        }
    }
}
