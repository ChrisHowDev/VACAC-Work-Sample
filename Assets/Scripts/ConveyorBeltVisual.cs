using UnityEngine;

/// <summary>
/// Scrolls a texture along a conveyor belt so you can see which way it moves.
/// Edit 'speed' and 'uvDirection' in the Inspector. That's it.
/// </summary>
public class ConveyorBeltVisual : MonoBehaviour
{
    [Header("Target")]
    [Tooltip("Renderer of the belt surface. Auto-found in children if empty.")]
    public Renderer beltRenderer;
    [Tooltip("Which material slot is the belt surface.")]
    public int materialIndex = 0;

    [Header("Movement")]
    [Tooltip("Texture repeats per second.")]
    public float speed = 0.5f;
    [Tooltip("Direction the pattern moves in texture space:\n(0,1) = +V   (0,-1) = -V   (1,0) = +U   (-1,0) = -U")]
    public Vector2 uvDirection = new Vector2(0f, 1f);

    [Header("Shader Graph (leave both empty for a normal material)")]
    [Tooltip("Reference name of your graph's Vector2 tiling property, e.g. _Tiling")]
    public string tilingProperty = "";
    [Tooltip("Reference name of your graph's Vector2 offset property, e.g. _Offset")]
    public string offsetProperty = "";

    [Header("Your own arrow texture (untick Generate Chevrons to use the texture on your material)")]
    [Tooltip("Which way the arrows in YOUR png point, in texture space: (0,1) = up/+V, (0,-1) = down/-V, (1,0) = right/+U, (-1,0) = left/-U.\nIf UV Direction points the opposite way, the texture is flipped automatically so the arrows still point the way it moves.")]
    public Vector2 textureArrowPoints = new Vector2(0f, 1f);

    [Header("Generated chevrons")]
    public bool generateChevrons = true;
    public Color baseColor = new Color(0.12f, 0.12f, 0.14f, 1f);
    public Color arrowColor = new Color(1f, 0.8f, 0.1f, 1f);
    [Tooltip("How many chevrons fit along the belt.")]
    [Min(1f)] public float repeatsAlongBelt = 4f;

    Material _mat;
    Texture2D _texture;
    Vector2 _offset;
    Vector2 _scrollDir;

    void Awake()
    {
        if (beltRenderer == null) beltRenderer = GetComponentInChildren<Renderer>();
        if (beltRenderer == null)
        {
            enabled = false;
            return;
        }

        var mats = beltRenderer.materials; // per-renderer copy, so other belts aren't affected
        _mat = mats[Mathf.Clamp(materialIndex, 0, mats.Length - 1)];

        // Shader Graph materials: pick up the usual property names automatically if they exist.
        if (string.IsNullOrEmpty(tilingProperty) && _mat.HasProperty("_Tiling")) tilingProperty = "_Tiling";
        if (string.IsNullOrEmpty(offsetProperty) && _mat.HasProperty("_Offset")) offsetProperty = "_Offset";

        _scrollDir = uvDirection.normalized;

        bool usesShaderGraph = !string.IsNullOrEmpty(tilingProperty) || !string.IsNullOrEmpty(offsetProperty);
        if (generateChevrons && !usesShaderGraph)
        {
            bool alongU = Mathf.Abs(uvDirection.x) > Mathf.Abs(uvDirection.y);
            bool positive = (alongU ? uvDirection.x : uvDirection.y) >= 0f;

            _texture = BuildChevrons(alongU, positive);
            _mat.mainTexture = _texture;
            _mat.color = Color.white;
            _mat.mainTextureScale = alongU ? new Vector2(repeatsAlongBelt, 1f) : new Vector2(1f, repeatsAlongBelt);
        }
        else
        {
            FlipTextureToMatchDirection();
        }
    }

    /// <summary>
    /// Keeps the arrows in a custom texture pointing the way the pattern moves. A negative tiling value
    /// mirrors the texture, so if UV Direction is opposite to the way the arrows point, flip that axis.
    /// The texture's Wrap Mode must be Repeat.
    /// </summary>
    void FlipTextureToMatchDirection()
    {
        bool moveAlongU = Mathf.Abs(uvDirection.x) > Mathf.Abs(uvDirection.y);
        bool arrowAlongU = Mathf.Abs(textureArrowPoints.x) > Mathf.Abs(textureArrowPoints.y);

        if (moveAlongU != arrowAlongU)
            Debug.LogWarning($"{name}: your arrows point along {(arrowAlongU ? "U" : "V")} but UV Direction moves along " +
                             $"{(moveAlongU ? "U" : "V")}. Rotate the png 90 degrees or change one of the two settings.", this);

        float moveSign = (moveAlongU ? uvDirection.x : uvDirection.y) >= 0f ? 1f : -1f;
        float arrowSign = (arrowAlongU ? textureArrowPoints.x : textureArrowPoints.y) >= 0f ? 1f : -1f;
        float flip = moveSign == arrowSign ? 1f : -1f;

        Vector2 scale = GetTiling();
        if (moveAlongU) scale.x = Mathf.Abs(scale.x) * flip;
        else scale.y = Mathf.Abs(scale.y) * flip;
        SetTiling(scale);

        // Mirroring (negative tiling) also reverses how the offset moves the pattern, so the offset must
        // always run in the direction the arrows point in the ORIGINAL texture. The mirror does the rest.
        _scrollDir = moveAlongU ? new Vector2(arrowSign, 0f) : new Vector2(0f, arrowSign);
    }

    void Update()
    {
        if (uvDirection.sqrMagnitude < 1e-6f) return;

        // Subtracting makes the pattern move toward +uvDirection.
        _offset -= _scrollDir * (speed * Time.deltaTime);
        _offset.x = Mathf.Repeat(_offset.x, 1f);
        _offset.y = Mathf.Repeat(_offset.y, 1f);

        SetOffset(_offset);
    }

    Vector2 GetTiling()
    {
        return string.IsNullOrEmpty(tilingProperty) ? _mat.mainTextureScale : (Vector2)_mat.GetVector(tilingProperty);
    }

    void SetTiling(Vector2 v)
    {
        if (string.IsNullOrEmpty(tilingProperty)) _mat.mainTextureScale = v;
        else _mat.SetVector(tilingProperty, v);
    }

    void SetOffset(Vector2 v)
    {
        if (string.IsNullOrEmpty(offsetProperty)) _mat.mainTextureOffset = v;
        else _mat.SetVector(offsetProperty, v);
    }

    // Chevron tip points the same way the pattern moves. Set uvDirection before pressing Play.
    Texture2D BuildChevrons(bool alongU, bool tipTowardsPositive)
    {
        const int size = 128;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, true)
        {
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Bilinear,
            anisoLevel = 4
        };

        var pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float u = x / (float)(size - 1);
                float v = y / (float)(size - 1);

                float along = alongU ? u : v;
                float across = alongU ? v : u;
                if (!tipTowardsPositive) along = 1f - along;

                float line = 0.70f - Mathf.Abs(across - 0.5f) * 0.9f;
                bool onChevron = Mathf.Abs(along - line) < 0.07f && across > 0.12f && across < 0.88f;
                pixels[y * size + x] = onChevron ? arrowColor : baseColor;
            }
        }

        tex.SetPixels(pixels);
        tex.Apply(true);
        return tex;
    }

    /// <summary>Right-click the component header to run this. Gives the numbers the Shader Graph needs.</summary>
    [ContextMenu("Print Belt Bounds (for the Shader Graph)")]
    void PrintBeltBounds()
    {
        var r = beltRenderer != null ? beltRenderer : GetComponentInChildren<Renderer>();
        if (r == null)
        {
            Debug.LogWarning("No renderer found.", this);
            return;
        }

        Bounds b = r.localBounds; // object space, the same space as the graph's Position (Object) node
        Debug.Log($"{r.name} object-space bounds:  centre {b.center.ToString("F3")}   size {b.size.ToString("F3")}", r);
    }

    void OnDestroy()
    {
        if (_texture != null) Destroy(_texture);
        if (_mat != null) Destroy(_mat);
    }
}