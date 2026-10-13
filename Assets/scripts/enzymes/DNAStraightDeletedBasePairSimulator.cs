using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Receives every base deleted by a DNAStraightReplicationSimulator and spawns it (or its
/// complement) in its own column. Prefabs, line style, spacing, fall speed, scales and all
/// pair geometry are inherited live from the source; nothing needs to be configured twice.
///
/// REQUIRES the small change to DNAStraightReplicationSimulator (BaseDeleted event + public Eff* properties).
/// NOTE: file name must be "DNAStraightDeletedBasePairSimulator.cs".
/// </summary>
[DisallowMultipleComponent]
public class DNAStraightDeletedBasePairSimulator : MonoBehaviour
{
    public enum BaseSide { Left, Right }

    // ------------------------------------------------------------------ Source
    [Header("Source")]
    [Tooltip("The straight strand whose deleted bases are received. Prefabs, line style, spacing, fall speed, " +
             "scales and pair geometry are all inherited from it (and from its own source).")]
    public DNAStraightReplicationSimulator source;

    // ------------------------------------------------------------------ Base pair options
    [Header("Base Pair Options")]
    [Tooltip("ON: A->T, T->A, C->G, G->C.   OFF: spawn the same base that was deleted.")]
    public bool createOppositeBasePair = true;
    [Tooltip("Which side of the column the base half is placed on (uses the source's half length and rotations).")]
    public BaseSide baseSide = BaseSide.Right;

    // ------------------------------------------------------------------ Spawn settings
    [Header("Spawn Settings")]
    [Tooltip("Local X / Z of the column. Y is ignored (use Top Spawn Height).")]
    public Vector3 spawnPosition = Vector3.zero;
    [Tooltip("Local Y at which new bases enter.")]
    public float topSpawnHeight = 10f;
    [Tooltip("Local Y below which a base is removed.")]
    public float bottomDespawnHeight = 0f;
    [Tooltip("ON = use the fall speed below instead of the source's.")]
    public bool overrideFallSpeed = false;
    [Min(0f)] public float fallSpeed = 0.25f;
    [Tooltip("Extra vertical gap added on top of the source's spacing.")]
    public float extraSpacing = 0f;

    // ------------------------------------------------------------------ Debug
    [Header("Debug")]
    public bool logDeletedBases = false;
    [Tooltip("Read-only: deleted bases received from the source since Play started.")]
    public int deletedBasesDetected;
    [Tooltip("Read-only: bases successfully spawned.")]
    public int basesSpawned;
    public bool drawGizmos = true;

    // ------------------------------------------------------------------ Runtime
    private const string ContainerName = "__DNA_DeletedBases_Generated";
    private const string PairPivotName = "__DeletedBasePivot";
    private const int MaxSpawnsPerFrame = 32;

    private sealed class PairRuntime { public Transform pivot; public float liveY; }

    private readonly List<PairRuntime> _pairs = new();
    private readonly Queue<char> _incoming = new();
    private Transform _container;
    private int _nextSerial;
    private DNAStraightReplicationSimulator _subscribedSource;
    private static Material _fallbackMaterial;

    public int PendingIncomingCount => _incoming.Count;
    public int ActivePairCount => _pairs.Count;

    // Inherited values (always read live from the source).
    private DNAReplicationSimulator Render => source != null ? source.source : null;
    private float FallSpeedNow => overrideFallSpeed || source == null ? fallSpeed : source.EffectiveFallSpeed;
    private float Spacing =>
        Mathf.Max(1e-4f, (source.EffStepHeight + source.EffStackGap) / source.EffPairsPerStep + extraSpacing);

    private Vector3 PivotScale
    {
        get
        {
            if (!source.matchSourceWorldScale || Render == null) return Vector3.one;
            Vector3 a = Render.transform.lossyScale, b = transform.lossyScale;
            return new Vector3(b.x != 0f ? a.x / b.x : 1f, b.y != 0f ? a.y / b.y : 1f, b.z != 0f ? a.z / b.z : 1f);
        }
    }

    private static char Complement(char b) => b switch { 'A' => 'T', 'T' => 'A', 'C' => 'G', _ => 'C' };

    private GameObject GetBasePrefab(char b) => b switch
    { 'A' => Render.prefabA, 'T' => Render.prefabT, 'C' => Render.prefabC, _ => Render.prefabG };

    private GameObject GetElementPrefab(char b) => b switch
    { 'A' => Render.elementA, 'T' => Render.elementT, 'C' => Render.elementC, _ => Render.elementG };

    private DNAStraightReplicationSimulator.ElementTweak GetTweak(char b)
    {
        var t = b switch { 'A' => source.interiorA, 'C' => source.interiorC, 'G' => source.interiorG, _ => source.interiorT };
        return t ?? new DNAStraightReplicationSimulator.ElementTweak();
    }

    // =================================================================== Unity callbacks
    private void OnEnable() { TrySubscribe(); }
    private void OnDisable() { Unsubscribe(); }
    private void OnDestroy() { Unsubscribe(); }

    private void Start()
    {
        if (!Application.isPlaying) return;
        ClearGenerated();
        deletedBasesDetected = 0;
        basesSpawned = 0;
        EnsureContainer();
        TrySubscribe();
    }

    private void Update()
    {
        if (!Application.isPlaying) return;
        if (_subscribedSource != source) TrySubscribe();
        if (source == null || Render == null) return;

        EnsureContainer();

        float dy = FallSpeedNow * Time.deltaTime;
        for (int i = 0; i < _pairs.Count; i++)
        {
            PairRuntime pr = _pairs[i];
            if (pr.pivot == null) continue;
            pr.liveY -= dy;
            Vector3 p = pr.pivot.localPosition;
            p.y = pr.liveY;
            pr.pivot.localPosition = p;
        }

        for (int i = _pairs.Count - 1; i >= 0; i--)
        {
            if (_pairs[i].liveY < bottomDespawnHeight)
            {
                if (_pairs[i].pivot != null) Destroy(_pairs[i].pivot.gameObject);
                _pairs.RemoveAt(i);
            }
        }

        TryDrainQueue();
    }

    // =================================================================== Event wiring
    private void TrySubscribe()
    {
        if (_subscribedSource == source) return;
        Unsubscribe();
        if (source != null)
        {
            source.BaseDeleted += OnBaseDeleted;
            _subscribedSource = source;
        }
    }

    private void Unsubscribe()
    {
        if (_subscribedSource != null)
        {
            _subscribedSource.BaseDeleted -= OnBaseDeleted;
            _subscribedSource = null;
        }
    }

    private void OnBaseDeleted(char deleted)
    {
        if (!Application.isPlaying) return;
        _incoming.Enqueue(deleted);
        deletedBasesDetected++;
        if (logDeletedBases)
            Debug.Log($"Deleted-base simulator: received '{deleted}' " +
                      $"(will spawn '{(createOppositeBasePair ? Complement(deleted) : deleted)}').", this);
    }

    // =================================================================== Spawning
    private void TryDrainQueue()
    {
        if (_incoming.Count == 0 || Render == null) return;

        float spacing = Spacing;
        int guard = 0;

        while (_incoming.Count > 0 && guard++ < MaxSpawnsPerFrame)
        {
            float y = topSpawnHeight;
            if (_pairs.Count > 0)
            {
                float highest = float.NegativeInfinity;
                for (int i = 0; i < _pairs.Count; i++) if (_pairs[i].liveY > highest) highest = _pairs[i].liveY;
                if (highest > topSpawnHeight - spacing) break;   // no room yet, keep order
                y = Mathf.Min(topSpawnHeight, highest + spacing);
            }

            char deleted = _incoming.Dequeue();
            char letter = createOppositeBasePair ? Complement(deleted) : deleted;
            if (SpawnBase(letter, y)) basesSpawned++;
        }
    }

    private bool SpawnBase(char letter, float startY)
    {
        DNAReplicationSimulator r = Render;
        GameObject prefab = GetBasePrefab(letter);
        if (prefab == null)
        {
            Debug.LogWarning($"DNAStraightDeletedBasePairSimulator: no prefab for base '{letter}' on the render source - skipped.", this);
            return false;
        }

        bool isLeft = baseSide == BaseSide.Left;
        float side = isLeft ? -1f : 1f;
        int serial = _nextSerial;

        var pivotGo = new GameObject($"{PairPivotName}_{serial}");
        pivotGo.transform.SetParent(_container, false);
        pivotGo.transform.localPosition = new Vector3(spawnPosition.x, startY, spawnPosition.z);
        pivotGo.transform.localRotation = Quaternion.Euler(source.baseRotation);
        pivotGo.transform.localScale = PivotScale;

        // --- Base half ---
        Vector3 baseLocalPos = Vector3.right * (side * source.pairHalfLength);
        Quaternion baseLocalRot = Quaternion.Euler(isLeft ? source.leftHalfRotation : source.rightHalfRotation);

        GameObject baseGo = Instantiate(prefab, pivotGo.transform);
        baseGo.name = $"{prefab.name}_{letter}_Deleted_{serial}";
        baseGo.transform.localPosition = baseLocalPos;
        baseGo.transform.localRotation = baseLocalRot;
        baseGo.transform.localScale = prefab.transform.localScale;

        bool haveBaseBounds = TryGetLocalMeshBounds(baseGo.transform, out Bounds baseBounds);

        // --- Interior element ---
        if (source.spawnElements)
        {
            GameObject ep = GetElementPrefab(letter);
            if (ep != null)
            {
                GameObject element = Instantiate(ep, baseGo.transform);
                element.name = $"{ep.name}_Element_{letter}";

                var tw = GetTweak(letter);
                Vector3 desiredInPivot = Vector3.right * (side * source.elementDistance);
                Vector3 delta = Quaternion.Inverse(baseLocalRot) * (desiredInPivot - baseLocalPos);
                delta = DivideSafe(delta, baseGo.transform.localScale);

                element.transform.localPosition = delta + source.elementLocalPosition + tw.positionOffset;
                element.transform.localRotation = Quaternion.Euler(source.elementLocalRotation) * Quaternion.Euler(tw.rotationOffset);
                element.transform.localScale = source.EffElementScale;
            }
        }

        // --- Sugar / phosphate ---
        bool haveSugar = false, havePhos = false;
        Vector3 sugarPos = Vector3.zero, phosPos = Vector3.zero;
        Quaternion sugarRot = Quaternion.identity, phosRot = Quaternion.identity;
        Vector3 sugarScale = source.EffSugarScale, phosScale = source.EffPhosphateScale;

        if (source.spawnSugar && r.prefabSugar != null)
        {
            GameObject sugar = Instantiate(r.prefabSugar, pivotGo.transform);
            sugar.name = $"{r.prefabSugar.name}_Sugar_{serial}";
            sugarPos = baseLocalPos + baseLocalRot * source.sugarLocalPosition;
            sugarRot = baseLocalRot * Quaternion.Euler(source.sugarLocalRotation);
            sugar.transform.localPosition = sugarPos;
            sugar.transform.localRotation = sugarRot;
            sugar.transform.localScale = sugarScale;
            haveSugar = true;
        }

        if (source.spawnPhosphate && r.prefabPhosphate != null)
        {
            GameObject phos = Instantiate(r.prefabPhosphate, pivotGo.transform);
            phos.name = $"{r.prefabPhosphate.name}_Phosphate_{serial}";
            phosPos = baseLocalPos + baseLocalRot * source.phosphateLocalPosition;
            phosRot = baseLocalRot * Quaternion.Euler(source.phosphateLocalRotation);
            phos.transform.localPosition = phosPos;
            phos.transform.localRotation = phosRot;
            phos.transform.localScale = phosScale;
            havePhos = true;
        }

        // --- Lines ---
        if (source.spawnLines)
        {
            Material mat = GetLineMaterial();

            if (haveSugar && r.sugarToBaseLine != null && r.sugarToBaseLine.enabled)
            {
                var l = r.sugarToBaseLine;
                Vector3 a = sugarPos + sugarRot * Vector3.Scale(sugarScale, source.sugarToBaseFrom);
                Vector3 baseScale = baseGo.transform.localScale;
                Vector3 b;

                if (source.sugarToBaseSnapToSurface && haveBaseBounds)
                {
                    Vector3 aInBase = DivideSafe(Quaternion.Inverse(baseLocalRot) * (a - baseLocalPos), baseScale);
                    Vector3 surface = ClosestPointOnBoundsSurface(baseBounds, aInBase);
                    b = baseLocalPos + baseLocalRot * Vector3.Scale(baseScale, surface + source.sugarToBaseTo);
                }
                else
                {
                    b = baseLocalPos + baseLocalRot * Vector3.Scale(baseScale, source.sugarToBaseTo);
                }
                CreateLine(pivotGo.transform, $"__SugarToBase_{serial}", a, b, l.dashed, l.color, l.width, mat);
            }

            if (haveSugar && havePhos && r.sugarToPhosphateLine != null && r.sugarToPhosphateLine.enabled)
            {
                var l = r.sugarToPhosphateLine;
                Vector3 a = sugarPos + sugarRot * Vector3.Scale(sugarScale, source.sugarToPhosphateFrom);
                Vector3 b = phosPos + phosRot * Vector3.Scale(phosScale, source.sugarToPhosphateTo);
                CreateLine(pivotGo.transform, $"__SugarToPhosphate_{serial}", a, b, l.dashed, l.color, l.width, mat);
            }

            if (source.spawnBondLines && source.bondLines != null)
            {
                for (int i = 0; i < source.bondLines.Count; i++)
                {
                    var bl = source.bondLines[i];
                    if (bl == null) continue;
                    CreateLine(pivotGo.transform, $"__Bond_{serial}_{i}", bl.startA, bl.endB, bl.dashed,
                               r.bondLineColor, r.bondLineWidth, mat);
                }
            }
        }

        _pairs.Add(new PairRuntime { pivot = pivotGo.transform, liveY = startY });
        _nextSerial++;
        return true;
    }

    // =================================================================== Lines
    private Material GetLineMaterial()
    {
        if (Render != null && Render.bondLineMaterial != null) return Render.bondLineMaterial;
        if (_fallbackMaterial == null)
        {
            Shader sh = Shader.Find("Sprites/Default");
            if (sh == null) return null;
            _fallbackMaterial = new Material(sh) { name = "DNA_DeletedLine_Fallback", hideFlags = HideFlags.DontSave };
        }
        return _fallbackMaterial;
    }

    private void CreateLine(Transform parent, string objName, Vector3 a, Vector3 b,
                            bool dashed, Color color, float width, Material mat)
    {
        var holder = new GameObject(objName);
        holder.transform.SetParent(parent, false);

        if (!dashed)
        {
            ConfigureLine(holder.AddComponent<LineRenderer>(), a, b, color, width, mat);
            return;
        }

        Vector3 delta = b - a;
        float len = delta.magnitude;
        if (len < 1e-5f) return;

        Vector3 dir = delta / len;
        float dash = Mathf.Max(0.001f, Render.dashLength);
        float period = dash + Mathf.Max(0f, Render.dashGap);
        int n = 0;

        for (float s0 = 0f; s0 < len && n < Render.maxDashesPerLine; s0 += period, n++)
        {
            float e0 = Mathf.Min(s0 + dash, len);
            var d = new GameObject($"Dash_{n}");
            d.transform.SetParent(holder.transform, false);
            ConfigureLine(d.AddComponent<LineRenderer>(), a + dir * s0, a + dir * e0, color, width, mat);
        }
    }

    private void ConfigureLine(LineRenderer lr, Vector3 a, Vector3 b, Color color, float width, Material mat)
    {
        lr.useWorldSpace = false;
        lr.positionCount = 2;
        lr.SetPosition(0, a);
        lr.SetPosition(1, b);
        lr.startWidth = width;
        lr.endWidth = width;
        lr.startColor = color;
        lr.endColor = color;
        lr.alignment = Render.bondLineAlignment;
        lr.numCapVertices = Render.bondLineCapVertices;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.sharedMaterial = mat;
    }

    // =================================================================== Surface snapping helpers
    private static bool TryGetLocalMeshBounds(Transform root, out Bounds bounds)
    {
        bounds = new Bounds();
        bool found = false;
        Matrix4x4 worldToRoot = root.worldToLocalMatrix;

        foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>())
        {
            Mesh mesh = mf.sharedMesh;
            if (mesh == null) continue;

            Matrix4x4 m = worldToRoot * mf.transform.localToWorldMatrix;
            Vector3 c = mesh.bounds.center, e = mesh.bounds.extents;

            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 v = m.MultiplyPoint3x4(c + new Vector3(
                    (corner & 1) == 0 ? -e.x : e.x,
                    (corner & 2) == 0 ? -e.y : e.y,
                    (corner & 4) == 0 ? -e.z : e.z));
                if (!found) { bounds = new Bounds(v, Vector3.zero); found = true; }
                else bounds.Encapsulate(v);
            }
        }
        return found;
    }

    private static Vector3 ClosestPointOnBoundsSurface(Bounds b, Vector3 p)
    {
        Vector3 min = b.min, max = b.max;
        Vector3 c = new Vector3(Mathf.Clamp(p.x, min.x, max.x), Mathf.Clamp(p.y, min.y, max.y), Mathf.Clamp(p.z, min.z, max.z));
        if (c != p) return c;

        float dxMin = p.x - min.x, dxMax = max.x - p.x;
        float dyMin = p.y - min.y, dyMax = max.y - p.y;
        float dzMin = p.z - min.z, dzMax = max.z - p.z;

        float best = dxMin; c = new Vector3(min.x, p.y, p.z);
        if (dxMax < best) { best = dxMax; c = new Vector3(max.x, p.y, p.z); }
        if (dyMin < best) { best = dyMin; c = new Vector3(p.x, min.y, p.z); }
        if (dyMax < best) { best = dyMax; c = new Vector3(p.x, max.y, p.z); }
        if (dzMin < best) { best = dzMin; c = new Vector3(p.x, p.y, min.z); }
        if (dzMax < best) { c = new Vector3(p.x, p.y, max.z); }
        return c;
    }

    private static Vector3 DivideSafe(Vector3 v, Vector3 s) => new Vector3(
        s.x != 0f ? v.x / s.x : v.x,
        s.y != 0f ? v.y / s.y : v.y,
        s.z != 0f ? v.z / s.z : v.z);

    // =================================================================== Container / cleanup
    private void EnsureContainer()
    {
        if (_container != null) return;
        if (!Application.isPlaying) return;   // never spawn in edit mode

        Transform existing = transform.Find(ContainerName);
        if (existing != null) { _container = existing; return; }

        var go = new GameObject(ContainerName);
        go.transform.SetParent(transform, false);
        _container = go.transform;
    }

    [ContextMenu("Clear Generated")]
    public void ClearGenerated()
    {
        _pairs.Clear();
        _incoming.Clear();
        _nextSerial = 0;

        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Transform child = transform.GetChild(i);
            if (child.name != ContainerName) continue;
            if (Application.isPlaying) { child.SetParent(null); Destroy(child.gameObject); }
            else DestroyImmediate(child.gameObject);
        }
        _container = null;
    }

    private void OnValidate()
    {
        fallSpeed = Mathf.Max(0f, fallSpeed);
    }

    private void OnDrawGizmos()
    {
        if (!drawGizmos) return;
        Vector3 top = transform.TransformPoint(new Vector3(spawnPosition.x, topSpawnHeight, spawnPosition.z));
        Vector3 bot = transform.TransformPoint(new Vector3(spawnPosition.x, bottomDespawnHeight, spawnPosition.z));
        Gizmos.color = Color.cyan;
        Gizmos.DrawSphere(top, 0.15f);
        Gizmos.color = Color.red;
        Gizmos.DrawSphere(bot, 0.15f);
        Gizmos.DrawLine(top, bot);
    }
}