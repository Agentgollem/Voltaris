using UnityEngine;
using UnityEngine.InputSystem;
using System.Linq;
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
    [Header("Wire Visuals")]
    [SerializeField] private Color wireColor = new Color(0.2f, 0.2f, 0.2f);  // dark grey
    [Header("Preview")]
    [SerializeField] private Color removalColor = Color.blue;
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

            // ── Use a shader that respects vertex colours ──
            var mat = new Material(Shader.Find("Sprites/Default"));
            mat.color = Color.white;           // no tint
            previewLine.material = mat;

            // ── Important: nullify any gradient so start/end colours are used ──
            previewLine.colorGradient = null;

            // Make it thin and crisp
            previewLine.startWidth = 0.05f;
            previewLine.endWidth = 0.05f;

            // Default colour (will be overwritten during drag)
            previewLine.startColor = Color.white;
            previewLine.endColor = Color.white;
        }
    }
    private void Update()
    {
        if (Mouse.current == null) return;

        // Only start wiring when Shift is held
        bool shiftHeld = Keyboard.current != null && Keyboard.current.shiftKey.isPressed;

        if (shiftHeld && Mouse.current.leftButton.wasPressedThisFrame) OnPress();
        if (Mouse.current.leftButton.wasReleasedThisFrame && isDragging) OnRelease();

        if (isDragging) UpdatePreview();
        else
        {
            // Safety: if Shift is released while dragging, cancel the drag
            if (!shiftHeld) ResetDrag();
        }
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

        var hover = GetPointUnderMouse();
        var endPos = hover != null ? hover.transform.position : GetMouseWorldPosition();

        bool alreadyConnected = hover != null && startPoint.IsConnectedTo(hover);

        // Check if both ends are producers
        bool bothProducers = hover != null
            && hover.owner is EnergyProducer
            && startPoint.owner is EnergyProducer;

        bool canConnect = hover != null
            && hover != startPoint
            && startPoint.CanAcceptConnection()
            && hover.CanAcceptConnection()
            && !alreadyConnected
            && !bothProducers;

        Color color;
        if (hover == null)
            color = Color.white;          // empty space
        else if (alreadyConnected)
            color = removalColor;        // blue
        else if (canConnect)
            color = validColor;          // green
        else
            color = invalidColor;        // red (includes both-producer case)

        previewLine.startColor = color;
        previewLine.endColor = color;
        previewLine.SetPosition(0, startPoint.transform.position);
        previewLine.SetPosition(1, endPos);
    }

    // ── Wire creation (updated) ─────────────────────────────────────────────────
    void TryCreateWire(ConnectionPoint a, ConnectionPoint b)
    {

        // --- Validation that must always hold ---
        if (a.owner == null)
        {
            Debug.LogError($"[WirePlacementTool] ConnectionPoint '{a.name}' has no owner.");
            return;
        }
        if (b.owner == null)
        {
            Debug.LogError($"[WirePlacementTool] ConnectionPoint '{b.name}' has no owner.");
            return;
        }

        // --- If the points are already connected, REMOVE the existing wire ---
        if (a.IsConnectedTo(b))
        {
            // Find the PowerLine that connects them
            PowerLine existing = null;
            foreach (var line_b in a.connectedLines)
            {
                if (line_b.Connects(a, b))
                {
                    existing = line_b;
                    break;
                }
            }

            if (existing != null)
            {
                Debug.Log($"[WirePlacementTool] Removing wire: {a.owner.name}/{a.name} → {b.owner.name}/{b.name}");
                // PowerLine.OnDestroy handles unregistering and notifying the grid
                Destroy(existing.gameObject);
            }
            else
            {
                Debug.LogWarning("[WirePlacementTool] IsConnectedTo returned true but no matching PowerLine found.");
            }
            return;   // Done – no new wire is created
        }

        // --- Normal “create” path below ---
        if (!a.CanAcceptConnection())
        {
            Debug.LogWarning($"[WirePlacementTool] {a.owner.name}/{a.name} is at max connections ({a.MaxConnections}).");
            return;
        }
        if (!b.CanAcceptConnection())
        {
            Debug.LogWarning($"[WirePlacementTool] {b.owner.name}/{b.name} is at max connections ({b.MaxConnections}).");
            return;
        }
        if (a.owner is EnergyProducer && b.owner is EnergyProducer)
        {
            Debug.LogWarning($"[WirePlacementTool] Producers cannot connect directly.");
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

        // Store the network ID of the start point so the merged grid keeps that name
        if (PowerGridManager.Instance != null)
        {
            string existingID = null;
            foreach (var net in PowerGridManager.Instance.Networks)
            {
                if (net.Nodes.Contains(a.owner))
                {
                    existingID = net.NetworkID;
                    break;
                }
            }
            if (!string.IsNullOrEmpty(existingID))
                line.mergeNetworkID = existingID;
        }
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


