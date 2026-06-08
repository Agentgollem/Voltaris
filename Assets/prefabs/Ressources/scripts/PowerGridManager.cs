using System.Collections.Generic;
using UnityEngine;

public class PowerGridManager : MonoBehaviour
{
    public static PowerGridManager Instance;

    [Header("All Nodes in Scene")]
    public List<ElectricalNode> allNodes = new();

    [Header("Detected Networks (Islands)")]
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
        SimulateNetworks();
    }

    #region Node Registration

    public void RegisterNode(ElectricalNode node)
    {
        if (!allNodes.Contains(node))
            allNodes.Add(node);
    }

    public void UnregisterNode(ElectricalNode node)
    {
        allNodes.Remove(node);
    }

    #endregion

    #region Network Building (Graph Search)

    void RebuildNetworks()
    {
        networks.Clear();

        HashSet<ElectricalNode> visited = new();

        foreach (var startNode in allNodes)
        {
            if (startNode == null || visited.Contains(startNode))
                continue;

            List<ElectricalNode> network = new();
            Queue<ElectricalNode> queue = new();

            queue.Enqueue(startNode);
            visited.Add(startNode);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                network.Add(current);

                foreach (var line in current.connections)
                {
                    if (line == null) continue;

                    ElectricalNode next = line.GetOther(current);

                    if (next != null && !visited.Contains(next))
                    {
                        visited.Add(next);
                        queue.Enqueue(next);
                    }
                }
            }

            networks.Add(network);
        }
    }

    #endregion

    #region Simulation

    void SimulateNetworks()
    {
        foreach (var net in networks)
        {
            float production = 0f;
            float consumption = 0f;

            foreach (var node in net)
            {
                if (node == null) continue;

                production += node.GetProductionMW();
                consumption += node.GetConsumptionMW();
            }

            float imbalance = production - consumption;

            float frequency = 50f + imbalance * 0.01f;

            frequency = Mathf.Clamp(frequency, 45f, 55f);

            // Debug per network
            Debug.Log($"Network [{net.Count} nodes] | P: {production} | C: {consumption} | F: {frequency}");
        }
    }

    #endregion
}