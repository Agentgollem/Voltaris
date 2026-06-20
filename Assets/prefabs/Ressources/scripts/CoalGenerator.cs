using UnityEngine;

public class CoalGenerator : EnergyProducer
{
    [Header("Capacity")]
    [SerializeField] private float maxPowerMW = 100f;

    [Header("Fuel")]
    [SerializeField] private float coalStored = 100f;
    [SerializeField] private float coalConsumptionPerSecond = 1f;

    private float accumulator;

    protected override void Start()
    {
        base.Start();
        // Default to full power if not already set in Inspector
        if (currentOutputMW <= 0f) currentOutputMW = maxPowerMW;
    }

    private void Update()
    {
        if (!isRunning) return;
        accumulator += coalConsumptionPerSecond * Time.deltaTime;
        if (accumulator >= 1f)
        {
            int units = Mathf.FloorToInt(accumulator);
            coalStored -= units;
            accumulator -= units;
        }
        if (coalStored <= 0f)
        {
            coalStored = 0f;
            isRunning = false;
            PowerGridManager.Instance?.MarkDirty();
            Debug.Log($"[CoalGenerator] {name} ran out of fuel and shut down.");
        }
    }

    public override float GetMaxPowerOutputMW() => maxPowerMW;

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