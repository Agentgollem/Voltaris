using System;
using Unity.VisualScripting;
using UnityEngine;

public class CoalGenerator : EnergyProducer
{
    [Header("Generator")]
    [SerializeField] private float maxPowerMW = 100f;

    [Header("Fuel")]
    [SerializeField] private float coalStored = 100f;
    [SerializeField] private float coalConsumptionPerSecond = 1f;

    float accumulator = 0f;
    private void Update()
    {
      if (!isRunning) return;

      accumulator += coalConsumptionPerSecond * Time.deltaTime;

      if (accumulator >= 1f)
      {
        int amount = Mathf.FloorToInt(accumulator);
        coalStored -= amount;
        accumulator -= amount;
      }

      if (coalStored <= 0)
      {
        coalStored = 0;
        isRunning = false;
      }
    }

    public override float GetPowerOutputMW()
    {
        if (!isRunning)
            return 0f;

        return maxPowerMW;
    }

    public void AddCoal(float amount)
    {
        coalStored += amount;
    }


}