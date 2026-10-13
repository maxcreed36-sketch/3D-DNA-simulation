using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// A straight (no helix twist) DNA strand that reacts to a DNAReplicationSimulator's deletions
/// in a repeating cycle. NOTHING is spawned randomly - only deleted pairs are re-used.
///
///   1. The source deletes a pair  -> that exact pair (same letters) is spawned at the top.
///   2. This repeats for "Spawn Batch Size" deletions (default 3).
///   3. Then a DELAY: the next "Deletion Delay" deletions (default 5) are NOT spawned.
///   4. The cycle repeats (3 spawned, 5 skipped, 3 spawned, ...).
///
/// • Spawn Side: Left = only the left base, Right = only the right base, Both = the full deleted pair.
/// • PREFABS ARE ALWAYS TAKEN FROM THE SOURCE - nothing to assign here:
///     base (exterior) prefabs A/T/C/G, interior element prefabs, sugar, phosphate,
///     and the LineRenderer style (material, alignment, caps, dash settings, colours, widths).
///   EXCEPTION: the "Additional Sugar-Side Prefab" is assigned directly on this component.
/// • POSITIONING IS SEPARATE: every position / rotation / scale / distance / line coordinate below
///   belongs to THIS strand only, so it can look different from the source strand.
///
/// NOTE: file name must be "DNADeletedPairBatchSpawner.cs".
/// </summary>
[DisallowMultipleComponent]
public class DNADeletedPairBatchSpawner : MonoBehaviour
{
    public enum SpawnSide { Left, Right, Both }

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

    [Tooltip("Which half of the deleted pair gets spawned. For an A-T pair: Left = A, Right = T, Both = A and T.")]
    public SpawnSide spawnSide = SpawnSide.Both;

    // ------------------------------------------------------------------ Cycle
    [Header("Deletion Cycle")]
    [Tooltip("DELAY: how many deletions are skipped (not spawned) after a spawn batch. Min 1.")]
    public int deletionThreshold = 5;
    [Tooltip("How many consecutive deleted pairs are spawned (1st, 2nd, 3rd deleted pair) before the delay starts. Min 1.")]
    public int spawnBatchSize = 3;

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
    [Tooltip("Extra local rotation of the LEFT base half.")]
    public Vector3 leftHalfRotation = Vector3.zero;
    [Tooltip("Extra local rotation of the RIGHT base half.")]
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

    // ------------------------------------------------------------------ Additional sugar-side prefab
    [Header("Additional Sugar-Side Prefab")]
    [Tooltip("ON = spawn the additional prefab. OFF = never spawned.")]
    public bool spawnAdditionalPrefab = true;
    [Tooltip("Standalone prefab (no line, NOT attached to any pair). It always spawns at the Top Spawn Height. " +
             "Leave empty to skip it.")]
    public GameObject additionalPrefab;
    [Min(1)]
    [Tooltip("One additional prefab is spawned for every N spawned pairs (the first pair of each group). " +
             "3 = one extra beside every group of 3 pairs, 1 = one extra beside every pair.")]
    public int additionalEveryNPairs = 3;
    [Tooltip("Offset from the strand column, in this object's local space: X / Z = how far beside the strand, " +
             "Y = height offset relative to Top Spawn Height. Independent of every other piece.")]
    public Vector3 additionalLocalPosition = new Vector3(1.5f, 0f, 0f);
    [Tooltip("Local rotation (Euler degrees) of the additional prefab.")]
    public Vector3 additionalLocalRotation = Vector3.zero;
    [Tooltip("Local scale of the additional prefab. Independent of every other piece.")]
    public Vector3 additionalLocalScale = Vector3.one;
    [Tooltip("ON = the additional prefab falls at the same speed as the strand. OFF = it stays where it spawned.")]
    public bool additionalFalls = true;

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

    [Header("Debug")]
    [Tooltip("Log every deleted pair received from the source and every pair spawned / skipped.")]
    public bool logSourceBases = false;
    [Tooltip("Read-only counters since Play started.")]
    [DNABatchReadOnly] public int receivedFromSource;
    [DNABatchReadOnly] public int spawnedFromSource;
    [DNABatchReadOnly] public int skippedByDelay;
    [DNABatchReadOnly] public string currentPhase = "Spawning";

    [Header("Options")]
    public bool drawGizmos = true;

    // ------------------------------------------------------------------ Runtime
    private const string ContainerName = "__DNA_BatchSpawned";
    private const string PairPivotName = "__BatchPairPivot";
    private const int MaxSpawnsPerFrame = 32;

    private sealed class PairRuntime
    {
        public Transform pivot;
        public float liveY;
        public int serial;
    }

    private sealed class ExtraRuntime
    {
        public Transform t;
        public float liveY;   // height of the spawn point (without the Y offset)
        public float yOffset;
        public int lastSerial;   // serial of the LAST pair of the group this extra belongs to
    }

    private int _lastDespawnedSerial = -1;

    private readonly List<PairRuntime> _pairs = new List<PairRuntime>();
    private readonly List<ExtraRuntime> _extras = new List<ExtraRuntime>();
    private int _extraCounter;
    // Pairs approved by the cycle, waiting for room at the entry point (FIFO = deletion order).
    // batchStart = first pair of a spawn batch -> it enters at the top height instead of stacking on the previous batch.
    private readonly Queue<(char left, char right, bool batchStart)> _incoming = new Queue<(char, char, bool)>();
    private bool _nextIsBatchStart = true;
    private Transform _container;
    private int _nextSerial;
    private DNAReplicationSimulator _subscribedSource;
    private bool _warnedMissingSource;
    private static Material _fallbackMaterial;

    private bool _spawningPhase = true;
    private int _phaseRemaining;

    public int PendingIncomingCount => _incoming.Count;
    public int ActivePairCount => _pairs.Count;

    public float EffectiveFallSpeed =>
        inheritFallSpeedFromSource && source != null ? source.fallingSpeed : fallSpeed;

    private float EffStepHeight => copySpacingFromSource && source != null ? source.StepHeight : stepHeight;
    private float EffPairsPerStep => Mathf.Max(0.01f, copySpacingFromSource && source != null ? source.pairsPerStep : pairsPerStep);
    private float EffStackGap => copySpacingFromSource && source != null ? source.stackGap : stackGap;

    private Vector3 EffElementScale => matchSourceScales && source != null ? source.elementLocalScale : elementLocalScale;
    private Vector3 EffSugarScale => matchSourceScales && source != null ? source.sugarLocalScale : sugarLocalScale;
    private Vector3 EffPhosphateScale => matchSourceScales && source != null ? source.phosphateLocalScale : phosphateLocalScale;

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
        ElementTweak t = null;
        switch (b)
        {
            case 'A': t = interiorA; break;
            case 'C': t = interiorC; break;
            case 'G': t = interiorG; break;
            case 'T': t = interiorT; break;
        }
        return t ?? new ElementTweak();
    }

    // Strict lookup: unknown letters return null instead of silently becoming G.
    private GameObject GetBasePrefab(char b)
    {
        if (source == null) return null;
        switch (b)
        {
            case 'A': return source.prefabA;
            case 'T': return source.prefabT;
            case 'C': return source.prefabC;
            case 'G': return source.prefabG;
            default: return null;
        }
    }

    private GameObject GetElementPrefab(char b)
    {
        if (source == null) return null;
        switch (b)
        {
            case 'A': return source.elementA;
            case 'T': return source.elementT;
            case 'C': return source.elementC;
            case 'G': return source.elementG;
            default: return null;
        }
    }

    // =================================================================== Unity callbacks
    private void Awake() { ResetCycle(); }
    private void OnEnable() { TrySubscribe(); }
    private void OnDisable() { Unsubscribe(); }
    private void OnDestroy() { Unsubscribe(); }

    private void Start()
    {
        if (!Application.isPlaying) return;

        ClearGenerated();
        receivedFromSource = 0;
        spawnedFromSource = 0;
        skippedByDelay = 0;
        ResetCycle();
        EnsureContainer();
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

        for (int i = _pairs.Count - 1; i >= 0; i--)
        {
            PairRuntime pr = _pairs[i];
            if (pr.pivot == null || pr.liveY < bottomDespawnHeight)
            {
                if (pr.pivot != null) Destroy(pr.pivot.gameObject);
                if (pr.serial > _lastDespawnedSerial) _lastDespawnedSerial = pr.serial;
                _pairs.RemoveAt(i);
            }
        }

        UpdateExtras(speed);

        TryDrainQueue();
    }

    private void UpdateExtras(float speed)
    {
        float dy = additionalFalls ? speed * Time.deltaTime : 0f;
        for (int i = _extras.Count - 1; i >= 0; i--)
        {
            ExtraRuntime ex = _extras[i];
            // Removed only once the LAST pair of its group has been deleted (not the first one).
            if (ex.t == null || _lastDespawnedSerial >= ex.lastSerial)
            {
                if (ex.t != null) Destroy(ex.t.gameObject);
                _extras.RemoveAt(i);
                continue;
            }

            // Never falls below the bottom height while it waits for the rest of its group.
            ex.liveY = Mathf.Max(bottomDespawnHeight, ex.liveY - dy);
            Vector3 p = ex.t.localPosition;
            p.y = ex.liveY + ex.yOffset;
            ex.t.localPosition = p;
        }
    }

    /// <summary>Spawns the standalone extra prefab at the TOP spawn height (not on a falling pair).</summary>
    private void SpawnAdditionalAtTop()
    {
        if (!spawnAdditionalPrefab || additionalPrefab == null || _container == null) return;

        int every = Mathf.Max(1, additionalEveryNPairs);
        bool spawnNow = (_extraCounter % every) == 0;
        _extraCounter++;
        if (!spawnNow) return;

        GameObject go = Instantiate(additionalPrefab, _container);
        go.name = $"{additionalPrefab.name}_Additional_{_nextSerial}";

        go.transform.localPosition = new Vector3(
            spawnPosition.x + additionalLocalPosition.x,
            topSpawnHeight + additionalLocalPosition.y,
            spawnPosition.z + additionalLocalPosition.z);
        go.transform.localRotation = Quaternion.Euler(additionalLocalRotation);
        go.transform.localScale = Vector3.Scale(additionalLocalScale, PivotScale);

        MarkTemporary(go);

        // This call happens right after a pair spawned, so that pair's serial is _nextSerial - 1.
        // The group covers 'every' pairs, so its last pair has serial (_nextSerial - 1) + (every - 1).
        _extras.Add(new ExtraRuntime
        {
            t = go.transform,
            liveY = topSpawnHeight,
            yOffset = additionalLocalPosition.y,
            lastSerial = _nextSerial + every - 2
        });
    }

    private void OnValidate()
    {
        additionalEveryNPairs = Mathf.Max(1, additionalEveryNPairs);
        deletionThreshold = Mathf.Max(1, deletionThreshold);
        spawnBatchSize = Mathf.Max(1, spawnBatchSize);
        stepHeight = Mathf.Max(0.0001f, stepHeight);
        pairsPerStep = Mathf.Max(0.01f, pairsPerStep);
        pairHalfLength = Mathf.Max(0f, pairHalfLength);
        elementDistance = Mathf.Max(0f, elementDistance);
        fallSpeed = Mathf.Max(0f, fallSpeed);
    }

    private void ResetCycle()
    {
        _spawningPhase = true;
        _phaseRemaining = Mathf.Max(1, spawnBatchSize);
        currentPhase = "Spawning";
        _nextIsBatchStart = true;
    }

    // =================================================================== Event wiring
    private void TrySubscribe()
    {
        if (_subscribedSource == source) return;
        Unsubscribe(); // never hold two subscriptions

        if (source == null)
        {
            if (!_warnedMissingSource)
            {
                Debug.LogWarning("DNADeletedPairBatchSpawner: no source DNAReplicationSimulator assigned.", this);
                _warnedMissingSource = true;
            }
            return;
        }

        _warnedMissingSource = false;
        source.PairDeleted += OnSourcePairDeleted;
        _subscribedSource = source;
    }

    private void Unsubscribe()
    {
        if (_subscribedSource == null) return;
        _subscribedSource.PairDeleted -= OnSourcePairDeleted;
        _subscribedSource = null;
    }

    /// <summary>Called once per deleted pair: spawn it (spawning phase) or skip it (delay phase).</summary>
    private void OnSourcePairDeleted(char leftLetter, char rightLetter)
    {
        receivedFromSource++;
        if (_phaseRemaining <= 0) ResetCycle(); // safety (e.g. values edited while playing)

        if (_spawningPhase)
        {
            _incoming.Enqueue((leftLetter, rightLetter, _nextIsBatchStart));
            _nextIsBatchStart = false;
            _phaseRemaining--;
            if (logSourceBases)
                Debug.Log($"Batch spawner: deleted '{leftLetter}-{rightLetter}' -> will spawn " +
                          $"({_phaseRemaining} more before delay).", this);

            if (_phaseRemaining <= 0)
            {
                _spawningPhase = false;
                _phaseRemaining = Mathf.Max(1, deletionThreshold);
                currentPhase = "Delay";
            }
        }
        else
        {
            skippedByDelay++;
            _phaseRemaining--;
            if (logSourceBases)
                Debug.Log($"Batch spawner: deleted '{leftLetter}-{rightLetter}' skipped by delay " +
                          $"({_phaseRemaining} left).", this);

            if (_phaseRemaining <= 0)
            {
                _spawningPhase = true;
                _phaseRemaining = Mathf.Max(1, spawnBatchSize);
                currentPhase = "Spawning";
                _nextIsBatchStart = true;
            }
        }
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
            // First pair of a batch -> always at the top height (a gap to the previous batch is kept).
            // Other pairs of the batch -> exactly one spacing above the highest live pair (never above the top).
            float y = topSpawnHeight;
            if (_pairs.Count > 0)
            {
                float highest = GetHighestY();
                if (highest > topSpawnHeight - spacing) break;   // no room yet - wait, keep deletion order
                y = _incoming.Peek().batchStart
                    ? topSpawnHeight
                    : Mathf.Min(topSpawnHeight, highest + spacing);
            }

            var pair = _incoming.Dequeue();   // FIFO = deletion order
            if (SpawnPair(pair.left, pair.right, y))
            {
                spawnedFromSource++;
                SpawnAdditionalAtTop();
                if (logSourceBases)
                    Debug.Log($"Batch spawner: spawned '{pair.left}-{pair.right}' ({spawnSide}).", this);
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
        bool doLeft = spawnSide == SpawnSide.Left || spawnSide == SpawnSide.Both;
        bool doRight = spawnSide == SpawnSide.Right || spawnSide == SpawnSide.Both;

        // Validate first so a pair is never half-built and a wrong letter is never substituted.
        if (doLeft && GetBasePrefab(leftLetter) == null) { WarnMissing(leftLetter); return false; }
        if (doRight && GetBasePrefab(rightLetter) == null) { WarnMissing(rightLetter); return false; }

        int serial = _nextSerial;

        GameObject pivotGo = new GameObject($"{PairPivotName}_{serial}");
        pivotGo.transform.SetParent(_container, false);
        pivotGo.transform.localPosition = new Vector3(spawnPosition.x, startY, spawnPosition.z);
        pivotGo.transform.localRotation = Quaternion.Euler(baseRotation);
        pivotGo.transform.localScale = PivotScale;

        Material mat = spawnLines ? GetLineMaterial() : null;

        if (doLeft) BuildHalf(pivotGo.transform, leftLetter, true, serial, mat);
        if (doRight) BuildHalf(pivotGo.transform, rightLetter, false, serial, mat);

        if (spawnLines && spawnBondLines && bondLines != null)
        {
            for (int i = 0; i < bondLines.Count; i++)
            {
                var bl = bondLines[i];
                if (bl == null) continue;
                CreateLine(pivotGo.transform, $"__Bond_{serial}_{i}", bl.startA, bl.endB, bl.dashed,
                           source.bondLineColor, source.bondLineWidth, mat);
            }
        }

        MarkTemporary(pivotGo);

        _pairs.Add(new PairRuntime { pivot = pivotGo.transform, liveY = startY, serial = serial });
        _nextSerial++;
        return true;
    }

    private void BuildHalf(Transform pivot, char letter, bool isLeft, int serial, Material mat)
    {
        GameObject prefab = GetBasePrefab(letter);
        float side = isLeft ? -1f : 1f;
        string tag = isLeft ? "L" : "R";

        // --- Base (exterior) half ---
        Vector3 baseLocalPos = Vector3.right * (side * pairHalfLength);
        Quaternion baseLocalRot = Quaternion.Euler(isLeft ? leftHalfRotation : rightHalfRotation);

        GameObject baseGo = Instantiate(prefab, pivot);
        baseGo.name = $"{prefab.name}_{letter}_Pair_{serial}_{tag}";
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
            GameObject sugar = Instantiate(source.prefabSugar, pivot);
            sugar.name = $"{source.prefabSugar.name}_Sugar_{serial}_{tag}";
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
            GameObject phos = Instantiate(source.prefabPhosphate, pivot);
            phos.name = $"{source.prefabPhosphate.name}_Phosphate_{serial}_{tag}";
            phosPos = baseLocalPos + baseLocalRot * phosphateLocalPosition;
            phosRot = baseLocalRot * Quaternion.Euler(phosphateLocalRotation);
            phos.transform.localPosition = phosPos;
            phos.transform.localRotation = phosRot;
            phos.transform.localScale = EffPhosphateScale;
            havePhos = true;
        }

        // --- Lines ---
        if (!spawnLines) return;

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

            CreateLine(pivot, $"__SugarToBase_{serial}_{tag}", a, b, l.dashed, l.color, l.width, mat);
        }

        if (haveSugar && havePhos && source.sugarToPhosphateLine != null && source.sugarToPhosphateLine.enabled)
        {
            var l = source.sugarToPhosphateLine;
            Vector3 a = sugarPos + sugarRot * Vector3.Scale(EffSugarScale, sugarToPhosphateFrom);
            Vector3 b = phosPos + phosRot * Vector3.Scale(EffPhosphateScale, sugarToPhosphateTo);
            CreateLine(pivot, $"__SugarToPhosphate_{serial}_{tag}", a, b, l.dashed, l.color, l.width, mat);
        }
    }

    private void WarnMissing(char letter)
    {
        Debug.LogWarning($"DNADeletedPairBatchSpawner: the source has no prefab for base '{letter}' - pair skipped.", this);
    }

    /// <summary>In edit mode, generated objects are never saved with the scene.</summary>
    private static void MarkTemporary(GameObject root)
    {
        if (Application.isPlaying) return;
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            t.gameObject.hideFlags = HideFlags.DontSave;
    }

    // =================================================================== Lines
    private Material GetLineMaterial()
    {
        if (source != null && source.bondLineMaterial != null) return source.bondLineMaterial;

        if (_fallbackMaterial == null)
        {
            Shader sh = Shader.Find("Sprites/Default");
            if (sh == null) return null;
            _fallbackMaterial = new Material(sh) { name = "DNA_BatchLine_Fallback", hideFlags = HideFlags.DontSave };
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
        _extras.Clear();
        _extraCounter = 0;
        _lastDespawnedSerial = -1;
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
            Gizmos.color = Color.magenta;
            if (spawnSide != SpawnSide.Right)
            {
                Vector3 o = transform.TransformDirection(Quaternion.Euler(baseRotation) * (Vector3.left * pairHalfLength));
                Gizmos.DrawLine(top, top + o);
                Gizmos.DrawSphere(top + o, 0.12f);
            }
            if (spawnSide != SpawnSide.Left)
            {
                Vector3 o = transform.TransformDirection(Quaternion.Euler(baseRotation) * (Vector3.right * pairHalfLength));
                Gizmos.DrawLine(top, top + o);
                Gizmos.DrawSphere(top + o, 0.12f);
            }
        }
    }
}

/// <summary>Shows a field in the Inspector greyed-out (read-only).</summary>
public class DNABatchReadOnlyAttribute : PropertyAttribute { }

#if UNITY_EDITOR
[CustomPropertyDrawer(typeof(DNABatchReadOnlyAttribute))]
public class DNABatchReadOnlyDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        bool prev = GUI.enabled;
        GUI.enabled = false;
        EditorGUI.PropertyField(position, property, label, true);
        GUI.enabled = prev;
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        => EditorGUI.GetPropertyHeight(property, label, true);
}
#endif