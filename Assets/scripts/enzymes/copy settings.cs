using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Copies the settings of a DNAStraightReplicationSimulator into a DNADeletedPairBatchSpawner.
/// Nothing in either script has to be modified.
///
/// Copied: source reference, size matching, base / interior / sugar  phosphate positions,
///         per-base interior tweaks, lines, spacing, fall speed, bounds, gizmo + log toggles.
///         "Copy Side" (Left / Right) becomes the target's "Spawn Side".
/// NOT copied (they only exist on the target): Deletion Threshold, Spawn Batch Size, counters.
/// NOT copied (they only exist on the straight strand): Initial Random Fill, edit-mode preview.
///
/// Use: add this to any GameObject, assign From + To, then press the "Copy Settings" button
/// (or right-click the component header > Copy Settings). Editor changes support Undo.
/// NOTE: file name must be "DNASettingsCopier.cs".
/// </summary>
[DisallowMultipleComponent]
public class DNASettingsCopier : MonoBehaviour
{
    [Header("Copy From -> To")]
    [Tooltip("The straight strand whose settings are read.")]
    public DNAStraightReplicationSimulator from;
    [Tooltip("The batch spawner that receives the settings.")]
    public DNADeletedPairBatchSpawner to;

    [Header("What to copy")]
    public bool copySourceReference = true;
    [Tooltip("Copy Side (Left / Right) -> Spawn Side.")]
    public bool copySide = true;
    public bool copySizeMatching = true;
    public bool copyBasePosition = true;
    public bool copyInterior = true;
    public bool copySugarAndPhosphate = true;
    public bool copyLines = true;
    public bool copySpacingAndFall = true;
    public bool copyBounds = true;
    public bool copyDebugAndGizmos = true;

    [Header("Options")]
    [Tooltip("Also copy automatically when Play starts.")]
    public bool copyOnStart = false;
    public bool logResult = true;

    private void Start()
    {
        if (copyOnStart && Application.isPlaying) CopySettings();
    }

    [ContextMenu("Copy Settings")]
    public void CopySettings()
    {
        if (from == null || to == null)
        {
            Debug.LogWarning("DNASettingsCopier: assign both 'From' and 'To'.", this);
            return;
        }
        if (from == (Object)to)
        {
            Debug.LogWarning("DNASettingsCopier: 'From' and 'To' must be different components.", this);
            return;
        }

#if UNITY_EDITOR
        if (!Application.isPlaying) Undo.RecordObject(to, "Copy DNA Settings");
#endif

        if (copySourceReference) to.source = from.source;

        if (copySide)
            to.spawnSide = from.copySide == DNAStraightReplicationSimulator.CopySide.Left
                ? DNADeletedPairBatchSpawner.SpawnSide.Left
                : DNADeletedPairBatchSpawner.SpawnSide.Right;

        if (copySizeMatching)
        {
            to.matchSourceScales = from.matchSourceScales;
            to.matchSourceWorldScale = from.matchSourceWorldScale;
        }

        if (copyBasePosition)
        {
            to.spawnPosition = from.spawnPosition;
            to.baseRotation = from.baseRotation;
            to.pairHalfLength = from.pairHalfLength;
            to.leftHalfRotation = from.leftHalfRotation;
            to.rightHalfRotation = from.rightHalfRotation;
        }

        if (copyInterior)
        {
            to.spawnElements = from.spawnElements;
            to.elementDistance = from.elementDistance;
            to.elementLocalPosition = from.elementLocalPosition;
            to.elementLocalRotation = from.elementLocalRotation;
            to.elementLocalScale = from.elementLocalScale;
            to.interiorA = CloneTweak(from.interiorA);
            to.interiorC = CloneTweak(from.interiorC);
            to.interiorG = CloneTweak(from.interiorG);
            to.interiorT = CloneTweak(from.interiorT);
        }

        if (copySugarAndPhosphate)
        {
            to.spawnSugar = from.spawnSugar;
            to.sugarLocalPosition = from.sugarLocalPosition;
            to.sugarLocalRotation = from.sugarLocalRotation;
            to.sugarLocalScale = from.sugarLocalScale;

            to.spawnPhosphate = from.spawnPhosphate;
            to.phosphateLocalPosition = from.phosphateLocalPosition;
            to.phosphateLocalRotation = from.phosphateLocalRotation;
            to.phosphateLocalScale = from.phosphateLocalScale;
        }

        if (copyLines)
        {
            to.spawnLines = from.spawnLines;
            to.sugarToBaseFrom = from.sugarToBaseFrom;
            to.sugarToBaseTo = from.sugarToBaseTo;
            to.sugarToBaseSnapToSurface = from.sugarToBaseSnapToSurface;
            to.sugarToPhosphateFrom = from.sugarToPhosphateFrom;
            to.sugarToPhosphateTo = from.sugarToPhosphateTo;
            to.spawnBondLines = from.spawnBondLines;
            to.bondLines = CloneBondLines(from.bondLines);
        }

        if (copySpacingAndFall)
        {
            to.copySpacingFromSource = from.copySpacingFromSource;
            to.stepHeight = from.stepHeight;
            to.pairsPerStep = from.pairsPerStep;
            to.stackGap = from.stackGap;
            to.inheritFallSpeedFromSource = from.inheritFallSpeedFromSource;
            to.fallSpeed = from.fallSpeed;
        }

        if (copyBounds)
        {
            to.topSpawnHeight = from.topSpawnHeight;
            to.bottomDespawnHeight = from.bottomDespawnHeight;
        }

        if (copyDebugAndGizmos)
        {
            to.logSourceBases = from.logSourceBases;
            to.drawGizmos = from.drawGizmos;
        }

#if UNITY_EDITOR
        if (!Application.isPlaying) EditorUtility.SetDirty(to);
#endif

        if (logResult)
            Debug.Log($"DNASettingsCopier: copied settings from '{from.name}' to '{to.name}'.", this);
    }

    // Deep copies so the two components never share the same list / object instances.
    private static DNADeletedPairBatchSpawner.ElementTweak CloneTweak(DNAStraightReplicationSimulator.ElementTweak t)
    {
        var c = new DNADeletedPairBatchSpawner.ElementTweak();
        if (t != null)
        {
            c.positionOffset = t.positionOffset;
            c.rotationOffset = t.rotationOffset;
        }
        return c;
    }

    private static List<DNAReplicationSimulator.BondLine> CloneBondLines(List<DNAReplicationSimulator.BondLine> src)
    {
        var list = new List<DNAReplicationSimulator.BondLine>();
        if (src == null) return list;

        for (int i = 0; i < src.Count; i++)
        {
            var b = src[i];
            if (b == null) { list.Add(null); continue; }
            list.Add(new DNAReplicationSimulator.BondLine(b.startA, b.endB, b.dashed));
        }
        return list;
    }
}

#if UNITY_EDITOR
[CustomEditor(typeof(DNASettingsCopier))]
public class DNASettingsCopierEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        EditorGUILayout.Space();

        var copier = (DNASettingsCopier)target;
        using (new EditorGUI.DisabledScope(copier.from == null || copier.to == null))
        {
            if (GUILayout.Button("Copy Settings  (From -> To)", GUILayout.Height(28)))
                copier.CopySettings();
        }
    }
}
#endif