using UnityEngine;

/// <summary>
/// Base class for any node that generates electrical power.
///
/// KEY DESIGN: GetProductionMW() is sealed here. It applies the isRunning gate
/// once so subclasses never have to check it themselves — they only implement
/// GetPowerOutputMW() (raw capacity while running).
///
/// Before this existed, EnergyProducer.GetPowerOutputMW() was never called by
/// the grid because PowerGridManager only knew about GetProductionMW(). That
/// disconnect is the root cause of generators silently producing nothing.
/// </summary>
public abstract class EnergyProducer : ElectricalNode
{
    [Header("Electrical Spec")]
    [SerializeField] protected float ratedVoltageKV    = 20f;
    [SerializeField] protected float ratedFrequencyHz  = 50f;

    [Header("State")]
    [SerializeField] protected bool isRunning = true;

    public float RatedVoltageKV   => ratedVoltageKV;
    public float RatedFrequencyHz => ratedFrequencyHz;
    public bool  IsRunning        => isRunning;

    // ── Grid interface ───────────────────────────────────────────────────────
    // Sealed: the isRunning check lives here, not scattered in every subclass.
    public sealed override float GetProductionMW() =>
        isRunning ? GetPowerOutputMW() : 0f;

    // ── Subclass contract ────────────────────────────────────────────────────
    /// <summary>
    /// Raw output while running. Do NOT check isRunning here —
    /// GetProductionMW() already does that.
    /// </summary>
    public abstract float GetPowerOutputMW();

    /// <summary>Amperage draw at rated voltage given current MW output.</summary>
    public virtual float GetCurrentAmps()
    {
        float voltageV = ratedVoltageKV * 1_000f;
        return voltageV > 0f
            ? (GetPowerOutputMW() * 1_000_000f) / voltageV
            : 0f;
    }
}