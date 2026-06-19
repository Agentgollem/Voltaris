using System.Collections.Generic;

/// <summary>
/// Rolling circular buffer of simulation snapshots for one PowerNetwork.
/// Populated by PowerGridManager every historyIntervalSeconds.
/// Read by SubgridPanel to draw the graph view.
/// </summary>
public class NetworkDataHistory
{
    public const int Capacity = 120;   // 2 min at 1 sample/sec

    public readonly Queue<float> Production = new();
    public readonly Queue<float> Consumption = new();
    public readonly Queue<float> Frequency = new();
    public readonly Queue<float> Voltage = new();
    public readonly Queue<float> Stability = new();

    public void Record(float prod, float cons, float freq, float volt, float stab)
    {
        Push(Production, prod);
        Push(Consumption, cons);
        Push(Frequency, freq);
        Push(Voltage, volt);
        Push(Stability, stab);
    }

    void Push(Queue<float> q, float val)
    {
        q.Enqueue(val);
        while (q.Count > Capacity) q.Dequeue();
    }

    public float[] GetProduction() => Production.ToArray();
    public float[] GetConsumption() => Consumption.ToArray();
    public float[] GetFrequency() => Frequency.ToArray();
    public float[] GetVoltage() => Voltage.ToArray();
    public float[] GetStability() => Stability.ToArray();
}