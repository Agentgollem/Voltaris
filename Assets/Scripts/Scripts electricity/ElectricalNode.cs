using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Base class for every entity participating in the power grid:
/// generators, consumers, junctions, batteries, transformers, etc.
///
/// Internal setup (layer + connection point discovery) happens in Awake.
/// Grid registration happens in Start so PowerGridManager.Awake() is
/// guaranteed to have already run, making Instance non-null.
/// </summary>
public abstract class ElectricalNode : MonoBehaviour
{
    [Header("Connection Points (auto-discovered from children)")]
    public List<ConnectionPoint> connectionPoints = new();

    protected virtual void Awake()
    {
        // Layer assignment
        int layer = LayerMask.NameToLayer("ElectricalNode");
        if (layer < 0)
            Debug.LogWarning($"[{nameof(ElectricalNode)}] Layer 'ElectricalNode' not found. " +
                             "Create it in Project Settings → Tags & Layers.");
        else
            gameObject.layer = layer;

        // Discover all ConnectionPoints in the hierarchy
        connectionPoints.Clear();
        connectionPoints.AddRange(GetComponentsInChildren<ConnectionPoint>());

        foreach (var cp in connectionPoints)
        {
            if (cp != null && cp.owner == null)
                cp.owner = this;
        }

        /*

    WE DONT WANT THAT WE WANT THEM TO BE ISOLATED !!!!!!!!!!!!!!!!!!
            // ── Auto-wire internalConnections ─────────────────────────────────
            // Every CP on the same node must be internally connected to its
            // siblings so the RebuildNetworks BFS visits ALL of them as a single
            // unit the moment it reaches ANY one of them via a wire.
            //
            // Without this, the BFS discovers e.g. CP1 (via a wire) but never
            // visits CP2.  The outer loop then starts a second BFS from CP2,
            // creating a duplicate standalone network for the same node.  That
            // duplicate network has VoltageKV = 0 and overwrites the correct
            // values that were assigned by the first network.
            foreach (var cp in connectionPoints)
            {
                if (cp == null) continue;
                foreach (var sibling in connectionPoints)
                {
                    if (sibling != null && sibling != cp &&
                        !cp.internalConnections.Contains(sibling))
                    {
                        cp.internalConnections.Add(sibling);
                    }
                }
            }
            */
    }


    protected virtual void Start()
    {
        PowerGridManager.Instance?.RegisterNode(this);
    }

    protected virtual void OnDestroy()
    {
        PowerGridManager.Instance?.UnregisterNode(this);
    }

    public virtual float GetProductionMW() => 0f;
    public virtual float GetConsumptionMW() => 0f;

    public virtual float GetProductionKV() => 0f;
    public virtual float GetConsumptionKV() => 0f;

}