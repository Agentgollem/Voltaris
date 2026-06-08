using UnityEngine;

public abstract class Power_Consumer : ElectricalNode
{
    public float demandMW = 10f;

    public override float GetConsumptionMW()
    {
        return demandMW;
    }
}