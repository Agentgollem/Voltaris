using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Attach to the SAME GameObject as PowerGridManager.
///
/// Responsibilities:
///   - Creates/updates one SubgridOverlay per MEANINGFUL network on each rebuild.
///     (Networks that contain only PowerLineStructure / pass-through nodes are skipped.)
///   - Opens a SubgridPanel when the player right-clicks inside a network hull.
///   - Brings an already-open panel to the front if right-clicked again.
/// </summary>
[RequireComponent(typeof(PowerGridManager))]
public class SubgridVisualizer : MonoBehaviour
{
    [Header("Outline")]
    [Tooltip("How far the outline extends beyond the outermost nodes.")]
    public float OutlinePadding = 2.5f;
    [Tooltip("World Y height at which the outline is drawn.")]
    public float OutlineY = 0.1f;
    [Tooltip("World Y height for the network label.")]
    public float LabelHeight = 4f;

    [Header("Network Palette")]
    [Tooltip("Colors cycle across networks. Add more to support more grids.")]
    public Color[] Palette = new Color[]
    {
        new Color(0.20f, 0.65f, 1.00f),
        new Color(1.00f, 0.55f, 0.12f),
        new Color(0.20f, 1.00f, 0.45f),
        new Color(1.00f, 0.25f, 0.75f),
        new Color(0.90f, 0.90f, 0.20f),
        new Color(0.60f, 0.30f, 1.00f),
    };

    // ── Runtime state ─────────────────────────────────────────────────────────
    private PowerGridManager manager;
    private readonly Dictionary<string, SubgridOverlay> overlays = new();
    private readonly Dictionary<string, SubgridPanel> panels = new();
    private readonly Dictionary<string, Color> colors = new();
    private int colorCursor = 0;

    // ── Setup ─────────────────────────────────────────────────────────────────

    void Awake()
    {
        manager = GetComponent<PowerGridManager>();
        manager.OnNetworksRebuilt += OnNetworksRebuilt;
    }

    void OnDestroy()
    {
        if (manager != null) manager.OnNetworksRebuilt -= OnNetworksRebuilt;
    }

    // ── Respond to topology changes ───────────────────────────────────────────

    void OnNetworksRebuilt()
    {
        // Collect IDs of networks worth displaying
        // (skip pure pass-through islands that have no generators or consumers)
        var liveIDs = new HashSet<string>();
        foreach (var net in manager.Networks)
            if (net.HasMeaningfulNodes())
                liveIDs.Add(net.NetworkID);

        // Remove stale overlays
        var dead = new List<string>();
        foreach (var id in overlays.Keys)
            if (!liveIDs.Contains(id)) dead.Add(id);
        foreach (var id in dead)
        {
            if (overlays[id] != null) Destroy(overlays[id].gameObject);
            overlays.Remove(id);
        }

        // Create or refresh overlays for meaningful networks
        foreach (var net in manager.Networks)
        {
            if (!net.HasMeaningfulNodes()) continue;

            if (!colors.ContainsKey(net.NetworkID))
                colors[net.NetworkID] = Palette[colorCursor++ % Palette.Length];

            Color col = colors[net.NetworkID];

            if (overlays.TryGetValue(net.NetworkID, out var ov))
            {
                ov.UpdateNetwork(net);
            }
            else
            {
                ov = SubgridOverlay.Create(net, col, transform);
                ov.Padding = OutlinePadding;
                ov.OutlineY = OutlineY;
                ov.LabelHeight = LabelHeight;
                overlays[net.NetworkID] = ov;
            }
        }
    }

    // ── Per-frame ─────────────────────────────────────────────────────────────

    void Update()
    {
        foreach (var ov in overlays.Values)
            ov.UpdateVisual();

        if (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame)
            HandleRightClick();

        // Clean up destroyed panels
        var stale = new List<string>();
        foreach (var kvp in panels)
            if (kvp.Value == null) stale.Add(kvp.Key);
        foreach (var id in stale) panels.Remove(id);
    }

    // ── Click handling ────────────────────────────────────────────────────────

    void HandleRightClick()
    {
        Vector3 worldPos = WorldPosUnderMouse();
        if (worldPos == Vector3.zero) return;

        foreach (var kvp in overlays)
        {
            if (!kvp.Value.ContainsPoint(worldPos)) continue;
            OpenOrFocus(kvp.Key);
            return;
        }
    }

    void OpenOrFocus(string netID)
    {
        if (panels.TryGetValue(netID, out var existing) && existing != null)
        {
            existing.transform.SetAsLastSibling();
            return;
        }

        Color col = colors.ContainsKey(netID) ? colors[netID] : Color.white;
        Vector2 screen = Mouse.current.position.ReadValue();
        panels[netID] = SubgridPanel.Open(netID, col, screen);
    }

    // ── World position ────────────────────────────────────────────────────────

    static readonly Plane GroundPlane = new Plane(Vector3.up, Vector3.zero);

    Vector3 WorldPosUnderMouse()
    {
        if (Camera.main == null) return Vector3.zero;
        Ray ray = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());
        if (Physics.Raycast(ray, out RaycastHit hit, 500f)) return hit.point;
        if (GroundPlane.Raycast(ray, out float d)) return ray.GetPoint(d);
        return Vector3.zero;
    }
}