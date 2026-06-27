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
            UpdateConnectionPointDisplay();
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

                    foreach (var line in current.connectedLines)
                    {
                        if (line == null) continue;
                        var other = line.GetOther(current);
                        if (other != null && visitedPoints.Add(other))
                            queue.Enqueue(other);
                    }

                    foreach (var internalCP in current.internalConnections)
                    {
                        if (internalCP != null && visitedPoints.Add(internalCP))
                            queue.Enqueue(internalCP);
                    }
                }

                if (networkNodes.Count == 0) continue;

                var nodeList = new List<ElectricalNode>(networkNodes);

                // Skip networks that consist solely of consumers (no producer/structure)
                bool hasProducerOrStructure = false;
                foreach (var n in nodeList)
                {
                    if (n is EnergyProducer || !(n is PowerConsumer)) // includes PowerLineStructure etc.
                    {
                        hasProducerOrStructure = true;
                        break;
                    }
                }
                if (!hasProducerOrStructure) continue;   // don't create a network


                // ── NEW: find all PowerLine objects that belong to this component ──
                var componentLines = new List<PowerLine>();
                foreach (var line in allLines)
                {
                    if (line == null) continue;
                    // a line belongs to this component if both endpoints' owners are in nodeList
                    if (nodeList.Contains(line.startPoint?.owner) &&
                        nodeList.Contains(line.endPoint?.owner))
                    {
                        componentLines.Add(line);
                    }
                }

                // Determine the final network ID:
                // If any line in this component has a mergeNetworkID, use it (preferring the only one, if multiple pick one deterministically – first found)
                string finalID = null;
                foreach (var line in componentLines)
                {
                    if (!string.IsNullOrEmpty(line.mergeNetworkID))
                    {
                        finalID = line.mergeNetworkID;
                        break;   // first found wins
                    }
                }

                if (finalID == null)
                    finalID = PowerNetwork.DeriveID(nodeList);   // fallback to auto-derived

                // Re-attach preserved history using the final ID
                historyCache.TryGetValue(finalID, out var existingHistory);
                var network = new PowerNetwork(nodeList, existingHistory, finalID);
                historyCache[finalID] = network.History;   // store under the ID we are using

                networks.Add(network);
            }
        }

        OnNetworksRebuilt?.Invoke();
    }
    void UpdateConnectionPointDisplay()
    {
        // Clear all
        foreach (var node in allNodes)
        {
            if (node == null) continue;
            foreach (var cp in node.connectionPoints)
            {
                cp.ActualVoltageKV = 0f;
                cp.CurrentFlowMW = 0f;
            }
        }

        // 1. Assign voltage from network
        foreach (var net in networks)
        {
            float netVoltage = net.VoltageKV;
            foreach (var node in net.Nodes)
            {
                if (node == null) continue;
                foreach (var cp in node.connectionPoints)
                    cp.ActualVoltageKV = netVoltage;
            }
        }

        // 2. Per‑line flow = average of the net power of the two endpoint owners
        // 2. Per‑line flow = average of the net power of the two endpoint owners
        foreach (var line in allLines)
        {
            if (line == null || line.startPoint == null || line.endPoint == null) continue;
            if (line.startPoint.owner == null || line.endPoint.owner == null) continue;   // ← guard

            float pA = Mathf.Abs(line.startPoint.owner.GetProductionMW() - line.startPoint.owner.GetConsumptionMW());
            float pB = Mathf.Abs(line.endPoint.owner.GetProductionMW() - line.endPoint.owner.GetConsumptionMW());
            float flow = (pA + pB) * 0.5f;

            line.startPoint.CurrentFlowMW += flow;
            line.endPoint.CurrentFlowMW += flow;
        }

        // 3. Emergency fallback – if a point still has 0 voltage but is connected to a line,
        //    copy voltage from the other end of that line
        foreach (var line in allLines)
        {
            if (line == null || line.startPoint == null || line.endPoint == null) continue;
            if (line.startPoint.ActualVoltageKV < 0.1f && line.endPoint.ActualVoltageKV > 0.1f)
                line.startPoint.ActualVoltageKV = line.endPoint.ActualVoltageKV;
            else if (line.endPoint.ActualVoltageKV < 0.1f && line.startPoint.ActualVoltageKV > 0.1f)
                line.endPoint.ActualVoltageKV = line.startPoint.ActualVoltageKV;
        }

        // 4. Update the label text
        foreach (var node in allNodes)
        {
            if (node == null) continue;
            foreach (var cp in node.connectionPoints)
                cp.UpdateDisplay();
        }
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