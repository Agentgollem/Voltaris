using UnityEngine;

public class PowerLineStructure : ElectricalNode
{
    public override float GetProductionMW()
    {
        return 0f;
    }

    public override float GetConsumptionMW()
    {
        return 0f;
    }
}