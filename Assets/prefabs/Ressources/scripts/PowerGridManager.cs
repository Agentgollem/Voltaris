using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Central registry and simulation engine.
///
/// CHANGES from previous version:
/// - OnNetworksRebuilt event fires after every topology rebuild.
/// - TotalProductionMW, OverallFrequencyHz, TotalCustomers aggregated every tick.
/// - History preserved across rebuilds via historyCache (same node set → same history).
/// - historyIntervalSeconds controls how often graph data is sampled.
/// </summary>
public class PowerGridManager : MonoBehaviour
{
    public static PowerGridManager Instance { get; private set; }

    [Header("Registry")]
    [SerializeField] private List<ElectricalNode> allNodes = new();
    [SerializeField] private List<PowerLine> allLines = new();

    [Header("Simulation")]
    [SerializeField] private float tickIntervalSeconds = 0.5f;
    [SerializeField] private float historyIntervalSeconds = 1f;

    // ── Networks ──────────────────────────────────────────────────────────────
    public IReadOnlyList<PowerNetwork> Networks => networks;
    private readonly List<PowerNetwork> networks = new();

    /// Preserves rolling history when the same logical island survives a rebuild.
    private readonly Dictionary<string, NetworkDataHistory> historyCache = new();

    /// Fired immediately after every RebuildNetworks() call.
    /// SubgridVisualizer subscribes to this to update overlays and panels.
    public event Action OnNetworksRebuilt;

    // ── Global aggregates (updated each simulation tick) ──────────────────────
    public float TotalProductionMW { get; private set; }
    public float OverallFrequencyHz { get; private set; }
    public int TotalCustomers { get; private set; }

    private bool isDirty = true;
    private float tickTimer;
    private float historyTimer;

    // ── Unity ─────────────────────────────────────────────────────────────────

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Update()
    {
        if (isDirty)
        {
            CleanNullRefs();
            RebuildNetworks();
            isDirty = false;
        }

        tickTimer += Time.deltaTime;
        if (tickTimer >= tickIntervalSeconds)
        {
            tickTimer = 0f;
            SimulateNetworks();
        }

        historyTimer += Time.deltaTime;
        if (historyTimer >= historyIntervalSeconds)
        {
            historyTimer = 0f;
            foreach (var net in networks)
                net.RecordHistory();
        }
    }

    // ── Registration ──────────────────────────────────────────────────────────

    public void RegisterNode(ElectricalNode node)
    {
        if (node != null && !allNodes.Contains(node)) { allNodes.Add(node); MarkDirty(); }
    }

    public void UnregisterNode(ElectricalNode node)
    {
        if (allNodes.Remove(node)) MarkDirty();
    }

    public void RegisterLine(PowerLine line)
    {
        if (line != null && !allLines.Contains(line)) { allLines.Add(line); MarkDirty(); }
    }

    public void UnregisterLine(PowerLine line)
    {
        if (allLines.Remove(line)) MarkDirty();
    }

    public void MarkDirty() => isDirty = true;

    // ── Graph building ────────────────────────────────────────────────────────

    void CleanNullRefs()
    {
        allNodes.RemoveAll(n => n == null);
        allLines.RemoveAll(l => l == null);
    }

    void RebuildNetworks()
    {
        networks.Clear();

        HashSet<ConnectionPoint> visitedPoints = new();

        foreach (var node in allNodes)
        {
            if (node == null) continue;

            foreach (var startPoint in node.connectionPoints)
            {
                if (startPoint == null || visitedPoints.Contains(startPoint))
                    continue;

                var networkNodes = new HashSet<ElectricalNode>();
                var queue = new Queue<ConnectionPoint>();

                queue.Enqueue(startPoint);
                visitedPoints.Add(startPoint);

                while (queue.Count > 0)
                {
                    var current = queue.Dequeue();

                    if (current.owner != null)
                        networkNodes.Add(current.owner);

                    // Follow external PowerLine edges
                    foreach (var line in current.connectedLines)
                    {
                        if (line == null) continue;
                        var other = line.GetOther(current);
                        if (other != null && visitedPoints.Add(other))
                            queue.Enqueue(other);
                    }

                    // Follow internal connections (e.g. PowerLineStructure)
                    foreach (var internalCP in current.internalConnections)
                    {
                        if (internalCP != null && visitedPoints.Add(internalCP))
                            queue.Enqueue(internalCP);
                    }
                }

                if (networkNodes.Count == 0) continue;

                var nodeList = new List<ElectricalNode>(networkNodes);
                string id = PowerNetwork.DeriveID(nodeList);

                // Re-attach preserved history so graphs survive wire changes
                historyCache.TryGetValue(id, out var existingHistory);
                var network = new PowerNetwork(nodeList, existingHistory, id);
                historyCache[id] = network.History;

                networks.Add(network);
            }
        }

        OnNetworksRebuilt?.Invoke();
    }

    // ── Simulation ────────────────────────────────────────────────────────────

    void SimulateNetworks()
    {
        TotalProductionMW = 0f;
        OverallFrequencyHz = 0f;
        TotalCustomers = 0;

        foreach (var net in networks)
        {
            net.Recalculate();
            TotalProductionMW += net.ProductionMW;
            OverallFrequencyHz += net.FrequencyHz;
        }

        OverallFrequencyHz = networks.Count > 0
            ? OverallFrequencyHz / networks.Count
            : PowerNetwork.NominalHz;

        foreach (var node in allNodes)
            if (node is PowerConsumer c)
                TotalCustomers += c.CustomerCount;
    }
}