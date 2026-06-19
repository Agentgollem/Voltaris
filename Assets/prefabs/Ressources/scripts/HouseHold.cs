using UnityEngine;

public class HouseHold : PowerConsumer
{
    [Header("Household")]
    [SerializeField]
    private float demandKW = 2f;

    private void Awake()
    {
        customerCount = 1;
        demandMW = demandKW / 1000f;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        customerCount = 1;
        demandMW = demandKW / 1000f;
    }
#endif
}