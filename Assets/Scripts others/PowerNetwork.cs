using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A snapshot of one electrically isolated island: its member nodes plus
/// the calculated production, consumption, imbalance, and grid frequency.
///
/// Created by PowerGridManager.RebuildNetworks() every time topology changes.
/// Simulation values are kept current by calling Recalculate() on each tick.
/// </summary>
public class PowerNetwork
{
    // ── Data ──────────────────────────────────────────────────────────────────
    public IReadOnlyList<ElectricalNode> Nodes => nodes;
    private readonly List<ElectricalNode> nodes;

    public float ProductionMW  { get; private set; }
    public float ConsumptionMW { get; private set; }
    public float ImbalanceMW   { get; private set; }

    /// <summary>
    /// Grid frequency in Hz. Nominally 50 Hz; rises with surplus, falls with deficit.
    /// Clamped between 45–55 Hz — outside that range, real grids collapse.
    /// </summary>
    public float FrequencyHz { get; private set; }

    // ── Constants ─────────────────────────────────────────────────────────────
    public const float NominalHz     = 50f;
    public const float FreqGainPerMW = 0.01f; // Hz gained per 1 MW surplus
    public const float MinFreqHz     = 45f;
    public const float MaxFreqHz     = 55f;

    // ── Constructor ───────────────────────────────────────────────────────────
    public PowerNetwork(List<ElectricalNode> nodeList)
    {
        nodes = nodeList;
        Recalculate();
    }

    // ── Simulation ────────────────────────────────────────────────────────────
    /// <summary>
    /// Recomputes production, consumption, imbalance, and frequency from
    /// current node states. Call this every simulation tick.
    /// </summary>
    public void Recalculate()
    {
        ProductionMW  = 0f;
        ConsumptionMW = 0f;

        foreach (var node in nodes)
        {
            if (node == null) continue;
            ProductionMW  += node.GetProductionMW();
            ConsumptionMW += node.GetConsumptionMW();
        }

        ImbalanceMW = ProductionMW - ConsumptionMW;
        FrequencyHz = Mathf.Clamp(
            NominalHz + ImbalanceMW * FreqGainPerMW,
            MinFreqHz,
            MaxFreqHz
        );
    }

    public override string ToString() =>
        $"[Network  {nodes.Count} nodes]  " +
        $"P = {ProductionMW,7:F1} MW   " +
        $"C = {ConsumptionMW,7:F1} MW   " +
        $"Δ = {ImbalanceMW,+8:F1} MW   " +
        $"f = {FrequencyHz:F2} Hz";
}