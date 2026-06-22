using UnityEngine;

public abstract class EnergyProducer : ElectricalNode
{
    [Header("Electrical Spec")]
    [SerializeField] protected float ratedVoltageKV = 20f;
    [SerializeField] protected float ratedFrequencyHz = 50f;

    [Header("State")]
    [SerializeField] protected bool isRunning = true;

    [Header("Pilotability")]
    [SerializeField] protected bool pilotable = false;      // show slider in panel
    [SerializeField] protected float currentOutputMW;        // adjustable output


    [Header("Pilot Adjustments")]
    [SerializeField][Range(0f, 100f)] protected float minOutputPercent = 35f;
    [SerializeField][Range(1f, 100f)] protected float stepPercent = 5f;

    public float MinOutputMW => GetMaxPowerOutputMW() * minOutputPercent / 100f;
    public float StepMW => GetMaxPowerOutputMW() * stepPercent / 100f;

    public float MinOutputPercent => minOutputPercent;
    public float StepPercent => stepPercent;


    public float RatedVoltageKV => ratedVoltageKV;
    public float RatedFrequencyHz => ratedFrequencyHz;
    public bool IsRunning => isRunning;
    public bool IsPilotable => pilotable;
    public float CurrentOutputMW => currentOutputMW;

    // Max capacity (defined by subclass)
    public abstract float GetMaxPowerOutputMW();

    // Grid interface – uses the adjustable field
    public sealed override float GetProductionMW() =>
        isRunning ? currentOutputMW : 0f;

    public virtual float GetCurrentAmps()
    {
        float voltageV = ratedVoltageKV * 1_000f;
        return voltageV > 0f
            ? (currentOutputMW * 1_000_000f) / voltageV
            : 0f;
    }

    /// <summary>Adjust output between 0 and max capacity.</summary>
    public void SetOutputMW(float mw)
    {
        float max = GetMaxPowerOutputMW();
        currentOutputMW = Mathf.Clamp(mw, 0f, max);
        PowerGridManager.Instance?.MarkDirty();
    }

    public void ToggleRunning()
    {
        isRunning = !isRunning;
        PowerGridManager.Instance?.MarkDirty();
    }
}