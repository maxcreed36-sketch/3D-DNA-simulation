using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// A straight (no helix twist) DNA strand that receives deleted base pairs from a
/// DNAReplicationSimulator and lets them fall straight down.
///
/// • Every time the source deletes a pair, this strand receives ONE half of it (Left or Right).
/// • PREFABS ARE ALWAYS TAKEN FROM THE SOURCE - nothing to assign here:
///     base (exterior) prefabs A/T/C/G, interior element prefabs, sugar, phosphate,
///     and the LineRenderer style (material, alignment, caps, dash settings, colours, widths).
///   The DNA backbone strand prefab is not used.
/// • POSITIONING IS SEPARATE: every position / rotation / scale / distance / line coordinate below
///   belongs to THIS strand only, so it can look different from the source strand.
///
/// • On start (and live in EDIT MODE) the strand is already filled with RANDOM bases from the bottom to the top
///   boundary. After that, every pair deleted by the source feeds in at the top, one base at a time.
///
/// • NEW: raises BaseDeleted(char) every time a base is removed at the bottom boundary.
///
/// NOTE: file name must be "DNAStraightReplicationSimulator.cs".
/// </summary>
[DisallowMultipleComponent]
public class DNAStraightReplicationSimulator : MonoBehaviour
{
    public enum CopySide { Left, Right }

    /// <summary>Extra offset for the INTERIOR element of ONE base letter, added on top of the shared
    /// interior values (Element Distance / Element Local Position / Element Local Rotation).</summary>
    [System.Serializable]
    public class ElementTweak
    {
        [Tooltip("Extra position offset of the interior element, in the base half's local space.")]
        public Vector3 positionOffset = Vector3.zero;
        [Tooltip("Extra local rotation (Euler degrees) of the interior element.")]
        public Vector3 rotationOffset = Vector3.zero;
    }

    // ------------------------------------------------------------------ Source
    [Header("Source Simulator (prefabs + line style come from here)")]
    public DNAReplicationSimulator source;

    [Tooltip("Which half of the deleted pair gets copied into this strand. For an A-T pair: Left = A, Right = T.")]
    public CopySide copySide = CopySide.Left;

    [Header("Size Matching")]
    [Tooltip("ON = sugar, phosphate and interior element scales are read from the source, so every piece " +
             "has exactly the same size as on the source strand (the scale fields below are then ignored).")]
    public bool matchSourceScales = true;
    [Tooltip("ON = compensates for a different Transform scale on this object vs. the source object, so " +
             "pieces end up the same WORLD size as the source even if the two GameObjects are scaled differently.")]
    public bool matchSourceWorldScale = true;

    // ------------------------------------------------------------------ Base (exterior)
    [Header("Position: Base (exterior) half")]
    [Tooltip("Local-space offset of the column. X / Z place the column, Y is ignored (use Top Spawn Height).")]
    public Vector3 spawnPosition = Vector3.zero;
    [Tooltip("Rotation applied to every spawned pivot.")]
    public Vector3 baseRotation = new Vector3(-90f, 0f, 0f);
    [Tooltip("Distance from the column centre to the base half (along the pivot's local X).")]
    public float pairHalfLength = 1f;
    [Tooltip("Extra local rotation of the base half when Copy Side = Left.")]
    public Vector3 leftHalfRotation = Vector3.zero;
    [Tooltip("Extra local rotation of the base half when Copy Side = Right.")]
    public Vector3 rightHalfRotation = Vector3.zero;

    // ------------------------------------------------------------------ Per-base interior tweaks
    [Header("Interior Element: Per-Base Offset (A, C, G, T)")]
    [Tooltip("Extra position / rotation for the interior element of A bases only.")]
    public ElementTweak interiorA = new ElementTweak();
    [Tooltip("Extra position / rotation for the interior element of C bases only.")]
    public ElementTweak interiorC = new ElementTweak();
    [Tooltip("Extra position / rotation for the interior element of G bases only.")]
    public ElementTweak interiorG = new ElementTweak();
    [Tooltip("Extra position / rotation for the interior element of T bases only.")]
    public ElementTweak interiorT = new ElementTweak();

    // ------------------------------------------------------------------ Interior
    [Header("Position: Interior element")]
    public bool spawnElements = true;
    [Tooltip("Distance of the element from the pair centre along the pivot's X axis.")]
    public float elementDistance = 0.5f;
    [Tooltip("Extra fine-tune offset in the base half's local space.")]
    public Vector3 elementLocalPosition = Vector3.zero;
    public Vector3 elementLocalRotation = Vector3.zero;
    public Vector3 elementLocalScale = Vector3.one;

    // ------------------------------------------------------------------ Sugar
    [Header("Position: Sugar")]
    public bool spawnSugar = true;
    [Tooltip("Offset relative to the base half, in the base half's local space.")]
    public Vector3 sugarLocalPosition = Vector3.zero;
    public Vector3 sugarLocalRotation = Vector3.zero;
    public Vector3 sugarLocalScale = Vector3.one;

    // ------------------------------------------------------------------ Phosphate
    [Header("Position: Phosphate")]
    public bool spawnPhosphate = true;
    public Vector3 phosphateLocalPosition = Vector3.zero;
    public Vector3 phosphateLocalRotation = Vector3.zero;
    public Vector3 phosphateLocalScale = Vector3.one;

    // ------------------------------------------------------------------ Lines
    [Header("Lines (LineRenderers - style copied from source, coordinates are local)")]
    [Tooltip("Master switch for all lines on this strand.")]
    public bool spawnLines = true;

    [Tooltip("Sugar -> base line. Colour / width / dashed / enabled come from the source's 'Sugar To Base Line'.")]
    public Vector3 sugarToBaseFrom = Vector3.zero;
    public Vector3 sugarToBaseTo = Vector3.zero;
    [Tooltip("End of the line snaps to the closest point on the base half's mesh bounds.")]
    public bool sugarToBaseSnapToSurface = true;

    [Tooltip("Sugar -> phosphate line. Style comes from the source's 'Sugar To Phosphate Line'.")]
    public Vector3 sugarToPhosphateFrom = Vector3.zero;
    public Vector3 sugarToPhosphateTo = Vector3.zero;

    [Tooltip("Optional free lines (e.g. dangling hydrogen-bond stubs). Coordinates are in pivot-local space. " +
             "Colour / width come from the source's Bond Line settings.")]
    public bool spawnBondLines = false;
    public List<DNAReplicationSimulator.BondLine> bondLines = new List<DNAReplicationSimulator.BondLine>();

    // ------------------------------------------------------------------ Spacing / fall
    [Header("Spacing")]
    [Tooltip("ON = pairs per step, stack gap and step height are read from the source.")]
    public bool copySpacingFromSource = true;
    public float stepHeight = 1f;
    [Min(0.01f)] public float pairsPerStep = 1f;
    public float stackGap = 0f;

    [Header("Fall Speed")]
    public bool inheritFallSpeedFromSource = true;
    [Min(0f)] public float fallSpeed = 0.25f;

    // ------------------------------------------------------------------ Bounds
    [Header("Bounds")]
    [Tooltip("Local Y at which new pairs enter.")]
    public float topSpawnHeight = 10f;
    [Tooltip("Local Y below which a pair is removed.")]
    public float bottomDespawnHeight = 0f;

    [Header("Initial Random Fill")]
    [Tooltip("ON = the FIRST group is random: the column is filled with random bases from the top boundary all the way " +
             "down to the bottom boundary (edit-mode preview AND when Play starts). After that group, every new base is the " +
             "pair most recently deleted by the source simulator, entering at the top. OFF = starts empty.")]
    public bool spawnInitialFill = true;
    [Tooltip("Seed for the random bases in EDIT MODE (keeps the preview stable while you tweak values). Change it to re-roll.")]
    public int randomSeed = 12345;
    [Tooltip("ON = a different random sequence every time Play starts. OFF = the Seed is also used in play mode.")]
    public bool randomizeEveryPlay = true;

    [Header("Debug")]
    [Tooltip("Log every base received from the source and every base spawned from it.")]
    public bool logSourceBases = false;
    [Tooltip("Read-only counters: bases received from the source / spawned from the source since Play started.")]
    public int receivedFromSource;
    public int spawnedFromSource;

    [Header("Options")]
    [Tooltip("Rebuild the preview in the editor whenever an inspector value changes.")]
    public bool liveUpdateInEditor = true;
    public bool drawGizmos = true;

    // ------------------------------------------------------------------ Deletion event (NEW)
    /// <summary>Raised once for every base removed at the bottom boundary, oldest first.
    /// Not raised by ClearGenerated / rebuilds.</summary>
    public event System.Action<char> BaseDeleted;
    private readonly List<char> _deletedThisFrame = new();

    // ------------------------------------------------------------------ Runtime
    private const string ContainerName = "__DNA_Straight_Generated";
    private const string PairPivotName = "__StraightPairPivot";
    private const int MaxSpawnsPerFrame = 32;

    private sealed class PairRuntime
    {
        public Transform pivot;
        public float liveY;
        public char letter;
        public int serial;
    }

    private readonly List<PairRuntime> _pairs = new();
    private readonly Queue<(char left, char right)> _incoming = new();
    private Transform _container;
    private int _nextSerial;
    private DNAReplicationSimulator _subscribedSource;
    private static Material _fallbackMaterial;
    private const string Bases = "ATCG";
#if UNITY_EDITOR
    private bool _updateQueued;
#endif

    public int PendingIncomingCount => _incoming.Count;
    public int ActivePairCount => _pairs.Count;

    public float EffectiveFallSpeed =>
        inheritFallSpeedFromSource && source != null ? source.fallingSpeed : fallSpeed;

    // Now PUBLIC so other scripts can inherit these values.
    public float EffStepHeight => copySpacingFromSource && source != null ? source.StepHeight : stepHeight;
    public float EffPairsPerStep => Mathf.Max(0.01f, copySpacingFromSource && source != null ? source.pairsPerStep : pairsPerStep);
    public float EffStackGap => copySpacingFromSource && source != null ? source.stackGap : stackGap;

    public Vector3 EffElementScale => matchSourceScales && source != null ? source.elementLocalScale : elementLocalScale;
    public Vector3 EffSugarScale => matchSourceScales && source != null ? source.sugarLocalScale : sugarLocalScale;
    public Vector3 EffPhosphateScale => matchSourceScales && source != null ? source.phosphateLocalScale : phosphateLocalScale;

    /// <summary>Scale factor applied to each pair pivot so its content has the same world size as on the source.</summary>
    private Vector3 PivotScale
    {
        get
        {
            if (!matchSourceWorldScale || source == null) return Vector3.one;
            Vector3 a = source.transform.lossyScale;
            Vector3 b = transform.lossyScale;
            return new Vector3(
                b.x != 0f ? a.x / b.x : 1f,
                b.y != 0f ? a.y / b.y : 1f,
                b.z != 0f ? a.z / b.z : 1f);
        }
    }

    private ElementTweak GetInteriorTweak(char b)
    {
        ElementTweak t = b switch { 'A' => interiorA, 'C' => interiorC, 'G' => interiorG, _ => interiorT };
        return t ?? new ElementTweak();
    }

    private GameObject GetBasePrefab(char b) => b switch
    {
        'A' => source.prefabA, 'T' => source.prefabT, 'C' => source.prefabC, _ => source.prefabG,
    };

    private GameObject GetElementPrefab(char b) => b switch
    {
        'A' => source.elementA, 'T' => source.elementT, 'C' => source.elementC, _ => source.elementG,
    };

    // =================================================================== Unity callbacks
    private void OnEnable()
    {
        TrySubscribe();
#if UNITY_EDITOR
        if (!Application.isPlaying && liveUpdateInEditor) QueuePreviewRebuild();
#endif
    }

    private void OnDisable() { Unsubscribe(); }

    private void Start()
    {
        if (!Application.isPlaying) return;

        // Throw away any edit-mode preview, then build the real starting column.
        ClearGenerated();
        receivedFromSource = 0;
        spawnedFromSource = 0;
        EnsureContainer();
        if (spawnInitialFill) FillInitial(randomizeEveryPlay ? System.Environment.TickCount : randomSeed);
        TrySubscribe();
    }

    private void Update()
    {
        if (!Application.isPlaying) return;

        if (_subscribedSource != source) TrySubscribe();

        EnsureContainer();
        if (_container == null) return;

        float speed = EffectiveFallSpeed;

        if (speed > 0f)
        {
            float dy = speed * Time.deltaTime;
            for (int i = 0; i < _pairs.Count; i++)
            {
                PairRuntime pr = _pairs[i];
                if (pr.pivot == null) continue;
                pr.liveY -= dy;
                Vector3 p = pr.pivot.localPosition;
                p.y = pr.liveY;
                pr.pivot.localPosition = p;
            }
        }

        // Despawn at the bottom and record the deleted letters (NEW).
        _deletedThisFrame.Clear();
        for (int i = _pairs.Count - 1; i >= 0; i--)
        {
            PairRuntime pr = _pairs[i];
            if (pr.liveY < bottomDespawnHeight)
            {
                if (pr.pivot != null) Destroy(pr.pivot.gameObject);
                _deletedThisFrame.Add(pr.letter);
                _pairs.RemoveAt(i);
            }
        }
        // The loop runs newest to oldest, so report in reverse to keep deletion order.
        for (int i = _deletedThisFrame.Count - 1; i >= 0; i--)
            BaseDeleted?.Invoke(_deletedThisFrame[i]);

        TryDrainQueue();
    }

    // =================================================================== Event wiring
    private void TrySubscribe()
    {
        if (_subscribedSource == source) return;
        Unsubscribe();

        if (source != null)
        {
            source.PairDeleted += OnSourcePairDeleted;
            _subscribedSource = source;
        }
    }

    private void Unsubscribe()
    {
        if (_subscribedSource != null)
        {
            _subscribedSource.PairDeleted -= OnSourcePairDeleted;
            _subscribedSource = null;
        }
    }

    private void OnSourcePairDeleted(char leftLetter, char rightLetter)
    {
        _incoming.Enqueue((leftLetter, rightLetter));
        receivedFromSource++;
        if (logSourceBases)
            Debug.Log($"Straight strand: received deleted pair '{leftLetter}-{rightLetter}' from source " +
                      $"(copying {(copySide == CopySide.Left ? leftLetter : rightLetter)}).", this);
    }

    // =================================================================== Spawning
    private void TryDrainQueue()
    {
        if (_incoming.Count == 0 || source == null) return;

        float spacing = (EffStepHeight + EffStackGap) / EffPairsPerStep;
        if (spacing < 1e-4f) spacing = 1e-4f;

        int guard = 0;
        while (_incoming.Count > 0 && guard++ < MaxSpawnsPerFrame)
        {
            // Spawn height: exactly one spacing above the highest live base (never above the top),
            // so the column stays evenly spaced even if a frame overshoots.
            float y = topSpawnHeight;
            if (_pairs.Count > 0)
            {
                float highest = GetHighestY();
                if (highest > topSpawnHeight - spacing) break;   // no room yet - wait, keep deletion order
                y = Mathf.Min(topSpawnHeight, highest + spacing);
            }

            // First in, first out: bases enter in the same order the source deleted them.
            var pair = _incoming.Dequeue();
            if (SpawnPair(pair.left, pair.right, y))
            {
                spawnedFromSource++;
                if (logSourceBases)
                    Debug.Log($"Straight strand: spawned '{(copySide == CopySide.Left ? pair.left : pair.right)}' " +
                              $"from deleted pair '{pair.left}-{pair.right}'.", this);
            }
        }
    }

    private float GetHighestY()
    {
        float best = float.NegativeInfinity;
        for (int i = 0; i < _pairs.Count; i++)
            if (_pairs[i].liveY > best) best = _pairs[i].liveY;
        return best;
    }

    private bool SpawnPair(char leftLetter, char rightLetter, float startY)
    {
        bool isLeft = copySide == CopySide.Left;
        char letter = isLeft ? leftLetter : rightLetter;
        float side = isLeft ? -1f : 1f;

        GameObject prefab = GetBasePrefab(letter);
        if (prefab == null)
        {
            Debug.LogWarning($"DNAStraightReplicationSimulator: the source has no prefab for base '{letter}' - skipped.", this);
            return false;
        }

        int serial = _nextSerial;

        GameObject pivotGo = new GameObject($"{PairPivotName}_{serial}");
        pivotGo.transform.SetParent(_container, false);
        pivotGo.transform.localPosition = new Vector3(spawnPosition.x, startY, spawnPosition.z);
        pivotGo.transform.localRotation = Quaternion.Euler(baseRotation);
        pivotGo.transform.localScale = PivotScale;

        // --- Base (exterior) half ---
        Vector3 baseLocalPos = Vector3.right * (side * pairHalfLength);
        Quaternion baseLocalRot = Quaternion.Euler(isLeft ? leftHalfRotation : rightHalfRotation);

        GameObject baseGo = Instantiate(prefab, pivotGo.transform);
        baseGo.name = $"{prefab.name}_{letter}_Pair_{serial}";
        baseGo.transform.localPosition = baseLocalPos;
        baseGo.transform.localRotation = baseLocalRot;
        baseGo.transform.localScale = prefab.transform.localScale;

        // Bounds of the base block only (measured before the interior element is added).
        bool haveBaseBounds = TryGetLocalMeshBounds(baseGo.transform, out Bounds baseBounds);

        // --- Interior element (child of the base half) ---
        if (spawnElements)
        {
            GameObject elementPrefab = GetElementPrefab(letter);
            if (elementPrefab != null)
            {
                GameObject element = Instantiate(elementPrefab, baseGo.transform);
                element.name = $"{elementPrefab.name}_Element_{letter}";

                ElementTweak tw = GetInteriorTweak(letter);
                Vector3 desiredInPivot = Vector3.right * (side * elementDistance);
                Vector3 delta = Quaternion.Inverse(baseLocalRot) * (desiredInPivot - baseLocalPos);
                Vector3 goScale = baseGo.transform.localScale;
                if (goScale.x != 0f) delta.x /= goScale.x;
                if (goScale.y != 0f) delta.y /= goScale.y;
                if (goScale.z != 0f) delta.z /= goScale.z;

                element.transform.localPosition = delta + elementLocalPosition + tw.positionOffset;
                element.transform.localRotation = Quaternion.Euler(elementLocalRotation) * Quaternion.Euler(tw.rotationOffset);
                element.transform.localScale = EffElementScale;
            }
        }

        // --- Sugar (sibling of the base half, own scale) ---
        bool haveSugar = false, havePhos = false;
        Vector3 sugarPos = Vector3.zero, phosPos = Vector3.zero;
        Quaternion sugarRot = Quaternion.identity, phosRot = Quaternion.identity;

        if (spawnSugar && source.prefabSugar != null)
        {
            GameObject sugar = Instantiate(source.prefabSugar, pivotGo.transform);
            sugar.name = $"{source.prefabSugar.name}_Sugar_{serial}";
            sugarPos = baseLocalPos + baseLocalRot * sugarLocalPosition;
            sugarRot = baseLocalRot * Quaternion.Euler(sugarLocalRotation);
            sugar.transform.localPosition = sugarPos;
            sugar.transform.localRotation = sugarRot;
            sugar.transform.localScale = EffSugarScale;
            haveSugar = true;
        }

        // --- Phosphate ---
        if (spawnPhosphate && source.prefabPhosphate != null)
        {
            GameObject phos = Instantiate(source.prefabPhosphate, pivotGo.transform);
            phos.name = $"{source.prefabPhosphate.name}_Phosphate_{serial}";
            phosPos = baseLocalPos + baseLocalRot * phosphateLocalPosition;
            phosRot = baseLocalRot * Quaternion.Euler(phosphateLocalRotation);
            phos.transform.localPosition = phosPos;
            phos.transform.localRotation = phosRot;
            phos.transform.localScale = EffPhosphateScale;
            havePhos = true;
        }

        // --- Lines ---
        if (spawnLines)
        {
            Material mat = GetLineMaterial();

            if (haveSugar && source.sugarToBaseLine != null && source.sugarToBaseLine.enabled)
            {
                var l = source.sugarToBaseLine;
                Vector3 a = sugarPos + sugarRot * Vector3.Scale(EffSugarScale, sugarToBaseFrom);
                Vector3 baseScale = baseGo.transform.localScale;
                Vector3 b;

                if (sugarToBaseSnapToSurface && haveBaseBounds)
                {
                    Vector3 aInBase = DivideSafe(Quaternion.Inverse(baseLocalRot) * (a - baseLocalPos), baseScale);
                    Vector3 surface = ClosestPointOnBoundsSurface(baseBounds, aInBase);
                    b = baseLocalPos + baseLocalRot * Vector3.Scale(baseScale, surface + sugarToBaseTo);
                }
                else
                {
                    b = baseLocalPos + baseLocalRot * Vector3.Scale(baseScale, sugarToBaseTo);
                }

                CreateLine(pivotGo.transform, $"__SugarToBase_{serial}", a, b, l.dashed, l.color, l.width, mat);
            }

            if (haveSugar && havePhos && source.sugarToPhosphateLine != null && source.sugarToPhosphateLine.enabled)
            {
                var l = source.sugarToPhosphateLine;
                Vector3 a = sugarPos + sugarRot * Vector3.Scale(EffSugarScale, sugarToPhosphateFrom);
                Vector3 b = phosPos + phosRot * Vector3.Scale(EffPhosphateScale, sugarToPhosphateTo);
                CreateLine(pivotGo.transform, $"__SugarToPhosphate_{serial}", a, b, l.dashed, l.color, l.width, mat);
            }

            if (spawnBondLines && bondLines != null)
            {
                for (int i = 0; i < bondLines.Count; i++)
                {
                    var bl = bondLines[i];
                    if (bl == null) continue;
                    CreateLine(pivotGo.transform, $"__Bond_{serial}_{i}", bl.startA, bl.endB, bl.dashed,
                               source.bondLineColor, source.bondLineWidth, mat);
                }
            }
        }

        MarkTemporary(pivotGo);

        _pairs.Add(new PairRuntime { pivot = pivotGo.transform, liveY = startY, letter = letter, serial = serial });
        _nextSerial++;
        return true;
    }

    // =================================================================== Initial random fill / edit-mode preview
    /// <summary>Fills the column from the top boundary downwards with random bases, one per spacing.</summary>
    private void FillInitial(int seed)
    {
        if (source == null || _container == null) return;

        float spacing = (EffStepHeight + EffStackGap) / EffPairsPerStep;
        if (spacing < 1e-4f) spacing = 1e-4f;
        if (topSpawnHeight <= bottomDespawnHeight) return;

        var rng = new System.Random(seed);
        int guard = 0;

        for (float y = topSpawnHeight; y >= bottomDespawnHeight && guard < 500; y -= spacing, guard++)
        {
            char left = Bases[rng.Next(Bases.Length)];
            SpawnPair(left, Complement(left), y);
        }
    }

    private static char Complement(char b) => b switch { 'A' => 'T', 'T' => 'A', 'C' => 'G', _ => 'C' };

    /// <summary>In edit mode, generated objects are never saved with the scene.</summary>
    private static void MarkTemporary(GameObject root)
    {
        if (Application.isPlaying) return;
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            t.gameObject.hideFlags = HideFlags.DontSave;
    }

    [ContextMenu("Rebuild Preview")]
    public void RebuildPreview()
    {
        if (Application.isPlaying) return;
        ClearGenerated();
        if (source == null || !spawnInitialFill) return;
        EnsureContainer();
        FillInitial(randomSeed);
    }

#if UNITY_EDITOR
    private void QueuePreviewRebuild()
    {
        if (_updateQueued) return;
        if (!gameObject.scene.IsValid()) return;
        if (PrefabUtility.IsPartOfPrefabAsset(this)) return;

        _updateQueued = true;
        EditorApplication.delayCall += () =>
        {
            _updateQueued = false;
            if (this == null || Application.isPlaying) return;
            RebuildPreview();
        };
    }
#endif

    // =================================================================== Lines
    private Material GetLineMaterial()
    {
        if (source != null && source.bondLineMaterial != null) return source.bondLineMaterial;

        if (_fallbackMaterial == null)
        {
            Shader sh = Shader.Find("Sprites/Default");
            if (sh == null) return null;
            _fallbackMaterial = new Material(sh) { name = "DNA_StraightLine_Fallback", hideFlags = HideFlags.DontSave };
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
        float dash = Mathf.Max(0.001f, source.dashLength);
        float period = dash + Mathf.Max(0f, source.dashGap);
        int n = 0;

        for (float s0 = 0f; s0 < len && n < source.maxDashesPerLine; s0 += period, n++)
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
        lr.alignment = source.bondLineAlignment;
        lr.numCapVertices = source.bondLineCapVertices;
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
            Vector3 c = mesh.bounds.center;
            Vector3 e = mesh.bounds.extents;

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
        Vector3 c = new Vector3(
            Mathf.Clamp(p.x, min.x, max.x),
            Mathf.Clamp(p.y, min.y, max.y),
            Mathf.Clamp(p.z, min.z, max.z));

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
        if (!Application.isPlaying && !gameObject.scene.IsValid()) return;

        Transform existing = transform.Find(ContainerName);
        if (existing != null) { _container = existing; return; }

        var go = new GameObject(ContainerName);
        go.transform.SetParent(transform, false);
        if (!Application.isPlaying) go.hideFlags = HideFlags.DontSave;
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
        stepHeight = Mathf.Max(0.0001f, stepHeight);
        pairsPerStep = Mathf.Max(0.01f, pairsPerStep);
        pairHalfLength = Mathf.Max(0f, pairHalfLength);
        elementDistance = Mathf.Max(0f, elementDistance);
        fallSpeed = Mathf.Max(0f, fallSpeed);

#if UNITY_EDITOR
        if (liveUpdateInEditor && !Application.isPlaying) QueuePreviewRebuild();
#endif
    }

    // =================================================================== Gizmos
    private void OnDrawGizmos()
    {
        if (!drawGizmos) return;

        Vector3 top = transform.TransformPoint(new Vector3(spawnPosition.x, topSpawnHeight, spawnPosition.z));
        Vector3 bot = transform.TransformPoint(new Vector3(spawnPosition.x, bottomDespawnHeight, spawnPosition.z));

        Gizmos.color = Color.yellow;
        Gizmos.DrawSphere(top, 0.15f);
        Gizmos.color = Color.red;
        Gizmos.DrawSphere(bot, 0.15f);
        Gizmos.DrawLine(top, bot);

        if (pairHalfLength > 0f)
        {
            float side = copySide == CopySide.Left ? -1f : 1f;
            Vector3 offset = transform.TransformDirection(Quaternion.Euler(baseRotation) * (Vector3.right * (side * pairHalfLength)));
            Gizmos.color = Color.magenta;
            Gizmos.DrawLine(top, top + offset);
            Gizmos.DrawSphere(top + offset, 0.12f);
        }
    }
}