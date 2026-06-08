using UnityEngine;

/// <summary>
/// Base class for any node that draws power from the grid.
/// Renamed from Power_Consumer to match C# naming conventions.
/// </summary>
public abstract class PowerConsumer : ElectricalNode
{
    [Header("Demand")]
    [SerializeField] protected float demandMW = 10f;

    [Header("State")]
    [SerializeField] protected bool isActive = true;

    public float DemandMW => demandMW;
    public bool  IsActive => isActive;

    public override float GetConsumptionMW() =>
        isActive ? demandMW : 0f;
}