using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Snapshot of one electrically isolated island.
/// Includes: stable NetworkID, VoltageKV, Stability (0–1),
/// and a NetworkDataHistory rolling buffer for graph display.
/// </summary>
public class PowerNetwork
{
    // ── Nodes ─────────────────────────────────────────────────────────────────
    public IReadOnlyList<ElectricalNode> Nodes => nodes;
    private readonly List<ElectricalNode> nodes;

    // ── Identity ──────────────────────────────────────────────────────────────
    public string NetworkID { get; private set; }

    // ── Simulation state ──────────────────────────────────────────────────────
    public float ProductionMW { get; private set; }
    public float ConsumptionMW { get; private set; }
    public float ImbalanceMW { get; private set; }
    public float FrequencyHz { get; private set; }
    public float VoltageKV { get; private set; }
    public float Stability { get; private set; }

    // ── History ───────────────────────────────────────────────────────────────
    public NetworkDataHistory History { get; private set; }

    // ── Constants ─────────────────────────────────────────────────────────────
    public const float NominalHz = 50f;
    public const float FreqGainPerMW = 0.01f;
    public const float MinFreqHz = 45f;
    public const float MaxFreqHz = 55f;

    // ── Constructor ───────────────────────────────────────────────────────────

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
        int voltageCount = 0;

        foreach (var node in nodes)
        {
            if (node == null) continue;

            ProductionMW += node.GetProductionMW();
            ConsumptionMW += node.GetConsumptionMW();

            // EnergyProducer sets voltage via its own RatedVoltageKV field.
            // All other nodes (including TransformerSide) set voltage by
            // returning a positive value from GetProductionKV() when they are
            // actively delivering power.  This keeps voltage at zero on a network
            // that is genuinely de-energised (transformer offline, no generators).
            if (node is EnergyProducer ep)
            {
                voltageSum += ep.RatedVoltageKV;
                voltageCount++;
            }
            else
            {
                float kv = node.GetProductionKV();
                if (kv > 0f) { voltageSum += kv; voltageCount++; }
            }
        }

        ImbalanceMW = ProductionMW - ConsumptionMW;
        FrequencyHz = Mathf.Clamp(
            NominalHz + ImbalanceMW * FreqGainPerMW,
            MinFreqHz, MaxFreqHz);
        VoltageKV = voltageCount > 0 ? voltageSum / voltageCount : 0f;
        Stability = 1f - Mathf.Abs(FrequencyHz - NominalHz) / (MaxFreqHz - NominalHz);
    }

    public void RecordHistory() =>
        History.Record(ProductionMW, ConsumptionMW, FrequencyHz, VoltageKV, Stability);

    public override string ToString() =>
        $"[{NetworkID} {nodes.Count} nodes]  " +
        $"P={ProductionMW:F1}MW  C={ConsumptionMW:F1}MW  " +
        $"Δ={ImbalanceMW:+0.0;-0.0}MW  f={FrequencyHz:F2}Hz  " +
        $"V={VoltageKV:F1}kV  S={Stability:P0}";

    // ── Display filter ────────────────────────────────────────────────────────

    public bool HasMeaningfulNodes()
    {
        foreach (var node in nodes)
            if (node is EnergyProducer || node is PowerConsumer)
                return true;
        return false;
    }
}