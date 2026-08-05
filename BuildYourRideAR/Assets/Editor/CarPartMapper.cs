using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Turns an imported FBX into CarCustomizer part groups by matching material
// names. Imported cars put many materials on one renderer, so matches are
// recorded per material slot rather than per renderer -- assigning by renderer
// alone would repaint whatever sits in slot 0 instead of the panel we matched.
public static class CarPartMapper
{
    public class GroupSpec
    {
        public string displayName;

        // Lowercased substrings matched against the material name.
        public string[] keywords;

        // Substrings that veto a match, so "Window" does not fall into Body
        // just because the material is called "BodyWindow".
        public string[] exclude;

        public KeyValuePair<string, Color>[] palette;
        public bool applyFinish = true;

        // Puts the FBX's own material first in the list so the factory look
        // stays one tap away.
        public bool includeOriginal = true;
    }

    public static CarCustomizer.PartGroup[] Build(GameObject root, GroupSpec[] specs, out int matchedSlots)
    {
        matchedSlots = 0;
        var groups = new List<CarCustomizer.PartGroup>();
        var renderers = root.GetComponentsInChildren<Renderer>(true);

        foreach (var spec in specs)
        {
            var targets = new List<CarCustomizer.PaintTarget>();
            Material original = null;
            int bestVerts = -1;

            foreach (var renderer in renderers)
            {
                var slots = renderer.sharedMaterials;
                for (int i = 0; i < slots.Length; i++)
                {
                    var m = slots[i];
                    if (m == null) continue;
                    if (!Matches(m.name, spec)) continue;

                    targets.Add(new CarCustomizer.PaintTarget(renderer, i));

                    // Prefer the material on the densest mesh as "the" original:
                    // on a car that is the bodyshell, not a mirror cap.
                    var mf = renderer.GetComponent<MeshFilter>();
                    int verts = mf != null && mf.sharedMesh != null ? mf.sharedMesh.vertexCount : 0;
                    if (verts > bestVerts)
                    {
                        bestVerts = verts;
                        original = m;
                    }
                }
            }

            if (targets.Count == 0) continue;
            matchedSlots += targets.Count;

            bool originalFirst;
            var options = BuildOptions(spec, original, out originalFirst);

            // Keep the model's own material available, but do not start on it if
            // it carries no actual colour -- otherwise a model whose real look
            // lived in shader nodes the exporter could not write comes up as a
            // white blob and looks broken.
            int defaultIndex = 0;
            if (originalFirst && CarriesNoColour(original) && options.Length > 1)
            {
                defaultIndex = 1;
                Debug.LogWarning("[CarPartMapper] '" + spec.displayName + "' original material '" +
                                 original.name + "' has no texture and no distinct colour, so it would " +
                                 "render as a white blob. Defaulting to '" + options[1].name +
                                 "' instead; the original is still the first swatch.");
            }

            groups.Add(new CarCustomizer.PartGroup
            {
                displayName = spec.displayName,
                targets = targets.ToArray(),
                options = options,
                applyFinish = spec.applyFinish,
                originalOptionIndex = originalFirst ? 0 : -1,
                defaultOptionIndex = defaultIndex
            });
        }

        return groups.ToArray();
    }

    // True when a material has nothing to show: no texture, and a colour that is
    // effectively white or grey. Blender's FBX exporter writes exactly this when
    // a Principled BSDF is driven by shader nodes it cannot represent -- the
    // base colour falls back to its 0.8 grey default.
    static bool CarriesNoColour(Material m)
    {
        if (m == null) return true;
        if (m.mainTexture != null) return false;
        if (!m.HasProperty("_Color")) return true;

        var c = m.color;
        float max = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
        float min = Mathf.Min(c.r, Mathf.Min(c.g, c.b));
        bool desaturated = (max - min) < 0.06f;
        return desaturated && max > 0.55f;
    }

    static bool Matches(string materialName, GroupSpec spec)
    {
        string n = materialName.ToLowerInvariant();

        if (spec.exclude != null)
        {
            foreach (var e in spec.exclude)
            {
                if (!string.IsNullOrEmpty(e) && n.Contains(e)) return false;
            }
        }

        if (spec.keywords == null) return false;
        foreach (var k in spec.keywords)
        {
            if (!string.IsNullOrEmpty(k) && n.Contains(k)) return true;
        }
        return false;
    }

    static Material[] BuildOptions(GroupSpec spec, Material original, out bool originalFirst)
    {
        var options = new List<Material>();
        originalFirst = spec.includeOriginal && original != null;
        if (originalFirst) options.Add(original);

        if (spec.palette != null)
        {
            foreach (var entry in spec.palette)
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/" + entry.Key + ".mat");
                if (m != null && !options.Contains(m)) options.Add(m);
            }
        }
        return options.ToArray();
    }

    // Last resort when no material name matched: paint the densest mesh, which
    // on any car model is the bodyshell.
    public static CarCustomizer.PartGroup LargestMeshFallback(GameObject root, KeyValuePair<string, Color>[] palette)
    {
        Renderer largest = null;
        int largestVerts = -1;
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            var mf = r.GetComponent<MeshFilter>();
            int verts = mf != null && mf.sharedMesh != null ? mf.sharedMesh.vertexCount : 0;
            if (verts <= largestVerts) continue;
            largestVerts = verts;
            largest = r;
        }

        if (largest == null) return null;

        Debug.LogWarning("No paint-named materials matched. Falling back to the largest mesh (" +
                         largest.name + ", " + largestVerts + " verts) as the paintable body.");

        // Target every material slot on the largest renderer, not just slot 0.
        // BMW and other Blender exports routinely split the bodyshell across
        // multiple submeshes, and painting only slot 0 leaves the rest white.
        var targets = new List<CarCustomizer.PaintTarget>();
        var slots = largest.sharedMaterials;
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] != null)
                targets.Add(new CarCustomizer.PaintTarget(largest, i));
        }

        var spec = new GroupSpec { displayName = "Body", palette = palette, applyFinish = true };
        bool originalFirst;
        var options = BuildOptions(spec, largest.sharedMaterial, out originalFirst);

        int defaultIndex = 0;
        if (originalFirst && CarriesNoColour(largest.sharedMaterial) && options.Length > 1)
	        defaultIndex = 1;

        return new CarCustomizer.PartGroup
        {
            displayName = "Body",
            targets = targets.ToArray(),
            options = options,
            applyFinish = true,
            originalOptionIndex = originalFirst ? 0 : -1,
            defaultOptionIndex = defaultIndex
        };
    }

    // When a car's bodyshell is split across multiple renderers (hood, doors,
    // fenders, trunk as separate GameObjects), painting only the densest one
    // leaves the rest looking wrong. This gathers ALL renderers whose names
    // do NOT suggest glass, interior, wheels, lights, or mechanical parts,
    // and targets every material slot on all of them.
    public static CarCustomizer.PartGroup AllBodyRenderersFallback(GameObject root, KeyValuePair<string, Color>[] palette, bool includeOriginal = true)
    {
        var renderers = root.GetComponentsInChildren<Renderer>(true);
        var targets = new List<CarCustomizer.PaintTarget>();
        Renderer largest = null;
        int largestVerts = -1;
        int totalTargets = 0;
        int excluded = 0;

        foreach (var r in renderers)
        {
            var mf = r.GetComponent<MeshFilter>();
            int verts = mf != null && mf.sharedMesh != null ? mf.sharedMesh.vertexCount : 0;
            if (verts > largestVerts) { largestVerts = verts; largest = r; }

            string n = r.name.ToLowerInvariant();
            if (IsNonBodyRenderer(n) || RendererHasNonBodyMaterial(r))
            {
                excluded++;
                continue;
            }

            var slots = r.sharedMaterials;
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] == null) continue;
                targets.Add(new CarCustomizer.PaintTarget(r, i));
                totalTargets++;
            }
        }

        if (targets.Count == 0 && largest != null)
        {
            var slots = largest.sharedMaterials;
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] != null)
                    targets.Add(new CarCustomizer.PaintTarget(largest, i));
            }
        }

        if (targets.Count == 0) return null;

        Debug.LogWarning("[CarPartMapper] No Body-named materials matched. Falling back to ALL body renderers (" +
                         totalTargets + " material slots across " + (renderers.Length - excluded) +
                         " renderers; excluded " + excluded + " non-body renderers).");

        var spec = new GroupSpec { displayName = "Body", palette = palette, applyFinish = true, includeOriginal = includeOriginal };
        bool originalFirst;
        var options = BuildOptions(spec, largest != null ? largest.sharedMaterial : null, out originalFirst);

        int defaultIndex = 0;
        if (originalFirst && largest != null && CarriesNoColour(largest.sharedMaterial) && options.Length > 1)
            defaultIndex = 1;

        return new CarCustomizer.PartGroup
        {
            displayName = "Body",
            targets = targets.ToArray(),
            options = options,
            applyFinish = true,
            originalOptionIndex = originalFirst ? 0 : -1,
            defaultOptionIndex = defaultIndex
        };
    }

    static readonly HashSet<string> NonBodyRenderers = new HashSet<string>
    {
        "glass", "glas", "window", "windscreen", "light", "headlight", "taillight", "tail_light",
        "backlight", "brakelight", "lamp", "reverse",
        "interior", "int_", "dash", "dashboard", "seat", "belt", "carpet", "leather",
        "steer", "wheel", "tyre", "tire", "rim", "alloy", "caliper", "calliper",
        "brake", "disc", "rotor", "engine", "exhaust", "badge", "plate", "speaker",
        "grill", "mesh", "carbon", "fiber", "number_plate", "indicator", "blinker"
    };

    static bool IsNonBodyRenderer(string name)
    {
        foreach (var kw in NonBodyRenderers)
        {
            if (name.Length > 0 && name.Contains(kw)) return true;
        }
        return false;
    }

    static bool RendererHasNonBodyMaterial(Renderer r)
    {
        foreach (var m in r.sharedMaterials)
        {
            if (m == null) continue;
            if (IsNonBodyRenderer(m.name.ToLowerInvariant())) return true;
        }
        return false;
    }
}
