using UnityEngine;

/// <summary>
/// A coal-burning power plant. Consumes coal over time; shuts down when the
/// fuel tank hits zero. Can be restarted by calling AddCoal().
///
/// Inheritance chain:
///   CoalGenerator
///     └─ EnergyProducer   (seals GetProductionMW → calls GetPowerOutputMW)
///          └─ ElectricalNode  (GetProductionMW virtual)
///
/// PowerGridManager calls GetProductionMW().
/// EnergyProducer.GetProductionMW() returns isRunning ? GetPowerOutputMW() : 0.
/// CoalGenerator.GetPowerOutputMW() returns maxPowerMW.
/// No isRunning check needed here — it's already handled one level up.
/// </summary>
public class CoalGenerator : EnergyProducer
{
    [Header("Capacity")]
    [SerializeField] private float maxPowerMW = 100f;

    [Header("Fuel")]
    [SerializeField] private float coalStored              = 100f;
    [SerializeField] private float coalConsumptionPerSecond = 1f;

    private float accumulator;

    // ── Fuel tick ────────────────────────────────────────────────────────────

    private void Update()
    {
        if (!isRunning) return;

        accumulator += coalConsumptionPerSecond * Time.deltaTime;

        if (accumulator >= 1f)
        {
            int units    = Mathf.FloorToInt(accumulator);
            coalStored  -= units;
            accumulator -= units;
        }

        if (coalStored <= 0f)
        {
            coalStored = 0f;
            isRunning  = false;

            // Notify the grid so it recalculates production on the next rebuild
            PowerGridManager.Instance?.MarkDirty();
            Debug.Log($"[CoalGenerator] {name} ran out of fuel and shut down.");
        }
    }

    // ── EnergyProducer contract ───────────────────────────────────────────────
    // Just the raw capacity. EnergyProducer.GetProductionMW() handles isRunning.
    public override float GetPowerOutputMW() => maxPowerMW;

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Adds coal to the fuel tank. If the generator was shut down due to empty
    /// fuel, this restarts it automatically.
    /// </summary>
    public void AddCoal(float amount)
    {
        if (amount <= 0f) return;

        bool wasShutdown = !isRunning && coalStored <= 0f;
        coalStored += amount;

        if (wasShutdown)
        {
            isRunning = true;
            PowerGridManager.Instance?.MarkDirty();
            Debug.Log($"[CoalGenerator] {name} restarted after refuel.");
        }
    }

    public float GetCoalStored() => coalStored;
}