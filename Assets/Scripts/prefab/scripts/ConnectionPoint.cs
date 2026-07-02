using System.Collections.Generic;
using UnityEngine;
using TMPro;

[RequireComponent(typeof(Collider))]
public class ConnectionPoint : MonoBehaviour
{
    [Header("Ownership")]
    public ElectricalNode owner;

    [Header("Wiring")]
    [SerializeField] private int maxConnections = 4;
    public List<PowerLine> connectedLines = new();
    public List<ConnectionPoint> internalConnections = new();

    [Header("Ratings")]
    [SerializeField] private float maxPowerMW = 300f;
    [SerializeField] private float nominalVoltageKV = 220f;
    [SerializeField] private float maxConnectionDistance = 100f;

    [Header("UI")]
    [Tooltip("Uncheck to permanently hide the hover info label.")]
    [SerializeField] private bool showInfoLabel = true;

    // Runtime state (set by PowerGridManager after simulation)
    public float CurrentFlowMW { get; set; }
    public float ActualVoltageKV { get; set; }

    // UI
    private TextMeshPro infoText;
    private Transform infoTransform;

    // ── Properties ────────────────────────────────────────────────────────
    public float MaxPowerMW => maxPowerMW;
    public float NominalVoltageKV => nominalVoltageKV;
    public float MaxConnectionDistance => maxConnectionDistance;
    public int MaxConnections => maxConnections;
    public bool CanAcceptConnection() => connectedLines.Count < maxConnections;

    // ── Unity ──────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (owner == null)
        {
            owner = GetComponentInParent<ElectricalNode>();
            if (owner == null)
                Debug.LogError($"[ConnectionPoint] '{name}' has no ElectricalNode above it.", gameObject);
        }

        Transform existing = transform.Find("InfoLabel");
        if (existing != null)
        {
            infoTransform = existing;
            infoText = existing.GetComponent<TextMeshPro>();
        }
        else
        {
            CreateLabel();
        }
    }

    private void CreateLabel()
    {
        var labelGO = new GameObject("InfoLabel");
        labelGO.transform.SetParent(transform, false);
        labelGO.transform.localPosition = new Vector3(0, 1.8f, 0);

        infoText = labelGO.AddComponent<TextMeshPro>();
        infoText.fontSize = 3f;
        infoText.alignment = TextAlignmentOptions.Center;
        infoText.color = Color.white;
        infoText.text = "";

        infoTransform = labelGO.transform;
        labelGO.SetActive(false);   // always start hidden, hover will reveal it
    }

    private void LateUpdate()
    {
        if (Camera.main == null) return;
        if (infoTransform != null)
            infoTransform.rotation =
                Quaternion.LookRotation(Camera.main.transform.forward, Vector3.up);
    }

    // ── Public API ────────────────────────────────────────────────────────

    public void DisableInfoLabel()
    {
        showInfoLabel = false;
        if (infoText != null) infoText.gameObject.SetActive(false);
    }

    public void SetLabelVisible(bool visible)
    {
        if (infoText != null) infoText.gameObject.SetActive(visible);
    }

    public void UpdateDisplay()
    {
        if (infoText == null) return;

        string flow = $"{CurrentFlowMW:+0.0;-0.0;0.0} MW";
        string volt = $"{ActualVoltageKV:F1} kV";
        string max = $"Max {maxPowerMW:F0} MW / {nominalVoltageKV:F0} kV";
        infoText.text = $"{flow}\n{volt}\n<size=70%>{max}</size>";
    }

    public bool IsConnectedTo(ConnectionPoint other)
    {
        if (other == null) return false;
        foreach (var line in connectedLines)
            if (line != null && line.Connects(this, other)) return true;
        return false;
    }

    public void RemoveLine(PowerLine line) => connectedLines.Remove(line);
}