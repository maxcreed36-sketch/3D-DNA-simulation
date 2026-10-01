using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Spawns a stacked, twisting DNA strand from prefabs, with optional base pairs (A-T, C-G).
/// Everything is built in this object's local space, so you can move / rotate / scale the object freely.
/// </summary>
[DisallowMultipleComponent]
public class DNAPrefabSpawner : MonoBehaviour
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
    [Tooltip("Extra rotation added for every base pair after the first (multiplied by the pair index). " +
             "Default is 0 on X and Z, 5 on Y.")]
    public Vector3 rotationPerPairStep = new Vector3(0f, 5f, 0f);

    [Header("Pair Rotation (per half, local)")]
    [Tooltip("Extra local rotation on the LEFT half only (relative to the pair pivot).")]
    public Vector3 leftPairRotation = Vector3.zero;
    [Tooltip("Extra local rotation on the RIGHT half only (relative to the pair pivot).")]
    public Vector3 rightPairRotation = Vector3.zero;

    [Tooltip("Shifts the whole pair pattern up, in fractions of a step.")]
    public float pairHeightOffset = 0f;

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

    // Height cache
    private bool _heightValid;
    private int _heightKey;
    private float _cachedHeight = 1f;

    private static readonly Color GizmoPairColor = new Color(1f, 0.2f, 1f); // magenta preview (sequence is random)

    /// <summary>All spawned objects (strand pieces and bases), useful for your simulation code.</summary>
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
            Debug.LogWarning("DNAPrefabSpawner: no strand prefab assigned.", this);
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

        // --- Base pairs ---
        if (spawnBasePairs)
            SpawnBasePairs(step);
    }

    private int TotalPairs => Mathf.Max(1, Mathf.RoundToInt(count * pairsPerStep));

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

    private void SpawnBasePairs(float step)
    {
        int total = TotalPairs;
        int warnedMask = 0;

        for (int p = 0; p < total; p++)
        {
            // Fractional step coordinate along the strand.
            float t = p / pairsPerStep + pairHeightOffset;

            // Random base -> complement. A<->T, C<->G.
            char leftLetter = Bases[Random.Range(0, Bases.Length)];
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
                    Debug.LogWarning($"DNAPrefabSpawner: prefab for base '{missing}' is missing - those pairs are skipped.", this);
                }
                continue;
            }

            Vector3 center = spawnPosition + Vector3.up * (t * step);
            Quaternion pairPivotRot = GetPairPivotRotation(p, t);

            // Pair pivot - rotating it orbits both halves together around the pair center.
            var pivotGo = new GameObject($"{PairPivotName}_{p}");
            pivotGo.transform.SetParent(_container, false);
            pivotGo.transform.SetLocalPositionAndRotation(center, pairPivotRot);
            MarkTemporary(pivotGo);
            spawned.Add(pivotGo);

            PlaceChild(leftPrefab, pivotGo.transform, Vector3.left * pairHalfLength,
                       Quaternion.Euler(leftPairRotation), $"{leftPrefab.name}_Pair_{p}_L");

            PlaceChild(rightPrefab, pivotGo.transform, Vector3.right * pairHalfLength,
                       Quaternion.Euler(rightPairRotation), $"{rightPrefab.name}_Pair_{p}_R");
        }
    }

    private void Place(GameObject source, Vector3 localPos, Quaternion localRot, string objName)
    {
        GameObject go = Instantiate(source, _container);
        go.name = objName;
        go.transform.SetLocalPositionAndRotation(localPos, localRot);
        MarkTemporary(go);
        spawned.Add(go);
    }

    private void PlaceChild(GameObject source, Transform parent, Vector3 localPos, Quaternion localRot, string objName)
    {
        GameObject go = Instantiate(source, parent);
        go.name = objName;
        go.transform.localPosition = localPos;
        go.transform.localRotation = localRot;
        go.transform.localScale = source.transform.localScale;
        MarkTemporary(go);
        spawned.Add(go);
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
        h = Mix(h, spawnBasePairs); h = Mix(h, pairsPerStep);
        h = Mix(h, pairHalfLength); h = Mix(h, followStrandTwist);
        h = Mix(h, basePairRotation); h = Mix(h, rotationPerPairStep);
        h = Mix(h, leftPairRotation); h = Mix(h, rightPairRotation);
        h = Mix(h, pairHeightOffset);
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

    // =================================================================== Gizmos
    private void OnDrawGizmos()
    {
        if (!drawGizmos) return;

        float stepHeight = GetStepHeight();
        float step = stepHeight + stackGap;
        int steps = Mathf.Max(1, count);

        // Strand (green -> cyan) - untouched
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

        // Base pair preview. Sequence is random at spawn time, so use one preview colour.
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