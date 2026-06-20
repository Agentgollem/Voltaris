using UnityEngine;

/// <summary>
/// Edge in the electrical graph. Connects two ConnectionPoints and
/// draws itself in the world using a LineRenderer every LateUpdate.
///
/// Lifecycle:
///   1. WirePlacementTool instantiates a GameObject
///   2. AddComponent&lt;PowerLine&gt;() auto-adds LineRenderer (via RequireComponent)
///   3. Initialize(a, b) wires everything up and notifies the grid
///   4. OnDestroy() cleans up both endpoints and the grid registry
/// </summary>
[RequireComponent(typeof(LineRenderer))]
    public class PowerLine : MonoBehaviour
{
    [Header("Visual")]
    [SerializeField] private Color wireColor = Color.white;   // default, change in inspector or via code

    [Header("Endpoints (set by Initialize)")]
    public ConnectionPoint startPoint;
    public ConnectionPoint endPoint;

    [Header("Capacity")]
    [SerializeField] private float maxCapacityMW = 500f;
    public float MaxCapacityMW => maxCapacityMW;

    private LineRenderer lr;
    public string mergeNetworkID;   // set by WirePlacementTool to force the network ID after a merge
                                    // ── Initialization ───────────────────────────────────────────────────────

    /// <summary>
    /// Call once immediately after the component is added.
    /// Registers both endpoints, sets up the renderer, and marks the grid dirty.
    /// </summary>



    private void Awake()
    {
        lr = GetComponent<LineRenderer>();
        // Hide the line until Initialize() is called
        lr.positionCount = 0;
        lr.enabled = false;
    }

    public void Initialize(ConnectionPoint a, ConnectionPoint b)
    {
        if (a == null || b == null)
        {
            Debug.LogError("[PowerLine] Initialize called with null ConnectionPoint(s). Destroying.");
            Destroy(gameObject);
            return;
        }

        startPoint = a;
        endPoint   = b;

        // Register with both sockets (guard against double-add)
        if (!startPoint.connectedLines.Contains(this))
            startPoint.connectedLines.Add(this);
        if (!endPoint.connectedLines.Contains(this))
            endPoint.connectedLines.Add(this);

        // Set up the visual
        lr = GetComponent<LineRenderer>();
        ConfigureRenderer();
        UpdateVisual();

        // Now show the line
        lr.enabled = true;


        // RegisterLine also calls MarkDirty
        PowerGridManager.Instance?.RegisterLine(this);
    }

    // ── Graph helpers ────────────────────────────────────────────────────────

    /// <summary>True if this line connects the given pair (order-independent).</summary>
    public bool Connects(ConnectionPoint a, ConnectionPoint b) =>
        (startPoint == a && endPoint == b) ||
        (startPoint == b && endPoint == a);

    /// <summary>Returns the endpoint that is not <paramref name="point"/>.</summary>
    public ConnectionPoint GetOther(ConnectionPoint point)
    {
        if (point == startPoint) return endPoint;
        if (point == endPoint)   return startPoint;
        return null;
    }

    // ── Visual ───────────────────────────────────────────────────────────────
    void ConfigureRenderer()
    {
        lr.positionCount = 2;
        lr.useWorldSpace = true;
        lr.startWidth = 0.1f;
        lr.endWidth = 0.1f;

        // Assign a default material that shows the actual colour
        if (lr.material == null)
        {
            // Use Sprites/Default shader – it renders the vertex colour as-is
            var mat = new Material(Shader.Find("Sprites/Default"));
            mat.color = Color.white;
            lr.material = mat;
        }

        // Apply the colour (the material’s tint is white, so the wire will be this colour)
        lr.startColor = wireColor;
        lr.endColor = wireColor;
    }

    public void UpdateVisual()
    {
        if (lr == null || startPoint == null || endPoint == null) return;
        lr.SetPosition(0, startPoint.transform.position);
        lr.SetPosition(1, endPoint.transform.position);
    }

    // LateUpdate so the wire tracks buildings that move during Update
    private void LateUpdate() => UpdateVisual();

    // ── Lifecycle ────────────────────────────────────────────────────────────

    private void OnDestroy()
    {
        // Clean up both endpoints, then notify the grid.
        // UnregisterLine calls MarkDirty so the BFS rebuilds without this edge.
        startPoint?.RemoveLine(this);
        endPoint?.RemoveLine(this);
        PowerGridManager.Instance?.UnregisterLine(this);
    }
}