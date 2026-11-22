using System.Collections.Generic;
using UnityEngine;

public class ConveyorForce : MonoBehaviour
{
    [Header("Direction")]
    [Tooltip("Belt carries items backwards. Use Reverse() / SetReversed() from code; this also works as a starting value.")]
    public bool reversed = false;

    [Header("Belt visuals (Shader Graph material properties that get their X sign flipped on reverse)")]
    [Tooltip("The part of the prefab whose materials are updated. Empty = this object's root.")]
    public Transform visualRoot;
    [Tooltip("The arrow Texture2D property of the graph (Reference name or display name). Its Tiling X is flipped " +
             "(this is the Tiling / Offset shown under the texture slot in the material).")]
    public string textureProperty = "_ArrowTexture";
    [Tooltip("Only if your graph ALSO has a separate Vector2 tiling property. Leave as is otherwise.")]
    public string tilingProperty = "_Tiling";
    [Tooltip("Reference name OR display name of the Direction property (Vector2). Its X is scaled with the belt speed " +
             "(and its sign follows the belt's direction), so the arrows move faster when the belts do.")]
    public string directionProperty = "_Direction";

    public float centerStrength = 5f; // 0 = don't pull items toward the belt's centre line

    // Shared by all conveyors: the last physics step in which each item was moved
    static readonly Dictionary<Rigidbody, float> s_lastMoved = new Dictionary<Rigidbody, float>();

    // The Direction X the material was authored with, per material. The live value is baseX * speed ratio.
    class DirEntry { public Material mat; public string prop; public float baseX; }
    readonly List<DirEntry> _dirs = new List<DirEntry>();
    float _appliedRatio = float.NaN;

    // Resolved once in Awake, while the belt is still a top-level object (the placer parents it afterwards),
    // so a shared parent folder is never mistaken for the belt's visuals.
    Transform _visualRoot;

    void Awake()
    {
        _visualRoot = visualRoot != null ? visualRoot : transform.root;
        CacheDirection();

        // A prefab saved as 'reversed' needs its visuals flipped once to match.
        if (reversed) FlipVisuals();
        ApplySpeed();
    }

    void Update()
    {
        // Arrows follow the belt speed (set from the placer panel's slider or any script).
        if (ConveyorManager.Instance == null) return;
        if (!Mathf.Approximately(ConveyorManager.Instance.ArrowFactor, _appliedRatio)) ApplySpeed();
    }

    void CacheDirection()
    {
        _dirs.Clear();
        Transform root = _visualRoot != null ? _visualRoot : (visualRoot != null ? visualRoot : transform.root);

        foreach (var r in root.GetComponentsInChildren<Renderer>())
        {
            foreach (var m in r.materials) // per-renderer copies, so other belts aren't affected
            {
                string prop = FindProperty(m, directionProperty, "Direction");
                if (prop == null) continue;
                _dirs.Add(new DirEntry { mat = m, prop = prop, baseX = m.GetVector(prop).x });
            }
        }
    }

    /// <summary>Sets Direction X on every material to (authored X) x (speed / starting speed) x (arrow match setting).</summary>
    void ApplySpeed()
    {
        float ratio = ConveyorManager.Instance != null ? ConveyorManager.Instance.ArrowFactor : 1f;
        _appliedRatio = ratio;

        foreach (var e in _dirs)
        {
            if (e.mat == null) continue;
            Vector4 v = e.mat.GetVector(e.prop);
            v.x = e.baseX * ratio;
            e.mat.SetVector(e.prop, v);
        }
    }

    /// <summary>Flip the belt's direction of travel and the arrows to match.</summary>
    public void Reverse() { SetReversed(!reversed); }

    public void SetReversed(bool value)
    {
        if (value == reversed) return;
        reversed = value;
        FlipVisuals();
    }

    /// <summary>Negates the X of Tiling (mirrors the arrows) and of Direction (reverses the scroll) on this belt.</summary>
    void FlipVisuals()
    {
        Transform root = _visualRoot != null ? _visualRoot : (visualRoot != null ? visualRoot : transform.root);

        foreach (var r in root.GetComponentsInChildren<Renderer>())
        {
            Material[] mats = r.materials; // per-renderer copies, so other belts aren't affected
            foreach (var m in mats)
            {
                FlipTextureTilingX(m);
                FlipX(m, tilingProperty, "Tiling");
            }
        }

        // Direction is kept as (authored X) x (speed ratio), so flip the authored value and re-apply.
        foreach (var e in _dirs) e.baseX = -e.baseX;
        ApplySpeed();
    }

    /// <summary>Negates the X tiling of the arrow texture (the Tiling shown under the texture slot).</summary>
    void FlipTextureTilingX(Material m)
    {
        string prop = FindTextureProperty(m, textureProperty, "ArrowTexture");
        if (prop == null) return;

        Vector2 scale = m.GetTextureScale(prop);
        scale.x = -scale.x;
        m.SetTextureScale(prop, scale);
    }

    static string FindTextureProperty(Material m, string reference, string displayName)
    {
        Shader sh = m.shader;
        if (sh == null) return null;

        if (!string.IsNullOrEmpty(reference) && m.HasProperty(reference)) return reference;

        int count = sh.GetPropertyCount();
        for (int i = 0; i < count; i++)
        {
            if (sh.GetPropertyType(i) != UnityEngine.Rendering.ShaderPropertyType.Texture) continue;
            if (string.Equals(sh.GetPropertyDescription(i), displayName, System.StringComparison.OrdinalIgnoreCase) ||
                string.Equals(sh.GetPropertyDescription(i), reference, System.StringComparison.OrdinalIgnoreCase))
                return sh.GetPropertyName(i);
        }

        // Last resort: the material's only texture.
        string only = null;
        for (int i = 0; i < count; i++)
        {
            if (sh.GetPropertyType(i) != UnityEngine.Rendering.ShaderPropertyType.Texture) continue;
            if (only != null) return null; // more than one, can't guess
            only = sh.GetPropertyName(i);
        }
        return only;
    }

    static void FlipX(Material m, string name, string displayName)
    {
        string prop = FindProperty(m, name, displayName);
        if (prop == null) return;

        Vector4 v = m.GetVector(prop);
        v.x = -v.x;
        m.SetVector(prop, v);
    }

    /// <summary>Finds a Vector property by reference name, or failing that by its display name in the graph.</summary>
    static string FindProperty(Material m, string reference, string displayName)
    {
        Shader sh = m.shader;
        if (sh == null) return null;

        if (!string.IsNullOrEmpty(reference) && m.HasProperty(reference)) return reference;

        int count = sh.GetPropertyCount();
        for (int i = 0; i < count; i++)
        {
            if (sh.GetPropertyType(i) != UnityEngine.Rendering.ShaderPropertyType.Vector) continue;
            if (string.Equals(sh.GetPropertyDescription(i), displayName, System.StringComparison.OrdinalIgnoreCase) ||
                string.Equals(sh.GetPropertyDescription(i), reference, System.StringComparison.OrdinalIgnoreCase))
                return sh.GetPropertyName(i);
        }
        return null;
    }

    private void OnTriggerStay(Collider other)
    {
        if (!other.CompareTag("Moveable"))
            return;

        Rigidbody rb = other.attachedRigidbody;
        if (rb == null)
            return;

        // Another conveyor (or another collider on this item) already moved it this step
        if (s_lastMoved.TryGetValue(rb, out float last) && last == Time.fixedTime)
            return;
        s_lastMoved[rb] = Time.fixedTime;

        // Drop entries for destroyed items occasionally so the dictionary doesn't grow forever
        if (s_lastMoved.Count > 256)
        {
            var dead = new List<Rigidbody>();
            foreach (var key in s_lastMoved.Keys)
                if (key == null) dead.Add(key);
            foreach (var key in dead) s_lastMoved.Remove(key);
        }

        float speed = ConveyorManager.Instance.speed;
        float dt = Time.fixedDeltaTime;

        Vector3 forward = reversed ? -transform.forward : transform.forward;
        Vector3 start = rb.position;
        Vector3 target = start + forward * (speed * dt);

        if (centerStrength > 0f)
        {
            Vector3 toCentre = Vector3.Project(transform.position - start, transform.right);
            Vector3 centred = start + toCentre;
            target = Vector3.Lerp(target, new Vector3(centred.x, target.y, centred.z) + forward * (speed * dt),
                                  centerStrength * dt);
        }

        rb.MovePosition(target);
    }
}