using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Single-script conveyor placer. Pick a belt, preview it as a ghost, click to place.
/// Belts of any length snap end to end, and placing a belt where it would overlap another is blocked.
/// Placed belts need no extra components: the placer keeps its own list of what it has placed.
///
/// 1-9 / buttons: select   R: rotate   Left click: place
/// X: delete mode   V: reverse belts   Esc: cancel
/// Uses the new Input System package.
/// </summary>
public class ConveyorPlacerSimple : MonoBehaviour
{
    [Serializable]
    public class Option
    {
        public string name = "Conveyor";
        public GameObject prefab;
    }

    public Option[] options;
    public Camera cam;
    public float groundHeight = 0f;
    [Tooltip("0 disables grid snapping of the position")]
    public float gridSize = 0.5f;
    public float rotateStep = 90f;

    [Header("Ghost look")]
    [Tooltip("Optional. Leave EMPTY to keep the belt's own materials (so arrows stay visible) and just tint them " +
             "green/red. If set, every ghost material is replaced by this one.")]
    public Material ghostMaterial;
    [Tooltip("How strongly the green/red tint is mixed into the ghost's own colours (when no Ghost Material is set)")]
    [Range(0f, 1f)] public float tintStrength = 0.5f;
    public Color validColor = new Color(0.4f, 1f, 0.4f, 0.6f);
    public Color invalidColor = new Color(1f, 0.3f, 0.3f, 0.6f);

    [Header("Snapping")]
    [Tooltip("After the ghost snaps and turns to match a belt, make R rotate from that angle instead of from the last angle you set by hand")]
    public bool rotateFromSnappedAngle = false;
    [Tooltip("Show in the panel why the ghost is red (which belt it overlaps)")]
    public bool showBlockReason = true;
    [Tooltip("How close (world units) a ghost end must be to an open belt end to snap onto it")]
    public float snapDistance = 1f;
    [Tooltip("Straight-line snapping ignores height when measuring snapDistance, so a belt can snap onto the top or " +
             "bottom of a slope. Ends further apart vertically than this won't snap (e.g. a belt on another level).")]
    public float maxSnapHeightDifference = 3f;
    [Tooltip("Only snap if the ghost points within this many degrees of the belt it snaps to")]
    [Range(0f, 90f)] public float snapAngle = 45f;
    [Tooltip("Space left between snapped belts. Negative values make them overlap slightly.")]
    public float endGap = 0f;
    public enum MeasureSource { SolidColliders, TriggerColliders, Renderers }

    [Header("Belt measurement")]
    [Tooltip("What defines a belt's size for snapping and overlap checks:\n" +
             "SolidColliders = its normal (non-trigger) colliders, usually just the belt part.\n" +
             "TriggerColliders = the box that pushes items.\n" +
             "Renderers = every visible mesh (includes legs/rails).\n" +
             "If the chosen type isn't found, it falls back to Renderers.")]
    public MeasureSource measureFrom = MeasureSource.SolidColliders;
    [Tooltip("Shrinks each belt's measured WIDTH by this much on each side. Use it when rails or legs stick out sideways, " +
             "so belts snap and overlap-check against the belt itself. Turn on Draw Bounds to see the effect.")]
    public float widthTrim = 0f;

    [Header("Snapping: 90 degree turns (separate from the straight-line snap above)")]
    [Tooltip("How close (world units) the ghost must be to a belt's end or side to snap square against it")]
    public float turnSnapDistance = 0.5f;
    [Tooltip("Space left between a turned belt and the belt it touches. Negative = closer / overlapping.")]
    public float turnGap = 0f;
    [Tooltip("Draw each belt's measured box in the Scene view (turn on Gizmos in the Game view too) to see what is being used for snapping")]
    public bool drawBounds = false;
    [Tooltip("After placing a belt, move the mouse cursor to where the next belt will snap. Can also be toggled in the panel.")]
    public bool moveCursorToSnap = true;

    [Header("Slope joins")]
    [Tooltip("Extra height (world units) given to a belt that joins end to end with a SLOPED belt, so items run from one onto " +
             "the other without a step up. Raise it if the belt after / before a slope sits too low; 0 = exact match to " +
             "the measured surfaces.")]
    public float slopeJoinLift = 0.03f;

    [Header("Delete / reverse highlight")]
    [Tooltip("Tint shown over the belt or spawner under the cursor in delete mode (X)")]
    public Color deleteTint = new Color(1f, 0.2f, 0.2f, 0.45f);
    [Tooltip("Tint shown over the belt under the cursor in reverse mode (V)")]
    public Color reverseTint = new Color(1f, 0.8f, 0.1f, 0.45f);

    [Header("Overlap")]
    [Tooltip("Belts may overlap by this much (world units) before placement is blocked")]
    public float overlapTolerance = 0.05f;

    [Header("Item spawners (any prefab with an ItemSpawner component)")]
    [Tooltip("Spawners hover this far above whatever is beneath them They skip snapping and overlap checks.")]
    public float spawnerHoverHeight = 1.5f;
    [Tooltip("Delete mode: how close (screen pixels) you must click to a spawner to delete it")]
    public float spawnerPickRadius = 30f;

    [Header("Height (belts sit on the floor; they only rise by snapping onto a higher belt)")]
    [Tooltip("The surface beneath the cursor is found by casting down from this far above the ground height")]
    public float probeHeight = 50f;
    [Tooltip("Lift the item so the bottom of its model/collider rests on the surface, whatever the prefab's pivot")]
    public bool liftToRestOnSurface = true;
    [Tooltip("Which layers count as 'floor' for the downward probe")]
    public LayerMask surfaceMask = ~0;

    enum Mode { Idle, Place, Delete, Reverse }

    /// <summary>A belt's measurements in its root's local space, plus the transform it belongs to.</summary>
    class Piece
    {
        public Transform transform;
        public Vector3 forwardLocal; // direction of travel (flat)
        public Vector3 back;         // centre of the start end, AT BELT SURFACE HEIGHT (so slopes line up)
        public Vector3 front;        // centre of the finish end, at belt surface height
        public Bounds bounds;        // bounds of all renderers
        public float halfLength;     // half the belt's size along its direction of travel
        public float halfWidth;      // half the belt's size across it

        public Vector3 CentreWorld => transform.TransformPoint(bounds.center);
        public Vector3 Forward => Flat(transform.TransformDirection(forwardLocal));
        public Vector3 BackWorld => transform.TransformPoint(back);
        public Vector3 FrontWorld => transform.TransformPoint(front);

        /// <summary>Swap the belt's ends and direction (used when it is reversed).</summary>
        public void Flip()
        {
            forwardLocal = -forwardLocal;
            (back, front) = (front, back);
        }

        public Piece CopyFor(Transform t)
        {
            return new Piece
            {
                transform = t,
                forwardLocal = forwardLocal,
                back = back,
                front = front,
                bounds = bounds,
                halfLength = halfLength,
                halfWidth = halfWidth
            };
        }
    }

    Mode _mode = Mode.Idle;
    bool _flipped;                   // new belts are placed reversed (F)
    int _selected = -1;
    GameObject _ghost;
    Piece _ghostPiece;               // measurements of the selected belt
    readonly List<Piece> _placed = new List<Piece>();
    readonly List<GameObject> _spawners = new List<GameObject>(); // placed item spawners
    bool _isSpawner;                 // the selected prefab is an item spawner
    Material _ghostMat;              // runtime copy of ghostMaterial, tinted for valid/invalid

    class TintTarget { public Material mat; public string prop; public Color original; }
    readonly List<TintTarget> _tintTargets = new List<TintTarget>();   // ghost's own materials that can be tinted
    readonly List<Material> _ghostInstances = new List<Material>();   // per-ghost material copies, destroyed with it
    bool _ghostValid = true;
    float _yaw;
    float _baseLift;                 // distance from the prefab's pivot down to the bottom of its model
    float _planeY;                   // height of the plane the mouse ray is intersected with
    Rect _panel;

    Vector3 MousePos => Mouse.current.position.ReadValue();

    void Start()
    {
        // Belts already in the scene should snap and block overlaps too.
        foreach (var force in FindObjectsByType<ConveyorForce>(FindObjectsSortMode.None))
        {
            Transform root = force.transform.root;
            if (!_placed.Exists(p => p.transform == root))
                _placed.Add(Measure(root.gameObject, widthTrim, measureFrom));
        }
    }

    void Update()
    {
        if (options == null) return;
        if (cam == null) cam = Camera.main;
        if (Keyboard.current == null || Mouse.current == null) return;

        _placed.RemoveAll(p => p.transform == null); // belts destroyed by anything else
        _spawners.RemoveAll(g => g == null);

        // Number keys follow the order shown in the panel: conveyors first, then spawners
        ClassifyOptions();
        for (int k = 0; k < _order.Length && k < 9; k++)
            if (Keyboard.current[(Key)((int)Key.Digit1 + k)].wasPressedThisFrame) Select(_order[k]);

        if (Keyboard.current.f11Key.wasPressedThisFrame) SetFullscreen(!Screen.fullScreen);
        if (Keyboard.current.xKey.wasPressedThisFrame) SetMode(Mode.Delete);
        if (Keyboard.current.vKey.wasPressedThisFrame) SetMode(Mode.Reverse);
        if (Keyboard.current.fKey.wasPressedThisFrame && _mode == Mode.Place && !_isSpawner && _ghost != null)
        {
            _flipped = !_flipped;
            FlipGhost();
        }
        if (Keyboard.current.escapeKey.wasPressedThisFrame) SetMode(Mode.Idle);
        if (Keyboard.current.rKey.wasPressedThisFrame) _yaw = (_yaw + rotateStep) % 360f;

        // IMGUI origin is top-left, MousePos is bottom-left
        var mouse = new Vector2(MousePos.x, Screen.height - MousePos.y);
        bool overUI = _panel.Contains(mouse);

        if (_mode == Mode.Place) UpdatePlace(overUI);
        else if (_mode == Mode.Delete && !overUI && Mouse.current.leftButton.wasPressedThisFrame) DeleteUnderMouse();
        else if (_mode == Mode.Reverse && !overUI && Mouse.current.leftButton.wasPressedThisFrame) ReverseUnderMouse();

        UpdateHighlight(overUI);
    }

    // ------------------------------------------------------------------ placing

    void UpdatePlace(bool overUI)
    {
        if (_ghost == null) return;

        Vector3 point = default;
        float cursorSurfaceY = groundHeight;
        bool hasPoint;

        if (_isSpawner)
        {
            // Spawners hover over whatever the cursor is actually pointing at (belt, floor...). Using the real
            // surface under the cursor, not a plane at the ghost's own height, stops the ghost jumping
            // between the belt and the ground when it is placed over a belt.
            hasPoint = !overUI && CursorSurface(out point, out cursorSurfaceY);
        }
        else
        {
            // Intersect the mouse ray with a plane at the ghost's current height so the item
            // stays under the cursor even when raised.
            hasPoint = !overUI && GroundPoint(_planeY, out point);
        }
        _ghost.SetActive(hasPoint);
        if (!hasPoint) return;

        if (gridSize > 0f)
        {
            point.x = Mathf.Round(point.x / gridSize) * gridSize;
            point.z = Mathf.Round(point.z / gridSize) * gridSize;
        }

        // Default height = surface beneath the item, plus the user's extra height.
        float y;
        if (_isSpawner)
        {
            y = cursorSurfaceY + _baseLift + spawnerHoverHeight;
        }
        else
        {
            y = FindSurfaceY(point.x, point.z) + _baseLift;
            _planeY = y;
        }

        // Put the middle of the belt (not its pivot) on the cursor. Most models have an off-centre pivot, so
        // positioning the pivot makes one end sit closer to the cursor than the other, and it changes when you rotate.
        Quaternion rot = Quaternion.Euler(0f, _yaw, 0f);
        Vector3 pivotToCentre = Vector3.zero;
        if (_ghostPiece != null)
        {
            Vector3 c = _ghostPiece.bounds.center;
            pivotToCentre = rot * Vector3.Scale(new Vector3(c.x, 0f, c.z), _ghost.transform.lossyScale);
        }

        _ghost.transform.SetPositionAndRotation(new Vector3(point.x - pivotToCentre.x, y, point.z - pivotToCentre.z), rot);

        if (!_isSpawner)
        {
            // Snapping can turn the ghost to match a belt. Keep that as the current rotation, so that R turns it
            // 90 degrees from what you SEE, not from the last angle you chose by hand.
            bool snapped = TrySnapToEnds();
            if (snapped && rotateFromSnappedAngle)
                _yaw = Mathf.Repeat(Mathf.Round(_ghost.transform.eulerAngles.y * 100f) / 100f, 360f);

            // Right after placing a belt, the next ghost jumps to its snap position. Move the mouse pointer there
            // too, so the pointer and the ghost stay together.
            if (_warpCursorNext)
            {
                _warpCursorNext = false;
                if (snapped && moveCursorToSnap) WarpCursorToGhost();
            }
        }

        bool valid = _isSpawner || IsPlacementValid(); // spawners are free-placed: no bounds checks
        SetGhostValid(valid);
        _blockInfo = (!valid && showBlockReason) ? DescribeBlocker() : "";

        if (valid && Mouse.current.leftButton.wasPressedThisFrame)
        {
            var placed = Instantiate(options[_selected].prefab, _ghost.transform.position, _ghost.transform.rotation);
            // Parent AFTER instantiating: belt scripts find their own root in Awake and must not see the folder
            placed.transform.SetParent(GetContainer(_isSpawner), true);
            if (_isSpawner) _spawners.Add(placed);
            else
            {
                if (_flipped) placed.GetComponentInChildren<ConveyorForce>()?.Reverse();
                _placed.Add(_ghostPiece.CopyFor(placed.transform)); // ghost piece is already flipped if needed
                _warpCursorNext = true;
            }
        }
    }

    /// <summary>
    /// Two kinds of snap, whichever is closest:
    ///  - Same direction: one end of the ghost meets an open end of a belt (a straight line of belts).
    ///  - At 90 degrees: the ghost is turned exactly square to a belt and pushed up flush against its
    ///    end or side, with its edge lined up to the belt's edges (corners and T-junctions).
    /// Works for any belt length because sizes and ends are measured from each model.
    /// </summary>
    struct SnapCandidate
    {
        public float distance;
        public Piece target;
        public bool flush;          // true: 90 degree snap, false: same-direction snap
        public bool anti;           // in-line snap where the ghost faces the opposite way to the target
        public bool pushOut;        // parallel / opposite belt that overlaps: just slide the ghost clear, no turning
        public bool ghostFollows;   // same-direction: ghost's back end meets the target's front end
        public Vector3 flushCentre;
    }

    readonly List<SnapCandidate> _candidates = new List<SnapCandidate>();

    /// <summary>Returns true if the ghost was snapped (and possibly turned) onto another belt.</summary>
    bool TrySnapToEnds()
    {
        Transform g = _ghost.transform;
        Vector3 pos0 = g.position;
        Quaternion rot0 = g.rotation;
        Vector3 gForward = Flat(g.TransformDirection(_ghostPiece.forwardLocal));
        Vector3 gBack = g.TransformPoint(_ghostPiece.back);
        Vector3 gFront = g.TransformPoint(_ghostPiece.front);
        Vector3 gCentre = g.TransformPoint(_ghostPiece.bounds.center);
        float gSurfaceY = (gBack.y + gFront.y) * 0.5f; // height of the ghost's belt surface

        float openTolerance = 0.1f + Mathf.Abs(endGap);

        // Gather every possible snap, then try them nearest first and keep the first one that doesn't overlap
        // anything. (Taking just the nearest could pull the ghost onto a belt end that is already used by a
        // turn, straight into the belt that is there.)
        _candidates.Clear();
        foreach (var p in _placed)
        {
            float angle = Vector3.Angle(gForward, p.Forward);

            // A belt facing the same or the opposite way that the ghost overlaps: slide it clear (last resort,
            // so real end-to-end snaps are always tried first).
            if ((angle <= snapAngle || angle >= 180f - snapAngle) && Overlap(_ghostPiece, p, overlapTolerance))
            {
                // Prefer continuing the line: in front of p (or behind it if the cursor is clearly past its back half),
                // and only then sliding alongside it.
                Vector3 fwd = p.Forward;
                float uCursor = Vector3.Dot(gCentre - p.CentreWorld, fwd);
                float endSign = Mathf.Abs(uCursor) < p.halfLength * 0.5f ? 1f : Mathf.Sign(uCursor);
                float vCursor = Vector3.Dot(gCentre - p.CentreWorld, Vector3.Cross(Vector3.up, fwd));
                float sideSign = vCursor >= 0f ? 1f : -1f;

                // Continuing the line uses the real end-to-end snap (exact ends, matching heights).
                bool antiLine = angle > 90f;
                AddEndSnap(p, endSign > 0f, antiLine, 1000f, openTolerance);
                AddEndSnap(p, endSign < 0f, antiLine, 1100f, openTolerance);
                AddClear(p, gCentre, false, sideSign, 1200f);
                AddClear(p, gCentre, false, -sideSign, 1300f);
            }

            if (angle <= snapAngle || angle >= 180f - snapAngle)
            {
                // In line with p, facing the same way (angle ~0) or the opposite way (angle ~180). Which end of the
                // ghost meets p depends on that, since 'back' and 'front' are the ghost's own physical ends.
                bool anti = angle > 90f;
                Vector3 nearAfter = anti ? gFront : gBack;    // ghost end that meets p's front
                Vector3 nearBefore = anti ? gBack : gFront;   // ghost end that meets p's back

                float dAfter = SnapDistanceBetween(nearAfter, p.FrontWorld);   // ghost continues the line after p
                if (dAfter <= snapDistance && FrontIsOpen(p, openTolerance))
                    _candidates.Add(new SnapCandidate { distance = dAfter, target = p, flush = false, ghostFollows = true, anti = anti });

                float dBefore = SnapDistanceBetween(nearBefore, p.BackWorld);  // ghost feeds into the start of p
                if (dBefore <= snapDistance && BackIsOpen(p, openTolerance))
                    _candidates.Add(new SnapCandidate { distance = dBefore, target = p, flush = false, ghostFollows = false, anti = anti });
            }
            else if (Mathf.Abs(angle - 90f) <= snapAngle)
            {
                if (TryFlush(p, gCentre, gSurfaceY, out float gap, out Vector3 newCentre))
                    _candidates.Add(new SnapCandidate { distance = Mathf.Max(gap, 0f), target = p, flush = true, flushCentre = newCentre });
            }
        }

        _candidates.Sort((a, b) => a.distance.CompareTo(b.distance));

        foreach (var c in _candidates)
        {
            g.SetPositionAndRotation(pos0, rot0);
            ApplySnap(c, gForward, gCentre);
            if (IsPlacementValid()) return true; // this one fits
        }

        // Nothing fits: leave the ghost where the cursor put it (it will show red if it overlaps).
        g.SetPositionAndRotation(pos0, rot0);
        return false;
    }

    void ApplySnap(SnapCandidate c, Vector3 gForward, Vector3 gCentre)
    {
        Transform g = _ghost.transform;
        Piece target = c.target;

        if (c.pushOut)
        {
            g.position += c.flushCentre - gCentre; // no turning, just slide clear
            return;
        }

        if (c.flush)
        {
            // Turn exactly square to the target (about the ghost's own centre), then move it into place.
            float a = Vector3.SignedAngle(target.Forward, gForward, Vector3.up);
            float delta = (a >= 0f ? 90f : -90f) - a;
            g.RotateAround(gCentre, Vector3.up, delta);
            g.position += c.flushCentre - gCentre;
            return;
        }

        // Line up exactly with the target (same way, or exactly opposite), then slide so the ends coincide.
        Vector3 aim = c.anti ? -target.Forward : target.Forward;
        float yaw = Vector3.SignedAngle(gForward, aim, Vector3.up);
        g.rotation = Quaternion.AngleAxis(yaw, Vector3.up) * g.rotation;

        if (c.ghostFollows)
        {
            Vector3 near = g.TransformPoint(c.anti ? _ghostPiece.front : _ghostPiece.back);
            g.position += target.FrontWorld + target.Forward * endGap - near;
        }
        else
        {
            Vector3 near = g.TransformPoint(c.anti ? _ghostPiece.back : _ghostPiece.front);
            g.position += target.BackWorld - target.Forward * endGap - near;
        }

        // The measured colliders rarely match the visible belts exactly where a slope meets a flat belt,
        // so give joins with slopes a little lift (tunable).
        if (IsSloped(target) || IsSloped(_ghostPiece))
            g.position += Vector3.up * slopeJoinLift;
    }

    /// <summary>
    /// Works out where the ghost's centre must be to sit flush against belt 'p' with the ghost square to it.
    /// 'gap' is the free space between them along the axis they separate on (negative = overlapping).
    /// Returns false if the ghost is too far away, or too deep inside p, to snap.
    /// </summary>
    bool TryFlush(Piece p, Vector3 gCentre, float ghostSurfaceY, out float gap, out Vector3 newCentre)
    {
        newCentre = gCentre;
        gap = float.MaxValue;

        Vector3 f = p.Forward;                          // along p
        Vector3 r = Vector3.Cross(Vector3.up, f);       // across p
        Vector3 tc = p.CentreWorld;

        Vector3 d = gCentre - tc;
        d.y = 0f;
        float u = Vector3.Dot(d, f);                    // ghost centre along p
        float v = Vector3.Dot(d, r);                    // ghost centre across p

        float lt = p.halfLength, wt = p.halfWidth;      // p: half length along f, half width along r
        float lg = _ghostPiece.halfLength, wg = _ghostPiece.halfWidth;
        // The ghost is (about to be) square to p, so its length runs along r and its width along f.

        float gapU = Mathf.Abs(u) - wg - lt;            // free space past p's end face
        float gapV = Mathf.Abs(v) - lg - wt;            // free space past p's side face
        gap = Mathf.Max(gapU, gapV);                    // the axis they separate on
        if (gap > turnSnapDistance) return false; // too far away. Overlapping (negative gap) is fine: it gets pushed out flush.

        float newU = u, newV = v;
        if (gapU >= gapV)
        {
            // Flush against p's end; line the ghost's side edges up with p's side edges (or centre it).
            newU = (u >= 0f ? 1f : -1f) * (lt + wg + turnGap);
            newV = NearestOrSame(v, -wt + lg, wt - lg, 0f);
        }
        else
        {
            // Flush against p's side; line the ghost's end edges up with p's end edges (or centre it).
            newV = (v >= 0f ? 1f : -1f) * (wt + lg + turnGap);
            newU = NearestOrSame(u, -lt + wg, lt - wg, 0f);
        }

        newCentre = tc + f * newU + r * newV;

        // Height: meet the target's belt surface at the point of contact, so a turn off a raised belt is raised too
        // (and a turn off a belt at floor level stays on the floor).
        float t = lt > 1e-4f ? Mathf.Clamp01((newU + lt) / (2f * lt)) : 0.5f;
        float targetY = Mathf.Lerp(p.BackWorld.y, p.FrontWorld.y, t);
        float dy = targetY - ghostSurfaceY;
        if (Mathf.Abs(dy) > maxSnapHeightDifference) return false;
        newCentre.y = gCentre.y + dy;
        return true;
    }

    /// <summary>Moves the OS mouse pointer onto the ghost's centre on screen.</summary>
    Transform _beltRoot, _spawnerRoot;

    /// <summary>Parent for placed objects so they don't clutter the top of the hierarchy.</summary>
    Transform GetContainer(bool spawner)
    {
        ref Transform root = ref (spawner ? ref _spawnerRoot : ref _beltRoot);
        if (root == null)
        {
            var go = new GameObject(spawner ? "Item Spawners" : "Conveyors");
            root = go.transform;
        }
        return root;
    }

    void WarpCursorToGhost()
    {
        if (cam == null || _ghostPiece == null) return;

        Vector3 screen = cam.WorldToScreenPoint(_ghost.transform.TransformPoint(_ghostPiece.bounds.center));
        if (screen.z <= 0f) return; // behind the camera

        // Keep the pointer on screen, and off the left-hand panel (clicks there don't place belts).
        screen.x = Mathf.Clamp(screen.x, 1f, Screen.width - 1f);
        screen.y = Mathf.Clamp(screen.y, 1f, Screen.height - 1f);
        if (_panel.Contains(new Vector2(screen.x, Screen.height - screen.y))) return;

        Mouse.current.WarpCursorPosition(new Vector2(screen.x, screen.y));
    }

    /// <summary>Adds an in-line snap to the front (ghostFollows) or back of p, if that end is free.</summary>
    void AddEndSnap(Piece p, bool ghostFollows, bool anti, float rank, float openTolerance)
    {
        bool open = ghostFollows ? FrontIsOpen(p, openTolerance) : BackIsOpen(p, openTolerance);
        if (open)
            _candidates.Add(new SnapCandidate { distance = rank, target = p, flush = false, ghostFollows = ghostFollows, anti = anti });
    }

    void AddClear(Piece p, Vector3 gCentre, bool end, float sign, float rank)
    {
        if (TryPushOut(p, gCentre, end, sign, out Vector3 newCentre))
            _candidates.Add(new SnapCandidate { distance = rank, target = p, pushOut = true, flushCentre = newCentre });
    }

    /// <summary>
    /// For a belt 'p' facing the same or the opposite way as the ghost: the ghost centre that puts the ghost flush
    /// against p's front/back end (end = true, sign +1 front / -1 back) or alongside it (end = false, sign = which side).
    /// The ghost keeps its own rotation. An end position is centred on p's lane so the two belts line up.
    /// </summary>
    bool TryPushOut(Piece p, Vector3 gCentre, bool end, float sign, out Vector3 newCentre)
    {
        Vector3 f = p.Forward;
        Vector3 r = Vector3.Cross(Vector3.up, f);
        Vector3 tc = p.CentreWorld;

        Vector3 d = gCentre - tc;
        d.y = 0f;
        float u = Vector3.Dot(d, f);
        float v = Vector3.Dot(d, r);

        float lt = p.halfLength, wt = p.halfWidth;
        float lg = _ghostPiece.halfLength, wg = _ghostPiece.halfWidth; // parallel: ghost length is along f too

        float newU, newV;
        if (end)
        {
            newU = sign * (lt + lg + endGap);
            newV = Mathf.Abs(v) <= wt ? 0f : NearestOrSame(v, -wt + wg, wt - wg, 0f); // same lane as p
        }
        else
        {
            newV = sign * (wt + wg + turnGap);
            newU = NearestOrSame(u, -lt + lg, lt - lg, 0f);
        }

        newCentre = tc + f * newU + r * newV;
        newCentre.y = gCentre.y;
        return true;
    }

    /// <summary>The candidate closest to 'value' if it is within turnSnapDistance, otherwise 'value' unchanged.</summary>
    float NearestOrSame(float value, float a, float b, float c)
    {
        float best = value;
        float bestDist = turnSnapDistance;
        foreach (float cand in new[] { a, b, c })
        {
            float dist = Mathf.Abs(cand - value);
            if (dist < bestDist)
            {
                bestDist = dist;
                best = cand;
            }
        }
        return best;
    }

    /// <summary>
    /// Distance used to decide whether two belt ends are close enough to snap: measured on the ground only, because
    /// the ghost rests on the floor while the end it is snapping to may be higher or lower (slopes). The height is
    /// then matched exactly by the snap. Returns infinity if the heights are too far apart.
    /// </summary>
    float SnapDistanceBetween(Vector3 a, Vector3 b)
    {
        if (Mathf.Abs(a.y - b.y) > maxSnapHeightDifference) return float.MaxValue;
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }

    static bool IsSloped(Piece p)
    {
        return Mathf.Abs(p.front.y - p.back.y) > 0.01f;
    }

    bool FrontIsOpen(Piece p, float tolerance)
    {
        foreach (var q in _placed)
            if (q != p && ((q.BackWorld - p.FrontWorld).sqrMagnitude < tolerance * tolerance ||
                           (q.FrontWorld - p.FrontWorld).sqrMagnitude < tolerance * tolerance)) return false;
        return true;
    }

    bool BackIsOpen(Piece p, float tolerance)
    {
        foreach (var q in _placed)
            if (q != p && ((q.FrontWorld - p.BackWorld).sqrMagnitude < tolerance * tolerance ||
                           (q.BackWorld - p.BackWorld).sqrMagnitude < tolerance * tolerance)) return false;
        return true;
    }

    bool IsPlacementValid()
    {
        foreach (var p in _placed)
            if (Overlap(_ghostPiece, p, overlapTolerance)) return false;
        return true;
    }

    string _blockInfo = "";
    bool _warpCursorNext;            // after placing, pull the pointer to the next snap position
    GameObject _highlight;           // translucent box over whatever delete/reverse would act on
    Material _highlightMat;

    /// <summary>Describes which belt the ghost overlaps and how they are oriented (shown in the panel).</summary>
    string DescribeBlocker()
    {
        foreach (var p in _placed)
        {
            if (!Overlap(_ghostPiece, p, overlapTolerance)) continue;

            Vector3 gForward = Flat(_ghost.transform.TransformDirection(_ghostPiece.forwardLocal));
            float angle = Vector3.Angle(gForward, p.Forward);
            Vector3 d = _ghost.transform.TransformPoint(_ghostPiece.bounds.center) - p.CentreWorld;
            return $"Overlaps '{p.transform.name}'\nangle {angle:0}°, centres {new Vector2(d.x, d.z).magnitude:0.00} apart\n" +
                   $"snap options tried: {_candidates.Count}";
        }
        return "";
    }

    void SetGhostValid(bool valid)
    {
        if (valid == _ghostValid) return;
        _ghostValid = valid;
        TintGhost(valid ? validColor : invalidColor);
    }

    void TintGhost(Color c)
    {
        if (_ghostMat != null)
        {
            // Override mode: one replacement material.
            if (_ghostMat.HasProperty("_BaseColor")) _ghostMat.SetColor("_BaseColor", c);
            else if (_ghostMat.HasProperty("_Color")) _ghostMat.SetColor("_Color", c);
            return;
        }

        // Keep the belt's own materials and mix the tint into their colour, so textures (the arrows) still show.
        foreach (var t in _tintTargets)
        {
            Color tinted = Color.Lerp(t.original, new Color(c.r, c.g, c.b, t.original.a), tintStrength);
            t.mat.SetColor(t.prop, tinted);
        }
    }

    /// <summary>Gives the ghost its own copies of its materials and remembers which ones have a colour to tint.</summary>
    void BuildTintTargets()
    {
        _tintTargets.Clear();
        _ghostInstances.Clear();

        foreach (var r in _ghost.GetComponentsInChildren<Renderer>())
        {
            Material[] mats = r.materials; // instances: changing them doesn't touch the real belt
            foreach (var m in mats)
            {
                _ghostInstances.Add(m);

                string prop = m.HasProperty("_BaseColor") ? "_BaseColor" : m.HasProperty("_Color") ? "_Color" : null;
                if (prop != null)
                    _tintTargets.Add(new TintTarget { mat = m, prop = prop, original = m.GetColor(prop) });
            }
        }
    }

    // ------------------------------------------------------------------ measuring

    static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v.sqrMagnitude > 1e-8f ? v.normalized : Vector3.forward;
    }

    /// <summary>
    /// Measure a belt from its renderers (call on an un-rotated instance). The belt's direction is the
    /// forward axis of its ConveyorForce object, i.e. the way it pushes items.
    /// </summary>
    /// <summary>
    /// Local height of the belt surface at the very end of the belt. Measured a little way in from the end, at two
    /// points, and extended out to the end in a straight line. Casting right at the end is unreliable: rounded ends,
    /// tilted slope colliders and end caps all give a height that is too low (or a miss).
    /// </summary>
    static float EndSurfaceHeightLocal(GameObject root, Vector3 endPoint, Vector3 inward, float length,
                                       Bounds localBounds, MeasureSource source)
    {
        float d1 = length * 0.15f, d2 = length * 0.35f;
        if (d2 > d1 + 1e-4f &&
            TrySurfaceLocal(root, endPoint + inward * d1, localBounds, source, out float y1) &&
            TrySurfaceLocal(root, endPoint + inward * d2, localBounds, source, out float y2))
            return y1 + (y1 - y2) * (d1 / (d2 - d1));

        if (TrySurfaceLocal(root, endPoint, localBounds, source, out float y0)) return y0;
        return localBounds.max.y; // no collider found: top of the bounds
    }

    /// <summary>Casts straight down onto the belt's own colliders at a local (x, z). Returns the local height hit.</summary>
    static bool TrySurfaceLocal(GameObject root, Vector3 localPoint, Bounds localBounds, MeasureSource source, out float localY)
    {
        localY = 0f;
        Transform t = root.transform;
        Physics.SyncTransforms(); // make sure a just-instantiated belt's colliders are where they appear

        Vector3 worldTop = t.TransformPoint(new Vector3(localPoint.x, localBounds.max.y + 1f, localPoint.z));
        var ray = new Ray(worldTop, -t.up);
        float reach = localBounds.size.y + 2f;

        bool wantTrigger = source == MeasureSource.TriggerColliders;
        float bestDist = float.MaxValue;
        foreach (var col in root.GetComponentsInChildren<Collider>())
        {
            if (!col.enabled || col.isTrigger != wantTrigger) continue;
            if (col.Raycast(ray, out RaycastHit hit, reach) && hit.distance < bestDist)
                bestDist = hit.distance;
        }

        if (bestDist == float.MaxValue) return false;
        localY = t.InverseTransformPoint(ray.GetPoint(bestDist)).y;
        return true;
    }

    static Piece Measure(GameObject root, float widthTrim, MeasureSource source)
    {
        Transform t = root.transform;

        var force = root.GetComponentInChildren<ConveyorForce>();
        Vector3 dir = t.InverseTransformDirection((force != null ? force.transform : t).forward);
        // Snapping uses the belt's physical shape only. Reversing the direction of travel doesn't change it.
        dir.y = 0f;
        dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.forward;

        bool has = false;
        Bounds b = default;

        void AddPoint(Vector3 worldPoint)
        {
            Vector3 local = t.InverseTransformPoint(worldPoint);
            if (!has) { b = new Bounds(local, Vector3.zero); has = true; }
            else b.Encapsulate(local);
        }

        void AddWorldBounds(Bounds wb)
        {
            Vector3 c = wb.center, e = wb.extents;
            for (int i = 0; i < 8; i++)
                AddPoint(c + new Vector3((i & 1) == 0 ? -e.x : e.x,
                                         (i & 2) == 0 ? -e.y : e.y,
                                         (i & 4) == 0 ? -e.z : e.z));
        }

        if (source != MeasureSource.Renderers)
        {
            bool wantTrigger = source == MeasureSource.TriggerColliders;
            foreach (var col in root.GetComponentsInChildren<Collider>())
            {
                if (col.isTrigger != wantTrigger) continue;

                if (col is BoxCollider box)
                {
                    // Use the box's real corners so a rotated child box isn't inflated.
                    Vector3 c = box.center, e = box.size * 0.5f;
                    for (int i = 0; i < 8; i++)
                        AddPoint(box.transform.TransformPoint(c + new Vector3((i & 1) == 0 ? -e.x : e.x,
                                                                              (i & 2) == 0 ? -e.y : e.y,
                                                                              (i & 4) == 0 ? -e.z : e.z)));
                }
                else
                {
                    AddWorldBounds(col.bounds);
                }
            }
        }

        if (!has) // no suitable colliders (or Renderers chosen): use the visible meshes
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>())
                AddWorldBounds(r.bounds);
        }
        if (!has) b = new Bounds(Vector3.zero, Vector3.one);

        float half = Mathf.Abs(dir.x) * b.extents.x + Mathf.Abs(dir.z) * b.extents.z;

        Vector3 backPoint = b.center - dir * half;
        Vector3 frontPoint = b.center + dir * half;

        // Heights of the belt surface at each end. For a flat belt both are the same; for a slope they differ,
        // so joins meet surface to surface instead of mid-height to mid-height (which sank slopes into the floor).
        float length = half * 2f;
        backPoint.y = EndSurfaceHeightLocal(root, backPoint, dir, length, b, source);
        frontPoint.y = EndSurfaceHeightLocal(root, frontPoint, -dir, length, b, source);

        return new Piece
        {
            transform = t,
            forwardLocal = dir,
            back = backPoint,
            front = frontPoint,
            bounds = b,
            halfLength = half,
            halfWidth = Mathf.Max(0.01f, Mathf.Abs(dir.z) * b.extents.x + Mathf.Abs(dir.x) * b.extents.z - widthTrim)
        };
    }

    // ------------------------------------------------------------------ overlap

    static readonly Vector2[] s_a = new Vector2[4];
    static readonly Vector2[] s_b = new Vector2[4];

    /// <summary>
    /// True if two belts overlap by more than 'tolerance' on the ground and in height.
    /// Belts that only touch end to end, or are at different heights, don't overlap.
    /// </summary>
    static bool Overlap(Piece a, Piece b, float tolerance)
    {
        Footprint(a, s_a, out float a0, out float a1);
        Footprint(b, s_b, out float b0, out float b1);

        if (Mathf.Min(a1, b1) - Mathf.Max(a0, b0) <= tolerance) return false;
        return RectsOverlap(s_a, s_b, tolerance);
    }

    static void Footprint(Piece p, Vector2[] corners, out float yMin, out float yMax)
    {
        Transform t = p.transform;
        Vector3 c = p.bounds.center;
        Vector3 along = p.forwardLocal * p.halfLength;
        Vector3 across = new Vector3(p.forwardLocal.z, 0f, -p.forwardLocal.x) * p.halfWidth;

        corners[0] = XZ(t.TransformPoint(c - along - across));
        corners[1] = XZ(t.TransformPoint(c + along - across));
        corners[2] = XZ(t.TransformPoint(c + along + across));
        corners[3] = XZ(t.TransformPoint(c - along + across));

        float ey = p.bounds.extents.y;
        float y0 = t.TransformPoint(new Vector3(c.x, c.y - ey, c.z)).y;
        float y1 = t.TransformPoint(new Vector3(c.x, c.y + ey, c.z)).y;
        yMin = Mathf.Min(y0, y1);
        yMax = Mathf.Max(y0, y1);
    }

    static Vector2 XZ(Vector3 v) { return new Vector2(v.x, v.z); }

    // Separating-axis test for two rectangles.
    static bool RectsOverlap(Vector2[] a, Vector2[] b, float tolerance)
    {
        for (int r = 0; r < 2; r++)
        {
            Vector2[] rect = r == 0 ? a : b;
            for (int k = 0; k < 2; k++)
            {
                Vector2 axis = k == 0 ? rect[1] - rect[0] : rect[3] - rect[0];
                if (axis.sqrMagnitude < 1e-8f) continue;
                axis.Normalize();

                Project(a, axis, out float minA, out float maxA);
                Project(b, axis, out float minB, out float maxB);
                if (Mathf.Min(maxA, maxB) - Mathf.Max(minA, minB) <= tolerance) return false;
            }
        }
        return true;
    }

    static void Project(Vector2[] pts, Vector2 axis, out float min, out float max)
    {
        min = float.MaxValue;
        max = float.MinValue;
        foreach (var p in pts)
        {
            float d = Vector2.Dot(p, axis);
            if (d < min) min = d;
            if (d > max) max = d;
        }
    }

    // ------------------------------------------------------------------ height

    /// <summary>
    /// Height of the floor at (x, z), found by casting straight down (same idea as the spawner's drop marker).
    /// Ignores triggers, loose rigidbodies (items) and other belts, so a belt never stacks on top of another one.
    /// Falls back to groundHeight if nothing is hit.
    /// </summary>
    bool IsPlacedBelt(Transform t)
    {
        foreach (var p in _placed)
            if (p.transform != null && t.IsChildOf(p.transform)) return true;
        return false;
    }

    float FindSurfaceY(float x, float z)
    {
        var origin = new Vector3(x, groundHeight + probeHeight, z);
        float bestDist = float.MaxValue;
        float surfaceY = groundHeight;

        foreach (var h in Physics.RaycastAll(origin, Vector3.down, probeHeight + 100f,
                                             surfaceMask, QueryTriggerInteraction.Ignore))
        {
            if (h.rigidbody != null && !h.rigidbody.isKinematic) continue; // a product, not floor
            if (IsPlacedBelt(h.collider.transform)) continue;                // belts are never floor
            if (h.distance < bestDist)
            {
                bestDist = h.distance;
                surfaceY = h.point.y;
            }
        }
        return surfaceY;
    }

    // ------------------------------------------------------------------ delete / picking

    /// <summary>The placed belt under the mouse cursor (the trigger box counts too), or null.</summary>
    Piece PickPlacedPiece()
    {
        var ray = cam.ScreenPointToRay(MousePos);
        Piece best = null;
        float bestDist = float.MaxValue;

        foreach (var hit in Physics.RaycastAll(ray, 500f, ~0, QueryTriggerInteraction.Collide))
        {
            if (hit.distance >= bestDist) continue;
            foreach (var p in _placed)
            {
                if (hit.collider.transform.IsChildOf(p.transform))
                {
                    best = p;
                    bestDist = hit.distance;
                    break;
                }
            }
        }
        return best;
    }

    void DeleteUnderMouse()
    {
        // Spawners first: they hover above belts and may have no collider, so pick them by screen distance.
        GameObject spawner = PickSpawner();
        if (spawner != null)
        {
            _spawners.Remove(spawner);
            Destroy(spawner);
            return;
        }

        Piece best = PickPlacedPiece();
        if (best != null)
        {
            _placed.Remove(best);
            Destroy(best.transform.gameObject);
        }
    }

    /// <summary>Reverse the ghost's direction (arrows included) and its snapping ends.</summary>
    void FlipGhost()
    {
        if (_ghost == null || _ghostPiece == null) return;
        _ghost.GetComponentInChildren<ConveyorForce>()?.Reverse();
    }

    void ReverseUnderMouse()
    {
        Piece best = PickPlacedPiece();
        if (best == null) return;

        var force = best.transform.GetComponentInChildren<ConveyorForce>();
        if (force != null) force.Reverse();
    }

    // ------------------------------------------------------------------ hover highlight

    /// <summary>Shows a tinted box over the belt (or spawner) that delete / reverse would act on.</summary>
    void UpdateHighlight(bool overUI)
    {
        bool active = (_mode == Mode.Delete || _mode == Mode.Reverse) && !overUI && cam != null;
        if (!active)
        {
            if (_highlight != null) _highlight.SetActive(false);
            return;
        }

        Color tint = _mode == Mode.Delete ? deleteTint : reverseTint;
        Vector3 centre = default, size = default;
        Quaternion rot = Quaternion.identity;
        bool found = false;

        if (_mode == Mode.Delete)
        {
            GameObject spawner = PickSpawner();
            if (spawner != null)
            {
                Bounds b = default;
                bool has = false;
                foreach (var r in spawner.GetComponentsInChildren<Renderer>())
                {
                    if (!has) { b = r.bounds; has = true; }
                    else b.Encapsulate(r.bounds);
                }
                if (!has) b = new Bounds(spawner.transform.position, Vector3.one * 0.4f);
                centre = b.center;
                size = b.size * 1.05f;
                found = true;
            }
        }

        if (!found)
        {
            Piece p = PickPlacedPiece();
            if (p != null && p.transform != null)
            {
                Transform t = p.transform;
                centre = t.TransformPoint(p.bounds.center);
                rot = t.rotation;
                size = Vector3.Scale(p.bounds.size, t.lossyScale) * 1.03f;
                found = true;
            }
        }

        if (!found)
        {
            if (_highlight != null) _highlight.SetActive(false);
            return;
        }

        EnsureHighlight();
        _highlightMat.color = tint;
        _highlight.transform.SetPositionAndRotation(centre, rot);
        _highlight.transform.localScale = size;
        _highlight.SetActive(true);
    }

    void EnsureHighlight()
    {
        if (_highlight != null) return;

        _highlight = GameObject.CreatePrimitive(PrimitiveType.Cube);
        _highlight.name = "PlacerHighlight";
        Destroy(_highlight.GetComponent<Collider>()); // must never block picking or push items

        // Sprites/Default is an unlit, see-through shader that works in any render pipeline.
        _highlightMat = new Material(Shader.Find("Sprites/Default"));
        var r = _highlight.GetComponent<Renderer>();
        r.sharedMaterial = _highlightMat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
    }

    void OnDestroy()
    {
        if (_highlight != null) Destroy(_highlight);
        if (_highlightMat != null) Destroy(_highlightMat);
        foreach (var t in _guiTex) if (t != null) Destroy(t);
        _guiTex.Clear();
    }

    GameObject PickSpawner()
    {
        GameObject best = null;
        float bestDist = spawnerPickRadius;
        foreach (var sp in _spawners)
        {
            if (sp == null) continue;
            Vector3 screen = cam.WorldToScreenPoint(sp.transform.position);
            if (screen.z < 0f) continue; // behind the camera
            float d = Vector2.Distance(new Vector2(screen.x, screen.y), new Vector2(MousePos.x, MousePos.y));
            if (d < bestDist)
            {
                bestDist = d;
                best = sp;
            }
        }
        return best;
    }

    /// <summary>
    /// First solid surface under the mouse cursor (ignores triggers and loose rigidbodies such as items).
    /// Falls back to the ground plane if the cursor isn't over anything solid.
    /// </summary>
    bool CursorSurface(out Vector3 point, out float surfaceY)
    {
        point = default;
        surfaceY = groundHeight;

        var ray = cam.ScreenPointToRay(MousePos);
        float best = float.MaxValue;
        bool found = false;

        foreach (var h in Physics.RaycastAll(ray, 1000f, surfaceMask, QueryTriggerInteraction.Ignore))
        {
            if (h.rigidbody != null && !h.rigidbody.isKinematic) continue; // a product, not floor
            if (h.distance < best)
            {
                best = h.distance;
                point = h.point;
                surfaceY = h.point.y;
                found = true;
            }
        }

        if (found) return true;

        if (!GroundPoint(groundHeight, out point)) return false;
        surfaceY = groundHeight;
        return true;
    }

    bool GroundPoint(float planeY, out Vector3 point)
    {
        point = default;
        var plane = new Plane(Vector3.up, new Vector3(0f, planeY, 0f));
        var ray = cam.ScreenPointToRay(MousePos);
        if (!plane.Raycast(ray, out float enter)) return false;
        point = ray.GetPoint(enter);
        return true;
    }

    // ------------------------------------------------------------------ selection / ghost

    void Select(int index)
    {
        _selected = index;
        _mode = Mode.Place;
        _planeY = groundHeight;
        DestroyGhost();

        var prefab = options[index].prefab;
        if (prefab == null) return;

        _ghost = Instantiate(prefab);

        // Item spawners are placed freely: no measuring, snapping or overlap checks.
        _isSpawner = prefab.GetComponentInChildren<ItemSpawner>() != null;
        if (_isSpawner)
        {
            // The ghost keeps its drop marker (so you can see where items will fall) but must not spawn items.
            foreach (var sp in _ghost.GetComponentsInChildren<ItemSpawner>()) sp.spawning = false;
            _ghostPiece = null;
        }
        else
        {
            // Measure the belt while it is still un-rotated and its colliders/renderers are untouched.
            _ghostPiece = Measure(_ghost, widthTrim, measureFrom);
            if (_flipped) FlipGhost();
        }
        _baseLift = liftToRestOnSurface ? ComputeBaseLift(_ghost) : 0f;

        // The ghost must not push anything or block raycasts:
        // disabling colliders stops OnTriggerStay on ConveyorForce from firing.
        foreach (var c in _ghost.GetComponentsInChildren<Collider>()) c.enabled = false;
        foreach (var rb in _ghost.GetComponentsInChildren<Rigidbody>()) rb.isKinematic = true;

        _ghostValid = true;
        if (ghostMaterial != null)
        {
            _ghostMat = new Material(ghostMaterial);

            foreach (var r in _ghost.GetComponentsInChildren<Renderer>())
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = _ghostMat;
                r.sharedMaterials = mats;
            }
        }
        else
        {
            BuildTintTargets();
        }
        TintGhost(validColor);
    }

    /// <summary>
    /// How far the pivot sits above the lowest point of the model. Uses renderer bounds, or the solid
    /// (non-trigger) colliders if there are no renderers. Must run before the ghost's colliders are disabled.
    /// Only vertical extent matters, so yaw rotation doesn't change it.
    /// </summary>
    static float ComputeBaseLift(GameObject instance)
    {
        bool has = false;
        float minY = 0f;

        foreach (var r in instance.GetComponentsInChildren<Renderer>())
        {
            if (!has || r.bounds.min.y < minY) minY = r.bounds.min.y;
            has = true;
        }

        if (!has)
        {
            foreach (var c in instance.GetComponentsInChildren<Collider>())
            {
                if (c.isTrigger) continue;
                if (!has || c.bounds.min.y < minY) minY = c.bounds.min.y;
                has = true;
            }
        }

        return has ? instance.transform.position.y - minY : 0f;
    }

    void SetMode(Mode mode)
    {
        _mode = mode;
        if (mode != Mode.Place)
        {
            DestroyGhost();
            _selected = -1;
        }
    }

    void DestroyGhost()
    {
        _blockInfo = "";
        if (_ghost != null) Destroy(_ghost);
        _ghost = null;
        if (_ghostMat != null) Destroy(_ghostMat);
        _ghostMat = null;

        foreach (var m in _ghostInstances)
            if (m != null) Destroy(m);
        _ghostInstances.Clear();
        _tintTargets.Clear();
    }

    // ------------------------------------------------------------------ debug

    void OnDrawGizmos()
    {
        if (!drawBounds) return;

        Gizmos.color = Color.cyan;
        foreach (var p in _placed)
            DrawPiece(p);

        if (_ghost != null && _ghost.activeSelf && _ghostPiece != null)
        {
            Gizmos.color = _ghostValid ? Color.green : Color.red;
            DrawPiece(_ghostPiece.CopyFor(_ghost.transform));
        }
    }

    static void DrawPiece(Piece p)
    {
        if (p.transform == null) return;

        // The box actually used for snapping and overlap checks (width already trimmed).
        Gizmos.matrix = p.transform.localToWorldMatrix *
                        Matrix4x4.TRS(p.bounds.center, Quaternion.LookRotation(p.forwardLocal, Vector3.up), Vector3.one);
        Gizmos.DrawWireCube(Vector3.zero, new Vector3(p.halfWidth * 2f, p.bounds.size.y, p.halfLength * 2f));

        Gizmos.matrix = p.transform.localToWorldMatrix;
        Gizmos.DrawSphere(p.back, 0.05f);
        Gizmos.DrawSphere(p.front, 0.05f);
        Gizmos.matrix = Matrix4x4.identity;
    }

    // ------------------------------------------------------------------ HUD

    static readonly Color ColTeal = new Color(0.16f, 0.72f, 0.68f);
    static readonly Color ColOrange = new Color(0.98f, 0.60f, 0.15f);
    static readonly Color ColRed = new Color(0.90f, 0.30f, 0.30f);
    static readonly Color ColYellow = new Color(0.95f, 0.80f, 0.20f);
    static readonly Color ColPanel = new Color(0.09f, 0.10f, 0.12f, 0.92f);
    static readonly Color ColBtn = new Color(0.17f, 0.19f, 0.22f, 1f);
    static readonly Color ColBtnHot = new Color(0.25f, 0.28f, 0.32f, 1f);
    static readonly Color ColText = new Color(0.92f, 0.94f, 0.96f);
    static readonly Color ColDim = new Color(0.60f, 0.65f, 0.70f);

    readonly List<Texture2D> _guiTex = new List<Texture2D>();
    GUIStyle _sPanel, _sTitle, _sSection, _sStatus, _sBtn, _sBtnTeal, _sBtnOrange, _sBtnRed, _sBtnYellow,
             _sBadge, _sToggle, _sHint, _sWarn, _sValue, _sTrack, _sFill, _sThumb, _sEmpty;
    bool[] _optSpawner;
    int[] _order; // option indices in display order (conveyors, then spawners); hotkey N selects _order[N-1]
    float _panelH;
    float _quitConfirmUntil;
    int _windowedW = 1280, _windowedH = 720;

    /// <summary>Borderless fullscreen at the desktop resolution, or a normal window. Only has an effect in a build.</summary>
    void SetFullscreen(bool on)
    {
        if (on)
        {
            if (!Screen.fullScreen) { _windowedW = Screen.width; _windowedH = Screen.height; }
            Resolution res = Screen.currentResolution;
            Screen.SetResolution(res.width, res.height, FullScreenMode.FullScreenWindow);
        }
        else
        {
            Screen.SetResolution(_windowedW, _windowedH, FullScreenMode.Windowed);
        }
    }

    static void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    Texture2D RoundTex(Color c, int r)
    {
        int size = r * 2 + 2;
        var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
        t.wrapMode = TextureWrapMode.Clamp;
        t.filterMode = FilterMode.Bilinear;
        var px = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float cx = Mathf.Clamp(x + 0.5f, r, size - r);
                float cy = Mathf.Clamp(y + 0.5f, r, size - r);
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy));
                float a = Mathf.Clamp01(r - d + 0.5f);
                px[y * size + x] = new Color(c.r, c.g, c.b, c.a * a);
            }
        t.SetPixels(px);
        t.Apply();
        _guiTex.Add(t);
        return t;
    }

    GUIStyle MakeButton(Color bg, Color hot, Color text)
    {
        const int r = 8;
        var st = new GUIStyle(GUI.skin.button);
        var a = RoundTex(bg, r);
        var b = RoundTex(hot, r);
        st.normal.background = a; st.focused.background = a;
        st.hover.background = b; st.active.background = b;
        st.normal.textColor = st.hover.textColor = st.active.textColor = st.focused.textColor = text;
        st.border = new RectOffset(r, r, r, r);
        st.alignment = TextAnchor.MiddleLeft;
        st.padding = new RectOffset(12, 34, 0, 0);
        st.margin = new RectOffset(0, 0, 3, 0);
        st.fontSize = 13;
        st.fixedHeight = 30;
        return st;
    }

    void BuildStyles()
    {
        if (_sPanel != null) return;

        _sPanel = new GUIStyle(GUI.skin.box) { border = new RectOffset(14, 14, 14, 14) };
        _sPanel.normal.background = RoundTex(ColPanel, 14);

        _sTitle = new GUIStyle(GUI.skin.label) { fontSize = 17, fontStyle = FontStyle.Bold };
        _sTitle.normal.textColor = ColText;

        _sSection = new GUIStyle(GUI.skin.label) { fontSize = 11, fontStyle = FontStyle.Bold };
        _sSection.margin = new RectOffset(0, 0, 10, 2);

        _sStatus = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true };
        _sStatus.normal.textColor = ColDim;

        _sBtn = MakeButton(ColBtn, ColBtnHot, ColText);
        _sBtnTeal = MakeButton(ColTeal, ColTeal * 1.1f, Color.black);
        _sBtnOrange = MakeButton(ColOrange, ColOrange * 1.1f, Color.black);
        _sBtnRed = MakeButton(ColRed, ColRed * 1.1f, Color.white);
        _sBtnYellow = MakeButton(ColYellow, ColYellow * 1.1f, Color.black);

        _sBadge = new GUIStyle(GUI.skin.label) { fontSize = 11, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight };

        _sToggle = new GUIStyle(GUI.skin.toggle) { fontSize = 12 };
        _sToggle.normal.textColor = _sToggle.hover.textColor = _sToggle.onNormal.textColor = _sToggle.onHover.textColor = ColText;

        _sHint = new GUIStyle(GUI.skin.label) { fontSize = 11, wordWrap = true };
        _sHint.normal.textColor = ColDim;

        _sWarn = new GUIStyle(GUI.skin.box)
        {
            fontSize = 12,
            wordWrap = true,
            alignment = TextAnchor.MiddleLeft,
            border = new RectOffset(8, 8, 8, 8),
            padding = new RectOffset(10, 10, 8, 8)
        };
        _sWarn.normal.background = RoundTex(new Color(0.55f, 0.15f, 0.15f, 0.9f), 8);
        _sWarn.normal.textColor = Color.white;

        _sValue = new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight };
        _sValue.normal.textColor = ColText;

        _sTrack = new GUIStyle { border = new RectOffset(3, 3, 3, 3) };
        _sTrack.normal.background = RoundTex(ColBtnHot, 3);
        _sFill = new GUIStyle { border = new RectOffset(3, 3, 3, 3) };
        _sFill.normal.background = RoundTex(ColTeal, 3);

        _sEmpty = new GUIStyle();
        _sThumb = new GUIStyle { fixedWidth = 18, fixedHeight = 18 };
        var th = RoundTex(Color.white, 8);
        _sThumb.normal.background = th; _sThumb.hover.background = th; _sThumb.active.background = th;
    }

    void ClassifyOptions()
    {
        int n = options != null ? options.Length : 0;
        if (_optSpawner != null && _optSpawner.Length == n) return;
        _optSpawner = new bool[n];
        for (int i = 0; i < n; i++)
            _optSpawner[i] = options[i].prefab != null && options[i].prefab.GetComponentInChildren<ItemSpawner>() != null;

        var order = new List<int>();
        for (int i = 0; i < n; i++) if (!_optSpawner[i]) order.Add(i);
        for (int i = 0; i < n; i++) if (_optSpawner[i]) order.Add(i);
        _order = order.ToArray();
    }

    void SectionLabel(string text, Color accent)
    {
        _sSection.normal.textColor = accent;
        GUILayout.Label(text, _sSection);
    }

    void OptionButtons(bool spawners, GUIStyle activeStyle)
    {
        for (int k = 0; k < _order.Length; k++)
        {
            int i = _order[k];
            if (_optSpawner[i] != spawners) continue;
            bool active = _mode == Mode.Place && _selected == i;
            if (GUILayout.Button(options[i].name, active ? activeStyle : _sBtn)) Select(i);

            Rect r = GUILayoutUtility.GetLastRect();
            if (k < 9 && Event.current.type == EventType.Repaint)
            {
                _sBadge.normal.textColor = active ? new Color(0, 0, 0, 0.6f) : ColDim;
                _sBadge.Draw(new Rect(r.x, r.y, r.width - 12, r.height), (k + 1).ToString(), false, false, false, false);
            }
        }
    }

    void OnGUI()
    {
        BuildStyles();
        ClassifyOptions();

        float s = Mathf.Clamp(Screen.height / 900f, 1f, 2f);
        var oldMatrix = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));

        const float w = 250f;
        float h = _panelH > 0f ? _panelH : 320f;
        var r = new Rect(12, 12, w, h);
        _panel = new Rect(r.x * s, r.y * s, r.width * s, r.height * s);

        GUI.Box(r, GUIContent.none, _sPanel);
        GUILayout.BeginArea(new Rect(r.x + 14, r.y + 12, w - 28, 3000));
        GUILayout.BeginVertical();

        GUILayout.Label("Conveyor Builder", _sTitle);
        string status =
            _mode == Mode.Place && _selected >= 0 && _selected < options.Length ? "Placing: " + options[_selected].name :
            _mode == Mode.Delete ? "Delete: click a belt or spawner" :
            _mode == Mode.Reverse ? "Reverse: click a belt" :
            "Pick something to place";
        GUILayout.Label(status, _sStatus);

        bool anyBelt = false, anySpawner = false;
        for (int i = 0; i < _optSpawner.Length; i++) { if (_optSpawner[i]) anySpawner = true; else anyBelt = true; }

        if (anyBelt)
        {
            SectionLabel("CONVEYORS", ColTeal);
            OptionButtons(false, _sBtnTeal);
        }
        if (anySpawner)
        {
            SectionLabel("ITEM SPAWNERS", ColOrange);
            OptionButtons(true, _sBtnOrange);
        }

        SectionLabel("TOOLS", ColDim);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Delete  X", _mode == Mode.Delete ? _sBtnRed : _sBtn)) SetMode(Mode.Delete);
        GUILayout.Space(6);
        if (GUILayout.Button("Reverse  V", _mode == Mode.Reverse ? _sBtnYellow : _sBtn)) SetMode(Mode.Reverse);
        GUILayout.EndHorizontal();

        var m = ConveyorManager.Instance;
        if (m != null)
        {
            SectionLabel("BELT SPEED", ColDim);
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.Label(m.speed.ToString("0.0"), _sValue);
            GUILayout.EndHorizontal();

            float max = Mathf.Max(0.1f, m.maxSpeed);
            Rect sr = GUILayoutUtility.GetRect(0, 18, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint)
            {
                var track = new Rect(sr.x, sr.y + 6, sr.width, 6);
                GUI.Box(track, GUIContent.none, _sTrack);
                float t = Mathf.Clamp01(m.speed / max);
                float fw = Mathf.Max(6f, (sr.width - 18f) * t + 9f);
                GUI.Box(new Rect(track.x, track.y, fw, 6), GUIContent.none, _sFill);
            }
            m.speed = GUI.HorizontalSlider(sr, m.speed, 0f, max, _sEmpty, _sThumb);
        }

        SectionLabel("OPTIONS", ColDim);
        moveCursorToSnap = GUILayout.Toggle(moveCursorToSnap, " Move cursor to next snap", _sToggle);
        bool fs = GUILayout.Toggle(Screen.fullScreen, " Fullscreen (F11)", _sToggle);
        if (fs != Screen.fullScreen) SetFullscreen(fs);

        SectionLabel("CONTROLS", ColDim);
        GUILayout.Label("R rotate   F flip direction\nX delete   V reverse   Esc cancel", _sHint);

        SectionLabel("CAMERA", ColDim);
        GUILayout.Label("W A S D move\nShift up   Ctrl down\nHold right mouse + move to look\nScroll wheel zoom", _sHint);

        GUILayout.Space(10);
        bool confirming = Time.unscaledTime < _quitConfirmUntil;
        if (GUILayout.Button(confirming ? "Click again to quit" : "Quit game", confirming ? _sBtnRed : _sBtn))
        {
            if (confirming) QuitGame();
            else _quitConfirmUntil = Time.unscaledTime + 3f;
        }

        if (_blockInfo.Length > 0)
        {
            GUILayout.Space(6);
            GUILayout.Label(_blockInfo, _sWarn);
        }

        GUILayout.EndVertical();
        Rect used = GUILayoutUtility.GetLastRect();
        GUILayout.EndArea();

        if (Event.current.type == EventType.Repaint) _panelH = used.yMax + 24f;
        GUI.matrix = oldMatrix;
    }
}