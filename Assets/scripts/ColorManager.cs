using UnityEngine;

[ExecuteAlways]
public class MaterialController : MonoBehaviour
{
    public enum RenderingMode { Opaque, Transparent }

    [System.Serializable]
    public class MaterialEntry
    {
        public Material material;

        [Header("Color when SeeInterior = TRUE (alpha = opacity)")]
        public Color colorTrue  = Color.white;

        [Header("Color when SeeInterior = FALSE (alpha = opacity)")]
        public Color colorFalse = new Color(0.6f, 0.6f, 0.6f, 1f);
    }

    [System.Serializable]
    public class MaterialGroup
    {
        [Header("Rendering Mode")]
        public RenderingMode renderingModeTrue  = RenderingMode.Opaque;
        public RenderingMode renderingModeFalse = RenderingMode.Transparent;

        [Header("Materials")]
        public MaterialEntry[] materials;
    }

    [Header("=== Toggle ===")]
    public bool SeeInterior = false;

    [Header("=== Transition ===")]
    [Tooltip("Seconds to blend opacity. 0 = instant.")]
    public float transitionDuration = 0.5f;

    [Header("=== Exterior (5 materials) ===")]
    public MaterialGroup exterior = new MaterialGroup { materials = new MaterialEntry[5] };

    [Header("=== Interior (6 materials) ===")]
    public MaterialGroup interior = new MaterialGroup { materials = new MaterialEntry[6] };

    // ---------------- runtime state ----------------
    bool _initialized;
    bool _lastState;
    float _t = 1f;
    Color[] _extFrom, _extTo, _intFrom, _intTo;

    // -------------------------------------------------

    void OnEnable()
    {
        InitArrays();
        ApplyImmediate();
        _lastState = SeeInterior;
        _t = 1f;
        _initialized = true;
    }

    void OnValidate()
    {
        if (!isActiveAndEnabled) return;
        if (!Application.isPlaying) ApplyImmediate();
    }

    void Update()
    {
        if (!_initialized) return;

        if (SeeInterior != _lastState)
        {
            _lastState = SeeInterior;

            if (Application.isPlaying && transitionDuration > 0f)
                BeginTransition();
            else
                ApplyImmediate();
        }

        if (_t < 1f)
        {
            _t += Time.deltaTime / Mathf.Max(0.0001f, transitionDuration);
            if (_t > 1f) _t = 1f;
            ApplyLerped(_t);
        }
    }

    // -------------------------------------------------
    //  SETUP
    // -------------------------------------------------

    void InitArrays()
    {
        int e = exterior?.materials?.Length ?? 0;
        int i = interior?.materials?.Length ?? 0;
        _extFrom = new Color[e]; _extTo = new Color[e];
        _intFrom = new Color[i]; _intTo = new Color[i];
    }

    // -------------------------------------------------
    //  INSTANT APPLY
    // -------------------------------------------------

    [ContextMenu("Apply Now")]
    public void ApplyImmediate()
    {
        _t = 1f;
        ApplyGroup(exterior, SeeInterior);
        ApplyGroup(interior, SeeInterior);
    }

    void ApplyGroup(MaterialGroup group, bool state)
    {
        if (group?.materials == null) return;
        int mode = (int)(state ? group.renderingModeTrue : group.renderingModeFalse);

        foreach (var entry in group.materials)
        {
            if (entry?.material == null) continue;
            Color c = state ? entry.colorTrue : entry.colorFalse;
            SetColorOnly(entry.material, c);
            SetRenderingMode(entry.material, mode);
        }
    }

    // -------------------------------------------------
    //  TRANSITION  (opacity-driven)
    // -------------------------------------------------

    void BeginTransition()
    {
        CaptureCurrent(_extFrom, exterior);
        CaptureCurrent(_intFrom, interior);

        ComputeTargets(_extTo, exterior, SeeInterior);
        ComputeTargets(_intTo, interior, SeeInterior);

        // Force Transparent rendering during the fade so alpha is actually visible.
        ForceTransparent(exterior);
        ForceTransparent(interior);

        _t = 0f;
    }

    void ApplyLerped(float t)
    {
        ApplyLerpToGroup(exterior, _extFrom, _extTo, t);
        ApplyLerpToGroup(interior, _intFrom, _intTo, t);

        // When transition finishes, restore configured render mode per group
        if (t >= 1f)
        {
            RestoreRenderMode(exterior, SeeInterior);
            RestoreRenderMode(interior, SeeInterior);
        }
    }

    void ApplyLerpToGroup(MaterialGroup g, Color[] from, Color[] to, float t)
    {
        if (g?.materials == null) return;
        int n = Mathf.Min(g.materials.Length, Mathf.Min(from.Length, to.Length));
        for (int i = 0; i < n; i++)
        {
            var entry = g.materials[i];
            if (entry?.material == null) continue;
            // Blend full color; the alpha channel is the "opacity" driver.
            SetColorOnly(entry.material, Color.Lerp(from[i], to[i], t));
        }
    }

    void CaptureCurrent(Color[] buf, MaterialGroup g)
    {
        if (g?.materials == null) return;
        int n = Mathf.Min(buf.Length, g.materials.Length);
        for (int i = 0; i < n; i++)
            buf[i] = GetCurrentColor(g.materials[i]?.material);
    }

    void ComputeTargets(Color[] buf, MaterialGroup g, bool state)
    {
        if (g?.materials == null) return;
        int n = Mathf.Min(buf.Length, g.materials.Length);
        for (int i = 0; i < n; i++)
        {
            var e = g.materials[i];
            buf[i] = e == null ? Color.white : (state ? e.colorTrue : e.colorFalse);
        }
    }

    void ForceTransparent(MaterialGroup g)
    {
        if (g?.materials == null) return;
        foreach (var e in g.materials)
        {
            if (e?.material == null) continue;
            SetRenderingMode(e.material, 1); // Transparent
        }
    }

    void RestoreRenderMode(MaterialGroup g, bool state)
    {
        if (g?.materials == null) return;
        int mode = (int)(state ? g.renderingModeTrue : g.renderingModeFalse);
        foreach (var e in g.materials)
        {
            if (e?.material == null) continue;
            SetRenderingMode(e.material, mode);
        }
    }

    // -------------------------------------------------
    //  HELPERS
    // -------------------------------------------------

    Color GetCurrentColor(Material m)
    {
        if (m == null) return Color.white;
        if (m.HasProperty("_BaseColor")) return m.GetColor("_BaseColor");
        if (m.HasProperty("_Color"))     return m.color;
        return Color.white;
    }

    void SetColorOnly(Material mat, Color color)
    {
        if (mat == null) return;
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color"))     mat.color = color;
    }

    void SetRenderingMode(Material mat, int mode)
    {
        if (mode == 0) // Opaque
        {
            mat.SetFloat("_Mode", 0);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
            mat.SetInt("_ZWrite", 1);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.DisableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
        }
        else // Transparent
        {
            mat.SetFloat("_Mode", 3);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }
    }
}