using UnityEngine;

/// <summary>
/// Base class for any node that draws power from the grid.
/// customerCount is used by the GlobalHUD to display the total served population.
/// </summary>
public abstract class PowerConsumer : ElectricalNode
{
    [Header("Demand")]
    [SerializeField] protected float demandMW = 10f;
    [SerializeField] protected float demandKV = 2f;

    [Header("State")]
    [SerializeField] protected bool isActive = true;

    [Header("Customers")]
    [Tooltip("Number of households / customers this node represents.")]
    [SerializeField] protected int customerCount = 100;

    public float DemandMW => demandMW;
    public float DemandKV => demandKV;

    public bool IsActive => isActive;
    public int CustomerCount => customerCount;

    public override float GetConsumptionMW() => isActive ? demandMW : 0f;

    public override float GetConsumptionKV() => isActive ? demandKV : 0f;
    public void ToggleActive()
    {
        isActive = !isActive;
        PowerGridManager.Instance?.MarkDirty();
    }
}