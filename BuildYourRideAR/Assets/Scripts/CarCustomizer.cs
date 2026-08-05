using System;
using System.Collections.Generic;
using UnityEngine;

// Owns everything the user can change on one car: per-part colour, paint
// finish, spoiler and wheel sets. Lives on the car prefab root.
public class CarCustomizer : MonoBehaviour
{
    // A single material slot on a single renderer. Cars imported from FBX
    // routinely put several materials on one renderer, so addressing the
    // submesh index matters -- assigning renderer.sharedMaterial would silently
    // repaint whatever happens to sit in slot 0.
    [Serializable]
    public class PaintTarget
    {
        public Renderer renderer;
        public int materialIndex;

        public PaintTarget() { }

        public PaintTarget(Renderer r, int index)
        {
            renderer = r;
            materialIndex = index;
        }
    }

    [Serializable]
    public class PartGroup
    {
        public string displayName = "Body";
        public PaintTarget[] targets;
        public Material[] options;

        [Tooltip("Whether the gloss/satin/matte selector applies to this part. " +
                 "True for paint, false for parts like glass or callipers.")]
        public bool applyFinish = true;

        [Tooltip("Index of the model's own material, or -1 if none. The finish " +
                 "selector never touches it, so the car can always be returned " +
                 "to exactly how it looked in the source file.")]
        public int originalOptionIndex = -1;

        [Tooltip("Which option to select when nothing is saved yet. Normally the " +
                 "model's own material, but a model whose material carries no " +
                 "real colour starts on a palette colour instead.")]
        public int defaultOptionIndex;

        [NonSerialized] public int index;
    }

    [Serializable]
    public class Finish
    {
        public string displayName = "Gloss";
        [Range(0f, 1f)] public float metallic = 0.7f;
        [Range(0f, 1f)] public float smoothness = 0.85f;
    }

    [Header("Paintable parts")]
    public PartGroup[] partGroups;

    [Header("Paint finish")]
    public Finish[] finishes;

    [Header("Spoiler (separate, auto-attached)")]
    public GameObject spoilerPrefab;

    [Tooltip("Manual override for spoiler localPosition. Set per-car in CarImporter. " +
             "Zero means auto-calculate from bounds. X=center, Y=height, Z=rear (more negative = farther rear).")]
    public Vector3 spoilerLocalPosition;

    [Tooltip("Manual override for spoiler localRotation. Set per-car in CarImporter. " +
             "Defaults to Quaternion.identity.")]
    public Quaternion spoilerLocalRotation = Quaternion.identity;

    [Header("Wheels")]
    public GameObject[] wheelSetOptions;

    public int FinishIndex { get; private set; }
    public bool SpoilerEnabled { get; private set; }

    // Real roster cars never populate wheelSetOptions (CarImporter always sets
    // it to an empty array) -- their rim colour lives entirely in the "Wheels"
    // part group instead. WheelIndex/NextWheels() route to whichever backing
    // store actually applies, so the WHEELS button keeps working either way:
    // legacyWheelIndex for the old GameObject-set cars (AddPlaceholderCars),
    // the Wheels group's own index for every real imported car.
    int legacyWheelIndex;

    public int WheelIndex
    {
        get
        {
            if (wheelSetOptions != null && wheelSetOptions.Length > 0) return legacyWheelIndex;
            int idx = WheelsGroupIndex();
            return idx >= 0 ? GetGroupIndex(idx) : 0;
        }
    }

    int WheelsGroupIndex()
    {
        for (int i = 0; i < GroupCount; i++)
        {
            if (partGroups[i] != null && partGroups[i].displayName == "Wheels") return i;
        }
        return -1;
    }

    // The body group is index 0 by convention; PaintIndex keeps the old
    // single-colour API working for callers that only care about body colour.
    public int PaintIndex
    {
        get { return GroupCount > 0 ? partGroups[0].index : 0; }
    }

    public int GroupCount
    {
        get { return partGroups != null ? partGroups.Length : 0; }
    }

    public string CarKey { get; private set; }

    // Raised with a human-readable description of what the user just changed
    // ("Body -> Racing Red"). The status overlay surfaces it so a change is
    // legible even when it is subtle on a small screen.
    public event Action<string> Changed;

    // False the first time a car is ever placed. Lets the placement controller
    // carry the previous car's colours onto a factory-fresh car, while a car
    // the user has already built comes back the way they left it.
    public bool HasSavedBuild { get; private set; }

    GameObject spoilerInstance;
    readonly Dictionary<long, Material> finishCache = new Dictionary<long, Material>();
    bool spoilerPending;
    float spoilerMoveSpeed = 0.05f;
    float spoilerFastMult = 5f;
    float nextSpoilerLog;
    Material blackMaterial;
    Material[] wheelMaterials;
    bool wheelsCached;
    GameObject[] builtInSpoilerObjects;
    bool hasBuiltInSpoiler;

    static readonly Color[] WheelColors = new Color[]
    {
        Color.black,
        new Color(0.45f, 0.45f, 0.45f),
        new Color(1f, 0.84f, 0f),
    };

    static readonly string[] WheelColorNames = { "Black", "Grey", "Gold" };

    void Awake()
    {
        CarKey = gameObject.name.Replace("(Clone)", string.Empty).Trim();
        EnsureFinishes();
        // If spoiler detection throws for any reason, the car should still
        // load with its paint and persisted build intact rather than Awake()
        // aborting partway and leaving the whole customizer half-initialized.
        try
        {
            DetectBuiltInSpoiler();
        }
        catch (System.Exception e)
        {
            Debug.LogError("[Spoiler] " + gameObject.name + ": DetectBuiltInSpoiler threw, continuing without a " +
                "built-in spoiler toggle for this car -- " + e.GetType().Name + ": " + e.Message);
        }
        LoadPersisted();
        if (hasBuiltInSpoiler)
            ApplySpoiler();
        ApplyAllGroups();
        ApplyWheels();
        spoilerPending = SpoilerEnabled && spoilerPrefab != null;
    }

    void DetectBuiltInSpoiler()
    {
        // REVERTED (v4.8): a "sibling of a matched object" pass and
        // rod/connector/aero keywords were added here on the theory that a
        // wing sits ungrouped next to matched struts under a shared parent.
        // Wrong, and badly so: this FBX's hierarchy is flat -- every part of
        // the car (engine, seats, dashboard, brakes, all of it) is a direct
        // child of the same "Model" node. The moment anything matched (e.g.
        // "rod" hit TwiXeR_992_pushrod_R / TwiXeR_992_tierod_F -- actual
        // suspension components, not spoiler struts), "sibling of the
        // matched object's parent" meant "every renderer in the whole car."
        // Combined with the Renderer.enabled toggle in ApplySpoiler(), that
        // disabled nearly the entire car whenever the spoiler was off (the
        // default), which is why Porsche stopped rendering at all and Coupe
        // lost all but its wheels. The plain keyword match below already
        // correctly finds the Porsche's real wing (TwiXeR_992_gt3rs_carbon_
        // Wing) and spoiler (TwiXeR_992_gt3rs_spoiler) on their own -- the
        // original "wing missing" report was never actually a detection
        // problem.
        //
        // "leg" added after the user identified TwiXeR_992_gt3rs_left_leg /
        // _right_leg via PartPicker.cs -- the wing's swan-neck mounting
        // struts, confirmed by direct visibility testing rather than a
        // keyword guess. Unlike "rod" (which collided with unrelated
        // suspension pushrod/tierod meshes and had to be reverted in v4.8),
        // this one is evidence-backed.
        var renderers = GetComponentsInChildren<Renderer>(true);
        var list = new System.Collections.Generic.List<GameObject>();

        foreach (var r in renderers)
        {
            bool match = false;
            var t = r.transform;
            for (int depth = 0; depth < 6 && t != null && t != transform; depth++)
            {
                string n = t.name.ToLower();
                if (n.Contains("spoiler") || n.Contains("wing") || n.Contains("stand") ||
                    n.Contains("post") || n.Contains("mount") || n.Contains("support") ||
                    n.Contains("leg"))
                {
                    match = true;
                    break;
                }
                t = t.parent;
            }
            if (match)
                list.Add(r.gameObject);
        }

        if (list.Count > 0)
        {
            hasBuiltInSpoiler = true;
            builtInSpoilerObjects = list.ToArray();
            // Diagnostics only, and expensive ones: the inventory below is one
            // line per renderer, which is 209 lines for the Maserati and 116
            // for the Porsche, built on every placement and swap, then piped
            // through ARDiagnosticLog's logMessageReceived hook straight to
            // disk. Useful while hunting the Porsche wing; pure overhead on a
            // user's phone.
            if (!Application.isEditor) return;

            var names = new System.Text.StringBuilder("[Spoiler] " + CarKey + " built-in spoiler objects: ");
            foreach (var obj in builtInSpoilerObjects)
                names.Append(obj.name).Append(" ");
            Debug.Log(names.ToString());

            // A partial match (e.g. only the support struts, not the wing
            // surface itself) means the toggle looks broken -- struts appear
            // or vanish but the wing mesh never does. Rather than guess more
            // keywords blind, list every renderer with its full hierarchy
            // path so the real name of whatever didn't match is visible
            // straight from the Console/diagnostics log without having to
            // dig through the Hierarchy by hand.
            var inventory = new System.Text.StringBuilder(
                "[Spoiler] " + CarKey + " full renderer inventory (check for a wing/aero mesh not listed above):\n");
            foreach (var r in renderers)
                inventory.Append("  ").Append(HierarchyPath(r.transform)).Append('\n');
            Debug.Log(inventory.ToString());
        }
    }

    string HierarchyPath(Transform t)
    {
        var sb = new System.Text.StringBuilder(t.name);
        t = t.parent;
        while (t != null && t != transform)
        {
            sb.Insert(0, "/").Insert(0, t.name);
            t = t.parent;
        }
        return sb.ToString();
    }

    void Start()
    {
        if (spoilerPending)
            ApplySpoiler();

        if (Application.isEditor && (spoilerPrefab != null || hasBuiltInSpoiler))
        {
            var euler = spoilerLocalRotation.eulerAngles;
            Debug.Log("[Spoiler] " + CarKey + " rotation = " +
                euler.x.ToString("F1") + ", " + euler.y.ToString("F1") + ", " + euler.z.ToString("F1") +
                "  pos = " + spoilerLocalPosition.x.ToString("F3") + ", " +
                spoilerLocalPosition.y.ToString("F3") + ", " + spoilerLocalPosition.z.ToString("F3"));
        }
    }

    void Update()
    {
        // Every line below this point is the editor-only spoiler-nudging rig
        // (W/A/D/Z, Alt to rotate, Ctrl for speed, P to log). On a phone there
        // is no keyboard to press any of it, so all it did was poll fifteen
        // Input.GetKey calls per frame, per placed car, forever.
        if (!Application.isEditor) return;
        if (spoilerInstance == null) return;

        // No more per-frame rotation enforcement here (that used to fight
        // any manual edit in the Inspector, reverting it a frame later --
        // full Inspector control now: drag the spoiler's Transform position
        // or rotation directly and it stays put). The one-time correction
        // for SpoilerImporter's baked Z-up tilt happens once, in
        // ApplySpoiler(), right after Instantiate.
        bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        bool alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
        bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);

        float speed = spoilerMoveSpeed * Time.deltaTime * (ctrl ? spoilerFastMult : 1f);

        var p = spoilerInstance.transform.localPosition;
        bool moved = false;

        // Guarded by !alt so Alt+W/A/D/Z drives rotation below instead of
        // also moving the spoiler at the same time.
        if (!alt)
        {
            if (Input.GetKey(KeyCode.W)) { p.z += speed; moved = true; }
            if (Input.GetKey(KeyCode.Z)) { p.z -= speed; moved = true; }
            if (Input.GetKey(KeyCode.A)) { p.x -= speed; moved = true; }
            if (Input.GetKey(KeyCode.D)) { p.x += speed; moved = true; }
        }
        if (shift && !alt && Input.GetKey(KeyCode.Q)) { p.y += speed; moved = true; }
        if (shift && !alt && Input.GetKey(KeyCode.E)) { p.y -= speed; moved = true; }

        if (moved)
            spoilerInstance.transform.localPosition = p;

        // Rotation: Alt is the modifier so it never collides with the
        // position keys above. Rotates the live instance transform directly
        // now (not a separate field the instance gets forced back to), so
        // it composes naturally with whatever the Inspector is doing too.
        bool rotated = false;
        if (alt)
        {
            float rotSpeed = 45f * Time.deltaTime * (ctrl ? spoilerFastMult : 1f);
            var e = spoilerInstance.transform.localEulerAngles;
            if (Input.GetKey(KeyCode.W)) { e.x -= rotSpeed; rotated = true; }
            if (Input.GetKey(KeyCode.Z)) { e.x += rotSpeed; rotated = true; }
            if (Input.GetKey(KeyCode.A)) { e.y -= rotSpeed; rotated = true; }
            if (Input.GetKey(KeyCode.D)) { e.y += rotSpeed; rotated = true; }
            if (Input.GetKey(KeyCode.Q)) { e.z -= rotSpeed; rotated = true; }
            if (Input.GetKey(KeyCode.E)) { e.z += rotSpeed; rotated = true; }
            if (rotated) spoilerInstance.transform.localEulerAngles = e;
        }

        // P logs the current pose on demand -- useful after dragging the
        // spoiler by hand in the Inspector, where nothing above fires.
        if (Input.GetKeyDown(KeyCode.P))
            LogSpoilerTransform();

        if ((moved || rotated) && Time.time >= nextSpoilerLog)
        {
            nextSpoilerLog = Time.time + 0.5f;
            LogSpoilerTransform();
        }
    }

    void LogSpoilerTransform()
    {
        if (spoilerInstance == null) return;
        var p = spoilerInstance.transform.localPosition;
        var e = spoilerInstance.transform.localEulerAngles;
        Debug.Log("[Spoiler] " + CarKey + " pos = " +
            p.x.ToString("F3") + "f, " + p.y.ToString("F3") + "f, " + p.z.ToString("F3") + "f" +
            "   rot = " + e.x.ToString("F1") + "f, " + e.y.ToString("F1") + "f, " + e.z.ToString("F1") + "f" +
            "   -> spoilerLocalPosition = new Vector3(" + p.x.ToString("F3") + "f, " + p.y.ToString("F3") + "f, " + p.z.ToString("F3") + "f)" +
            ", spoilerLocalRotation = Quaternion.Euler(" + e.x.ToString("F1") + "f, " + e.y.ToString("F1") + "f, " + e.z.ToString("F1") + "f)");
    }

    void OnDestroy()
    {
        foreach (var m in finishCache.Values)
        {
            if (m == null) continue;
            if (Application.isPlaying) Destroy(m);
            else DestroyImmediate(m);
        }
        finishCache.Clear();

        if (blackMaterial != null)
        {
            if (Application.isPlaying) Destroy(blackMaterial);
            else DestroyImmediate(blackMaterial);
            blackMaterial = null;
        }

        if (wheelMaterials != null)
        {
            foreach (var m in wheelMaterials)
            {
                if (m == null) continue;
                if (Application.isPlaying) Destroy(m);
                else DestroyImmediate(m);
            }
            wheelMaterials = null;
        }
    }

    void EnsureFinishes()
    {
    }

    // ---- persistence -------------------------------------------------------

    void LoadPersisted()
    {
        if (string.IsNullOrEmpty(CarKey)) return;
        HasSavedBuild = ConfigStore.HasSavedBuild(CarKey);

        for (int i = 0; i < GroupCount; i++)
        {
            var g = partGroups[i];
            if (g == null) continue;
            int count = g.options != null ? g.options.Length : 0;
            g.index = count > 0 ? Wrap(ConfigStore.GetPart(CarKey, i, g.defaultOptionIndex), count) : 0;
        }

        FinishIndex = Wrap(ConfigStore.GetFinish(CarKey, FinishIndex), finishes.Length);
        SpoilerEnabled = ConfigStore.GetSpoiler(CarKey, SpoilerEnabled);

        // Real cars restore rim colour through the generic per-group loop
        // above (the Rims group is just partGroups[i]); this only matters for
        // the legacy wheelSetOptions cars.
        if (wheelSetOptions != null && wheelSetOptions.Length > 0)
            legacyWheelIndex = Wrap(ConfigStore.GetWheels(CarKey, legacyWheelIndex), WheelColorNames.Length);
    }

    void Persist()
    {
        if (string.IsNullOrEmpty(CarKey)) return;
        for (int i = 0; i < GroupCount; i++)
        {
            if (partGroups[i] == null) continue;
            ConfigStore.SetPart(CarKey, i, partGroups[i].index);
        }
        ConfigStore.SetFinish(CarKey, FinishIndex);
        ConfigStore.SetSpoiler(CarKey, SpoilerEnabled);
        ConfigStore.SetWheels(CarKey, WheelIndex);
        // Deferred, not synchronous: this runs on every swatch tap, finish
        // change, spoiler toggle and wheel cycle. See ConfigStore.SaveDeferred.
        ConfigStore.SaveDeferred();
        HasSavedBuild = true;
    }

    static int Wrap(int value, int count)
    {
        if (count <= 0) return 0;
        int m = value % count;
        return m < 0 ? m + count : m;
    }

    static bool ColorsClose(Color a, Color b)
    {
        const float threshold = 0.02f;
        return (a.r - b.r) * (a.r - b.r) + (a.g - b.g) * (a.g - b.g) + (a.b - b.b) * (a.b - b.b) < threshold;
    }

    // ---- public API --------------------------------------------------------

    public string GetGroupName(int group)
    {
        return IsGroup(group) ? partGroups[group].displayName : string.Empty;
    }

    public int GetGroupOptionCount(int group)
    {
        if (!IsGroup(group)) return 0;
        var opts = partGroups[group].options;
        return opts != null ? opts.Length : 0;
    }

    public int GetGroupIndex(int group)
    {
        return IsGroup(group) ? partGroups[group].index : 0;
    }

    public Material GetGroupOption(int group, int option)
    {
        if (!IsGroup(group)) return null;
        var opts = partGroups[group].options;
        if (opts == null || option < 0 || option >= opts.Length) return null;
        return opts[option];
    }

    // Swatch colour for the UI. Falls back to the material's main texture
    // average being unavailable -- an FBX's original paint has no useful
    // _Color, so it shows as its tint rather than as a blank chip.
    public Color GetGroupOptionColor(int group, int option)
    {
        var m = GetGroupOption(group, option);
        if (m == null) return Color.grey;
        if (m.HasProperty("_Color")) return m.color;
        return Color.grey;
    }

    public void SetGroupIndex(int group, int option)
    {
        if (!IsGroup(group)) return;
        int count = GetGroupOptionCount(group);
        if (count == 0) return;
        partGroups[group].index = Wrap(option, count);
        ApplyGroup(group);
        Persist();
        Announce(partGroups[group].displayName + " -> " + GetGroupOptionName(group, partGroups[group].index));
    }

    public string GetGroupOptionName(int group, int option)
    {
        // The "original" option is whatever material the source FBX shipped
        // with, named for a 3D asset browser, not an end user -- "Twi Xe R
        // 992 ID04 plastic textured 001 060606 FF.001" and "EXT caliper.2"
        // are actual examples from this roster. No amount of string cleanup
        // makes an asset-pipeline name read well; showing a clean, fixed
        // label for it is the actual fix.
        if (IsGroup(group) && option == partGroups[group].originalOptionIndex)
            return "Original";
        var m = GetGroupOption(group, option);
        return m != null ? Prettify(m.name) : "None";
    }

    // Material assets are named for the asset browser (Paint_RacingRed);
    // the overlay needs something a person reads (Racing Red).
    public static string Prettify(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return string.Empty;

        string s = raw;
        int bracket = s.IndexOf(" [");
        if (bracket > 0) s = s.Substring(0, bracket);

        foreach (var prefix in new[] { "Paint_", "Rim", "Calliper", "Trim" })
        {
            if (s.Length > prefix.Length && s.StartsWith(prefix))
            {
                s = s.Substring(prefix.Length);
                break;
            }
        }
        s = s.Replace('_', ' ').Trim();
        if (s.Length == 0) return raw;

        var sb = new System.Text.StringBuilder(s.Length + 6);
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (i > 0 && char.IsUpper(c) && !char.IsUpper(s[i - 1]) && s[i - 1] != ' ')
                sb.Append(' ');
            sb.Append(c);
        }
        return sb.ToString();
    }

    void Announce(string message)
    {
        var handler = Changed;
        if (handler != null) handler(message);
    }

    public void NextGroupOption(int group)
    {
        SetGroupIndex(group, GetGroupIndex(group) + 1);
    }

    public void NextPaint()
    {
        NextGroupOption(0);
    }

    public string GetFinishName(int index)
    {
        return finishes != null && index >= 0 && index < finishes.Length
            ? finishes[index].displayName
            : string.Empty;
    }

    public int FinishCount
    {
        get { return finishes != null ? finishes.Length : 0; }
    }

    public void SetFinish(int index)
    {
        if (FinishCount == 0) return;
        FinishIndex = Wrap(index, finishes.Length);
        ApplyAllGroups();
        Persist();
        Announce("Finish -> " + GetFinishName(FinishIndex));
    }

    public void NextFinish()
    {
        SetFinish(FinishIndex + 1);
    }

    public void ToggleSpoiler()
    {
        SpoilerEnabled = !SpoilerEnabled;
        ApplySpoiler();
        Persist();
        Announce("Spoiler -> " + (SpoilerEnabled ? "On" : "Off"));
    }

    public void NextWheels()
    {
        if (wheelSetOptions != null && wheelSetOptions.Length > 0)
        {
            legacyWheelIndex = (legacyWheelIndex + 1) % WheelColorNames.Length;
            ApplyWheels();
            Persist();
            Announce("Wheels -> " + WheelColorNames[legacyWheelIndex]);
            return;
        }

        // Real imported cars: cycle the Rims part group directly.
        // NextGroupOption already applies, persists and announces.
        int idx = WheelsGroupIndex();
        if (idx >= 0) NextGroupOption(idx);
    }

    public bool HasWheelSets
    {
        get
        {
            if (wheelSetOptions != null && wheelSetOptions.Length > 0) return true;
            int idx = WheelsGroupIndex();
            return idx >= 0 && GetGroupOptionCount(idx) > 0;
        }
    }

    public bool HasSpoiler
    {
        get { return spoilerPrefab != null || hasBuiltInSpoiler; }
    }

    public bool NoSpoilerAvailable
    {
        get { return spoilerPrefab == null && !hasBuiltInSpoiler; }
    }

    // Carries a build across a car swap. paintColor is the OLD car's actual
    // rendered body colour, not a raw option index -- an index number means
    // nothing across two cars with differently-ordered palettes (Maserati's
    // index 0 is White; on another car index 0 might be Black), which used
    // to land a freshly-switched-to Maserati on White almost every time
    // simply because most cars happen to start at index 0. Matching by the
    // actual colour, and falling back to this car's own intended default
    // (defaultOptionIndex) when nothing matches closely enough, actually
    // honours "inherits the previous car's colour" as designed.
    public void ApplyState(Color? paintColor, bool spoiler, int wheels)
    {
        if (GroupCount > 0)
        {
            int count = GetGroupOptionCount(0);
            if (count > 0)
            {
                int idx = partGroups[0].defaultOptionIndex;
                if (paintColor.HasValue)
                {
                    for (int i = 0; i < count; i++)
                    {
                        if (ColorsClose(GetGroupOptionColor(0, i), paintColor.Value)) { idx = i; break; }
                    }
                }
                partGroups[0].index = Wrap(idx, count);
            }
            else
            {
                partGroups[0].index = 0;
            }
        }
        SpoilerEnabled = spoiler;

        if (wheelSetOptions != null && wheelSetOptions.Length > 0)
        {
            legacyWheelIndex = Wrap(wheels, WheelColorNames.Length);
        }
        else
        {
            int idx = WheelsGroupIndex();
            int count = idx >= 0 ? GetGroupOptionCount(idx) : 0;
            if (count > 0) partGroups[idx].index = Wrap(wheels, count);
        }

        ApplyAll();
        Persist();
    }

    // ---- application -------------------------------------------------------

    bool IsGroup(int group)
    {
        return partGroups != null && group >= 0 && group < partGroups.Length;
    }

    void ApplyAll()
    {
        ApplyAllGroups();
        ApplySpoiler();
        ApplyWheels();
    }

    void ApplyAllGroups()
    {
        for (int i = 0; i < GroupCount; i++)
            ApplyGroup(i);
    }

    void ApplyGroup(int group)
    {
        if (!IsGroup(group)) return;
        var g = partGroups[group];
        if (g == null || g.targets == null || g.options == null || g.options.Length == 0) return;

        int option = Wrap(g.index, g.options.Length);
        var chosen = g.options[option];
        if (chosen == null) return;

        // The model's own material is applied untouched. Forcing the finish
        // preset's metallic/smoothness onto it would change how the car looked
        // in its source file, which defeats having an "original" option at all.
        bool isOriginal = option == g.originalOptionIndex;
        var resolved = g.applyFinish && !isOriginal ? ResolveFinish(chosen) : chosen;

        foreach (var target in g.targets)
        {
            if (target == null || target.renderer == null) continue;
            var slots = target.renderer.sharedMaterials;
            if (slots.Length == 0) continue;
            int idx = Mathf.Clamp(target.materialIndex, 0, slots.Length - 1);
            if (slots[idx] == resolved) continue;
            slots[idx] = resolved;
            target.renderer.sharedMaterials = slots;
        }
    }

    // Finishes are a metallic/smoothness tweak on top of the chosen colour.
    // Variants are cached and owned by this component so repeated switching
    // does not spawn a new material every tap.
    Material ResolveFinish(Material source)
    {
        if (FinishCount == 0) return source;
        var finish = finishes[Wrap(FinishIndex, finishes.Length)];
        if (finish == null) return source;

        long key = ((long)source.GetInstanceID() << 8) ^ (uint)FinishIndex;
        Material variant;
        if (finishCache.TryGetValue(key, out variant) && variant != null)
            return variant;

        variant = new Material(source);
        variant.name = source.name + " [" + finish.displayName + "]";
        if (variant.HasProperty("_Metallic")) variant.SetFloat("_Metallic", finish.metallic);
        if (variant.HasProperty("_Glossiness")) variant.SetFloat("_Glossiness", finish.smoothness);
        if (variant.HasProperty("_Smoothness")) variant.SetFloat("_Smoothness", finish.smoothness);
        finishCache[key] = variant;
        return variant;
    }

    void ApplySpoiler()
    {
        spoilerPending = false;

        if (hasBuiltInSpoiler)
        {
            if (builtInSpoilerObjects != null)
            {
                foreach (var obj in builtInSpoilerObjects)
                {
                    if (obj == null) continue;
                    obj.SetActive(SpoilerEnabled);
                    // SetActive alone can be insufficient if the source FBX
                    // shipped this mesh's Renderer pre-disabled (a common way
                    // exporters hide an LOD/proxy variant) -- the GameObject
                    // is active while its own Renderer stays off, which reads
                    // as "permanently invisible" no matter what the toggle
                    // does. Belt and braces against exactly the Porsche wing
                    // symptom: struts show, the wing surface never does.
                    var r = obj.GetComponent<Renderer>();
                    if (r != null) r.enabled = SpoilerEnabled;
                }
            }
            return;
        }

        if (SpoilerEnabled && spoilerInstance == null && spoilerPrefab != null)
        {
            spoilerInstance = Instantiate(spoilerPrefab, transform);
            spoilerInstance.transform.localPosition = CalcSpoilerPosition();
            spoilerInstance.transform.localRotation = spoilerLocalRotation;
            // One-time only: SpoilerImporter bakes a Z-up correction into the
            // child mesh transforms, which needs zeroing out relative to the
            // root right after instantiation. This used to re-run every
            // frame in Update() via FixSpoilerRotation(), fighting any
            // manual edit in the Inspector -- now it's just the starting
            // pose, and the spoiler is fully free to move/rotate afterward
            // via the keyboard controls or the Inspector directly.
            foreach (Transform child in spoilerInstance.transform)
                child.localRotation = Quaternion.identity;

            if (blackMaterial == null)
            {
                blackMaterial = new Material(Shader.Find("Standard"));
                blackMaterial.color = Color.black;
            }
            foreach (var r in spoilerInstance.GetComponentsInChildren<Renderer>())
                r.material = blackMaterial;
        }
        else if (!SpoilerEnabled && spoilerInstance != null)
        {
            if (Application.isPlaying) Destroy(spoilerInstance);
            else DestroyImmediate(spoilerInstance);
            spoilerInstance = null;
        }
    }

    void ApplyWheels()
    {
        if (wheelSetOptions == null || wheelSetOptions.Length == 0) return;

        if (!wheelsCached)
        {
            for (int i = 0; i < wheelSetOptions.Length; i++)
            {
                if (wheelSetOptions[i] != null)
                    wheelSetOptions[i].SetActive(i == 0);
            }

            var firstSet = wheelSetOptions[0];
            if (firstSet == null) return;
            var renderers = firstSet.GetComponentsInChildren<Renderer>();
            wheelMaterials = new Material[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
            {
                var src = renderers[i].sharedMaterial;
                wheelMaterials[i] = new Material(src);
                wheelMaterials[i].color = Color.black;
                renderers[i].material = wheelMaterials[i];
            }
            wheelsCached = true;
        }

        if (wheelMaterials == null) return;
        int colorIdx = WheelIndex % WheelColors.Length;
        foreach (var mat in wheelMaterials)
            mat.color = WheelColors[colorIdx];
    }

    Vector3 CalcSpoilerPosition()
    {
        // Manual override set in CarImporter (per-car spoilerLocalPosition).
        // Baked into the prefab so it survives roster rebuilds.
        if (spoilerLocalPosition != Vector3.zero)
            return spoilerLocalPosition;

        // Compute local-space bounds. The car's Model child is centered at XZ
        // with identity rotation, so local bounds directly describe the car's
        // geometry relative to the root. The spoiler lands at the rear (min Z),
        // centered in X, near the top (80% height).
        var bounds = ComputeLocalBounds(gameObject);
        float x = bounds.center.x;
        float y = bounds.min.y + bounds.size.y * 0.80f;
        float z = bounds.center.z - bounds.extents.z * 0.90f;
        return new Vector3(x, y, z);
    }

    Bounds ComputeLocalBounds(GameObject go)
    {
        var renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return new Bounds(Vector3.zero, Vector3.one);
        bool first = true;
        var b = new Bounds();
        foreach (var r in renderers)
        {
            var wb = r.bounds;
            var min = go.transform.InverseTransformPoint(wb.min);
            var max = go.transform.InverseTransformPoint(wb.max);
            var cb = new Bounds((min + max) * 0.5f, max - min);
            if (first) { b = cb; first = false; }
            else b.Encapsulate(cb);
        }
        return b;
    }

    Bounds ComputeBounds(GameObject go)
    {
        var renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return new Bounds(Vector3.zero, Vector3.one);
        var b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            b.Encapsulate(renderers[i].bounds);
        return b;
    }
}
