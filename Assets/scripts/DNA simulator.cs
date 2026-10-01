using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Spawns a stacked, twisting DNA strand from prefabs, with optional base pairs (A-T, C-G),
/// optional interior nucleotide elements (A, T, C, G) placed inside each base half, sugar and
/// phosphate pieces, and independent LineRenderer "bond lines" per pair orientation.
/// Each of the four pair orientations (A-T, T-A, C-G, G-C) can have its own interior-element
/// rotation, interior-element distance override and its own list of bond lines (any number,
/// each with its own A/B coordinates). Every pair gets its own random base, and the sequence is
/// cached so it only re-rolls when count / pairsPerStep / pairHeightOffset change. Everything
/// is built in this object's local space, so you can move / rotate / scale freely.
/// </summary>
[DisallowMultipleComponent]
public class DNASimulator : MonoBehaviour
{
    /// <summary>One bond line, authored in PAIR-PIVOT local space.</summary>
    [System.Serializable]
    public class BondLine
    {
        [Tooltip("Start point (A) in pair-pivot local space. The pivot's X axis runs from the left base to the right base.")]
        public Vector3 startA = new Vector3(-0.2f, 0f, 0f);
        [Tooltip("End point (B) in pair-pivot local space.")]
        public Vector3 endB = new Vector3(0.2f, 0f, 0f);
        [Tooltip("Draw this line as a dashed line (dash / gap lengths are set in the Bond Lines section).")]
        public bool dashed = true;

        public BondLine() { }
        public BondLine(Vector3 a, Vector3 b, bool dashed = true) { startA = a; endB = b; this.dashed = dashed; }
    }

    /// <summary>A single connector line (sugar -> base, sugar -> phosphate, phosphate -> next sugar)
    /// drawn on every base half. Endpoints are the two pieces' positions plus an optional offset in
    /// each piece's OWN local space. Flipped pairs (odd pairs when Alternating Flip is on) can use
    /// their own set of offsets.</summary>
    [System.Serializable]
    public class LinkLine
    {
        public bool enabled = true;
        public bool dashed = false;
        public Color color = Color.white;
        [Min(0f)] public float width = 0.03f;

        [Tooltip("Sugar -> base line only. If on, the line's END snaps to the closest point on the base half's " +
                 "mesh bounds (toward the sugar) instead of the base pivot. The 'To' offsets are then added " +
                 "on top of that surface point, in the base half's local space.")]
        public bool snapToTargetSurface = false;

        [Header("Normal pairs")]
        [Tooltip("LEFT side: offset of the line's start, in the FROM piece's local space (rotation + scale applied).")]
        public Vector3 fromOffsetLeft = Vector3.zero;
        [Tooltip("LEFT side: offset of the line's end, in the TO piece's local space (rotation + scale applied).")]
        public Vector3 toOffsetLeft = Vector3.zero;
        [Tooltip("RIGHT side: offset of the line's start, in the FROM piece's local space.")]
        public Vector3 fromOffsetRight = Vector3.zero;
        [Tooltip("RIGHT side: offset of the line's end, in the TO piece's local space.")]
        public Vector3 toOffsetRight = Vector3.zero;

        [Header("Flipped pairs (odd pairs when Alternating Flip is on)")]
        [Tooltip("If off, flipped pairs reuse the normal offsets above (old behaviour).")]
        public bool useFlippedOffsets = false;
        public Vector3 fromOffsetLeftFlipped = Vector3.zero;
        public Vector3 toOffsetLeftFlipped = Vector3.zero;
        public Vector3 fromOffsetRightFlipped = Vector3.zero;
        public Vector3 toOffsetRightFlipped = Vector3.zero;

        public Vector3 GetFrom(bool left, bool flipped)
        {
            if (flipped && useFlippedOffsets) return left ? fromOffsetLeftFlipped : fromOffsetRightFlipped;
            return left ? fromOffsetLeft : fromOffsetRight;
        }

        public Vector3 GetTo(bool left, bool flipped)
        {
            if (flipped && useFlippedOffsets) return left ? toOffsetLeftFlipped : toOffsetRightFlipped;
            return left ? toOffsetLeft : toOffsetRight;
        }
    }

    // ------------------------------------------------------------------ Strand
    [Header("Prefab - DNA Strand")]
    [Tooltip("The backbone strand prefab. Spawned once per step.")]
    public GameObject prefab;

    // ------------------------------------------------------------------ Base prefabs
    [Header("Base Pair Prefabs (A-T, C-G)")]
    [Tooltip("A always pairs with T, C always pairs with G.")]
    public GameObject prefabA;
    public GameObject prefabT;
    public GameObject prefabC;
    public GameObject prefabG;

    public bool spawnBasePairs = true;

    // ------------------------------------------------------------------ Interior elements
    [Header("Interior Elements (A, T, C, G)")]
    [Tooltip("Interior element prefabs. Each one is spawned as a child INSIDE its matching base half.")]
    public GameObject elementA;
    public GameObject elementT;
    public GameObject elementC;
    public GameObject elementG;

    public bool spawnElements = true;

    [Tooltip("Default distance of each element from the PAIR CENTER, along the pair pivot's local X axis. " +
             "The base half's own half length is NOT affected. 0 = element sits at the pair center. " +
             "Can be overridden per pattern below.")]
    public float elementDistance = 0.5f;
    [Tooltip("Extra fine-tune offset applied on top of Element Distance, in the base half's own local space.")]
    public Vector3 elementLocalPosition = Vector3.zero;
    [Tooltip("Default local rotation of the element inside its base half. Can be overridden per pattern below.")]
    public Vector3 elementLocalRotation = Vector3.zero;
    [Tooltip("Local scale of the element inside its base half.")]
    public Vector3 elementLocalScale = Vector3.one;

    // ------------------------------------------------------------------ Sugar elements
    [Header("Sugar Elements")]
    [Tooltip("Sugar prefab, spawned beside every base half (one per base, left and right). " +
             "Parented to the pair pivot, NOT to the base half, so it does not inherit the base's scale.")]
    public GameObject prefabSugar;
    public bool spawnSugar = true;

    [Tooltip("Offset of the sugar relative to its base half, in the base half's LOCAL space, LEFT side.")]
    public Vector3 sugarLocalPositionLeft = Vector3.zero;
    [Tooltip("Offset of the sugar relative to its base half, in the base half's LOCAL space, RIGHT side.")]
    public Vector3 sugarLocalPositionRight = Vector3.zero;

    [Tooltip("Rotation of the sugar relative to its base half, LEFT side.")]
    public Vector3 sugarLocalRotationLeft = Vector3.zero;
    [Tooltip("Rotation of the sugar relative to its base half, RIGHT side.")]
    public Vector3 sugarLocalRotationRight = Vector3.zero;

    [Tooltip("Scale of the sugar (applied directly, not multiplied by the base half's scale).")]
    public Vector3 sugarLocalScale = Vector3.one;

    // ------------------------------------------------------------------ Phosphate elements
    [Header("Phosphate Elements")]
    [Tooltip("Phosphate prefab, spawned beside every base half (one per base, left and right). " +
             "Parented to the pair pivot, NOT to the base half, so it does not inherit the base's scale.")]
    public GameObject prefabPhosphate;
    public bool spawnPhosphate = true;

    [Tooltip("Offset of the phosphate relative to its base half, in the base half's LOCAL space, LEFT side.")]
    public Vector3 phosphateLocalPositionLeft = Vector3.zero;
    [Tooltip("Offset of the phosphate relative to its base half, in the base half's LOCAL space, RIGHT side.")]
    public Vector3 phosphateLocalPositionRight = Vector3.zero;

    [Tooltip("Rotation of the phosphate relative to its base half, LEFT side.")]
    public Vector3 phosphateLocalRotationLeft = Vector3.zero;
    [Tooltip("Rotation of the phosphate relative to its base half, RIGHT side.")]
    public Vector3 phosphateLocalRotationRight = Vector3.zero;

    [Tooltip("Scale of the phosphate (applied directly, not multiplied by the base half's scale).")]
    public Vector3 phosphateLocalScale = Vector3.one;

    // ------------------------------------------------------------------ Flip alternate pairs
    [Header("Sugar / Phosphate Alternating Flip")]
    [Tooltip("If enabled, sugar and phosphate on every OTHER base pair use the Flipped values " +
             "below instead of the normal ones. Pair 0 = normal, pair 1 = flipped, pair 2 = normal, ...")]
    public bool alternateSugarPhosphateFlip = true;
    [Tooltip("If enabled, on flipped pairs the phosphate is carried along with its sugar: it keeps its normal " +
             "position/rotation relative to the sugar, so whatever flip the sugar gets, the phosphate gets too. " +
             "The 'Flipped Phosphate' values below are then ignored (they are still used if no sugar is spawned).")]
    public bool phosphateFollowsSugarFlip = true;

    [Header("Flipped Sugar (used on odd pairs when Alternating Flip is on)")]
    [Tooltip("Flipped position offset for the LEFT sugar (base half's local space).")]
    public Vector3 sugarLocalPositionLeftFlipped = Vector3.zero;
    [Tooltip("Flipped position offset for the RIGHT sugar (base half's local space).")]
    public Vector3 sugarLocalPositionRightFlipped = Vector3.zero;

    [Tooltip("Flipped rotation for the LEFT sugar.")]
    public Vector3 sugarLocalRotationLeftFlipped = new Vector3(0f, 180f, 0f);
    [Tooltip("Flipped rotation for the RIGHT sugar.")]
    public Vector3 sugarLocalRotationRightFlipped = new Vector3(0f, 180f, 0f);

    [Header("Flipped Phosphate (used on odd pairs when Alternating Flip is on)")]
    [Tooltip("Flipped position offset for the LEFT phosphate (base half's local space).")]
    public Vector3 phosphateLocalPositionLeftFlipped = Vector3.zero;
    [Tooltip("Flipped position offset for the RIGHT phosphate (base half's local space).")]
    public Vector3 phosphateLocalPositionRightFlipped = Vector3.zero;

    [Tooltip("Flipped rotation for the LEFT phosphate.")]
    public Vector3 phosphateLocalRotationLeftFlipped = new Vector3(0f, 180f, 0f);
    [Tooltip("Flipped rotation for the RIGHT phosphate.")]
    public Vector3 phosphateLocalRotationRightFlipped = new Vector3(0f, 180f, 0f);

    // ------------------------------------------------------------------ Pairs per step
    [Header("Base Pairs Per Step")]
    [Tooltip("How many base pairs fit inside one strand step. Fractional values are allowed (e.g. 2.5). " +
             "Total pairs = Round(count * pairsPerStep).")]
    [Min(0.01f)] public float pairsPerStep = 1f;

    // ------------------------------------------------------------------ Pair layout
    [Header("Base Pair Layout")]
    [Tooltip("Distance from the strand center to each base prefab (along the pair pivot's local X).")]
    public float pairHalfLength = 1f;
    [Tooltip("If true, each pair rotates with the strand twist so the bases stay inside the helix.")]
    public bool followStrandTwist = true;

    [Header("Pair Rotation (whole pair, global)")]
    [Tooltip("Rotation of the pair pivot. Both halves orbit the pair center with this rotation.")]
    public Vector3 basePairRotation = Vector3.zero;
    [Tooltip("Extra rotation added for every base pair after the first (multiplied by the pair index).")]
    public Vector3 rotationPerPairStep = new Vector3(0f, 5f, 0f);

    [Header("Pair Rotation (per half, local)")]
    [Tooltip("Extra local rotation on the LEFT half only (relative to the pair pivot).")]
    public Vector3 leftPairRotation = Vector3.zero;
    [Tooltip("Extra local rotation on the RIGHT half only (relative to the pair pivot).")]
    public Vector3 rightPairRotation = Vector3.zero;

    [Tooltip("Shifts the whole pair pattern up, in fractions of a step.")]
    public float pairHeightOffset = 0f;

    // ------------------------------------------------------------------ Per-pattern overrides
    [Header("Per-Pattern Interior Element Rotation")]
    [Tooltip("Extra LOCAL rotation on the interior element of A-T pairs, LEFT half (A).")]
    public Vector3 elementRotationAT_L = Vector3.zero;
    [Tooltip("Extra LOCAL rotation on the interior element of A-T pairs, RIGHT half (T).")]
    public Vector3 elementRotationAT_R = Vector3.zero;

    [Tooltip("Extra LOCAL rotation on the interior element of T-A pairs, LEFT half (T).")]
    public Vector3 elementRotationTA_L = Vector3.zero;
    [Tooltip("Extra LOCAL rotation on the interior element of T-A pairs, RIGHT half (A).")]
    public Vector3 elementRotationTA_R = Vector3.zero;

    [Tooltip("Extra LOCAL rotation on the interior element of C-G pairs, LEFT half (C).")]
    public Vector3 elementRotationCG_L = Vector3.zero;
    [Tooltip("Extra LOCAL rotation on the interior element of C-G pairs, RIGHT half (G).")]
    public Vector3 elementRotationCG_R = Vector3.zero;

    [Tooltip("Extra LOCAL rotation on the interior element of G-C pairs, LEFT half (G).")]
    public Vector3 elementRotationGC_L = Vector3.zero;
    [Tooltip("Extra LOCAL rotation on the interior element of G-C pairs, RIGHT half (C).")]
    public Vector3 elementRotationGC_R = Vector3.zero;

    [Header("Per-Pattern Interior Element Distance")]
    [Tooltip("Element Distance for A-T pairs. 0 = use the default Element Distance above.")]
    [Min(0f)] public float elementDistanceAT = 0f;
    [Tooltip("Element Distance for T-A pairs. 0 = use the default Element Distance above.")]
    [Min(0f)] public float elementDistanceTA = 0f;
    [Tooltip("Element Distance for C-G pairs. 0 = use the default Element Distance above.")]
    [Min(0f)] public float elementDistanceCG = 0f;
    [Tooltip("Element Distance for G-C pairs. 0 = use the default Element Distance above.")]
    [Min(0f)] public float elementDistanceGC = 0f;

    // ------------------------------------------------------------------ Bond lines
    [Header("Bond Lines (LineRenderer, independent of base prefabs)")]
    [Tooltip("Spawn LineRenderer bond lines for every base pair.")]
    public bool spawnBondLines = true;
    [Tooltip("Material for all bond lines. If empty, a basic Sprites/Default material is used " +
             "(assign your own for builds, since Shader.Find can be stripped).")]
    public Material bondLineMaterial;
    public Color bondLineColor = Color.white;
    [Min(0f)] public float bondLineWidth = 0.03f;
    [Tooltip("View = lines always face the camera. TransformZ = lines face the pair pivot's Z axis.")]
    public LineAlignment bondLineAlignment = LineAlignment.View;
    [Min(0)] public int bondLineCapVertices = 0;

    [Header("Dashed Line Settings (bond lines + sugar links)")]
    [Tooltip("Length of each dash, in pair-pivot local units. Dashes are built from short separate LineRenderers.")]
    [Min(0.001f)] public float dashLength = 0.06f;
    [Tooltip("Gap between dashes, in pair-pivot local units.")]
    [Min(0f)] public float dashGap = 0.04f;
    [Tooltip("Safety cap on dashes per line (each dash is one LineRenderer).")]
    [Min(1)] public int maxDashesPerLine = 40;

    [Tooltip("Bond lines for A-T pairs (A on the left). Size = number of lines. " +
             "Coordinates are in PAIR-PIVOT local space (X runs left base -> right base).")]
    public List<BondLine> bondLinesAT = new List<BondLine>
    {
        new BondLine(new Vector3(-0.2f, 0.05f, 0f), new Vector3(0.2f, 0.05f, 0f)),
        new BondLine(new Vector3(-0.2f, -0.05f, 0f), new Vector3(0.2f, -0.05f, 0f)),
    };
    [Tooltip("Bond lines for T-A pairs (T on the left).")]
    public List<BondLine> bondLinesTA = new List<BondLine>
    {
        new BondLine(new Vector3(-0.2f, 0.05f, 0f), new Vector3(0.2f, 0.05f, 0f)),
        new BondLine(new Vector3(-0.2f, -0.05f, 0f), new Vector3(0.2f, -0.05f, 0f)),
    };
    [Tooltip("Bond lines for C-G pairs (C on the left).")]
    public List<BondLine> bondLinesCG = new List<BondLine>
    {
        new BondLine(new Vector3(-0.2f, 0.08f, 0f), new Vector3(0.2f, 0.08f, 0f)),
        new BondLine(new Vector3(-0.2f, 0f, 0f), new Vector3(0.2f, 0f, 0f)),
        new BondLine(new Vector3(-0.2f, -0.08f, 0f), new Vector3(0.2f, -0.08f, 0f)),
    };
    [Tooltip("Bond lines for G-C pairs (G on the left).")]
    public List<BondLine> bondLinesGC = new List<BondLine>
    {
        new BondLine(new Vector3(-0.2f, 0.08f, 0f), new Vector3(0.2f, 0.08f, 0f)),
        new BondLine(new Vector3(-0.2f, 0f, 0f), new Vector3(0.2f, 0f, 0f)),
        new BondLine(new Vector3(-0.2f, -0.08f, 0f), new Vector3(0.2f, -0.08f, 0f)),
    };

    // ------------------------------------------------------------------ Sugar links
    [Header("Sugar Link Lines (share Material / Alignment / Caps from Bond Lines)")]
    [Tooltip("Line from each SUGAR to the base half (A / T / C / G) it belongs to. " +
             "FROM = sugar, TO = base half.")]
    public LinkLine sugarToBaseLine = new LinkLine { snapToTargetSurface = true };
    [Tooltip("Line from each SUGAR to the PHOSPHATE next to it. FROM = sugar, TO = phosphate.")]
    public LinkLine sugarToPhosphateLine = new LinkLine { color = new Color(1f, 0.8f, 0.2f) };

    [Tooltip("Line from each PHOSPHATE to the SUGAR of the NEXT pair on the same side, forming the " +
             "continuous sugar-phosphate backbone chain. FROM = phosphate (this pair), TO = sugar (next pair). " +
             "The FROM pair's flip drives the phosphate offsets, the TO pair's flip drives the sugar offsets.")]
    public LinkLine phosphateToNextSugarLine = new LinkLine { color = new Color(0.3f, 0.85f, 1f) };

    // ------------------------------------------------------------------ Spawn / stacking
    [Header("Spawn")]
    [Tooltip("Local-space offset of the first step.")]
    public Vector3 spawnPosition = Vector3.zero;

    [Header("Base Rotation")]
    [Tooltip("Rotation applied to every spawned strand piece before the per-step offset.")]
    public Vector3 baseRotation = new Vector3(-90f, 0f, 0f);

    [Header("Count & Stacking")]
    [Min(1)] public int count = 1;
    [Tooltip("Rotation applied to each strand piece after the first (multiplied by index).")]
    public Vector3 rotationOffsetPerStep = new Vector3(0f, 36f, 0f);
    [Tooltip("Extra gap between stacked pieces.")]
    public float stackGap = 0f;
    [Tooltip("0 = measure the strand prefab automatically (taking Base Rotation into account). >0 = use this height.")]
    [Min(0f)] public float stepHeightOverride = 0f;

    // ------------------------------------------------------------------ Options
    [Header("Options")]
    public bool spawnOnStart = true;
    [Tooltip("Live-update in the editor when inspector values change.")]
    public bool liveUpdateInEditor = true;
    public bool drawGizmos = true;

    // ------------------------------------------------------------------ Runtime state
    private const string ContainerName = "__DNA_Generated";
    private const string PairPivotName = "__PairPivot";
    private const string Bases = "ATCG";

    private readonly List<GameObject> spawned = new();
    private Transform _container;
    private int _lastHash;

    // Cached per-PAIR base sequence. Only re-rolled when the pair layout changes.
    private char[] _pairSequence = System.Array.Empty<char>();
    private int _sequencePairCount = -1;
    private int _sequenceKeyCount = -1;
    private float _sequenceKeyPairsPerStep = -1f;
    private float _sequenceKeyHeightOffset = -1f;

    // Height cache
    private bool _heightValid;
    private int _heightKey;
    private float _cachedHeight = 1f;

    // Fallback bond line material (only created if none is assigned)
    private static Material _fallbackBondMaterial;

    private static readonly Color GizmoPairColor = new Color(1f, 0.2f, 1f); // magenta preview

    /// <summary>Info about one spawned base half, used to build cross-pair lines
    /// (phosphate of this pair -> sugar of the next pair).</summary>
    private struct HalfRuntimeInfo
    {
        public bool hasSugar;
        public bool hasPhos;
        public bool flipped;
        public bool isLeft;
        public Transform pivot;
        public Vector3 sugarPos;
        public Quaternion sugarRot;
        public Vector3 phosPos;
        public Quaternion phosRot;
    }

    /// <summary>All spawned objects (strand, pair pivots, base halves, elements, sugars, phosphates, bond lines).</summary>
    public IReadOnlyList<GameObject> Spawned => spawned;

    // =================================================================== Unity callbacks
    private void Start()
    {
        if (spawnOnStart) Spawn();
    }

    private void OnValidate()
    {
        count = Mathf.Max(1, count);
        pairsPerStep = Mathf.Max(0.01f, pairsPerStep);
        stepHeightOverride = Mathf.Max(0f, stepHeightOverride);
        elementDistance = Mathf.Max(0f, elementDistance);
        elementDistanceAT = Mathf.Max(0f, elementDistanceAT);
        elementDistanceTA = Mathf.Max(0f, elementDistanceTA);
        elementDistanceCG = Mathf.Max(0f, elementDistanceCG);
        elementDistanceGC = Mathf.Max(0f, elementDistanceGC);
        bondLineWidth = Mathf.Max(0f, bondLineWidth);
        bondLineCapVertices = Mathf.Max(0, bondLineCapVertices);
        dashLength = Mathf.Max(0.001f, dashLength);
        dashGap = Mathf.Max(0f, dashGap);
        maxDashesPerLine = Mathf.Max(1, maxDashesPerLine);

#if UNITY_EDITOR
        if (!liveUpdateInEditor || Application.isPlaying) return;
        if (!gameObject.scene.IsValid()) return;
        if (PrefabUtility.IsPartOfPrefabAsset(this)) return;
        if (_updateQueued) return;

        _updateQueued = true;
        EditorApplication.delayCall += () =>
        {
            _updateQueued = false;
            if (this == null || Application.isPlaying) return;
            if (ComputeSettingsHash() != _lastHash) Spawn();
        };
#endif
    }

#if UNITY_EDITOR
    private bool _updateQueued;
#endif

    // =================================================================== Spawning
    [ContextMenu("Spawn")]
    public void Spawn()
    {
        ClearSpawned();
        _heightValid = false;
        _lastHash = ComputeSettingsHash();

        if (prefab == null)
        {
            Debug.LogWarning("DNASimulator: no strand prefab assigned.", this);
            return;
        }

        float stepHeight = GetStepHeight();
        float step = stepHeight + stackGap;

        _container = new GameObject(ContainerName).transform;
        _container.SetParent(transform, false);
        MarkTemporary(_container.gameObject);

        // --- Strand ---
        Quaternion baseRot = Quaternion.Euler(baseRotation);
        for (int i = 0; i < count; i++)
        {
            Vector3 pos = spawnPosition + Vector3.up * (i * step);
            Quaternion rot = Quaternion.Euler(rotationOffsetPerStep * i) * baseRot;
            Place(prefab, pos, rot, $"{prefab.name}_{i}");
        }

        // --- Base pairs (+ interior elements + sugar + phosphate + bond lines) ---
        if (spawnBasePairs)
            SpawnBasePairs(step);
    }

    /// <summary>Re-rolls the base sequence and respawns. Use this when you want new random bases
    /// without changing any other setting.</summary>
    [ContextMenu("Respawn (new sequence)")]
    public void RespawnWithNewSequence()
    {
        _sequenceKeyCount = -1;
        Spawn();
    }

    private int TotalPairs => Mathf.Max(1, Mathf.RoundToInt(count * pairsPerStep));

    /// <summary>Number of pairs the sequence needs to cover, including any extra slots caused
    /// by a fractional height offset shift. Cached key for the sequence.</summary>
    private int SequencePairCount
    {
        get
        {
            int total = TotalPairs;
            int offsetPairs = Mathf.CeilToInt(Mathf.Abs(pairHeightOffset) * pairsPerStep);
            return Mathf.Max(1, total + offsetPairs + 1);
        }
    }

    /// <summary>Returns the cached base letter for a given PAIR index. Each pair gets its own
    /// random base; the sequence only re-rolls when count / pairsPerStep / pairHeightOffset change.</summary>
    private char GetPairLetter(int pairIndex)
    {
        int pairCount = SequencePairCount;

        bool needsReroll =
            _pairSequence.Length < pairCount ||
            _sequencePairCount != pairCount ||
            _sequenceKeyCount != count ||
            !Mathf.Approximately(_sequenceKeyPairsPerStep, pairsPerStep) ||
            !Mathf.Approximately(_sequenceKeyHeightOffset, pairHeightOffset);

        if (needsReroll)
        {
            _pairSequence = new char[pairCount];
            for (int i = 0; i < pairCount; i++)
                _pairSequence[i] = Bases[Random.Range(0, Bases.Length)];

            _sequencePairCount = pairCount;
            _sequenceKeyCount = count;
            _sequenceKeyPairsPerStep = pairsPerStep;
            _sequenceKeyHeightOffset = pairHeightOffset;
        }

        // Wrap so negative / out-of-range pair indices still resolve.
        int idx = ((pairIndex % pairCount) + pairCount) % pairCount;
        return _pairSequence[idx];
    }

    /// <summary>Rotation of the pair pivot for pair index p at fractional step position t.</summary>
    private Quaternion GetPairPivotRotation(int p, float t)
    {
        Quaternion twist = followStrandTwist
            ? Quaternion.Euler(rotationOffsetPerStep * t)
            : Quaternion.identity;

        return twist *
               Quaternion.Euler(rotationPerPairStep * p) *
               Quaternion.Euler(basePairRotation);
    }

    /// <summary>True when this pair is a "flipped" one (odd index) and the flip option is on.</summary>
    private bool IsFlippedPair(int pairIndex)
        => alternateSugarPhosphateFlip && (pairIndex & 1) == 1;

    /// <summary>Element Distance to use, taking the per-pattern override into account.</summary>
    private float GetElementDistance(char leftLetter)
    {
        float over = leftLetter switch
        {
            'A' => elementDistanceAT,   // A on left -> A-T pattern
            'T' => elementDistanceTA,   // T on left -> T-A pattern
            'C' => elementDistanceCG,   // C on left -> C-G pattern
            'G' => elementDistanceGC,   // G on left -> G-C pattern
            _ => 0f,
        };
        return over > 0f ? over : elementDistance;
    }

    /// <summary>Per-pattern extra LOCAL rotation for the interior element on the LEFT half.</summary>
    private Vector3 GetElementRotationLeft(char leftLetter) => leftLetter switch
    {
        'A' => elementRotationAT_L,
        'T' => elementRotationTA_L,
        'C' => elementRotationCG_L,
        'G' => elementRotationGC_L,
        _ => Vector3.zero,
    };

    /// <summary>Per-pattern extra LOCAL rotation for the interior element on the RIGHT half.</summary>
    private Vector3 GetElementRotationRight(char leftLetter) => leftLetter switch
    {
        'A' => elementRotationAT_R,
        'T' => elementRotationTA_R,
        'C' => elementRotationCG_R,
        'G' => elementRotationGC_R,
        _ => Vector3.zero,
    };

    /// <summary>Bond line list for the pattern identified by the LEFT letter.</summary>
    private List<BondLine> GetBondLines(char leftLetter) => leftLetter switch
    {
        'A' => bondLinesAT,
        'T' => bondLinesTA,
        'C' => bondLinesCG,
        'G' => bondLinesGC,
        _ => null,
    };

    private void SpawnBasePairs(float step)
    {
        int total = TotalPairs;
        int warnedMask = 0;

        // Track the previous pair's half info so we can draw phosphate -> next sugar lines.
        HalfRuntimeInfo prevL = default, prevR = default;
        bool havePrev = false;

        for (int p = 0; p < total; p++)
        {
            float t = p / pairsPerStep + pairHeightOffset;

            // Each pair gets its own random base, cached across respawns so changing
            // unrelated settings does not reshuffle the sequence.
            char leftLetter = GetPairLetter(p);
            char rightLetter = Complement(leftLetter);

            GameObject leftPrefab = GetBasePrefab(leftLetter);
            GameObject rightPrefab = GetBasePrefab(rightLetter);

            if (leftPrefab == null || rightPrefab == null)
            {
                char missing = leftPrefab == null ? leftLetter : rightLetter;
                int bit = 1 << Bases.IndexOf(missing);
                if ((warnedMask & bit) == 0)
                {
                    warnedMask |= bit;
                    Debug.LogWarning($"DNASimulator: prefab for base '{missing}' is missing - those pairs are skipped.", this);
                }
                continue;
            }

            Vector3 center = spawnPosition + Vector3.up * (t * step);
            Quaternion pairPivotRot = GetPairPivotRotation(p, t);

            float elemDist = GetElementDistance(leftLetter);

            Quaternion leftBaseRot = Quaternion.Euler(leftPairRotation);
            Quaternion rightBaseRot = Quaternion.Euler(rightPairRotation);

            Quaternion leftElemRot = Quaternion.Euler(elementLocalRotation) *
                                     Quaternion.Euler(GetElementRotationLeft(leftLetter));
            Quaternion rightElemRot = Quaternion.Euler(elementLocalRotation) *
                                      Quaternion.Euler(GetElementRotationRight(leftLetter));

            bool flipped = IsFlippedPair(p);

            var pivotGo = new GameObject($"{PairPivotName}_{p}");
            pivotGo.transform.SetParent(_container, false);
            pivotGo.transform.SetLocalPositionAndRotation(center, pairPivotRot);
            MarkTemporary(pivotGo);
            spawned.Add(pivotGo);

            HalfRuntimeInfo infoL = PlaceChild(leftPrefab, pivotGo.transform, Vector3.left * pairHalfLength, -1f, elemDist,
                                               leftBaseRot, leftElemRot, flipped,
                                               $"{leftPrefab.name}_Pair_{p}_L", leftLetter);

            HalfRuntimeInfo infoR = PlaceChild(rightPrefab, pivotGo.transform, Vector3.right * pairHalfLength, 1f, elemDist,
                                               rightBaseRot, rightElemRot, flipped,
                                               $"{rightPrefab.name}_Pair_{p}_R", rightLetter);

            // Bond lines belong to the pair pivot, so they twist / move with the DNA.
            if (spawnBondLines)
                SpawnBondLinesForPair(pivotGo.transform, leftLetter, rightLetter, p);

            // Phosphate (previous pair) -> Sugar (this pair), per side, forming the backbone chain.
            if (havePrev && phosphateToNextSugarLine != null && phosphateToNextSugarLine.enabled)
            {
                if (prevL.hasPhos && infoL.hasSugar)
                    CreateCrossPairLine(prevL, infoL, phosphateToNextSugarLine, "L");

                if (prevR.hasPhos && infoR.hasSugar)
                    CreateCrossPairLine(prevR, infoR, phosphateToNextSugarLine, "R");
            }

            prevL = infoL;
            prevR = infoR;
            havePrev = true;
        }
    }

    /// <summary>Creates one LineRenderer object per entry in the list for this pair's pattern.
    /// Coordinates are pair-pivot local (useWorldSpace = false, identity local transform).</summary>
    private void SpawnBondLinesForPair(Transform pivot, char leftLetter, char rightLetter, int pairIndex)
    {
        List<BondLine> lines = GetBondLines(leftLetter);
        if (lines == null || lines.Count == 0) return;

        Material mat = GetBondMaterial();

        for (int i = 0; i < lines.Count; i++)
        {
            BondLine bl = lines[i];
            if (bl == null) continue;

            CreateLine(pivot, $"__Bond_{leftLetter}{rightLetter}_Pair_{pairIndex}_{i}",
                       bl.startA, bl.endB, bl.dashed, bondLineColor, bondLineWidth, mat);
        }
    }

    /// <summary>Creates a line from the phosphate of one pair to the sugar of the next pair on the
    /// same side. The FROM info must carry a phosphate; the TO info must carry a sugar. The line is
    /// parented to the container so it simply connects two fixed points regardless of the two
    /// pair pivots' individual rotations.</summary>
    private void CreateCrossPairLine(HalfRuntimeInfo fromInfo, HalfRuntimeInfo toInfo,
                                     LinkLine line, string sideLabel)
    {
        // Offsets are authored in each piece's own local space.
        Vector3 fromOffset = line.GetFrom(fromInfo.isLeft, fromInfo.flipped);
        Vector3 toOffset   = line.GetTo  (toInfo.isLeft,   toInfo.flipped);

        // Pivot-local poses with the offsets applied (scale, then rotate, then translate).
        Vector3 aInPivot = fromInfo.phosPos + fromInfo.phosRot *
                           Vector3.Scale(phosphateLocalScale, fromOffset);
        Vector3 bInPivot = toInfo.sugarPos + toInfo.sugarRot *
                           Vector3.Scale(sugarLocalScale, toOffset);

        // Both pivots are direct children of _container, so pivot-local -> container-local is just
        // the pivot's own local TRS applied to the point.
        Vector3 a = fromInfo.pivot.localPosition + fromInfo.pivot.localRotation * aInPivot;
        Vector3 b = toInfo.pivot.localPosition   + toInfo.pivot.localRotation   * bInPivot;

        CreateLine(_container, $"__PhosToNextSugar_{sideLabel}_{fromInfo.pivot.name}", a, b,
                   line.dashed, line.color, line.width, GetBondMaterial());
    }

    /// <summary>Creates a solid line (one LineRenderer) or a dashed line (a holder object with one
    /// short LineRenderer per dash). a / b are in the parent's local space.</summary>
    private void CreateLine(Transform parent, string objName, Vector3 a, Vector3 b,
                            bool dashed, Color color, float width, Material mat)
    {
        var holder = new GameObject(objName);
        holder.transform.SetParent(parent, false);
        holder.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        holder.transform.localScale = Vector3.one;
        MarkTemporary(holder);
        spawned.Add(holder);

        if (!dashed)
        {
            ConfigureLine(holder.AddComponent<LineRenderer>(), a, b, color, width, mat);
            return;
        }

        Vector3 delta = b - a;
        float len = delta.magnitude;
        if (len < 1e-5f) return;

        Vector3 dir = delta / len;
        float dash = Mathf.Max(0.001f, dashLength);
        float period = dash + Mathf.Max(0f, dashGap);
        int n = 0;

        for (float s0 = 0f; s0 < len && n < maxDashesPerLine; s0 += period, n++)
        {
            float e0 = Mathf.Min(s0 + dash, len);

            var d = new GameObject($"Dash_{n}");
            d.transform.SetParent(holder.transform, false);
            d.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            d.transform.localScale = Vector3.one;
            MarkTemporary(d);
            spawned.Add(d);

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
        lr.alignment = bondLineAlignment;
        lr.numCapVertices = bondLineCapVertices;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.sharedMaterial = mat;
    }

    private Material GetBondMaterial()
    {
        if (bondLineMaterial != null) return bondLineMaterial;

        if (_fallbackBondMaterial == null)
        {
            Shader sh = Shader.Find("Sprites/Default");
            if (sh == null)
            {
                Debug.LogWarning("DNASimulator: no bond line material assigned and Sprites/Default was not found.", this);
                return null;
            }
            _fallbackBondMaterial = new Material(sh) { name = "DNA_BondLine_Fallback", hideFlags = HideFlags.DontSave };
        }
        return _fallbackBondMaterial;
    }

    private void Place(GameObject source, Vector3 localPos, Quaternion localRot, string objName)
    {
        GameObject go = Instantiate(source, _container);
        go.name = objName;
        go.transform.SetLocalPositionAndRotation(localPos, localRot);
        MarkTemporary(go);
        spawned.Add(go);
    }

    /// <summary>Spawns a base half and, if enabled, its matching interior element as a child of
    /// that half, plus sugar and phosphate prefabs beside that half.
    ///
    /// The interior element is a CHILD of the base half (so it inherits the base's transform).
    /// Sugar and phosphate are SIBLINGS of the base half - parented to the PAIR PIVOT instead -
    /// so they do NOT inherit the base half's scale, and their own scale is applied directly.
    /// Their offset is still authored relative to the base half (position / rotation are
    /// composed with the base half's local transform).
    ///
    /// Returns the poses of the spawned sugar / phosphate so cross-pair link lines can be built.</summary>
    private HalfRuntimeInfo PlaceChild(GameObject source, Transform parent, Vector3 localPos, float side,
                            float elementDist, Quaternion baseLocalRot, Quaternion elementLocalRot,
                            bool flipped,
                            string objName, char letter)
    {
        GameObject go = Instantiate(source, parent);
        go.name = objName;
        go.transform.localPosition = localPos;
        go.transform.localRotation = baseLocalRot;
        go.transform.localScale = source.transform.localScale;
        MarkTemporary(go);
        spawned.Add(go);

        // Measure the base half's own mesh bounds (in its local space) BEFORE the interior element
        // is added as a child, so the sugar -> base line snaps to the base block only.
        bool haveBaseBounds = TryGetLocalMeshBounds(go.transform, out Bounds baseBounds);

        // --- Interior element (child of the base half, inherits its scale) ---
        if (spawnElements)
        {
            GameObject elementPrefab = GetElementPrefab(letter);
            if (elementPrefab != null)
            {
                GameObject element = Instantiate(elementPrefab, go.transform);
                element.name = $"{elementPrefab.name}_Element_{letter}";

                // Target the element's PIVOT-space position: +/-Element Distance along the pivot's X axis.
                Vector3 desiredInPivotSpace = Vector3.right * (side * elementDist);

                // elementWorldInPivot = localPos + baseLocalRot * (goScale .* elementLocalPos)
                // Solve for elementLocalPos so the result equals desiredInPivotSpace.
                Vector3 deltaInPivotSpace = desiredInPivotSpace - localPos;
                Vector3 deltaInGoLocalSpace = Quaternion.Inverse(baseLocalRot) * deltaInPivotSpace;

                // Compensate for the base half's local scale (non-uniform scale safe).
                Vector3 goScale = go.transform.localScale;
                if (goScale.x != 0f) deltaInGoLocalSpace.x /= goScale.x;
                if (goScale.y != 0f) deltaInGoLocalSpace.y /= goScale.y;
                if (goScale.z != 0f) deltaInGoLocalSpace.z /= goScale.z;

                element.transform.localPosition = deltaInGoLocalSpace + elementLocalPosition;
                element.transform.localRotation = elementLocalRot;
                element.transform.localScale = elementLocalScale;
                MarkTemporary(element);
                spawned.Add(element);
            }
        }

        bool isLeft = side < 0f;

        // Poses (pair-pivot space) captured for the sugar link lines.
        bool haveSugar = false, havePhos = false;
        Vector3 sugarPos = Vector3.zero, phosPos = Vector3.zero;
        Quaternion sugarRot = Quaternion.identity, phosRot = Quaternion.identity;

        // --- Sugar (sibling of the base half, parented to the pivot - scale independent) ---
        if (spawnSugar && prefabSugar != null)
        {
            GameObject sugar = Instantiate(prefabSugar, parent);
            sugar.name = $"{prefabSugar.name}_Sugar_{(isLeft ? "L" : "R")}_{letter}";

            Vector3 sugarOffset = flipped
                ? (isLeft ? sugarLocalPositionLeftFlipped : sugarLocalPositionRightFlipped)
                : (isLeft ? sugarLocalPositionLeft : sugarLocalPositionRight);

            Vector3 sugarRotEuler = flipped
                ? (isLeft ? sugarLocalRotationLeftFlipped : sugarLocalRotationRightFlipped)
                : (isLeft ? sugarLocalRotationLeft : sugarLocalRotationRight);

            Quaternion sugarRotOffset = Quaternion.Euler(sugarRotEuler);

            sugar.transform.localPosition = localPos + baseLocalRot * sugarOffset;
            sugar.transform.localRotation = baseLocalRot * sugarRotOffset;
            sugar.transform.localScale = sugarLocalScale;
            sugarPos = sugar.transform.localPosition;
            sugarRot = sugar.transform.localRotation;
            haveSugar = true;

            MarkTemporary(sugar);
            spawned.Add(sugar);
        }

        // --- Phosphate (sibling of the base half, parented to the pivot - scale independent) ---
        if (spawnPhosphate && prefabPhosphate != null)
        {
            GameObject phosphate = Instantiate(prefabPhosphate, parent);
            phosphate.name = $"{prefabPhosphate.name}_Phosphate_{(isLeft ? "L" : "R")}_{letter}";

            Vector3 phosOffset = flipped
                ? (isLeft ? phosphateLocalPositionLeftFlipped : phosphateLocalPositionRightFlipped)
                : (isLeft ? phosphateLocalPositionLeft : phosphateLocalPositionRight);

            Vector3 phosRotEuler = flipped
                ? (isLeft ? phosphateLocalRotationLeftFlipped : phosphateLocalRotationRightFlipped)
                : (isLeft ? phosphateLocalRotationLeft : phosphateLocalRotationRight);

            Quaternion phosRotOffset = Quaternion.Euler(phosRotEuler);

            Vector3 phosPose = localPos + baseLocalRot * phosOffset;
            Quaternion phosRotPose = baseLocalRot * phosRotOffset;

            // Phosphate rides along with its sugar: apply the sugar's flip (normal pose -> flipped pose)
            // as a rigid transform to the phosphate's NORMAL pose.
            if (flipped && phosphateFollowsSugarFlip && haveSugar)
            {
                Vector3 sugarNormalPos = localPos + baseLocalRot *
                    (isLeft ? sugarLocalPositionLeft : sugarLocalPositionRight);
                Quaternion sugarNormalRot = baseLocalRot * Quaternion.Euler(
                    isLeft ? sugarLocalRotationLeft : sugarLocalRotationRight);
                Vector3 phosNormalPos = localPos + baseLocalRot *
                    (isLeft ? phosphateLocalPositionLeft : phosphateLocalPositionRight);
                Quaternion phosNormalRot = baseLocalRot * Quaternion.Euler(
                    isLeft ? phosphateLocalRotationLeft : phosphateLocalRotationRight);

                // sugarPos / sugarRot are the sugar's FLIPPED pose (captured above).
                Quaternion flipDelta = sugarRot * Quaternion.Inverse(sugarNormalRot);
                phosPose = sugarPos + flipDelta * (phosNormalPos - sugarNormalPos);
                phosRotPose = flipDelta * phosNormalRot;
            }

            phosphate.transform.localPosition = phosPose;
            phosphate.transform.localRotation = phosRotPose;
            phosphate.transform.localScale = phosphateLocalScale;
            phosPos = phosphate.transform.localPosition;
            phosRot = phosphate.transform.localRotation;
            havePhos = true;

            MarkTemporary(phosphate);
            spawned.Add(phosphate);
        }

        // --- Sugar link lines (children of the pair pivot, so they twist with the DNA) ---
        if (haveSugar)
        {
            Material mat = GetBondMaterial();
            Vector3 baseScale = go.transform.localScale;

            if (sugarToBaseLine != null && sugarToBaseLine.enabled)
            {
                LinkLine l = sugarToBaseLine;
                Vector3 a = sugarPos + sugarRot * Vector3.Scale(sugarLocalScale, l.GetFrom(isLeft, flipped));
                Vector3 toOffset = l.GetTo(isLeft, flipped);
                Vector3 b;

                if (l.snapToTargetSurface && haveBaseBounds)
                {
                    // Sugar end point -> base half's local (unscaled) space.
                    Vector3 aInBase = Quaternion.Inverse(baseLocalRot) * (a - localPos);
                    aInBase = DivideSafe(aInBase, baseScale);

                    Vector3 surface = ClosestPointOnBoundsSurface(baseBounds, aInBase);
                    b = localPos + baseLocalRot * Vector3.Scale(baseScale, surface + toOffset);
                }
                else
                {
                    b = localPos + baseLocalRot * Vector3.Scale(baseScale, toOffset);
                }

                CreateLine(parent, $"__SugarToBase_{(isLeft ? "L" : "R")}_{letter}", a, b,
                           l.dashed, l.color, l.width, mat);
            }

            if (havePhos && sugarToPhosphateLine != null && sugarToPhosphateLine.enabled)
            {
                LinkLine l = sugarToPhosphateLine;
                Vector3 a = sugarPos + sugarRot * Vector3.Scale(sugarLocalScale, l.GetFrom(isLeft, flipped));
                Vector3 b = phosPos + phosRot * Vector3.Scale(phosphateLocalScale, l.GetTo(isLeft, flipped));
                CreateLine(parent, $"__SugarToPhosphate_{(isLeft ? "L" : "R")}_{letter}", a, b,
                           l.dashed, l.color, l.width, mat);
            }
        }

        return new HalfRuntimeInfo
        {
            hasSugar = haveSugar,
            hasPhos  = havePhos,
            flipped  = flipped,
            isLeft   = isLeft,
            pivot    = parent,
            sugarPos = sugarPos,
            sugarRot = sugarRot,
            phosPos  = phosPos,
            phosRot  = phosRot,
        };
    }

    [ContextMenu("Clear Spawned")]
    public void ClearSpawned()
    {
        spawned.Clear();
        _container = null;

        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Transform child = transform.GetChild(i);
            if (child.name != ContainerName) continue;

            if (Application.isPlaying)
            {
                child.SetParent(null);
                Destroy(child.gameObject);
            }
            else
            {
                DestroyImmediate(child.gameObject);
            }
        }
    }

    private static void MarkTemporary(GameObject go)
    {
        if (!Application.isPlaying) go.hideFlags = HideFlags.DontSave;
    }

    // =================================================================== Helpers
    private static char Complement(char b) => b switch
    {
        'A' => 'T',
        'T' => 'A',
        'C' => 'G',
        _ => 'C',
    };

    private GameObject GetBasePrefab(char b) => b switch
    {
        'A' => prefabA,
        'T' => prefabT,
        'C' => prefabC,
        _ => prefabG,
    };

    private GameObject GetElementPrefab(char b) => b switch
    {
        'A' => elementA,
        'T' => elementT,
        'C' => elementC,
        _ => elementG,
    };

    // =================================================================== Surface snapping
    /// <summary>Bounds of all meshes under 'root', expressed in root's LOCAL (unscaled-by-root) space.</summary>
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

    /// <summary>Closest point on the SURFACE of the box to p. If p is inside the box it is pushed
    /// out to the nearest face, so the line never ends inside the block.</summary>
    private static Vector3 ClosestPointOnBoundsSurface(Bounds b, Vector3 p)
    {
        Vector3 min = b.min, max = b.max;
        Vector3 c = new Vector3(
            Mathf.Clamp(p.x, min.x, max.x),
            Mathf.Clamp(p.y, min.y, max.y),
            Mathf.Clamp(p.z, min.z, max.z));

        if (c != p) return c; // p was outside: the clamped point is on the surface

        // p is inside: move to the nearest face.
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

    private static Vector3 DivideSafe(Vector3 v, Vector3 s)
    {
        return new Vector3(
            s.x != 0f ? v.x / s.x : v.x,
            s.y != 0f ? v.y / s.y : v.y,
            s.z != 0f ? v.z / s.z : v.z);
    }

    // =================================================================== Height measuring
    private float GetStepHeight()
    {
        if (stepHeightOverride > 0f) return stepHeightOverride;
        if (prefab == null) return 1f;

        int key = Mix(Mix(17, prefab), baseRotation);
        if (_heightValid && key == _heightKey) return _cachedHeight;

        _heightKey = key;
        _heightValid = true;
        _cachedHeight = MeasureHeight(prefab, Quaternion.Euler(baseRotation));
        return _cachedHeight;
    }

    private static float MeasureHeight(GameObject source, Quaternion rot)
    {
        var filters = source.GetComponentsInChildren<MeshFilter>();
        Transform root = source.transform;
        Matrix4x4 toRoot = Matrix4x4.Scale(root.localScale) * root.worldToLocalMatrix;

        bool found = false;
        float minY = float.MaxValue, maxY = float.MinValue;

        for (int f = 0; f < filters.Length; f++)
        {
            Mesh mesh = filters[f].sharedMesh;
            if (mesh == null) continue;

            Matrix4x4 m = toRoot * filters[f].transform.localToWorldMatrix;
            Vector3 c = mesh.bounds.center;
            Vector3 e = mesh.bounds.extents;

            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 local = c + new Vector3(
                    (corner & 1) == 0 ? -e.x : e.x,
                    (corner & 2) == 0 ? -e.y : e.y,
                    (corner & 4) == 0 ? -e.z : e.z);

                float y = (rot * m.MultiplyPoint3x4(local)).y;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
                found = true;
            }
        }

        return found ? Mathf.Max(0.0001f, maxY - minY) : 1f;
    }

    // =================================================================== Change detection
    private int ComputeSettingsHash()
    {
        int h = 17;
        h = Mix(h, prefab); h = Mix(h, prefabA); h = Mix(h, prefabT); h = Mix(h, prefabC); h = Mix(h, prefabG);
        h = Mix(h, elementA); h = Mix(h, elementT); h = Mix(h, elementC); h = Mix(h, elementG);
        h = Mix(h, spawnElements); h = Mix(h, elementDistance);
        h = Mix(h, elementLocalPosition); h = Mix(h, elementLocalRotation); h = Mix(h, elementLocalScale);
        h = Mix(h, prefabSugar); h = Mix(h, spawnSugar);
        h = Mix(h, sugarLocalPositionLeft); h = Mix(h, sugarLocalPositionRight);
        h = Mix(h, sugarLocalRotationLeft); h = Mix(h, sugarLocalRotationRight);
        h = Mix(h, sugarLocalScale);
        h = Mix(h, prefabPhosphate); h = Mix(h, spawnPhosphate);
        h = Mix(h, phosphateLocalPositionLeft); h = Mix(h, phosphateLocalPositionRight);
        h = Mix(h, phosphateLocalRotationLeft); h = Mix(h, phosphateLocalRotationRight);
        h = Mix(h, phosphateLocalScale);
        h = Mix(h, alternateSugarPhosphateFlip); h = Mix(h, phosphateFollowsSugarFlip);
        h = Mix(h, sugarLocalPositionLeftFlipped); h = Mix(h, sugarLocalPositionRightFlipped);
        h = Mix(h, sugarLocalRotationLeftFlipped); h = Mix(h, sugarLocalRotationRightFlipped);
        h = Mix(h, phosphateLocalPositionLeftFlipped); h = Mix(h, phosphateLocalPositionRightFlipped);
        h = Mix(h, phosphateLocalRotationLeftFlipped); h = Mix(h, phosphateLocalRotationRightFlipped);
        h = Mix(h, spawnBasePairs); h = Mix(h, pairsPerStep);
        h = Mix(h, pairHalfLength); h = Mix(h, followStrandTwist);
        h = Mix(h, basePairRotation); h = Mix(h, rotationPerPairStep);
        h = Mix(h, leftPairRotation); h = Mix(h, rightPairRotation);
        h = Mix(h, pairHeightOffset);
        h = Mix(h, elementRotationAT_L); h = Mix(h, elementRotationAT_R);
        h = Mix(h, elementRotationTA_L); h = Mix(h, elementRotationTA_R);
        h = Mix(h, elementRotationCG_L); h = Mix(h, elementRotationCG_R);
        h = Mix(h, elementRotationGC_L); h = Mix(h, elementRotationGC_R);
        h = Mix(h, elementDistanceAT); h = Mix(h, elementDistanceTA);
        h = Mix(h, elementDistanceCG); h = Mix(h, elementDistanceGC);

        // Bond lines
        h = Mix(h, spawnBondLines); h = Mix(h, bondLineMaterial);
        h = Mix(h, bondLineColor); h = Mix(h, bondLineWidth);
        h = Mix(h, (int)bondLineAlignment); h = Mix(h, bondLineCapVertices);
        h = Mix(h, bondLinesAT); h = Mix(h, bondLinesTA);
        h = Mix(h, bondLinesCG); h = Mix(h, bondLinesGC);
        h = Mix(h, dashLength); h = Mix(h, dashGap); h = Mix(h, maxDashesPerLine);
        h = Mix(h, sugarToBaseLine); h = Mix(h, sugarToPhosphateLine);
        h = Mix(h, phosphateToNextSugarLine);

        h = Mix(h, spawnPosition); h = Mix(h, baseRotation);
        h = Mix(h, count); h = Mix(h, rotationOffsetPerStep); h = Mix(h, stackGap); h = Mix(h, stepHeightOverride);
        return h;
    }

    private static int Mix(int h, int v) { unchecked { return h * 31 + v; } }
    private static int Mix(int h, float v) => Mix(h, v.GetHashCode());
    private static int Mix(int h, bool v) => Mix(h, v ? 1 : 0);
    private static int Mix(int h, Vector3 v) => Mix(h, v.GetHashCode());
    private static int Mix(int h, Color c) => Mix(h, c.GetHashCode());
    private static int Mix(int h, string s) => Mix(h, s != null ? s.GetHashCode() : 0);
    private static int Mix(int h, Object o) => Mix(h, o != null ? o.GetInstanceID() : 0);
    private static int Mix(int h, LinkLine l)
    {
        if (l == null) return Mix(h, -1);
        h = Mix(h, l.enabled); h = Mix(h, l.dashed); h = Mix(h, l.color); h = Mix(h, l.width);
        h = Mix(h, l.snapToTargetSurface);
        h = Mix(h, l.fromOffsetLeft); h = Mix(h, l.toOffsetLeft);
        h = Mix(h, l.fromOffsetRight); h = Mix(h, l.toOffsetRight);
        h = Mix(h, l.useFlippedOffsets);
        h = Mix(h, l.fromOffsetLeftFlipped); h = Mix(h, l.toOffsetLeftFlipped);
        h = Mix(h, l.fromOffsetRightFlipped); h = Mix(h, l.toOffsetRightFlipped);
        return h;
    }
    private static int Mix(int h, List<BondLine> list)
    {
        if (list == null) return Mix(h, -1);
        h = Mix(h, list.Count);
        for (int i = 0; i < list.Count; i++)
        {
            BondLine bl = list[i];
            if (bl == null) { h = Mix(h, 0); continue; }
            h = Mix(h, bl.startA);
            h = Mix(h, bl.endB);
            h = Mix(h, bl.dashed);
        }
        return h;
    }

    // =================================================================== Gizmos
    private void OnDrawGizmos()
    {
        if (!drawGizmos) return;

        float stepHeight = GetStepHeight();
        float step = stepHeight + stackGap;
        int steps = Mathf.Max(1, count);

        // Strand (green -> cyan)
        for (int i = 0; i < steps; i++)
        {
            Vector3 start = transform.TransformPoint(spawnPosition + Vector3.up * (i * step));
            Vector3 end = transform.TransformPoint(spawnPosition + Vector3.up * (i * step + stepHeight));

            Gizmos.color = Color.Lerp(Color.green, Color.cyan, steps <= 1 ? 0f : (float)i / (steps - 1));
            Gizmos.DrawSphere(start, 0.15f);
            Gizmos.DrawLine(start, end);
            Gizmos.DrawSphere(end, 0.1f);
        }

        if (!spawnBasePairs) return;

        // Base pair preview (baseline rotation / distance - the real sequence is random).
        int total = TotalPairs;
        for (int p = 0; p < total; p++)
        {
            float t = p / pairsPerStep + pairHeightOffset;

            Quaternion pairPivotRot = GetPairPivotRotation(p, t);

            Vector3 center = spawnPosition + Vector3.up * (t * step);
            Vector3 leftOffset = pairPivotRot * (Vector3.left * pairHalfLength);
            Vector3 rightOffset = pairPivotRot * (Vector3.right * pairHalfLength);

            Vector3 leftPos = transform.TransformPoint(center + leftOffset);
            Vector3 rightPos = transform.TransformPoint(center + rightOffset);

            Gizmos.color = GizmoPairColor;
            Gizmos.DrawSphere(leftPos, 0.12f);
            Gizmos.DrawSphere(rightPos, 0.12f);
            Gizmos.DrawLine(leftPos, rightPos);
        }
    }
}