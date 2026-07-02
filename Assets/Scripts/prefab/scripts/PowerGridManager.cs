using System;
using System.Collections.Generic;
using UnityEngine;

public class PowerGridManager : MonoBehaviour
{
    public static PowerGridManager Instance { get; private set; }

    [Header("Registry")]
    [SerializeField] private List<ElectricalNode> allNodes = new();
    [SerializeField] private List<PowerLine> allLines = new();
    [SerializeField] private List<Transformer> allTransformers = new();

    [Header("Simulation")]
    [SerializeField] private float tickIntervalSeconds = 0.5f;
    [SerializeField] private float historyIntervalSeconds = 1f;

    public IReadOnlyList<PowerNetwork> Networks => networks;
    private readonly List<PowerNetwork> networks = new();
    private readonly Dictionary<string, NetworkDataHistory> historyCache = new();

    /// <summary>
    /// Maps each ConnectionPoint to the network it was discovered in.
    /// Per-CP (not per-node) so that a tower's two CPs can sit in different
    /// networks, and so that a transformer's SideA and SideB are always in
    /// separate islands even though they share a root GameObject.
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

    /// <summary>
    /// Called by <see cref="Transformer.Start"/>.
    /// TransformerSide nodes self-register as ElectricalNodes through the normal
    /// RegisterNode path; only the Transformer controller goes here.
    /// </summary>
    public void RegisterTransformer(Transformer transformer)
    {
        if (transformer != null && !allTransformers.Contains(transformer))
        {
            allTransformers.Add(transformer);
            MarkDirty();
        }
    }

    public void UnregisterTransformer(Transformer transformer)
    {
        if (allTransformers.Remove(transformer)) MarkDirty();
    }

    public void MarkDirty() => isDirty = true;

    // ── Public query ──────────────────────────────────────────────────────────

    /// Returns the PowerNetwork that owns <paramref name="cp"/> this tick, or null.
    public PowerNetwork GetNetworkForCP(ConnectionPoint cp)
    {
        cpNetworkMap.TryGetValue(cp, out var net);
        return net;
    }

    // ── Graph building ────────────────────────────────────────────────────────

    void CleanNullRefs()
    {
        allNodes.RemoveAll(n => n == null);
        allLines.RemoveAll(l => l == null);
        allTransformers.RemoveAll(t => t == null);
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

                foreach (var cp in componentCPs)
                    cpNetworkMap[cp] = network;
            }
        }

        OnNetworksRebuilt?.Invoke();
    }

    // ── Simulation ────────────────────────────────────────────────────────────

    /// <summary>
    /// Two-pass simulation with transformer resolution in between.
    ///
    /// Pass 1  Recalculate every network using each transformer's transfer value
    ///         from the previous tick.  This gives accurate consumer and native
    ///         generator totals before any adjustment.
    ///
    /// Resolve Each transformer removes its own previous-tick contribution from
    ///         the pass-1 snapshot to find the native surplus/deficit on each
    ///         side, then sets a new signed transfer capped at ratedCapacityMW.
    ///         Flow direction is automatic: surplus side feeds deficit side.
    ///
    /// Pass 2  Recalculate every network with the updated transformer values.
    ///         This is the authoritative state used for display and history.
    /// </summary>
    void SimulateNetworks()
    {
        // ── Pass 1 ───────────────────────────────────────────────────────────
        foreach (var net in networks)
            net.Recalculate();

        // ── Transformer resolution ────────────────────────────────────────────
        foreach (var tf in allTransformers)
        {
            if (tf == null) continue;
            FindNetworks(tf, out var netA, out var netB);
            tf.ResolveTransfer(netA, netB);
        }

        // ── Pass 2 + aggregate totals ─────────────────────────────────────────
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

    /// Resolves the PowerNetwork for SideA and SideB of a transformer by looking
    /// up the first ConnectionPoint of each side in cpNetworkMap.
    private void FindNetworks(Transformer tf,
                              out PowerNetwork netA, out PowerNetwork netB)
    {
        netA = netB = null;
        if (tf.SideA?.connectionPoints.Count > 0)
            cpNetworkMap.TryGetValue(tf.SideA.connectionPoints[0], out netA);
        if (tf.SideB?.connectionPoints.Count > 0)
            cpNetworkMap.TryGetValue(tf.SideB.connectionPoints[0], out netB);
    }

    // ── Connection-point display ──────────────────────────────────────────────

    void UpdateConnectionPointDisplay()
    {
        // 0. Reset.
        foreach (var node in allNodes)
        {
            if (node == null) continue;
            foreach (var cp in node.connectionPoints)
            {
                cp.ActualVoltageKV = 0f;
                cp.CurrentFlowMW = 0f;
            }
        }

        // 1. Voltage per-CP from its BFS component.
        foreach (var kvp in cpNetworkMap)
            kvp.Key.ActualVoltageKV = kvp.Value.VoltageKV;

        // 2. Flow per node/CP.
        //
        //   EnergyProducer   → +production MW
        //   PowerConsumer    → -consumption MW
        //   TransformerSide  → signed net (+ = outputting into network,
        //                                  - = drawing from network)
        //   Everything else  → net surplus of the whole island
        foreach (var node in allNodes)
        {
            if (node == null) continue;

            foreach (var cp in node.connectionPoints)
            {
                if (node is EnergyProducer ep)
                {
                    cp.CurrentFlowMW = ep.GetProductionMW();
                    cp.ActualVoltageKV = ep.GetProductionKV();
                }
                else if (node is PowerConsumer pc)
                {
                    cp.CurrentFlowMW = -pc.GetConsumptionMW();
                    cp.ActualVoltageKV = pc.GetConsumptionKV();
                }
                else if (node is TransformerSide)
                {
                    // Positive when outputting power into this side's network.
                    // Negative when drawing power from this side's network.
                    cp.CurrentFlowMW = node.GetProductionMW() - node.GetConsumptionMW();
                    // ActualVoltageKV already set from cpNetworkMap above.
                }
                else
                {
                    // Pass-through / infrastructure: net surplus of the island.
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

    // ── Utilities ─────────────────────────────────────────────────────────────

    /// <summary>
    /// BFS from <paramref name="start"/>, not crossing <paramref name="excludeLine"/>.
    /// Returns the signed sum of (production − consumption) for every reachable node.
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
}