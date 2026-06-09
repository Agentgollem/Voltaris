using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Central registry and simulation engine for the electrical grid.
///
/// Uses a dirty flag so the BFS graph traversal only runs when the topology
/// actually changes (node/line added or removed), not every frame.
/// Simulation (frequency calculation) runs on a separate fixed-interval timer
/// so it's cheap and consistent regardless of frame rate.
/// </summary>
public class PowerGridManager : MonoBehaviour
{
    public static PowerGridManager Instance { get; private set; }

    [Header("Registry (modified at runtime)")]
    [SerializeField] private List<ElectricalNode> allNodes = new();
    [SerializeField] private List<PowerLine>      allLines = new();

    [Header("Simulation")]
    [SerializeField] private float tickIntervalSeconds = 0.5f;

    // Public read-only view; grid UI or game logic can subscribe/read from here
    public IReadOnlyList<PowerNetwork> Networks => networks;
    private readonly List<PowerNetwork> networks = new();

    private bool  isDirty  = true;
    private float tickTimer;

    // ── Unity lifecycle ───────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Update()
    {
        // Topology rebuild — only when something changed
        if (isDirty)
        {
            CleanNullRefs();
            RebuildNetworks();
            isDirty = false;
        }

        // Simulation tick — independent of topology rebuilds
        tickTimer += Time.deltaTime;
        if (tickTimer >= tickIntervalSeconds)
        {
            tickTimer = 0f;
            SimulateNetworks();
        }
    }

    // ── Registration ──────────────────────────────────────────────────────────

    public void RegisterNode(ElectricalNode node)
    {
        if (node != null && !allNodes.Contains(node))
        {
            allNodes.Add(node);
            MarkDirty();
        }
    }

    public void UnregisterNode(ElectricalNode node)
    {
        if (allNodes.Remove(node)) MarkDirty();
    }

    public void RegisterLine(PowerLine line)
    {
        if (line != null && !allLines.Contains(line))
        {
            allLines.Add(line);
            MarkDirty();
        }
    }

    public void UnregisterLine(PowerLine line)
    {
        if (allLines.Remove(line)) MarkDirty();
    }

    /// <summary>
    /// Call this whenever something changes that affects power flow
    /// (generator shuts down, consumer activates, etc.) but does NOT
    /// change the physical wire topology.
    /// </summary>
    public void MarkDirty() => isDirty = true;

    // ── Graph building ────────────────────────────────────────────────────────

    /// <summary>Removes Unity-destroyed objects that left null slots in the lists.</summary>
    void CleanNullRefs()
    {
        allNodes.RemoveAll(n => n == null);
        allLines.RemoveAll(l => l == null);
    }

    /// <summary>
    /// BFS flood-fill across the ConnectionPoint graph to find all isolated
    /// electrical islands and wrap them in PowerNetwork objects.
    /// </summary>
void RebuildNetworks()
{
    networks.Clear();

    HashSet<ConnectionPoint> visitedPoints = new();

    foreach (var node in allNodes)
    {
        if (node == null)
            continue;

        foreach (var startPoint in node.connectionPoints)
        {
            if (startPoint == null)
                continue;

            if (visitedPoints.Contains(startPoint))
                continue;

            HashSet<ElectricalNode> networkNodes = new();
            Queue<ConnectionPoint> queue = new();

            queue.Enqueue(startPoint);
            visitedPoints.Add(startPoint);

            while (queue.Count > 0)
            {
                ConnectionPoint currentPoint = queue.Dequeue();

                if (currentPoint.owner != null)
                    networkNodes.Add(currentPoint.owner);

                //
                // Follow power lines
                //
                foreach (var line in currentPoint.connectedLines)
                {
                    if (line == null)
                        continue;

                    ConnectionPoint otherPoint =
                        line.GetOther(currentPoint);

                    if (otherPoint == null)
                        continue;

                    if (visitedPoints.Add(otherPoint))
                        queue.Enqueue(otherPoint);
                }

                //
                // Follow explicit internal connections
                //
                foreach (var internalPoint in currentPoint.internalConnections)
                {
                    if (internalPoint == null)
                        continue;

                    if (visitedPoints.Add(internalPoint))
                        queue.Enqueue(internalPoint);
                }
            }

            if (networkNodes.Count > 0)
            {
                networks.Add(
                    new PowerNetwork(
                        new List<ElectricalNode>(networkNodes)
                    )
                );
            }
        }
    }
}

    // ── Simulation ────────────────────────────────────────────────────────────

    void SimulateNetworks()
    {
        foreach (var net in networks)
        {
            net.Recalculate();
            Debug.Log(net.ToString());
        }
    }
}