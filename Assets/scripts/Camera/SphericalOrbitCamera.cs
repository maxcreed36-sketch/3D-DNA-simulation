using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Serialization;
using UnityEngine.UI;

/// <summary>
/// Orbit + zoom camera for PC (mouse) and phone (touch) with an INVISIBLE touch sphere.
/// Gestures only control the camera when they START on the sphere around the target.
///
/// MODES (one-finger drag / left-drag):
///   OrbitAroundObject : drag = orbit around the object
///   DragToZoom        : drag up or right = zoom in, drag down or left = zoom out
///   RollCamera        : drag left/right = roll (tilt) the camera around its own Z axis
///   MoveXY            : drag = move the view along the screen X and Y axes, kept inside the sphere.
///                       A wire cube gizmo around the target shows (and limits) the move area.
///                       You can change its size in the Inspector.
///
/// Always available in both modes:
///   PC:    scroll wheel = zoom, middle-drag = pan
///   Phone: two-finger pinch = zoom
///
/// Switch mode: Inspector, the on-screen buttons, keyboard key (PC), or the public functions
/// SetModeOrbit(), SetModeDragZoom(), SetModeRoll(), SetModeMove(), ToggleMode() (hook them to UI Buttons).
/// Unity 2022.3+, built-in (old) Input Manager.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
public class SphereTouchOrbitCamera : MonoBehaviour
{
    public enum InteractionMode { OrbitAroundObject, DragToZoom, RollCamera, MoveXY }
    public enum TouchDetection { TargetObject, InvisibleSphere, Anywhere }

    // ------------------------------------------------------------ Mode
    [Header("Selection Mode")]
    [SerializeField] private InteractionMode mode = InteractionMode.OrbitAroundObject;

    [Tooltip("Draw two small buttons on screen to switch mode (simple, no Canvas needed).")]
    [SerializeField] private bool showOnScreenModeButtons = true;

    [Tooltip("PC shortcut to toggle the mode. None = disabled.")]
    [SerializeField] private KeyCode toggleModeKey = KeyCode.Tab;

    // ------------------------------------------------------------ Target
    [Header("Target")]
    [SerializeField] private Transform target;

    [Tooltip("Offset of the orbit center from the target pivot (target local space).")]
    [SerializeField] private Vector3 orbitCenterOffset = Vector3.zero;

    // ------------------------------------------------------------ Touch sphere
    [Header("Touch Detection")]
    [Tooltip("TargetObject: gesture must start ON the target object itself (its colliders, or its renderer bounds if it has none).\n" +
             "InvisibleSphere: gesture must start on the invisible sphere.\n" +
             "Anywhere: gesture works anywhere on screen.")]
    [SerializeField] private TouchDetection touchDetection = TouchDetection.TargetObject;

    [Tooltip("Finger forgiveness in pixels for TargetObject mode (touch only). 0 = the finger must be exactly on the object.")]
    [SerializeField, Min(0f)] private float targetTouchTolerancePixels = 8f;

    [Header("Invisible Sphere (only used by InvisibleSphere detection)")]
    [Tooltip("Radius of the invisible sphere in world units.")]
    [SerializeField, Min(0.01f)] private float sphereRadius = 2f;

    [Tooltip("Size the sphere automatically from the target's renderers (overrides Sphere Radius).")]
    [SerializeField] private bool autoSphereFromBounds = false;

    [SerializeField, Min(1f)] private float autoSpherePadding = 1.1f;

    [Tooltip("Minimum size of the touch area on screen, as a fraction of screen height (radius). " +
             "Keeps the object touchable when you zoom far out and the sphere becomes tiny. 0 = off.")]
    [SerializeField, Range(0f, 0.5f)] private float minTouchRadiusScreenFraction = 0.12f;

    [Tooltip("Show the sphere as a wire gizmo (Scene view, or Game view with Gizmos enabled).")]
    [SerializeField] private bool drawSphereGizmo = true;

    // ------------------------------------------------------------ Orbit
    [Header("Orbit Rotation")]
    [SerializeField, Min(0f)] private float orbitSensitivity = 1f;
    [Tooltip("Degrees per pixel of mouse movement.")]
    [SerializeField, Min(0f)] private float mouseSensitivity = 0.25f;
    [Tooltip("Degrees per pixel of finger movement.")]
    [SerializeField, Min(0f)] private float touchSensitivity = 0.2f;
    [Tooltip("0 = instant. 10-20 feels smooth.")]
    [SerializeField, Min(0f)] private float rotationSmoothing = 18f;
    [SerializeField] private bool invertHorizontal = false;
    [SerializeField] private bool invertVertical = false;

    // ------------------------------------------------------------ Zoom
    [Header("Zoom / Orbit Size")]
    [Tooltip("OFF: sizes below are world units (distance from the orbit center).\n" +
             "ON: sizes below are multipliers of the touch sphere radius, so they follow the object's size.")]
    [SerializeField] private bool orbitSizeRelativeToSphere = false;
    [Tooltip("Starting orbit size (camera distance from the orbit center).")]
    [FormerlySerializedAs("initialDistance")]
    [SerializeField, Min(0.01f)] private float initialOrbitSize = 5f;
    [Tooltip("Closest the camera can get (min orbit size).")]
    [FormerlySerializedAs("minDistance")]
    [SerializeField, Min(0.01f)] private float minOrbitSize = 1f;
    [Tooltip("Farthest the camera can get (max orbit size).")]
    [FormerlySerializedAs("maxDistance")]
    [SerializeField, Min(0.01f)] private float maxOrbitSize = 20f;
    [Tooltip("Mouse wheel strength (percentage per notch).")]
    [SerializeField, Min(0f)] private float zoomSensitivity = 0.12f;
    [Tooltip("Pinch strength. 1 = 1:1.")]
    [SerializeField, Min(0f)] private float pinchZoomSensitivity = 1f;
    [SerializeField, Min(0f)] private float zoomSmoothing = 14f;

    // ------------------------------------------------------------ Touch feel
    [Header("Touch Feel (Phone)")]
    [Tooltip("Multiplies finger orbit AND finger drag-zoom speed. Raise it if touch feels slow.")]
    [SerializeField, Min(0.1f)] private float touchSensitivityBoost = 2.5f;

    [Tooltip("Makes the touch area bigger than the visible sphere gizmo. 1 = exact sphere, 1.5 = 50% bigger. " +
             "Raise it if touches on the object often do nothing.")]
    [SerializeField, Min(1f)] private float touchHitAreaScale = 1.5f;

    [Tooltip("Rotation/zoom smoothing used while a finger is down. 0 = instant (most responsive).")]
    [SerializeField, Min(0f)] private float touchSmoothing = 0f;

    // ------------------------------------------------------------ Drag zoom mode
    [Header("Drag To Zoom Mode")]
    [Tooltip("Zoom per pixel of mouse drag (exponential, so it feels even at any distance).")]
    [SerializeField, Min(0f)] private float mouseDragZoomSensitivity = 0.005f;
    [Tooltip("Zoom per pixel of finger drag.")]
    [SerializeField, Min(0f)] private float touchDragZoomSensitivity = 0.006f;
    [Tooltip("Off: drag up/right = zoom in, down/left = zoom out. On: reversed.")]
    [SerializeField] private bool invertDragZoom = false;

    // ------------------------------------------------------------ Roll mode
    [Header("Roll Camera Mode (Z axis)")]
    [Tooltip("Roll speed multiplier. Uses the same degrees-per-pixel as orbit (mouse or touch).")]
    [SerializeField, Min(0f)] private float rollSensitivity = 1f;
    [Tooltip("Off: dragging right turns the picture clockwise. On: reversed.")]
    [SerializeField] private bool invertRoll = false;

    // ------------------------------------------------------------ Pan / Move
    [Header("Move X/Y (Move mode + Middle Mouse)")]
    [Tooltip("Allows middle-mouse panning on PC (Move mode works regardless).")]
    [SerializeField] private bool enablePan = true;
    [Tooltip("1 = the scene follows the finger/cursor exactly.")]
    [SerializeField, Min(0f)] private float panSensitivity = 1f;
    [Tooltip("The orbit center can only move while it stays INSIDE the sphere around the target.")]
    [SerializeField] private bool keepMoveInsideSphere = true;
    [Tooltip("How far the center may travel, as a fraction of the sphere radius. 1 = up to the sphere surface.")]
    [SerializeField, Range(0.05f, 1f)] private float moveLimitOfSphereRadius = 1f;
    [Tooltip("Extra hard limit in world units. 0 = none.")]
    [SerializeField, Min(0f)] private float maxPanDistance = 0f;

    // ------------------------------------------------------------ Move cube gizmo
    [Header("Move Cube Gizmo (around the target)")]
    [Tooltip("Cube size in world units (X, Y, Z). Used when Auto Size is off.")]
    [SerializeField] private Vector3 moveCubeSize = new Vector3(3f, 3f, 3f);

    [Tooltip("Size the cube automatically from the target's renderers (overrides Move Cube Size).")]
    [SerializeField] private bool moveCubeAutoSize = false;
    [SerializeField, Min(1f)] private float moveCubeAutoPadding = 1.2f;

    [Tooltip("The move (X/Y) cannot leave the cube. Overrides the sphere limit while ON.")]
    [SerializeField] private bool moveCubeLimitsMovement = true;

    [Tooltip("Draw the cube in the Scene view (and Game view with Gizmos on).")]
    [SerializeField] private bool drawMoveCubeGizmo = true;

    [SerializeField] private Color moveCubeColor = new Color(1f, 0.85f, 0.2f, 1f);

    // ------------------------------------------------------------ Origin gizmo
    [Header("Origin Gizmo")]
    [Tooltip("Draw X (red), Y (green), Z (blue) axes at the target's origin (its pivot).")]
    [SerializeField] private bool drawTargetOriginGizmo = true;

    [Tooltip("ON: axes follow the target's rotation. OFF: axes stay aligned to the world.")]
    [SerializeField] private bool targetOriginUsesLocalAxes = true;

    [Tooltip("Also draw the axes at the world origin (0, 0, 0).")]
    [SerializeField] private bool drawWorldOriginGizmo = false;

    [Tooltip("Length of each axis in world units.")]
    [SerializeField, Min(0.01f)] private float originGizmoSize = 1f;

    // ------------------------------------------------------------ Start view
    [Header("Initial / Reset View")]
    [Tooltip("ON: the camera keeps the position and rotation you placed it at in the editor. " +
             "Start Yaw / Pitch / Orbit Size below are ignored at start. Reset View returns to that pose.\n" +
             "OFF: the camera is moved to the Initial Yaw / Pitch / Orbit Size when the game starts.")]
    [SerializeField] private bool useCurrentCameraPoseOnStart = true;
    [SerializeField] private float initialYaw = 0f;
    [SerializeField] private float initialPitch = 20f;

    // ------------------------------------------------------------ Auto fit
    [Header("Auto Fit (optional)")]
    [Tooltip("Derive start/min/max distance and orbit center from the target's size.")]
    [SerializeField] private bool autoFitToTargetBounds = false;
    [SerializeField, Min(1f)] private float fitPadding = 1.25f;
    [SerializeField, Min(0.1f)] private float autoFitMinDistanceFactor = 1f;
    [SerializeField, Min(1f)] private float autoFitMaxDistanceMultiplier = 4f;

    // ------------------------------------------------------------ Input
    [Header("Input")]
    [Tooltip("Ignore gestures that start on a UI element (needs an EventSystem).")]
    [SerializeField] private bool blockInputOverUI = true;

    // ------------------------------------------------------------ Runtime state
    private Camera cam;
    private Transform initializedTarget;
    private bool warnedNoTarget;

    private Quaternion targetRotation = Quaternion.identity;
    private Quaternion smoothedRotation = Quaternion.identity;
    private float targetDistance, smoothedDistance;
    private Vector3 targetPan, smoothedPan;
    private float yawSign = 1f;
    private Quaternion startRotation = Quaternion.identity;
    private float startDistance;
    private Vector3 startPan;

    private float minDist, maxDist, initDist;
    private Vector3 runtimeAutoOffset;     // local-space center offset from auto fit
    private float runtimeSphereRadius;
    private Renderer[] targetRenderers = new Renderer[0];
    private bool targetHasColliders;

    // Mouse
    private Vector2 lastMousePos;
    private bool mouseDragging, mousePanning;

    // Touch
    private enum TouchMode { None, Drag, Pinch }
    private TouchMode touchMode = TouchMode.None;
    private int trackedId0 = -1, trackedId1 = -1;
    private Vector2 lastTouchPos0;
    private float lastPinchDistance;
    private bool pinchAllowed;
    private readonly List<int> uiBlockedFingers = new List<int>(10);
    private readonly List<int> offSphereFingers = new List<int>(10);

    // UI raycast helpers
    private readonly List<RaycastResult> uiHits = new List<RaycastResult>(16);
    private PointerEventData pointerData;
    private EventSystem pointerDataOwner;

    private Vector3 autoMoveCubeSize;   // move cube size derived from the target's bounds

    // On-screen mode buttons
    private GUIStyle buttonStyle;

    // ============================================================ Unity messages
    private void Awake()
    {
        cam = GetComponent<Camera>();
    }

    private void OnValidate()
    {
        minOrbitSize = Mathf.Max(0.01f, minOrbitSize);
        maxOrbitSize = Mathf.Max(minOrbitSize, maxOrbitSize);
        initialOrbitSize = Mathf.Clamp(initialOrbitSize, minOrbitSize, maxOrbitSize);
    }

    private void OnEnable()
    {
        lastMousePos = Input.mousePosition;
    }

    private void Update()
    {
        if (!EnsureInitialized()) return;

        if (toggleModeKey != KeyCode.None && Input.GetKeyDown(toggleModeKey)) ToggleMode();

        // Touch has priority; also stops touch-simulated mouse events from double-driving the camera.
        if (Input.touchCount > 0)
        {
            mouseDragging = mousePanning = false;
            HandleTouch();
        }
        else
        {
            ResetTouchState();
            HandleMouse();
        }

        lastMousePos = Input.mousePosition;
    }

    private void LateUpdate()
    {
        if (!EnsureInitialized()) return;

        float dt = Time.unscaledDeltaTime;
        bool touching = Input.touchCount > 0;
        float rotT = SmoothFactor(touching ? touchSmoothing : rotationSmoothing, dt);
        float zoomT = SmoothFactor(touching ? touchSmoothing : zoomSmoothing, dt);

        smoothedRotation = rotT >= 1f ? targetRotation : Quaternion.Slerp(smoothedRotation, targetRotation, rotT);
        smoothedDistance = Mathf.Lerp(smoothedDistance, targetDistance, zoomT);
        smoothedPan = Vector3.Lerp(smoothedPan, targetPan, rotT);

        ApplyPose();
    }

    // ============================================================ Public API
    public InteractionMode CurrentMode => mode;

    /// <summary>Drag = orbit around the object. Hook to a UI Button.</summary>
    public void SetModeOrbit() { SetMode(InteractionMode.OrbitAroundObject); }

    /// <summary>Drag up/down/left/right = zoom in/out. Hook to a UI Button.</summary>
    public void SetModeDragZoom() { SetMode(InteractionMode.DragToZoom); }

    /// <summary>Drag left/right = roll the camera around its Z axis. Hook to a UI Button.</summary>
    public void SetModeRoll() { SetMode(InteractionMode.RollCamera); }

    /// <summary>Drag = move the view along X and Y (inside the sphere). Hook to a UI Button.</summary>
    public void SetModeMove() { SetMode(InteractionMode.MoveXY); }

    /// <summary>Cycles Rotate -> Zoom -> Roll -> Move -> Rotate. Hook to a UI Button.</summary>
    public void ToggleMode()
    {
        switch (mode)
        {
            case InteractionMode.OrbitAroundObject: SetMode(InteractionMode.DragToZoom); break;
            case InteractionMode.DragToZoom:        SetMode(InteractionMode.RollCamera); break;
            case InteractionMode.RollCamera:        SetMode(InteractionMode.MoveXY); break;
            default:                                SetMode(InteractionMode.OrbitAroundObject); break;
        }
    }

    public void SetMode(InteractionMode newMode)
    {
        mode = newMode;
        // Stop any gesture in progress so the camera never jumps when the mode changes.
        mouseDragging = false;
        ResetTouchState();
    }

    /// <summary>Smoothly returns to the initial view. Hook to a UI Button.</summary>
    public void ResetView()
    {
        if (!EnsureInitialized()) return;
        targetRotation = startRotation;
        targetDistance = startDistance;
        targetPan = startPan;
        yawSign = 1f;
    }

    /// <summary>Instantly snaps to the initial view.</summary>
    public void ResetViewInstant()
    {
        if (!EnsureInitialized()) return;
        ResetView();
        smoothedRotation = targetRotation;
        smoothedDistance = targetDistance;
        smoothedPan = targetPan;
        ApplyPose();
    }

    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
        initializedTarget = null;
        EnsureInitialized();
    }

    // ============================================================ Initialization
    private bool EnsureInitialized()
    {
        if (target == null)
        {
            if (!warnedNoTarget)
            {
                Debug.LogWarning("[SphereTouchOrbitCamera] No target assigned.", this);
                warnedNoTarget = true;
            }
            initializedTarget = null;
            return false;
        }

        warnedNoTarget = false;
        if (target != initializedTarget) Initialize();
        return true;
    }

    private void Initialize()
    {
        if (cam == null) cam = GetComponent<Camera>();
        initializedTarget = target;
        runtimeAutoOffset = Vector3.zero;

        runtimeSphereRadius = sphereRadius;

        targetRenderers = target.GetComponentsInChildren<Renderer>();
        Collider[] cols = target.GetComponentsInChildren<Collider>();
        targetHasColliders = false;
        for (int i = 0; i < cols.Length; i++)
            if (cols[i].enabled && !cols[i].isTrigger) { targetHasColliders = true; break; }

        Bounds b;
        bool hasBounds = TryGetTargetBounds(out b);
        autoMoveCubeSize = hasBounds ? b.size * moveCubeAutoPadding : Vector3.zero;

        if (autoSphereFromBounds && hasBounds)
            runtimeSphereRadius = Mathf.Max(b.extents.magnitude * autoSpherePadding, 0.01f);

        // Orbit sizes: world units, or multipliers of the sphere radius.
        float unit = orbitSizeRelativeToSphere ? runtimeSphereRadius : 1f;
        minDist = Mathf.Max(minOrbitSize * unit, 0.01f);
        maxDist = Mathf.Max(maxOrbitSize * unit, minDist);
        initDist = initialOrbitSize * unit;

        // Auto fit (if enabled) overrides the orbit sizes.
        if (autoFitToTargetBounds && hasBounds) ApplyAutoFit(b);

        initDist = Mathf.Clamp(initDist, minDist, maxDist);

        if (useCurrentCameraPoseOnStart)
        {
            // Rebuild the orbit state from where the camera already is, so nothing moves on start.
            Quaternion rot = transform.rotation;
            Vector3 basePivot = GetBasePivot();
            Vector3 fwd = rot * Vector3.forward;

            float d = Vector3.Dot(basePivot - transform.position, fwd);
            if (d < 0.01f) d = Mathf.Max(0.01f, Vector3.Distance(transform.position, basePivot));

            // If the camera isn't aimed exactly at the target, the leftover offset becomes the pan,
            // so the exact same position and rotation are reproduced.
            Vector3 viewCenter = transform.position + fwd * d;
            startRotation = rot;
            startDistance = d;
            startPan = viewCenter - basePivot;

            // Make sure the start distance is inside the limits, so the first zoom doesn't snap.
            minDist = Mathf.Min(minDist, d);
            maxDist = Mathf.Max(maxDist, d);
        }
        else
        {
            startRotation = Quaternion.Euler(initialPitch, initialYaw, 0f);
            startDistance = initDist;
            startPan = Vector3.zero;
        }

        targetRotation = smoothedRotation = startRotation;
        targetDistance = smoothedDistance = startDistance;
        targetPan = smoothedPan = startPan;
        yawSign = 1f;
        ApplyPose();
    }

    private bool TryGetTargetBounds(out Bounds bounds)
    {
        Renderer[] renderers = target.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
        {
            bounds = new Bounds(target.position, Vector3.one);
            return false;
        }
        bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        return true;
    }

    private void ApplyAutoFit(Bounds b)
    {
        runtimeAutoOffset = target.InverseTransformVector(b.center - target.position);

        float radius = Mathf.Max(b.extents.magnitude, 0.01f);

        float vHalf = cam.orthographic ? 30f * Mathf.Deg2Rad : cam.fieldOfView * 0.5f * Mathf.Deg2Rad;
        float hHalf = Mathf.Atan(Mathf.Tan(vHalf) * Mathf.Max(cam.aspect, 0.01f));
        float half = Mathf.Min(vHalf, hHalf);

        float fitDistance = radius / Mathf.Sin(half) * fitPadding;
        minDist = radius * autoFitMinDistanceFactor;
        maxDist = Mathf.Max(fitDistance * autoFitMaxDistanceMultiplier, minDist + 0.01f);
        initDist = fitDistance;
    }

    // ============================================================ Touch sphere
    private Vector3 SphereCenter()
    {
        return GetBasePivot() + smoothedPan;
    }

    /// <summary>Does a gesture starting at this screen point count? Depends on Touch Detection.</summary>
    private bool HitsTarget(Vector2 screenPos, bool isTouch = false)
    {

        switch (touchDetection)
        {
            case TouchDetection.Anywhere:
                return true;
            case TouchDetection.InvisibleSphere:
                return HitsInvisibleSphere(screenPos, isTouch ? touchHitAreaScale : 1f);
            default:
                return HitsTargetObject(screenPos, isTouch);
        }
    }

    /// <summary>True if the point is on the target object (finger tolerance for touch).</summary>
    private bool HitsTargetObject(Vector2 screenPos, bool isTouch)
    {
        if (RayHitsTargetObject(screenPos)) return true;

        float tol = isTouch ? targetTouchTolerancePixels : 0f;
        if (tol <= 0f) return false;

        return RayHitsTargetObject(screenPos + new Vector2(tol, 0f))
            || RayHitsTargetObject(screenPos + new Vector2(-tol, 0f))
            || RayHitsTargetObject(screenPos + new Vector2(0f, tol))
            || RayHitsTargetObject(screenPos + new Vector2(0f, -tol));
    }

    private bool RayHitsTargetObject(Vector2 screenPos)
    {
        Ray ray = cam.ScreenPointToRay(screenPos);

        // 1) Real colliders on the target (accurate, and blocked by objects in front of it).
        if (targetHasColliders)
        {
            RaycastHit hit;
            if (Physics.Raycast(ray, out hit, Mathf.Infinity, ~0, QueryTriggerInteraction.Ignore))
                return hit.collider.transform.IsChildOf(target);
            return false;
        }

        // 2) No colliders: fall back to the renderers' bounding boxes.
        float dist;
        for (int i = 0; i < targetRenderers.Length; i++)
        {
            Renderer r = targetRenderers[i];
            if (r != null && r.enabled && r.bounds.IntersectRay(ray, out dist)) return true;
        }
        return false;
    }

    /// <summary>Ray vs invisible sphere. Pure math, no physics.</summary>
    private bool HitsInvisibleSphere(Vector2 screenPos, float radiusScale)
    {

        // Screen-space minimum: when zoomed far out the sphere shrinks to a few pixels,
        // so anything within this many pixels of the object's center still counts.
        if (minTouchRadiusScreenFraction > 0f)
        {
            Vector3 sp = cam.WorldToScreenPoint(SphereCenter());
            if (sp.z > 0f)
            {
                float minPixels = Screen.height * minTouchRadiusScreenFraction;
                if ((screenPos - new Vector2(sp.x, sp.y)).sqrMagnitude <= minPixels * minPixels)
                    return true;
            }
        }

        float radius = runtimeSphereRadius * radiusScale;

        Ray ray = cam.ScreenPointToRay(screenPos);
        Vector3 oc = ray.origin - SphereCenter();
        float b = Vector3.Dot(oc, ray.direction);
        float c = Vector3.Dot(oc, oc) - radius * radius;
        float disc = b * b - c;
        if (disc < 0f) return false;

        // Far intersection must be in front of the ray (works when the camera is inside the sphere too).
        return -b + Mathf.Sqrt(disc) >= 0f;
    }

    // ============================================================ Mode actions
    /// <summary>One-finger / left-drag action, depends on the current mode.</summary>
    private void ApplyDrag(Vector2 deltaPixels, float orbitDeviceSensitivity, float zoomDeviceSensitivity)
    {
        switch (mode)
        {
            case InteractionMode.OrbitAroundObject: Orbit(deltaPixels, orbitDeviceSensitivity); break;
            case InteractionMode.DragToZoom:        DragZoom(deltaPixels, zoomDeviceSensitivity); break;
            case InteractionMode.RollCamera:        Roll(deltaPixels, orbitDeviceSensitivity); break;
            case InteractionMode.MoveXY:            Pan(deltaPixels); break;
        }
    }

    /// <summary>Rolls the camera around its own forward (Z) axis. The camera position doesn't change.</summary>
    private void Roll(Vector2 deltaPixels, float deviceSensitivity)
    {
        if (Mathf.Abs(deltaPixels.x) < 1e-3f) return;

        float degrees = deltaPixels.x * deviceSensitivity * rollSensitivity * (invertRoll ? -1f : 1f);

        // Unity is left-handed: a positive angle turns the camera clockwise (the picture counter-clockwise),
        // so negate it to make "drag right" turn the picture clockwise.
        targetRotation = Quaternion.Normalize(targetRotation * Quaternion.AngleAxis(-degrees, Vector3.forward));
    }

    /// <summary>Up or right = zoom in. Down or left = zoom out.</summary>
    private void DragZoom(Vector2 deltaPixels, float deviceSensitivity)
    {
        if (deltaPixels.sqrMagnitude < 1e-6f) return;

        // Up and right both push the value positive, down and left push it negative.
        float amount = deltaPixels.x + deltaPixels.y;
        if (invertDragZoom) amount = -amount;

        ZoomByFactor(Mathf.Exp(-amount * deviceSensitivity));
    }

    // ============================================================ Input: mouse
    private void HandleMouse()
    {
        Vector2 pos = Input.mousePosition;
        Vector2 delta = pos - lastMousePos;

        if (Input.GetMouseButtonDown(0))
        {
            mouseDragging = !IsOverUI(pos) && HitsTarget(pos);
            delta = Vector2.zero;
        }
        if (Input.GetMouseButtonDown(2))
        {
            mousePanning = enablePan && !IsOverUI(pos) && HitsTarget(pos);
            delta = Vector2.zero;
        }
        if (!Input.GetMouseButton(0)) mouseDragging = false;
        if (!Input.GetMouseButton(2)) mousePanning = false;

        if (mouseDragging) ApplyDrag(delta, mouseSensitivity, mouseDragZoomSensitivity);
        if (mousePanning) Pan(delta);

        // Scroll zoom: only while the pointer is on the sphere.
        float scroll = Input.mouseScrollDelta.y;
        if (Mathf.Abs(scroll) > Mathf.Epsilon && !IsOverUI(pos) && HitsTarget(pos))
            ZoomByFactor(Mathf.Exp(-scroll * zoomSensitivity));
    }

    // ============================================================ Input: touch
    private void ResetTouchState()
    {
        touchMode = TouchMode.None;
        trackedId0 = trackedId1 = -1;
        lastPinchDistance = 0f;
        pinchAllowed = false;
        uiBlockedFingers.Clear();
        offSphereFingers.Clear();
    }

    private void HandleTouch()
    {
        int eligible = 0;
        int id0 = -1, id1 = -1;
        Vector2 p0 = Vector2.zero, p1 = Vector2.zero;

        int count = Input.touchCount;
        for (int i = 0; i < count; i++)
        {
            Touch t = Input.GetTouch(i);

            if (t.phase == TouchPhase.Began)
            {
                if (IsOverUI(t.position)) uiBlockedFingers.Add(t.fingerId);
                else if (!HitsTarget(t.position, true)) offSphereFingers.Add(t.fingerId);
            }

            if (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled)
            {
                uiBlockedFingers.Remove(t.fingerId);
                offSphereFingers.Remove(t.fingerId);
                continue;
            }
            if (uiBlockedFingers.Contains(t.fingerId)) continue;

            if (eligible == 0) { id0 = t.fingerId; p0 = t.position; }
            else if (eligible == 1) { id1 = t.fingerId; p1 = t.position; }
            eligible++;
        }

        TouchMode tm = eligible == 0 ? TouchMode.None : (eligible == 1 ? TouchMode.Drag : TouchMode.Pinch);
        float pinchDist = tm == TouchMode.Pinch ? (p1 - p0).magnitude : 0f;

        bool sameGesture = tm == touchMode && id0 == trackedId0 && (tm != TouchMode.Pinch || id1 == trackedId1);

        // A new pinch is allowed if its midpoint or either finger is on the sphere.
        if (!sameGesture && tm == TouchMode.Pinch)
        {
            pinchAllowed = touchDetection == TouchDetection.Anywhere
                           || HitsTarget((p0 + p1) * 0.5f, true)
                           || !offSphereFingers.Contains(id0)
                           || !offSphereFingers.Contains(id1);
        }

        if (sameGesture)
        {
            if (tm == TouchMode.Drag)
            {
                if (!offSphereFingers.Contains(id0))
                    ApplyDrag(p0 - lastTouchPos0, touchSensitivity * touchSensitivityBoost, touchDragZoomSensitivity * touchSensitivityBoost);
            }
            else if (tm == TouchMode.Pinch && pinchAllowed && lastPinchDistance > 1f && pinchDist > 1f)
            {
                // Fingers apart -> distance shrinks (zoom in).
                ZoomByFactor(Mathf.Pow(lastPinchDistance / pinchDist, pinchZoomSensitivity));
            }
        }

        touchMode = tm;
        trackedId0 = id0;
        trackedId1 = id1;
        lastTouchPos0 = p0;
        lastPinchDistance = pinchDist;
    }

    // ============================================================ Camera math
    private void Orbit(Vector2 deltaPixels, float deviceSensitivity)
    {
        if (deltaPixels.sqrMagnitude < 1e-6f) return;

        float degPerPixel = deviceSensitivity * orbitSensitivity;
        float yaw = deltaPixels.x * degPerPixel * (invertHorizontal ? -1f : 1f);
        float pitch = -deltaPixels.y * degPerPixel * (invertVertical ? -1f : 1f);

        // Mirror yaw when upside down so dragging right still turns the same way on screen.
        float upY = (targetRotation * Vector3.up).y;
        if (upY > 0.01f) yawSign = 1f;
        else if (upY < -0.01f) yawSign = -1f;

        targetRotation = Quaternion.AngleAxis(yaw * yawSign, Vector3.up)
                       * targetRotation
                       * Quaternion.AngleAxis(pitch, Vector3.right);
        targetRotation = Quaternion.Normalize(targetRotation);
    }

    private void ZoomByFactor(float factor)
    {
        targetDistance = Mathf.Clamp(targetDistance * factor, minDist, maxDist);
    }

    private void Pan(Vector2 deltaPixels)
    {
        if (deltaPixels.sqrMagnitude < 1e-6f) return;

        float screenH = Mathf.Max(1f, Screen.height);
        float worldPerPixel = cam.orthographic
            ? 2f * cam.orthographicSize / screenH
            : 2f * smoothedDistance * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / screenH;

        Vector3 right = targetRotation * Vector3.right;
        Vector3 up = targetRotation * Vector3.up;
        targetPan -= (right * deltaPixels.x + up * deltaPixels.y) * worldPerPixel * panSensitivity;

        if (moveCubeLimitsMovement)
        {
            Vector3 half = GetMoveCubeSize() * 0.5f;
            targetPan = new Vector3(
                Mathf.Clamp(targetPan.x, -half.x, half.x),
                Mathf.Clamp(targetPan.y, -half.y, half.y),
                Mathf.Clamp(targetPan.z, -half.z, half.z));
        }
        else if (keepMoveInsideSphere)
            targetPan = Vector3.ClampMagnitude(targetPan, runtimeSphereRadius * moveLimitOfSphereRadius);
        if (maxPanDistance > 0f) targetPan = Vector3.ClampMagnitude(targetPan, maxPanDistance);
    }

    private Vector3 GetBasePivot()
    {
        return target.position
             + target.TransformVector(runtimeAutoOffset)
             + target.TransformVector(orbitCenterOffset);
    }

    private void ApplyPose()
    {
        Vector3 pivot = GetBasePivot() + smoothedPan;
        Vector3 position = pivot - smoothedRotation * Vector3.forward * smoothedDistance;
        transform.SetPositionAndRotation(position, smoothedRotation);
    }

    private static float SmoothFactor(float speed, float dt)
    {
        return speed <= 0f ? 1f : 1f - Mathf.Exp(-speed * dt);
    }

    // ============================================================ UI blocking
    private bool IsOverUI(Vector2 screenPosition)
    {
        // The on-screen mode buttons always block camera input under them.
        if (showOnScreenModeButtons && ModeButtonsArea().Contains(ScreenToGui(screenPosition)))
            return true;

        if (!blockInputOverUI) return false;

        EventSystem es = EventSystem.current;
        if (es == null) return false;

        if (pointerData == null || pointerDataOwner != es)
        {
            pointerData = new PointerEventData(es);
            pointerDataOwner = es;
        }

        pointerData.position = screenPosition;
        uiHits.Clear();
        es.RaycastAll(pointerData, uiHits);

        for (int i = 0; i < uiHits.Count; i++)
            if (uiHits[i].module is GraphicRaycaster) return true;
        return false;
    }

    // ============================================================ Move cube size
    /// <summary>Current cube size (auto from the target, or the Inspector value).</summary>
    private Vector3 GetMoveCubeSize()
    {
        Vector3 size = (moveCubeAutoSize && autoMoveCubeSize != Vector3.zero) ? autoMoveCubeSize : moveCubeSize;
        return new Vector3(Mathf.Max(0.01f, size.x), Mathf.Max(0.01f, size.y), Mathf.Max(0.01f, size.z));
    }

    // ============================================================ On-screen mode buttons
    private static Vector2 ScreenToGui(Vector2 screenPos)
    {
        return new Vector2(screenPos.x, Screen.height - screenPos.y);
    }

    private Rect ModeButtonsArea()
    {
        float h = Mathf.Max(40f, Screen.height * 0.07f);
        float w = Mathf.Max(90f, Screen.width * 0.16f);
        float margin = h * 0.25f;
        float gap = h * 0.125f;
        return new Rect(margin, margin, w * 4f + gap * 3f, h);
    }

    private void OnGUI()
    {
        if (!showOnScreenModeButtons) return;

        if (buttonStyle == null)
        {
            buttonStyle = new GUIStyle(GUI.skin.button) { alignment = TextAnchor.MiddleCenter };
        }

        Rect area = ModeButtonsArea();
        buttonStyle.fontSize = Mathf.RoundToInt(area.height * 0.38f);

        float gap = area.height * 0.125f;
        float w = (area.width - gap * 3f) / 4f;
        Rect r1 = new Rect(area.x, area.y, w, area.height);
        Rect r2 = new Rect(area.x + (w + gap), area.y, w, area.height);
        Rect r3 = new Rect(area.x + (w + gap) * 2f, area.y, w, area.height);
        Rect r4 = new Rect(area.x + (w + gap) * 3f, area.y, w, area.height);

        Color old = GUI.backgroundColor;

        GUI.backgroundColor = mode == InteractionMode.OrbitAroundObject ? new Color(0.3f, 0.8f, 1f) : Color.white;
        if (GUI.Button(r1, "Rotate", buttonStyle)) SetModeOrbit();

        GUI.backgroundColor = mode == InteractionMode.DragToZoom ? new Color(0.3f, 0.8f, 1f) : Color.white;
        if (GUI.Button(r2, "Zoom", buttonStyle)) SetModeDragZoom();

        GUI.backgroundColor = mode == InteractionMode.RollCamera ? new Color(0.3f, 0.8f, 1f) : Color.white;
        if (GUI.Button(r3, "Roll", buttonStyle)) SetModeRoll();

        GUI.backgroundColor = mode == InteractionMode.MoveXY ? new Color(0.3f, 0.8f, 1f) : Color.white;
        if (GUI.Button(r4, "Move", buttonStyle)) SetModeMove();

        GUI.backgroundColor = old;
    }

    // ============================================================ Gizmos
    private void DrawOriginAxes(Vector3 origin, Quaternion rotation, float size)
    {
        Vector3 x = rotation * Vector3.right * size;
        Vector3 y = rotation * Vector3.up * size;
        Vector3 z = rotation * Vector3.forward * size;
        float tip = size * 0.08f;

        Gizmos.color = Color.red;
        Gizmos.DrawLine(origin, origin + x);
        Gizmos.DrawWireCube(origin + x, Vector3.one * tip);

        Gizmos.color = Color.green;
        Gizmos.DrawLine(origin, origin + y);
        Gizmos.DrawWireCube(origin + y, Vector3.one * tip);

        Gizmos.color = Color.blue;
        Gizmos.DrawLine(origin, origin + z);
        Gizmos.DrawWireCube(origin + z, Vector3.one * tip);

        // Origin point
        Gizmos.color = Color.white;
        Gizmos.DrawSphere(origin, size * 0.06f);
    }

    private void DrawOriginGizmos()
    {
        if (drawWorldOriginGizmo)
            DrawOriginAxes(Vector3.zero, Quaternion.identity, originGizmoSize);

        if (drawTargetOriginGizmo && target != null)
            DrawOriginAxes(target.position,
                           targetOriginUsesLocalAxes ? target.rotation : Quaternion.identity,
                           originGizmoSize);
    }

    private void OnDrawGizmos()
    {
        DrawOriginGizmos();

        if (!drawSphereGizmo || target == null) return;

        Vector3 center = target.position
                       + target.TransformVector(runtimeAutoOffset)
                       + target.TransformVector(orbitCenterOffset);
        if (Application.isPlaying) center += smoothedPan;

        float r = Application.isPlaying ? runtimeSphereRadius : sphereRadius;

        // Touch sphere
        Gizmos.color = new Color(0f, 0.8f, 1f, 0.7f);
        Gizmos.DrawWireSphere(center, r);

        // Move cube (editable size)
        if (drawMoveCubeGizmo)
        {
            Vector3 cubeCenter = center - (Application.isPlaying ? smoothedPan : Vector3.zero);
            Vector3 cubeSize = moveCubeSize;
            if (moveCubeAutoSize)
            {
                Bounds gb;
                if (Application.isPlaying) cubeSize = GetMoveCubeSize();
                else if (TryGetTargetBounds(out gb)) cubeSize = gb.size * moveCubeAutoPadding;
            }
            Gizmos.color = moveCubeColor;
            Gizmos.DrawWireCube(cubeCenter, cubeSize);
        }

        // Orbit center
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(center, 0.1f);

        // Min / max camera distance
        float gizmoUnit = orbitSizeRelativeToSphere ? r : 1f;
        float gMin = Application.isPlaying ? minDist : minOrbitSize * gizmoUnit;
        float gMax = Application.isPlaying ? maxDist : maxOrbitSize * gizmoUnit;
        Gizmos.color = new Color(0f, 1f, 0f, 0.35f);   // min orbit size
        Gizmos.DrawWireSphere(center, gMin);
        Gizmos.color = new Color(1f, 0.5f, 0f, 0.35f); // max orbit size
        Gizmos.DrawWireSphere(center, gMax);
    }
}