using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Snapshot of one electrically isolated island.
/// Now includes: stable NetworkID, VoltageKV, Stability (0–1),
/// and a NetworkDataHistory rolling buffer for graph display.
/// </summary>
public class PowerNetwork
{
    // ── Nodes ─────────────────────────────────────────────────────────────────
    public IReadOnlyList<ElectricalNode> Nodes => nodes;
    private readonly List<ElectricalNode> nodes;

    // ── Identity ──────────────────────────────────────────────────────────────
    /// Stable ID derived from the sorted set of node instance IDs.
    /// Stays the same as long as the same nodes are connected together.
    public string NetworkID { get; private set; }

    // ── Simulation state ──────────────────────────────────────────────────────
    public float ProductionMW { get; private set; }
    public float ConsumptionMW { get; private set; }
    public float ImbalanceMW { get; private set; }
    public float FrequencyHz { get; private set; }
    public float VoltageKV { get; private set; }   // avg of all producers
    public float Stability { get; private set; }   // 0 = critical, 1 = perfect

    // ── History ───────────────────────────────────────────────────────────────
    public NetworkDataHistory History { get; private set; }

    // ── Constants ─────────────────────────────────────────────────────────────
    public const float NominalHz = 50f;
    public const float FreqGainPerMW = 0.01f;
    public const float MinFreqHz = 45f;
    public const float MaxFreqHz = 55f;

    // ── Constructor ───────────────────────────────────────────────────────────

    /// <param name="nodeList">Member nodes of this island.</param>
    /// <param name="existingHistory">Pass a previous history to survive topology rebuilds.</param>
    /// <param name="existingID">Reuse a previous ID if already derived.</param>
    public PowerNetwork(List<ElectricalNode> nodeList,
                        NetworkDataHistory existingHistory = null,
                        string existingID = null)
    {
        nodes = nodeList;
        NetworkID = existingID ?? DeriveID(nodeList);
        History = existingHistory ?? new NetworkDataHistory();
        Recalculate();
    }

    // ── ID derivation ─────────────────────────────────────────────────────────

    /// Deterministic hash of the sorted node instance IDs.
    /// The same set of nodes always produces the same ID regardless of BFS order.
    public static string DeriveID(List<ElectricalNode> nodeList)
    {
        if (nodeList == null || nodeList.Count == 0) return "Grid-0000";

        int hash = 17;
        foreach (int id in nodeList
            .Where(n => n != null)
            .Select(n => n.GetInstanceID())
            .OrderBy(id => id))
        {
            hash = hash * 31 + id;
        }

        return $"Grid-{(uint)hash % 9999 + 1:D4}";
    }

    // ── Simulation ────────────────────────────────────────────────────────────

    public void Recalculate()
    {
        ProductionMW = 0f;
        ConsumptionMW = 0f;
        float voltageSum = 0f;
        int producerCount = 0;

        foreach (var node in nodes)
        {
            if (node == null) continue;
            ProductionMW += node.GetProductionMW();
            ConsumptionMW += node.GetConsumptionMW();

            if (node is EnergyProducer ep)
            {
                voltageSum += ep.RatedVoltageKV;
                producerCount++;
            }
        }

        ImbalanceMW = ProductionMW - ConsumptionMW;
        FrequencyHz = Mathf.Clamp(
            NominalHz + ImbalanceMW * FreqGainPerMW,
            MinFreqHz, MaxFreqHz
        );
        VoltageKV = producerCount > 0 ? voltageSum / producerCount : 0f;

        // Stability: 1.0 at nominal frequency, 0.0 at either hard limit
        Stability = 1f - Mathf.Abs(FrequencyHz - NominalHz) / (MaxFreqHz - NominalHz);
    }

    /// Called by PowerGridManager on the history timer to append a data point.
    public void RecordHistory() =>
        History.Record(ProductionMW, ConsumptionMW, FrequencyHz, VoltageKV, Stability);

    public override string ToString() =>
        $"[{NetworkID} {nodes.Count} nodes]  " +
        $"P={ProductionMW:F1}MW  C={ConsumptionMW:F1}MW  " +
        $"Δ={ImbalanceMW:+0.0;-0.0}MW  f={FrequencyHz:F2}Hz  " +
        $"V={VoltageKV:F1}kV  S={Stability:P0}";


    // ── Display filter ────────────────────────────────────────────────────────

    /// <summary>
    /// Returns true if this network contains at least one EnergyProducer
    /// or PowerConsumer. Pure-infrastructure networks (only PowerLineStructure
    /// nodes with no generators or consumers connected) return false and are
    /// excluded from the overlay display.
    /// </summary>
    public bool HasMeaningfulNodes()
    {
        foreach (var node in nodes)
            if (node is EnergyProducer || node is PowerConsumer)
                return true;
        return false;
    }
}

