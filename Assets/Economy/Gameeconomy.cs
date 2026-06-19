using System;
using UnityEngine;

/// <summary>
/// Singleton that tracks the player's money.
/// Call AddMoney() when selling power, SpendMoney() for purchases.
/// Attach to any persistent GameObject in the scene.
/// </summary>
public class GameEconomy : MonoBehaviour
{
    public static GameEconomy Instance { get; private set; }

    [Header("Economy")]
    [SerializeField] private float startingMoney = 50_000f;

    public float Money { get; private set; }

    public event Action<float> OnMoneyChanged;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        Money = startingMoney;
    }

    public void AddMoney(float amount)
    {
        Money += amount;
        OnMoneyChanged?.Invoke(Money);
    }

    /// <returns>True if funds were deducted, false if insufficient.</returns>
    public bool SpendMoney(float amount)
    {
        if (Money < amount) return false;
        Money -= amount;
        OnMoneyChanged?.Invoke(Money);
        return true;
    }
}