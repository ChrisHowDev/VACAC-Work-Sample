using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Drops prefabs (with Rigidbody + the "Moveable" tag) from this object's position at an interval,
/// and shows a marker on whatever lies below to indicate where they will land.
/// </summary>
public class ItemSpawner : MonoBehaviour
{
    [Header("Items")]
    public List<GameObject> prefabs = new List<GameObject>();
    public bool randomOrder = true;
    [Min(0.05f)] public float interval = 2f;
    [Tooltip("Items spawn at a random point inside this radius (XZ). 0 = exactly below the spawner.")]
    [Min(0f)] public float spawnRadius = 0.2f;
    public bool randomYaw = true;
    [Tooltip("Stop spawning once this many items are alive. 0 = unlimited.")]
    public int maxAlive = 50;
    [Tooltip("Destroy items after this many seconds. 0 = never.")]
    public float lifetime = 60f;
    public bool spawning = true;

    [Header("Drop marker")]
    public bool showMarker = true;
    [Tooltip("Optional custom marker. If empty a flat disc is generated.")]
    public GameObject markerPrefab;
    public Color markerColor = new Color(1f, 0.85f, 0.1f, 1f);
    public float markerMaxDistance = 100f;
    public float markerLift = 0.02f;

    readonly List<GameObject> _alive = new List<GameObject>();
    GameObject _marker;
    float _timer;
    int _nextIndex;

    void Start()
    {
        if (showMarker) CreateMarker();
    }

    void Update()
    {
        UpdateMarker();

        if (!spawning || prefabs.Count == 0) return;

        _timer += Time.deltaTime;
        if (_timer < interval) return;

        _alive.RemoveAll(g => g == null);
        if (maxAlive > 0 && _alive.Count >= maxAlive) return;

        _timer = 0f;
        Spawn();
    }

    void Spawn()
    {
        var prefab = randomOrder
            ? prefabs[Random.Range(0, prefabs.Count)]
            : prefabs[_nextIndex++ % prefabs.Count];
        if (prefab == null) return;

        Vector3 pos =  transform.position;
        pos.y -= 1f; // start below the spawner so it doesn't collide with it
        var item = Instantiate(prefab, pos, Quaternion.identity);

       
            var renderers = item.GetComponentsInChildren<Renderer>();
            if (renderers.Length > 0)
            {
                Bounds b = renderers[0].bounds;
                foreach (var r in renderers) b.Encapsulate(r.bounds);
                item.transform.position += pos - b.center;
            }
        

        _alive.Add(item);
        if (lifetime > 0f) Destroy(item, lifetime);
    }

    // ------------------------------------------------------------------ marker

    void CreateMarker()
    {
        if (markerPrefab != null)
        {
            _marker = Instantiate(markerPrefab);
        }
        else
        {
            _marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            float diameter = Mathf.Max(spawnRadius * 2f, 0.3f);
            _marker.transform.localScale = new Vector3(diameter, 0.005f, diameter);

            var r = _marker.GetComponent<Renderer>();
            r.material.color = markerColor;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        _marker.name = name + " DropMarker";
        foreach (var c in _marker.GetComponentsInChildren<Collider>()) Destroy(c); // never block rays or items
    }

    void UpdateMarker()
    {
        if (_marker == null) return;

        if (TryGetLandingPoint(out RaycastHit hit))
        {
            _marker.SetActive(true);
            _marker.transform.position = hit.point + hit.normal * markerLift;
            _marker.transform.rotation = Quaternion.FromToRotation(Vector3.up, hit.normal);
        }
        else
        {
            _marker.SetActive(false);
        }
    }

    /// <summary>Nearest solid surface below the spawner, ignoring triggers, rigidbodies (items) and ourselves.</summary>
    bool TryGetLandingPoint(out RaycastHit result)
    {
        result = default;
        bool found = false;
        float best = float.MaxValue;

        var hits = Physics.RaycastAll(transform.position, Vector3.down, markerMaxDistance,
                                      ~0, QueryTriggerInteraction.Ignore);
        foreach (var h in hits)
        {
            if (h.rigidbody != null && !h.rigidbody.isKinematic) continue; // a falling item
            if (h.transform.IsChildOf(transform)) continue;
            if (h.distance < best)
            {
                best = h.distance;
                result = h;
                found = true;
            }
        }
        return found;
    }

    void OnDestroy()
    {
        if (_marker != null) Destroy(_marker);
    }

    void OnDisable()
    {
        if (_marker != null) _marker.SetActive(false);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = markerColor;
        Gizmos.DrawWireSphere(transform.position, 0.1f);
        Gizmos.DrawLine(transform.position, transform.position + Vector3.down * 5f);
    }
}