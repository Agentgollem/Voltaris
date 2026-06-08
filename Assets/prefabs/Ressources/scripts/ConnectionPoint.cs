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