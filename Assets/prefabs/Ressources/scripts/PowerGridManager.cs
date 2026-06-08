using System.Collections.Generic;
using UnityEngine;

public class PowerGridManager : MonoBehaviour
{
    public static PowerGridManager Instance;

    public List<ElectricalNode> allNodes = new();

    public List<List<ElectricalNode>> networks = new();

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    private void Update()
    {
        RebuildNetworks();
    }

    // Called by nodes
    public void RegisterNode(ElectricalNode node)
    {
        if (!allNodes.Contains(node))
            allNodes.Add(node);
    }

    public void UnregisterNode(ElectricalNode node)
    {
        allNodes.Remove(node);
    }
}