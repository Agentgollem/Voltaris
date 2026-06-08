using UnityEngine;

public abstract class EnergyProducer : MonoBehaviour
{
    [Header("Electrical Properties")]
    [SerializeField] protected float voltageKV = 20f;
    [SerializeField] protected float frequencyHz = 50f;

    [SerializeField] protected float Amperage = 50f;
    [Header("State")]
    [SerializeField] protected bool isRunning = true;

    public float VoltageKV => voltageKV;
    public float FrequencyHz => frequencyHz;
    public bool IsRunning => isRunning;


    /// <summary>
    /// Current frequency output in hz.
    /// Every producer calculates this differently.
    /// </summary>
    //public abstract float GetFrequencyOutput();

    /// <summary>
    /// Current power output in MW.
    /// Every producer calculates this differently.
    /// </summary>
    public abstract float GetPowerOutputMW();

    /// <summary>
    /// Optional current output calculation.
    /// </summary>
    public virtual float GetCurrentAmps()
    {
        float powerW = GetPowerOutputMW() * 1_000_000f;
        float voltageV = voltageKV * 1_000f;

        if (voltageV <= 0)
            return 0;

        return powerW / voltageV;
    }
}