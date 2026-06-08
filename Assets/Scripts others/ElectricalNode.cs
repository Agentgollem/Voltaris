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
        // Layer assignment — warns clearly instead of silently writing layer -1
        int layer = LayerMask.NameToLayer("ElectricalNode");
        if (layer < 0)
            Debug.LogWarning($"[{nameof(ElectricalNode)}] Layer 'ElectricalNode' not found. " +
                             $"Create it in Project Settings → Tags & Layers.");
        else
            gameObject.layer = layer;

        // Discover all ConnectionPoints in the hierarchy
        connectionPoints.Clear();
        connectionPoints.AddRange(GetComponentsInChildren<ConnectionPoint>());

        foreach (var cp in connectionPoints)
        {
            // Only assign if not already set (supports manual override in editor)
            if (cp != null && cp.owner == null)
                cp.owner = this;
        }
    }

    protected virtual void Start()
    {
        // Registration deferred to Start: all Awake() calls in the scene run
        // before any Start(), so Instance is guaranteed to exist.
        PowerGridManager.Instance?.RegisterNode(this);
    }

    protected virtual void OnDestroy()
    {
        PowerGridManager.Instance?.UnregisterNode(this);
    }

    /// <summary>Megawatts injected into the grid by this node.</summary>
    public virtual float GetProductionMW() => 0f;

    /// <summary>Megawatts drawn from the grid by this node.</summary>
    public virtual float GetConsumptionMW() => 0f;
}