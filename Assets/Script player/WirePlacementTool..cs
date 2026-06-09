using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Click-and-drag wire placement tool.
///
/// Left-click a ConnectionPoint → drag → release on another ConnectionPoint
/// to spawn a PowerLine between them.
///
/// Preview line:
///   White   — hovering over empty space
///   Green   — valid connection
///   Red     — invalid (same point, at capacity, or already connected)
/// </summary>
public class WirePlacementTool : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Camera cam;

    [Header("Preview")]
    [SerializeField] private LineRenderer previewLine;
    [SerializeField] private Color validColor   = Color.green;
    [SerializeField] private Color invalidColor = Color.red;

    [Header("Wire Prefab (optional)")]
    [Tooltip("Assign a prefab with a pre-configured LineRenderer material. " +
             "Leave null to create a plain GameObject at runtime.")]
    [SerializeField] private GameObject wireLinePrefab;

    [Header("Raycast")]
    [Tooltip("Set to the 'ElectricalNode' layer mask to avoid hitting unrelated colliders.")]
    [SerializeField] private LayerMask connectionPointMask = ~0; // Default: all layers

    // ── State ─────────────────────────────────────────────────────────────────
    private ConnectionPoint startPoint;
    private bool isDragging;

    // Fallback: intersect with the Y=0 ground plane when the mouse isn't
    // over any geometry, so the preview line never snaps to world origin.
    private static readonly Plane GroundPlane = new(Vector3.up, Vector3.zero);

    // ── Unity lifecycle ───────────────────────────────────────────────────────

    private void Start()
    {
        if (cam == null) cam = Camera.main;

        if (previewLine != null)
        {
            previewLine.positionCount = 0;
            previewLine.useWorldSpace = true;
        }
    }

    private void Update()
    {
        if (Mouse.current == null) return;

        if (Mouse.current.leftButton.wasPressedThisFrame)  OnPress();
        if (Mouse.current.leftButton.wasReleasedThisFrame) OnRelease();

        if (isDragging) UpdatePreview();
    }

    // ── Input handlers ────────────────────────────────────────────────────────

    void OnPress()
    {
        var point = GetPointUnderMouse();
        if (point == null) return; // Clicked empty space — ignore

        startPoint = point;
        isDragging = true;

        if (previewLine != null)
            previewLine.positionCount = 2;
    }

    void OnRelease()
    {
        if (!isDragging) { ResetDrag(); return; }

        var endPoint = GetPointUnderMouse();

        if (endPoint != null && endPoint != startPoint)
            TryCreateWire(startPoint, endPoint);

        ResetDrag();
    }

    // ── Preview ───────────────────────────────────────────────────────────────

    void UpdatePreview()
    {
        if (previewLine == null || startPoint == null) return;

        var   hover  = GetPointUnderMouse();
        var   endPos = hover != null
            ? hover.transform.position
            : GetMouseWorldPosition();

        bool canConnect = hover != null
            && hover != startPoint
            && startPoint.CanAcceptConnection()
            && hover.CanAcceptConnection()
            && !startPoint.IsConnectedTo(hover);

        var color = hover == null ? Color.white
                  : canConnect   ? validColor
                                 : invalidColor;

        previewLine.startColor = previewLine.endColor = color;
        previewLine.SetPosition(0, startPoint.transform.position);
        previewLine.SetPosition(1, endPos);
    }

    // ── Wire creation ─────────────────────────────────────────────────────────
void TryCreateWire(ConnectionPoint a, ConnectionPoint b)
{
    // Catch the most common setup mistake immediately with a clear message
    if (a.owner == null)
    {
        Debug.LogError($"[WirePlacementTool] ConnectionPoint '{a.name}' has no owner. " +
                       "Make sure it is a child GameObject of an ElectricalNode.");
        return;
    }
    if (b.owner == null)
    {
        Debug.LogError($"[WirePlacementTool] ConnectionPoint '{b.name}' has no owner. " +
                       "Make sure it is a child GameObject of an ElectricalNode.");
        return;
    }

    if (!a.CanAcceptConnection())
    {
        Debug.LogWarning($"[WirePlacementTool] {a.owner.name}/{a.name} is at max " +
                         $"connections ({a.MaxConnections}).");
        return;
    }

    if (!b.CanAcceptConnection())
    {
        Debug.LogWarning($"[WirePlacementTool] {b.owner.name}/{b.name} is at max " +
                         $"connections ({b.MaxConnections}).");
        return;
    }

    if (a.IsConnectedTo(b))
    {
        Debug.LogWarning("[WirePlacementTool] These points are already connected.");
        return;
    }
    if (a.owner is EnergyProducer && b.owner is EnergyProducer)
    {
        Debug.LogWarning($"[WirePlacementTool] {a.owner.name} is an energy producer trying to connect to {b.owner.name} also an energy procuer");
        return;
    }

    var wireObj = wireLinePrefab != null
        ? Instantiate(wireLinePrefab)
        : new GameObject("PowerLine");

    var line = wireObj.GetComponent<PowerLine>()
            ?? wireObj.AddComponent<PowerLine>();

    if (line == null)
    {
        Debug.LogError("[WirePlacementTool] Failed to get or add PowerLine component.");
        Destroy(wireObj);
        return;
    }

    line.Initialize(a, b);

    Debug.Log($"[WirePlacementTool] Wire created: {a.owner.name}/{a.name} → {b.owner.name}/{b.name}");
}

    // ── Helpers ───────────────────────────────────────────────────────────────

    void ResetDrag()
    {
        isDragging = false;
        startPoint = null;

        if (previewLine != null)
            previewLine.positionCount = 0;
    }

    ConnectionPoint GetPointUnderMouse()
{
    if (cam == null) return null;

    Ray ray = cam.ScreenPointToRay(Mouse.current.position.ReadValue());

    if (!Physics.Raycast(ray, out RaycastHit hit, 100f, connectionPointMask))
        return null;

    // Only accept a direct hit on a ConnectionPoint collider.
    // Removed GetComponentInParent — that walked up into parent building colliders
    // and returned points that had nothing to do with where the user actually clicked.
    var point = hit.collider.GetComponent<ConnectionPoint>();

    if (point != null && point.owner == null)
    {
        // Self-heal should have run already; if owner is still null the hierarchy is wrong.
        Debug.LogError(
            $"[WirePlacementTool] '{point.name}' was hit but its owner is still null after " +
            $"Awake. Make sure it is parented under a GameObject with an ElectricalNode component.",
            point.gameObject
        );
        return null; // Don't hand back a broken point
    }

    return point;
}

    /// <summary>
    /// Returns the world position under the mouse cursor.
    /// Falls back to the Y=0 ground plane if nothing is hit,
    /// so the preview line never snaps to Vector3.zero.
    /// </summary>
    Vector3 GetMouseWorldPosition()
    {
        if (cam == null) return Vector3.zero;

        Ray ray = cam.ScreenPointToRay(Mouse.current.position.ReadValue());

        if (Physics.Raycast(ray, out RaycastHit hit, 500f))
            return hit.point;

        if (GroundPlane.Raycast(ray, out float dist))
            return ray.GetPoint(dist);

        return Vector3.zero;
    }
}