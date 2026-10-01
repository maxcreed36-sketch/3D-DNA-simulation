using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Spawns a stacked, twisting DNA strand from prefabs, with optional base pairs (A-T, C-G)
/// and optional interior nucleotide elements (A, T, C, G) placed inside each base half.
/// Each of the four pair orientations (A-T, T-A, C-G, G-C) can have its own interior-element
/// rotation and interior-elemnt distance override, so they can be styled independently even
/// when they share the same bases. Every pair gets its own random base, and the sequence is
/// cached so it only re-rolls when count / pairsPerStep / pairHeightOffset change. Everything
/// is built in this object's local space, so you can move / rotate / scale freely.
///
/// GAME MODE (new): call GameMode() (Context Menu or from code, in Play Mode) to start a
/// base-pairing mini-game on the generated DNA. See the "=== GAME MODE ===" inspector section.
/// </summary>
[DisallowMultipleComponent]
public class DNASimulator2 : MonoBehaviour
{
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

    // =================================================================== GAME MODE (inspector)
    // ------------------------------------------------------------------ General
    [Header("=== GAME MODE ===")]
    [Tooltip("Master switch. If off, GameMode() does nothing.")]
    public bool enableGameMode = true;
    [Tooltip("If true, Game Mode starts automatically in Start() (this replaces the normal Spawn on start).")]
    public bool startGameModeOnStart = false;
    [Tooltip("0 = a different set of missing bases every time. Any other number = the same missing bases every time " +
             "(for the same sequence).")]
    public int gameSeed = 0;

    // ------------------------------------------------------------------ Camera
    [Header("Game Mode - Camera")]
    [Tooltip("Camera used for gameplay. If empty, Camera.main is used.")]
    public Camera gameCamera;
    [Min(0f)] public float cameraMoveDuration = 1f;
    [Tooltip("Camera position relative to the DNA center, in this object's LOCAL space. " +
             "The default (0,0,-10) views the DNA from the front (looking along +Z).")]
    public Vector3 gameCameraOffset = new Vector3(0f, 0f, -10f);

    // ------------------------------------------------------------------ Feedback
    [Header("Game Mode - Feedback")]
    public Color correctFlashColor = Color.green;
    public Color wrongFlashColor = Color.red;
    [Min(0f)] public float correctFlashDuration = 0.5f;
    [Min(0f)] public float wrongFlashDuration = 0.5f;
    [Tooltip("Tint the visible partner base so the player can see which pair is the current target.")]
    public bool highlightActiveTarget = true;
    public Color targetHighlightColor = new Color(1f, 0.92f, 0.2f);

    // ------------------------------------------------------------------ Movement
    [Header("Game Mode - Section Movement")]
    [Tooltip("How far (in this object's local units) the section moves down after each completed base.")]
    [Min(0f)] public float strandMoveDownDistance = 1f;
    [Min(0f)] public float strandMoveDuration = 0.5f;

    public enum SectionMoveMode
    {
        /// <summary>Only the section that contains the completed base moves down.</summary>
        CurrentSectionOnly,
        /// <summary>The completed base's section AND every section after it (Section 2, then 3...) moves down.</summary>
        CurrentAndFollowingSections,
        /// <summary>The whole generated DNA moves down.</summary>
        WholeDNA,
    }
    public SectionMoveMode sectionMoveMode = SectionMoveMode.CurrentSectionOnly;

    // ------------------------------------------------------------------ Sections
    [Header("Game Mode - Sections")]
    [Tooltip("ON: the pairs are divided evenly between the sections (Start Pair Index / Pair Count are ignored). " +
             "OFF: each section uses its own Start Pair Index and Pair Count.")]
    public bool autoDivideSections = true;
    [Tooltip("Only used when Auto Divide is ON. True = Section 1 is at the TOP of the DNA and the last section at " +
             "the bottom. False = Section 1 at the bottom.")]
    public bool section1AtTop = true;

    /// <summary>Settings for one game section (stage).</summary>
    [System.Serializable]
    public class GameSection
    {
        public string name = "Section";
        [Tooltip("Manual mode only: first pair index of this section (pair 0 is the bottom pair).")]
        [Min(0)] public int startPairIndex = 0;
        [Tooltip("Manual mode only: how many pairs the section has. End index = Start + Pair Count - 1.")]
        [Min(0)] public int pairCount = 6;
        [Tooltip("How many bases are missing in this section. 0 = fully paired (Stage 1).")]
        [Min(0)] public int missingBases = 0;
        [Tooltip("Distance this section moves down after a completed base. Negative = use the global " +
                 "Strand Move Down Distance.")]
        public float moveDownDistanceOverride = -1f;

        /// <summary>Last pair index (inclusive) when using manual ranges.</summary>
        public int EndPairIndex => startPairIndex + pairCount - 1;
    }

    public List<GameSection> gameSections = new List<GameSection>
    {
        new GameSection { name = "Section 1 (Complete)",           startPairIndex = 0,  pairCount = 6, missingBases = 0 },
        new GameSection { name = "Section 2 (Partially Complete)", startPairIndex = 6,  pairCount = 6, missingBases = 3 },
        new GameSection { name = "Section 3 (Many Missing)",       startPairIndex = 12, pairCount = 6, missingBases = 5 },
    };

    // ------------------------------------------------------------------ Buttons
    [Header("Game Mode - UI Buttons")]
    [Tooltip("Automatically hook the four buttons' onClick to SubmitBaseAnswer().")]
    public bool autoConnectButtons = true;
    public Button buttonC;
    public Button buttonA;
    public Button buttonT;
    public Button buttonG;
    [Tooltip("Optional. Activated when Game Mode starts and deactivated by ExitGameMode().")]
    public GameObject buttonsPanel;
    [Tooltip("Also flash the UI button green / red.")]
    public bool flashButtons = true;

    // ------------------------------------------------------------------ Events
    [Header("Game Mode - Events")]
    public UnityEvent OnGameStarted = new UnityEvent();
    public UnityEvent OnAnswerCorrect = new UnityEvent();
    public UnityEvent OnAnswerWrong = new UnityEvent();
    public UnityEvent OnGameCompleted = new UnityEvent();

    // ------------------------------------------------------------------ Game state
    /// <summary>Internal state of the mini-game.</summary>
    public enum GameState
    {
        NotStarted,
        MovingCamera,
        Playing,
        FlashingCorrect,
        FlashingWrong,
        MovingSection,
        Completed,
    }

    // ------------------------------------------------------------------ Runtime state
    private const string ContainerName = "__DNA_Generated";
    private const string PairPivotName = "__PairPivot";
    private const string SectionRootName = "__Section";
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

    private static readonly Color GizmoPairColor = new Color(1f, 0.2f, 1f); // magenta preview

    // ------------------------------------------------------------------ Game runtime data
    /// <summary>Runtime (resolved) version of a GameSection.</summary>
    private class SectionRuntime
    {
        public int index;
        public string name;
        public int start;   // first pair index (inclusive)
        public int end;     // last pair index (inclusive); end < start means empty
        public int missing;
        public float moveDistance; // negative = use global
        public Transform root;     // logical parent of this section's pair pivots + strand pieces
    }

    /// <summary>One missing base: everything needed to spawn a base into the exact slot later, using the
    /// same placement values the normal generator used.</summary>
    private class MissingSlot
    {
        public int pairIndex;
        public int sectionIndex;
        public bool isLeft;
        public char correctLetter;   // the letter that belongs in the missing slot
        public char visibleLetter;   // the letter already shown on the other half
        public Transform parent;     // the pair pivot
        public Vector3 localPos;
        public float side;
        public float elemDist;
        public Quaternion baseRot;
        public Quaternion elemRot;
        public bool flipped;
        public GameObject visibleGo; // the base half that IS present
        public GameObject currentGo; // whatever is currently spawned in the missing slot (may be null)
    }

    /// <summary>Saved original color of one material property so it can be restored exactly.</summary>
    private class TintRecord
    {
        public Material mat;
        public int propId;
        public Color original;
    }

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    private GameState _state = GameState.NotStarted;
    private bool _gameModeActive;
    private bool _buildingGame; // true only while GameMode() is respawning the DNA

    private readonly List<SectionRuntime> _sections = new();
    private readonly Dictionary<int, int> _missingSidePlan = new();   // pairIndex -> -1 (left) / +1 (right)
    private readonly Dictionary<int, MissingSlot> _slots = new();     // pairIndex -> slot
    private readonly List<MissingSlot> _targets = new();
    private int _targetIndex = -1;
    private MissingSlot _currentTarget;

    private readonly Dictionary<GameObject, List<TintRecord>> _tints = new();
    private readonly Dictionary<Graphic, Color> _buttonOriginals = new();

    private UnityAction _clickC, _clickA, _clickT, _clickG;

    private bool _camSaved;
    private Vector3 _camOrigPos;
    private Quaternion _camOrigRot;
    private Camera _camUsed;

    /// <summary>All spawned objects (strand, pair pivots, base halves, elements, sugars, phosphates).</summary>
    public IReadOnlyList<GameObject> Spawned => spawned;

    /// <summary>Current Game Mode state.</summary>
    public GameState CurrentGameState => _state;
    /// <summary>True between GameMode() and ExitGameMode() / respawn.</summary>
    public bool IsGameModeActive => _gameModeActive;

    // =================================================================== Unity callbacks
    private void Start()
    {
        if (startGameModeOnStart) GameMode();
        else if (spawnOnStart) Spawn();
    }

    private void OnEnable()
    {
        ConnectButtons();
    }

    private void OnDisable()
    {
        DisconnectButtons();
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

        // Game Mode clamps
        cameraMoveDuration = Mathf.Max(0f, cameraMoveDuration);
        correctFlashDuration = Mathf.Max(0f, correctFlashDuration);
        wrongFlashDuration = Mathf.Max(0f, wrongFlashDuration);
        strandMoveDownDistance = Mathf.Max(0f, strandMoveDownDistance);
        strandMoveDuration = Mathf.Max(0f, strandMoveDuration);

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
        ClearSpawned(); // also cancels a running game (unless GameMode() itself is rebuilding)
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

        // Game Mode: logical section roots that pair pivots + strand pieces get parented to,
        // so a whole section can be moved without touching the rest of the DNA.
        if (_gameModeActive) CreateSectionRoots();

        // --- Strand ---
        Quaternion baseRot = Quaternion.Euler(baseRotation);
        for (int i = 0; i < count; i++)
        {
            Vector3 pos = spawnPosition + Vector3.up * (i * step);
            Quaternion rot = Quaternion.Euler(rotationOffsetPerStep * i) * baseRot;
            Transform strandParent = _gameModeActive ? GetSectionRootForStrand(i) : _container;
            Place(prefab, pos, rot, $"{prefab.name}_{i}", strandParent);
        }

        // --- Base pairs (+ interior elements + sugar + phosphate) ---
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

    /// <summary>Element Distance to use, taking the per-pattern override into account
    /// ('\0' = no override, used by the gizmo preview).</summary>
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

    /// <summary>Per-pattern extra LOCAL rotation for the interior element on the LEFT half
    /// (added on top of elementLocalRotation). Returns Vector3.zero for '\0' (gizmo preview).</summary>
    private Vector3 GetElementRotationLeft(char leftLetter) => leftLetter switch
    {
        'A' => elementRotationAT_L,   // A on left -> A-T pattern
        'T' => elementRotationTA_L,   // T on left -> T-A pattern
        'C' => elementRotationCG_L,   // C on left -> C-G pattern
        'G' => elementRotationGC_L,   // G on left -> G-C pattern
        _ => Vector3.zero,
    };

    /// <summary>Per-pattern extra LOCAL rotation for the interior element on the RIGHT half
    /// (added on top of elementLocalRotation). Returns Vector3.zero for '\0' (gizmo preview).</summary>
    private Vector3 GetElementRotationRight(char leftLetter) => leftLetter switch
    {
        'A' => elementRotationAT_R,   // A on left -> A-T pattern, right = T
        'T' => elementRotationTA_R,   // T on left -> T-A pattern, right = A
        'C' => elementRotationCG_R,   // C on left -> C-G pattern, right = G
        'G' => elementRotationGC_R,   // G on left -> G-C pattern, right = C
        _ => Vector3.zero,
    };

    private void SpawnBasePairs(float step)
    {
        int total = TotalPairs;
        int warnedMask = 0;

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

            // Element distance is keyed off which letter is on the left, so each of the
            // four patterns (A-T, T-A, C-G, G-C) can look different even when they share
            // the same bases.
            float elemDist = GetElementDistance(leftLetter);

            // Base halves use the global left/right pair rotations.
            Quaternion leftBaseRot = Quaternion.Euler(leftPairRotation);
            Quaternion rightBaseRot = Quaternion.Euler(rightPairRotation);

            // Interior elements use the global element rotation plus the per-pattern override.
            Quaternion leftElemRot = Quaternion.Euler(elementLocalRotation) *
                                     Quaternion.Euler(GetElementRotationLeft(leftLetter));
            Quaternion rightElemRot = Quaternion.Euler(elementLocalRotation) *
                                      Quaternion.Euler(GetElementRotationRight(leftLetter));

            // Sugar / phosphate alternate between the normal and "Flipped" position+rotation sets.
            bool flipped = IsFlippedPair(p);

            // Game Mode: is one half of this pair planned to be missing? (-1 = left, +1 = right, 0 = none)
            int missingSide = 0;
            if (_gameModeActive) _missingSidePlan.TryGetValue(p, out missingSide);

            // Game Mode parents pivots to their section root; normal mode uses the container as before.
            Transform pivotParent = _gameModeActive ? GetSectionRootForPair(p) : _container;

            var pivotGo = new GameObject($"{PairPivotName}_{p}");
            pivotGo.transform.SetParent(pivotParent, false);
            pivotGo.transform.SetLocalPositionAndRotation(center, pairPivotRot);
            MarkTemporary(pivotGo);
            spawned.Add(pivotGo);

            // A missing half skips ONLY its base (+ interior element). Its sugar / phosphate are still
            // spawned so the backbone stays intact; the base is added later by the game.
            GameObject leftGo = PlaceChild(leftPrefab, pivotGo.transform, Vector3.left * pairHalfLength, -1f, elemDist,
                       leftBaseRot, leftElemRot, flipped,
                       $"{leftPrefab.name}_Pair_{p}_L", leftLetter,
                       spawnBase: missingSide >= 0, spawnBackbone: true);

            GameObject rightGo = PlaceChild(rightPrefab, pivotGo.transform, Vector3.right * pairHalfLength, 1f, elemDist,
                       rightBaseRot, rightElemRot, flipped,
                       $"{rightPrefab.name}_Pair_{p}_R", rightLetter,
                       spawnBase: missingSide <= 0, spawnBackbone: true);

            // Remember the slot so the game can fill it with the exact same placement values.
            if (missingSide != 0)
            {
                bool leftIsMissing = missingSide < 0;
                _slots[p] = new MissingSlot
                {
                    pairIndex = p,
                    sectionIndex = GetSectionIndexForPair(p),
                    isLeft = leftIsMissing,
                    correctLetter = leftIsMissing ? leftLetter : rightLetter,
                    visibleLetter = leftIsMissing ? rightLetter : leftLetter,
                    parent = pivotGo.transform,
                    localPos = Vector3.right * (leftIsMissing ? -pairHalfLength : pairHalfLength),
                    side = leftIsMissing ? -1f : 1f,
                    elemDist = elemDist,
                    baseRot = leftIsMissing ? leftBaseRot : rightBaseRot,
                    elemRot = leftIsMissing ? leftElemRot : rightElemRot,
                    flipped = flipped,
                    visibleGo = leftIsMissing ? rightGo : leftGo,
                    currentGo = null,
                };
            }
        }
    }

    private GameObject Place(GameObject source, Vector3 localPos, Quaternion localRot, string objName, Transform parent = null)
    {
        GameObject go = Instantiate(source, parent != null ? parent : _container);
        go.name = objName;
        go.transform.SetLocalPositionAndRotation(localPos, localRot);
        MarkTemporary(go);
        spawned.Add(go);
        return go;
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
    /// Game Mode additions: spawnBase = false skips the base half (+ its element), and
    /// spawnBackbone = false skips sugar / phosphate. This lets the game spawn a missing base later
    /// with exactly the same placement math, without duplicating the sugar / phosphate.
    /// Returns the spawned base half (null if the base was not spawned).</summary>
    private GameObject PlaceChild(GameObject source, Transform parent, Vector3 localPos, float side,
                            float elementDist, Quaternion baseLocalRot, Quaternion elementLocalRot,
                            bool flipped,
                            string objName, char letter,
                            bool spawnBase = true, bool spawnBackbone = true)
    {
        GameObject go = null;

        if (spawnBase && source != null)
        {
            go = Instantiate(source, parent);
            go.name = objName;
            go.transform.localPosition = localPos;
            go.transform.localRotation = baseLocalRot;
            go.transform.localScale = source.transform.localScale;
            MarkTemporary(go);
            spawned.Add(go);

            // --- Interior element (child of the base half, inherits its scale) ---
            if (spawnElements)
            {
                GameObject elementPrefab = GetElementPrefab(letter);
                if (elementPrefab != null)
                {
                    GameObject element = Instantiate(elementPrefab, go.transform);
                    element.name = $"{elementPrefab.name}_Element_{letter}";

                    // Target the element's PIVOT-space position: ±Element Distance along the pivot's X axis.
                    Vector3 desiredInPivotSpace = Vector3.right * (side * elementDist);

                    // The base half sits at localPos (pivot space) with rotation baseLocalRot and scale goScale.
                    // elementWorldInPivot = localPos + baseLocalRot * (goScale .* elementLocalPos)
                    // Solve for elementLocalPos so the result equals desiredInPivotSpace.
                    Vector3 deltaInPivotSpace = desiredInPivotSpace - localPos;
                    Vector3 deltaInGoLocalSpace = Quaternion.Inverse(baseLocalRot) * deltaInPivotSpace;

                    // Compensate for the base half's local scale so the element lands where we asked
                    // even when the base prefab is stretched (non-uniform scale).
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
        }

        if (!spawnBackbone) return go;

        bool isLeft = side < 0f;

        // --- Sugar (sibling of the base half, parented to the pivot - scale independent) ---
        if (spawnSugar && prefabSugar != null)
        {
            GameObject sugar = Instantiate(prefabSugar, parent);
            sugar.name = $"{prefabSugar.name}_Sugar_{(isLeft ? "L" : "R")}_{letter}";

            // Pick normal vs flipped authored offset / rotation.
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

            phosphate.transform.localPosition = localPos + baseLocalRot * phosOffset;
            phosphate.transform.localRotation = baseLocalRot * phosRotOffset;
            phosphate.transform.localScale = phosphateLocalScale;

            MarkTemporary(phosphate);
            spawned.Add(phosphate);
        }

        return go;
    }

    [ContextMenu("Clear Spawned")]
    public void ClearSpawned()
    {
        // Destroying the DNA invalidates a running game, so cancel it - unless GameMode() itself
        // is respawning the DNA as part of starting the game.
        if (!_buildingGame) CancelGameInternal();
        else
        {
            // Keep the plan, but the old section roots are about to be destroyed.
            for (int i = 0; i < _sections.Count; i++) _sections[i].root = null;
            _slots.Clear();
        }

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
        h = Mix(h, alternateSugarPhosphateFlip);
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
        h = Mix(h, spawnPosition); h = Mix(h, baseRotation);
        h = Mix(h, count); h = Mix(h, rotationOffsetPerStep); h = Mix(h, stackGap); h = Mix(h, stepHeightOverride);
        return h;
    }

    private static int Mix(int h, int v) { unchecked { return h * 31 + v; } }
    private static int Mix(int h, float v) => Mix(h, v.GetHashCode());
    private static int Mix(int h, bool v) => Mix(h, v ? 1 : 0);
    private static int Mix(int h, Vector3 v) => Mix(h, v.GetHashCode());
    private static int Mix(int h, string s) => Mix(h, s != null ? s.GetHashCode() : 0);
    private static int Mix(int h, Object o) => Mix(h, o != null ? o.GetInstanceID() : 0);

    // =================================================================== GAME MODE
    // ---------------------------------------------------------------------------------
    // Flow:
    //   GameMode()  -> BuildGamePlan() (sections + which pairs/sides are missing)
    //               -> Spawn() (normal generator; missing halves skip their base, sections get roots)
    //               -> BuildTargets() (ordered list of missing slots)
    //               -> GameStartRoutine(): camera moves in, first target is selected
    //   SubmitBaseAnswer(letter) -> AnswerRoutine(): flash, fill the slot, move the section, next target
    // ---------------------------------------------------------------------------------

    /// <summary>Starts the DNA base-pairing mini-game. Call from the Context Menu (Play Mode) or from code.</summary>
    [ContextMenu("Game Mode")]
    public void GameMode()
    {
        if (!enableGameMode)
        {
            Debug.LogWarning("DNASimulator: Game Mode is disabled (Enable Game Mode is off).", this);
            return;
        }
        if (!Application.isPlaying)
        {
            Debug.LogWarning("DNASimulator: Game Mode needs Play Mode (camera / flash animations use coroutines).", this);
            return;
        }
        if (prefab == null)
        {
            Debug.LogWarning("DNASimulator: no strand prefab assigned - cannot start Game Mode.", this);
            return;
        }
        if (!spawnBasePairs)
        {
            Debug.LogWarning("DNASimulator: 'Spawn Base Pairs' must be enabled for Game Mode.", this);
            return;
        }

        // Stop any previous game and restore its temporary colors.
        CancelGameInternal();
        ConnectButtons();

        // Build the plan first, then respawn the DNA with the plan applied.
        // The cached base sequence is kept, so the DNA looks the same as before (unless settings changed).
        _buildingGame = true;
        _gameModeActive = true;
        try
        {
            BuildGamePlan();
            Spawn();
        }
        finally
        {
            _buildingGame = false;
        }

        BuildTargets();

        if (buttonsPanel != null) buttonsPanel.SetActive(true);
        OnGameStarted?.Invoke();

        StartCoroutine(GameStartRoutine());
    }

    /// <summary>Leaves Game Mode: stops the game, restores the camera and respawns the normal DNA.</summary>
    [ContextMenu("Exit Game Mode")]
    public void ExitGameMode()
    {
        CancelGameInternal();
        RestoreCamera();
        if (buttonsPanel != null) buttonsPanel.SetActive(false);
        if (Application.isPlaying) Spawn();
    }

    /// <summary>Called by the C / A / T / G buttons (or by your own script).
    /// Ignored unless the game is waiting for an answer.</summary>
    public void SubmitBaseAnswer(char letter)
    {
        if (!_gameModeActive || _state != GameState.Playing || _currentTarget == null) return;

        letter = char.ToUpperInvariant(letter);
        if (Bases.IndexOf(letter) < 0) return;

        // Lock input immediately so a second click during the flash is ignored.
        _state = letter == _currentTarget.correctLetter ? GameState.FlashingCorrect : GameState.FlashingWrong;
        StartCoroutine(AnswerRoutine(letter));
    }

    // ------------------------------------------------------------------ Buttons
    /// <summary>Hooks the four UI buttons to SubmitBaseAnswer. Safe to call repeatedly
    /// (e.g. after assigning buttons from code).</summary>
    public void ConnectButtons()
    {
        DisconnectButtons();
        if (!autoConnectButtons) return;

        _clickC ??= () => SubmitBaseAnswer('C');
        _clickA ??= () => SubmitBaseAnswer('A');
        _clickT ??= () => SubmitBaseAnswer('T');
        _clickG ??= () => SubmitBaseAnswer('G');

        if (buttonC != null) buttonC.onClick.AddListener(_clickC);
        if (buttonA != null) buttonA.onClick.AddListener(_clickA);
        if (buttonT != null) buttonT.onClick.AddListener(_clickT);
        if (buttonG != null) buttonG.onClick.AddListener(_clickG);
    }

    private void DisconnectButtons()
    {
        if (_clickC != null && buttonC != null) buttonC.onClick.RemoveListener(_clickC);
        if (_clickA != null && buttonA != null) buttonA.onClick.RemoveListener(_clickA);
        if (_clickT != null && buttonT != null) buttonT.onClick.RemoveListener(_clickT);
        if (_clickG != null && buttonG != null) buttonG.onClick.RemoveListener(_clickG);
    }

    private Button GetButton(char letter) => letter switch
    {
        'C' => buttonC,
        'A' => buttonA,
        'T' => buttonT,
        'G' => buttonG,
        _ => null,
    };

    /// <summary>Briefly tints a UI button's graphic, then restores its exact original color.</summary>
    private void FlashButton(Button button, Color color, float duration)
    {
        if (!flashButtons || button == null || button.targetGraphic == null) return;
        StartCoroutine(FlashButtonRoutine(button.targetGraphic, color, duration));
    }

    private IEnumerator FlashButtonRoutine(Graphic graphic, Color color, float duration)
    {
        if (!_buttonOriginals.ContainsKey(graphic)) _buttonOriginals[graphic] = graphic.color;
        graphic.color = color;

        yield return new WaitForSeconds(duration);

        if (graphic != null && _buttonOriginals.TryGetValue(graphic, out Color original))
        {
            graphic.color = original;
            _buttonOriginals.Remove(graphic);
        }
    }

    private void RestoreAllButtonColors()
    {
        foreach (var kv in _buttonOriginals)
            if (kv.Key != null) kv.Key.color = kv.Value;
        _buttonOriginals.Clear();
    }

    // ------------------------------------------------------------------ Plan: sections + missing bases
    /// <summary>Resolves the section ranges and randomly chooses which pair / side is missing in each section.</summary>
    private void BuildGamePlan()
    {
        _missingSidePlan.Clear();
        _slots.Clear();
        ResolveSections();

        var rng = new System.Random(gameSeed != 0 ? gameSeed : System.Environment.TickCount);

        for (int s = 0; s < _sections.Count; s++)
        {
            SectionRuntime sec = _sections[s];
            int len = sec.end - sec.start + 1;
            if (len <= 0) continue;

            int missing = Mathf.Clamp(sec.missing, 0, len);

            // Partial Fisher-Yates: pick 'missing' distinct pair indices.
            var pool = new List<int>(len);
            for (int p = sec.start; p <= sec.end; p++) pool.Add(p);

            for (int k = 0; k < missing; k++)
            {
                int r = rng.Next(k, pool.Count);
                (pool[k], pool[r]) = (pool[r], pool[k]);

                int side = rng.Next(2) == 0 ? -1 : 1; // -1 = LEFT half missing, +1 = RIGHT half missing
                _missingSidePlan[pool[k]] = side;
            }
        }
    }

    /// <summary>Turns the serialized GameSection list into concrete pair ranges.</summary>
    private void ResolveSections()
    {
        _sections.Clear();

        if (gameSections == null || gameSections.Count == 0)
        {
            Debug.LogWarning("DNASimulator: no Game Sections configured - Game Mode has nothing to play.", this);
            return;
        }

        int total = TotalPairs;
        int n = gameSections.Count;
        int[] starts = new int[n];
        int[] ends = new int[n];

        if (autoDivideSections)
        {
            // Split the pairs evenly. Pair 0 is the BOTTOM pair, so with section1AtTop the LAST section
            // gets the lowest pair indices.
            int baseSize = total / n;
            int rem = total % n;
            int cursor = 0;
            for (int i = 0; i < n; i++)
            {
                int k = section1AtTop ? (n - 1 - i) : i;
                int size = baseSize + (k < rem ? 1 : 0);
                starts[k] = cursor;
                ends[k] = cursor + size - 1;
                cursor += size;
            }
        }
        else
        {
            for (int k = 0; k < n; k++)
            {
                GameSection gs = gameSections[k] ?? new GameSection();
                starts[k] = Mathf.Max(0, gs.startPairIndex);
                ends[k] = Mathf.Min(total - 1, gs.startPairIndex + Mathf.Max(0, gs.pairCount) - 1);
            }
        }

        for (int k = 0; k < n; k++)
        {
            GameSection gs = gameSections[k] ?? new GameSection();
            _sections.Add(new SectionRuntime
            {
                index = k,
                name = string.IsNullOrEmpty(gs.name) ? $"Section {k + 1}" : gs.name,
                start = starts[k],
                end = ends[k],
                missing = Mathf.Max(0, gs.missingBases),
                moveDistance = gs.moveDownDistanceOverride,
                root = null,
            });
        }

        if (total < n)
            Debug.LogWarning($"DNASimulator: only {total} base pair(s) exist but {n} sections are configured. " +
                             "Increase Count / Pairs Per Step so every section has pairs.", this);
    }

    private int GetSectionIndexForPair(int p)
    {
        for (int i = 0; i < _sections.Count; i++)
            if (p >= _sections[i].start && p <= _sections[i].end) return i;
        return -1;
    }

    private void CreateSectionRoots()
    {
        for (int i = 0; i < _sections.Count; i++)
        {
            var go = new GameObject($"{SectionRootName}_{i}");
            go.transform.SetParent(_container, false); // identity: pivots keep their exact container-local positions
            MarkTemporary(go);
            _sections[i].root = go.transform;
        }
    }

    private Transform GetSectionRootForPair(int p)
    {
        for (int i = 0; i < _sections.Count; i++)
        {
            SectionRuntime s = _sections[i];
            if (s.root != null && p >= s.start && p <= s.end) return s.root;
        }
        return _container;
    }

    /// <summary>A strand piece belongs to the section that contains the pair at the piece's vertical center.</summary>
    private Transform GetSectionRootForStrand(int stepIndex)
    {
        int total = TotalPairs;
        int p = Mathf.Clamp(Mathf.RoundToInt((stepIndex + 0.5f - pairHeightOffset) * pairsPerStep - 0.5f), 0, total - 1);
        return GetSectionRootForPair(p);
    }

    /// <summary>Builds the ordered target list: Section 1 first, and inside a section from the lowest pair index up.</summary>
    private void BuildTargets()
    {
        _targets.Clear();
        _targetIndex = -1;
        _currentTarget = null;

        for (int s = 0; s < _sections.Count; s++)
        {
            SectionRuntime sec = _sections[s];
            for (int p = sec.start; p <= sec.end; p++)
            {
                if (_slots.TryGetValue(p, out MissingSlot slot))
                {
                    slot.sectionIndex = s;
                    _targets.Add(slot);
                }
            }
        }
    }

    // ------------------------------------------------------------------ Game routines
    private IEnumerator GameStartRoutine()
    {
        _state = GameState.MovingCamera;
        yield return MoveCameraRoutine();

        AdvanceToNextTarget();
    }

    /// <summary>Selects the next missing base, or completes the game if there are none left.</summary>
    private void AdvanceToNextTarget()
    {
        _targetIndex++;

        if (_targetIndex >= _targets.Count)
        {
            CompleteGame();
            return;
        }

        _currentTarget = _targets[_targetIndex];
        ApplyHighlight(_currentTarget);
        _state = GameState.Playing;
    }

    private void CompleteGame()
    {
        _currentTarget = null;
        _state = GameState.Completed;
        OnGameCompleted?.Invoke();
    }

    private IEnumerator AnswerRoutine(char chosen)
    {
        MissingSlot t = _currentTarget;
        ClearHighlight(t);

        Button pressed = GetButton(chosen);
        Button correctButton = GetButton(t.correctLetter);

        if (chosen == t.correctLetter)
        {
            // ---- CORRECT: fill the slot, green flash, restore original color ----
            FlashButton(pressed, correctFlashColor, correctFlashDuration);
            GameObject go = RevealBase(t, t.correctLetter);
            OnAnswerCorrect?.Invoke();
            yield return FlashObjectRoutine(go, correctFlashColor, correctFlashDuration);
        }
        else
        {
            // ---- WRONG: show the chosen (wrong) base in red, remove it, then show the right one in green ----
            FlashButton(pressed, wrongFlashColor, wrongFlashDuration);
            GameObject wrongGo = RevealBase(t, chosen);
            OnAnswerWrong?.Invoke();
            yield return FlashObjectRoutine(wrongGo, wrongFlashColor, wrongFlashDuration);

            DestroySpawnedBase(wrongGo);
            t.currentGo = null;

            _state = GameState.FlashingCorrect;
            FlashButton(correctButton, correctFlashColor, correctFlashDuration);
            GameObject rightGo = RevealBase(t, t.correctLetter);
            yield return FlashObjectRoutine(rightGo, correctFlashColor, correctFlashDuration);
        }

        // ---- Move the section down, then pick the next target ----
        _state = GameState.MovingSection;
        yield return MoveSectionsRoutine(t.sectionIndex);

        AdvanceToNextTarget();
    }

    /// <summary>Spawns a base of the given letter into a missing slot using the SAME placement math as the
    /// normal generator (PlaceChild), without duplicating sugar / phosphate.</summary>
    private GameObject RevealBase(MissingSlot t, char letter)
    {
        GameObject src = GetBasePrefab(letter);
        if (src == null || t.parent == null)
        {
            Debug.LogWarning($"DNASimulator: cannot spawn base '{letter}' (prefab or pair pivot missing).", this);
            return null;
        }

        string nm = $"{src.name}_Pair_{t.pairIndex}_{(t.isLeft ? "L" : "R")}";
        GameObject go = PlaceChild(src, t.parent, t.localPos, t.side, t.elemDist, t.baseRot, t.elemRot,
                                   t.flipped, nm, letter, spawnBase: true, spawnBackbone: false);
        t.currentGo = go;
        return go;
    }

    private void DestroySpawnedBase(GameObject go)
    {
        if (go == null) return;

        RestoreObject(go);

        // The base and its interior element were both added to 'spawned'.
        foreach (Transform tr in go.GetComponentsInChildren<Transform>(true))
            spawned.Remove(tr.gameObject);

        Destroy(go);
    }

    // ------------------------------------------------------------------ Highlight
    private void ApplyHighlight(MissingSlot t)
    {
        if (!highlightActiveTarget || t == null || t.visibleGo == null) return;
        TintObject(t.visibleGo, targetHighlightColor);
    }

    private void ClearHighlight(MissingSlot t)
    {
        if (t == null || t.visibleGo == null) return;
        RestoreObject(t.visibleGo);
    }

    // ------------------------------------------------------------------ Material feedback
    /// <summary>Tints an object, waits, then restores its exact original colors.</summary>
    private IEnumerator FlashObjectRoutine(GameObject go, Color color, float duration)
    {
        if (go == null)
        {
            yield return new WaitForSeconds(duration);
            yield break;
        }

        TintObject(go, color);
        yield return new WaitForSeconds(duration);
        RestoreObject(go);
    }

    /// <summary>Sets the color of every renderer material under 'go'. The ORIGINAL color of every
    /// _BaseColor / _Color property is saved the first time, and materials are per-renderer INSTANCES
    /// (renderer.materials), so the shared prefab materials are never modified. Alpha is preserved.</summary>
    private void TintObject(GameObject go, Color color)
    {
        if (go == null) return;

        if (!_tints.TryGetValue(go, out List<TintRecord> records))
        {
            records = new List<TintRecord>();
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
            {
                Material[] mats = r.materials; // creates instances - does not touch the shared prefab materials
                for (int i = 0; i < mats.Length; i++)
                {
                    Material m = mats[i];
                    if (m == null) continue;

                    if (m.HasProperty(BaseColorId))
                        records.Add(new TintRecord { mat = m, propId = BaseColorId, original = m.GetColor(BaseColorId) });
                    if (m.HasProperty(ColorId))
                        records.Add(new TintRecord { mat = m, propId = ColorId, original = m.GetColor(ColorId) });
                }
            }
            _tints[go] = records;
        }

        for (int i = 0; i < records.Count; i++)
        {
            TintRecord rec = records[i];
            if (rec.mat == null) continue;
            rec.mat.SetColor(rec.propId, new Color(color.r, color.g, color.b, rec.original.a));
        }
    }

    /// <summary>Restores the exact original colors saved by TintObject.</summary>
    private void RestoreObject(GameObject go)
    {
        if (go == null) return;
        if (!_tints.TryGetValue(go, out List<TintRecord> records)) return;

        for (int i = 0; i < records.Count; i++)
        {
            TintRecord rec = records[i];
            if (rec.mat != null) rec.mat.SetColor(rec.propId, rec.original);
        }
        _tints.Remove(go);
    }

    private void RestoreAllTints()
    {
        foreach (var kv in _tints)
        {
            List<TintRecord> records = kv.Value;
            for (int i = 0; i < records.Count; i++)
                if (records[i].mat != null) records[i].mat.SetColor(records[i].propId, records[i].original);
        }
        _tints.Clear();
    }

    // ------------------------------------------------------------------ Section movement
    /// <summary>Smoothly slides the finished section (or sections, depending on Section Move Mode) down.</summary>
    private IEnumerator MoveSectionsRoutine(int sectionIndex)
    {
        var roots = new List<Transform>();
        float distance = strandMoveDownDistance;

        if (sectionIndex >= 0 && sectionIndex < _sections.Count && _sections[sectionIndex].moveDistance >= 0f)
            distance = _sections[sectionIndex].moveDistance;

        switch (sectionMoveMode)
        {
            case SectionMoveMode.CurrentSectionOnly:
                if (sectionIndex >= 0 && sectionIndex < _sections.Count && _sections[sectionIndex].root != null)
                    roots.Add(_sections[sectionIndex].root);
                break;

            case SectionMoveMode.CurrentAndFollowingSections:
                for (int i = Mathf.Max(0, sectionIndex); i < _sections.Count; i++)
                    if (_sections[i].root != null) roots.Add(_sections[i].root);
                break;

            case SectionMoveMode.WholeDNA:
                if (_container != null) roots.Add(_container);
                break;
        }

        if (roots.Count == 0 || distance <= 0f) yield break;

        var startPositions = new Vector3[roots.Count];
        var endPositions = new Vector3[roots.Count];
        for (int i = 0; i < roots.Count; i++)
        {
            startPositions[i] = roots[i].localPosition;
            endPositions[i] = startPositions[i] + Vector3.down * distance; // down in the DNA's local space
        }

        float duration = Mathf.Max(0f, strandMoveDuration);
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float s = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
            for (int i = 0; i < roots.Count; i++)
                if (roots[i] != null) roots[i].localPosition = Vector3.Lerp(startPositions[i], endPositions[i], s);
            yield return null;
        }

        for (int i = 0; i < roots.Count; i++)
            if (roots[i] != null) roots[i].localPosition = endPositions[i];
    }

    // ------------------------------------------------------------------ Camera
    private Camera ResolveGameCamera()
    {
        if (gameCamera != null) return gameCamera;
        return Camera.main;
    }

    /// <summary>World-space center of the generated DNA (renderer bounds), with a computed fallback.</summary>
    private Vector3 GetDnaCenterWorld()
    {
        if (_container != null)
        {
            Renderer[] rends = _container.GetComponentsInChildren<Renderer>();
            bool has = false;
            Bounds b = default;
            for (int i = 0; i < rends.Length; i++)
            {
                if (!has) { b = rends[i].bounds; has = true; }
                else b.Encapsulate(rends[i].bounds);
            }
            if (has) return b.center;
        }

        float stepHeight = GetStepHeight();
        float totalHeight = count * stepHeight + (count - 1) * stackGap;
        return transform.TransformPoint(spawnPosition + Vector3.up * (totalHeight * 0.5f));
    }

    /// <summary>Smoothly moves / rotates the camera to look at the DNA center from the front.
    /// The DNA itself is never rotated.</summary>
    private IEnumerator MoveCameraRoutine()
    {
        Camera cam = ResolveGameCamera();
        if (cam == null)
        {
            Debug.LogWarning("DNASimulator: no Game Camera assigned and no Camera.main found - the camera will not move.", this);
            yield break;
        }

        Transform ct = cam.transform;

        // Remember where the camera was so ExitGameMode() can put it back.
        if (!_camSaved)
        {
            _camUsed = cam;
            _camOrigPos = ct.position;
            _camOrigRot = ct.rotation;
            _camSaved = true;
        }

        Vector3 center = GetDnaCenterWorld();
        Vector3 targetPos = center + transform.TransformVector(gameCameraOffset);
        Vector3 lookDir = center - targetPos;
        if (lookDir.sqrMagnitude < 1e-6f) lookDir = transform.forward;
        Quaternion targetRot = Quaternion.LookRotation(lookDir, transform.up);

        Vector3 startPos = ct.position;
        Quaternion startRot = ct.rotation;

        float duration = Mathf.Max(0f, cameraMoveDuration);
        if (duration <= 0f)
        {
            ct.SetPositionAndRotation(targetPos, targetRot);
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float s = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
            ct.SetPositionAndRotation(Vector3.Lerp(startPos, targetPos, s), Quaternion.Slerp(startRot, targetRot, s));
            yield return null;
        }

        ct.SetPositionAndRotation(targetPos, targetRot);
    }

    private void RestoreCamera()
    {
        if (!_camSaved) return;
        if (_camUsed != null) _camUsed.transform.SetPositionAndRotation(_camOrigPos, _camOrigRot);
        _camSaved = false;
        _camUsed = null;
    }

    // ------------------------------------------------------------------ Cancel / reset
    /// <summary>Stops every game routine and clears all game data. Does NOT respawn and does NOT move the camera.</summary>
    private void CancelGameInternal()
    {
        StopAllCoroutines();
        RestoreAllTints();
        RestoreAllButtonColors();

        _state = GameState.NotStarted;
        _gameModeActive = false;
        _currentTarget = null;
        _targetIndex = -1;
        _targets.Clear();
        _slots.Clear();
        _sections.Clear();
        _missingSidePlan.Clear();
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

        // Base pair preview. The sequence is random at spawn time, so this uses the baseline
        // rotation and distance - an approximation, not an exact match.
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