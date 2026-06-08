using UnityEngine;

public class PowerLine : MonoBehaviour
{
    public ElectricalNode a;
    public ElectricalNode b;

    private void Awake()
    {
        if (a != null) a.connections.Add(this);
        if (b != null) b.connections.Add(this);
    }

    public ElectricalNode GetOther(ElectricalNode node)
    {
        if (node == a) return b;
        if (node == b) return a;
        return null;
    }
}