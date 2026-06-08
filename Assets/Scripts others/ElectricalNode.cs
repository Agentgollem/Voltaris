using UnityEngine;

using System.Collections.Generic;


public abstract class ElectricalNode : MonoBehaviour
{
  public bool CanConnect => true;
  [Header("Connections")]
  public List<PowerLine> connections = new();
  protected virtual void Awake()
  {
    if (PowerGridManager.Instance == null)
    {
      // retry later
      Invoke(nameof(TryRegister), 0.1f);
      return;
    }
    PowerGridManager.Instance.RegisterNode(this);
  }
  
  void TryRegister()
  {
    if (PowerGridManager.Instance != null)
    PowerGridManager.Instance.RegisterNode(this);
  }

  protected virtual void OnDestroy()
  {
    if (PowerGridManager.Instance != null)
    {
      PowerGridManager.Instance.UnregisterNode(this);
    }
  }

  public virtual float GetProductionMW() => 0f;
  public virtual float GetConsumptionMW() => 0f;
}