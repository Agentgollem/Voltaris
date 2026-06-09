using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A physical socket on an ElectricalNode where PowerLines attach.
///
/// Nodes are never connected directly; every wire goes through ConnectionPoints.
/// Requires a Collider so WirePlacementTool can raycast to it.
/// </summary>
[RequireComponent(typeof(Collider))]
public class ConnectionPoint : MonoBehaviour
{
    [Header("Ownership (assigned by ElectricalNode.Awake)")]
    public ElectricalNode owner;

    [Header("Wiring")]
    [SerializeField] private int maxConnections = 4;
    public List<PowerLine> connectedLines = new();

    public int  MaxConnections       => maxConnections;
    public bool CanAcceptConnection() => connectedLines.Count < maxConnections;
    public List<ConnectionPoint> internalConnections = new();

private void Awake()
{
    if (owner != null) return; // Already assigned by ElectricalNode.Awake — nothing to do

    // Fallback: find the nearest ElectricalNode above us in the hierarchy.
    // This self-heals if serialization wiped the reference or Awake order was unusual.
    owner = GetComponentInParent<ElectricalNode>();

    if (owner == null)
        Debug.LogError(
            $"[ConnectionPoint] '{name}' has no ElectricalNode anywhere in its parent chain. " +
            $"It must be a child (or deeper descendant) of a GameObject that has an " +
            $"ElectricalNode component (e.g. CoalGenerator).",
            gameObject  // passing gameObject pings it in the Hierarchy when you click the error
        );
}

    /// <summary>
    /// True if a direct line already exists between this point and <paramref name="other"/>.
    /// Delegates to PowerLine.Connects() so the duplicate-check is order-independent.
    /// </summary>
    public bool IsConnectedTo(ConnectionPoint other)
    {
        if (other == null) return false;

        foreach (var line in connectedLines)
        {
            if (line != null && line.Connects(this, other))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Removes a line reference from this point's list.
    /// Deliberately does NOT call MarkDirty — PowerLine.OnDestroy handles
    /// the full cleanup chain (remove from both points + unregister from manager).
    /// </summary>
    public void RemoveLine(PowerLine line) => connectedLines.Remove(line);
}