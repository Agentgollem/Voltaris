using System.Collections.Generic;
using UnityEngine;

public class PowerNetwork : MonoBehaviour
{
    public List<ElectricalNode> nodes = new();

    public float frequency = 50f;

    [Header("Debug")]
    public float totalProduction;
    public float totalConsumption;

    public void Register(ElectricalNode node)
    {
        if (!nodes.Contains(node))
            nodes.Add(node);
    }

    public void Unregister(ElectricalNode node)
    {
        nodes.Remove(node);
    }

    private void Update()
    {
        totalProduction = 0f;
        totalConsumption = 0f;

        foreach (var n in nodes)
        {
            if (n == null) continue;

            totalProduction += n.GetProductionMW();
            totalConsumption += n.GetConsumptionMW();
        }

        float imbalance = totalProduction - totalConsumption;

        frequency += imbalance * 0.01f * Time.deltaTime;
        frequency = Mathf.Clamp(frequency, 45f, 55f);
    }
}