using UnityEngine;

public class Transformer : ElectricalNode
{
  [Header("Transformer Specs")]
  [SerializeField] private float ratedPowerMW = 100f;
  [SerializeField] private float inputVoltageKV = 220f;
  [SerializeField] private float outputVoltageKV = 20f;

  [Header("Connection Points (auto‑assigned if named correctly)")]
  [SerializeField] private ConnectionPoint inputPoint;
  [SerializeField] private ConnectionPoint outputPoint;

  protected override void Awake()
  {
    base.Awake();   // handles layer + parent node assignment

    // Auto‑find children named "Input" and "Output" if not manually set
    if (inputPoint == null) inputPoint = transform.Find("Input")?.GetComponent<ConnectionPoint>();
    if (outputPoint == null) outputPoint = transform.Find("Output")?.GetComponent<ConnectionPoint>();

    // Set nominal voltages on the points so they display correctly
    if (inputPoint != null) inputPoint.ActualVoltageKV = inputVoltageKV;
    if (outputPoint != null) outputPoint.ActualVoltageKV = outputVoltageKV;

    // Ensure the two points are NOT internally connected – they must be separate networks
    // (they already are, because they are separate GameObjects)
  }

  public override float GetProductionMW() => 0f;
  public override float GetConsumptionMW() => 0f;

  // For the asset inspector panel (optional)
  public float RatedPowerMW => ratedPowerMW;
  public float InputVoltageKV => inputVoltageKV;
  public float OutputVoltageKV => outputVoltageKV;
}