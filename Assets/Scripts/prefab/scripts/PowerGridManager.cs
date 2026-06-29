using System;
using System.Collections.Generic;
using UnityEngine;

public class PowerGridManager : MonoBehaviour
{
    public static PowerGridManager Instance { get; private set; }

    [Header("Registry")]
    [SerializeField] private List<ElectricalNode> allNodes = new();
    [SerializeField] private List<PowerLine> allLines = new();

    [Header("Simulation")]
    [SerializeField] private float tickIntervalSeconds = 0.5f;
    [SerializeField] private float historyIntervalSeconds = 1f;

    public IReadOnlyList<PowerNetwork> Networks => networks;
    private readonly List<PowerNetwork> networks = new();
    private readonly Dictionary<string, NetworkDataHistory> historyCache = new();

    /// <summary>
    /// Maps each ConnectionPoint to the network component it was discovered in.
    /// This is the key to per-CP independence: Tower.CP1 and Tower.CP2 can be
    /// in different networks (different voltages/grids) simultaneously, because
    /// we track which BFS run found each CP rather than which node owns it.
    /// </summary>
    private readonly Dictionary<ConnectionPoint, PowerNetwork> cpNetworkMap = new();

    public event Action OnNetworksRebuilt;

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
            SimulateNetworks();
            UpdateConnectionPointDisplay();
            tickTimer = 0f;
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
        cpNetworkMap.Clear();

        var visitedPoints = new HashSet<ConnectionPoint>();

        foreach (var node in allNodes)
        {
            if (node == null) continue;

            foreach (var startPoint in node.connectionPoints)
            {
                if (startPoint == null || visitedPoints.Contains(startPoint))
                    continue;

                var networkNodes = new HashSet<ElectricalNode>();
                // Track exactly which CPs belong to this BFS component so we
                // can assign them to the right network in cpNetworkMap later.
                var componentCPs = new HashSet<ConnectionPoint>();
                var queue = new Queue<ConnectionPoint>();

                visitedPoints.Add(startPoint);
                componentCPs.Add(startPoint);
                queue.Enqueue(startPoint);

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
                        {
                            queue.Enqueue(other);
                            componentCPs.Add(other);
                        }
                    }

                    foreach (var internalCP in current.internalConnections)
                    {
                        if (internalCP != null && visitedPoints.Add(internalCP))
                        {
                            queue.Enqueue(internalCP);
                            componentCPs.Add(internalCP);
                        }
                    }
                }

                if (networkNodes.Count == 0) continue;

                var nodeList = new List<ElectricalNode>(networkNodes);

                bool hasProducerOrStructure = false;
                foreach (var n in nodeList)
                {
                    if (n is EnergyProducer || !(n is PowerConsumer))
                    {
                        hasProducerOrStructure = true;
                        break;
                    }
                }
                if (!hasProducerOrStructure) continue;

                var componentLines = new List<PowerLine>();
                foreach (var line in allLines)
                {
                    if (line == null) continue;
                    if (nodeList.Contains(line.startPoint?.owner) &&
                        nodeList.Contains(line.endPoint?.owner))
                        componentLines.Add(line);
                }

                string finalID = null;
                foreach (var line in componentLines)
                {
                    if (!string.IsNullOrEmpty(line.mergeNetworkID))
                    {
                        finalID = line.mergeNetworkID;
                        break;
                    }
                }
                if (finalID == null)
                    finalID = PowerNetwork.DeriveID(nodeList);

                historyCache.TryGetValue(finalID, out var existingHistory);
                var network = new PowerNetwork(nodeList, existingHistory, finalID);
                historyCache[finalID] = network.History;
                networks.Add(network);

                // Register every CP discovered in this run → this network.
                // Because we use per-CP mapping (not per-node), Tower.CP1
                // and Tower.CP2 can legally point to different networks.
                foreach (var cp in componentCPs)
                    cpNetworkMap[cp] = network;
            }
        }

        OnNetworksRebuilt?.Invoke();
    }

    // ── Connection-point display ──────────────────────────────────────────────

    void UpdateConnectionPointDisplay()
    {
        // 0. Reset everything.
        foreach (var node in allNodes)
        {
            if (node == null) continue;
            foreach (var cp in node.connectionPoints)
            {
                cp.ActualVoltageKV = 0f;
                cp.CurrentFlowMW = 0f;
            }
        }

        // 1. Voltage per-CP from its own BFS component.
        //    Using cpNetworkMap means a tower whose CP1 is in a 20 kV grid and
        //    CP2 is in an isolated segment correctly shows 20 kV / 0 kV.
        foreach (var kvp in cpNetworkMap)
            kvp.Key.ActualVoltageKV = kvp.Value.VoltageKV;

        // 2. Flow per node/CP, with sign.
        //
        //   • EnergyProducer → +production MW  (injecting into grid)
        //   • PowerConsumer  → -consumption MW  (drawing from grid)
        //   • Infrastructure → signed sum of net power from each connected segment.
        //       SumNetPowerFrom(otherCP, line) returns production-consumption of
        //       everything reachable from the far end of that line, preserving the
        //       sign.  A household-only segment returns −40 MW; a coal plant
        //       segment returns +45 MW.  Summing them gives the net flow seen by
        //       that individual connection point.
        foreach (var node in allNodes)
        {
            if (node == null) continue;

            foreach (var cp in node.connectionPoints)
            {
                if (node is EnergyProducer ep)
                {
                    cp.CurrentFlowMW = ep.GetProductionMW();
                }
                else if (node is PowerConsumer pc)
                {
                    cp.CurrentFlowMW = -pc.GetConsumptionMW();
                }
                else
                {
                    // Pass‑through node: show the net surplus of the whole electrical island
                    if (cpNetworkMap.TryGetValue(cp, out var net))
                        cp.CurrentFlowMW = net.ProductionMW - net.ConsumptionMW;
                    else
                        cp.CurrentFlowMW = 0f;
                }
            }
        }

        // 3. Refresh labels.
        foreach (var node in allNodes)
        {
            if (node == null) continue;
            foreach (var cp in node.connectionPoints)
                cp.UpdateDisplay();
        }
    }

    /// <summary>
    /// BFS from <paramref name="start"/>, not crossing <paramref name="excludeLine"/>.
    /// Returns the signed sum of (production − consumption) for every reachable node.
    /// Positive  = net generating side.
    /// Negative  = net consuming side.
    /// </summary>
    private float SumNetPowerFrom(ConnectionPoint start, PowerLine excludeLine)
    {
        var visitedNodes = new HashSet<ElectricalNode>();
        var visitedCPs = new HashSet<ConnectionPoint>();
        var queue = new Queue<ConnectionPoint>();

        visitedCPs.Add(start);
        queue.Enqueue(start);

        while (queue.Count > 0)
        {
            var cp = queue.Dequeue();

            if (cp.owner != null)
                visitedNodes.Add(cp.owner);

            foreach (var line in cp.connectedLines)
            {
                if (line == null || line == excludeLine) continue;
                var other = line.GetOther(cp);
                if (other != null && visitedCPs.Add(other))
                    queue.Enqueue(other);
            }

            foreach (var internalCP in cp.internalConnections)
            {
                if (internalCP != null && visitedCPs.Add(internalCP))
                    queue.Enqueue(internalCP);
            }
        }

        float net = 0f;
        foreach (var n in visitedNodes)
            net += n.GetProductionMW() - n.GetConsumptionMW();
        return net;
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