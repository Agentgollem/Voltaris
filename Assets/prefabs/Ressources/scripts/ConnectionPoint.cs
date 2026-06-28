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
    }

    private void Start()
    {
        // All Awake() calls have completed by now, so showInfoLabel is
        // definitively set (e.g. by HouseHold.Awake → DisableInfoLabel).
        // Don't create any objects at all when the label is suppressed.
        if (!showInfoLabel) return;

        var labelGO = new GameObject("InfoLabel");
        labelGO.transform.SetParent(transform, false);
        labelGO.transform.localPosition = new Vector3(0, 1.8f, 0);
        infoTransform = labelGO.transform;

        var bgGO = new GameObject("Bg");
        bgGO.transform.SetParent(labelGO.transform, false);
        var bg = bgGO.AddComponent<SpriteRenderer>();
        bg.sprite = Resources.Load<Sprite>("Sprites/WhiteSquare");
        bg.color = new Color(0, 0, 0, 0.6f);
        bg.transform.localScale = new Vector3(2.5f, 1.2f, 1f);
        bg.transform.localPosition = Vector3.zero;

        infoText = labelGO.AddComponent<TextMeshPro>();
        infoText.fontSize = 3f;
        infoText.alignment = TextAlignmentOptions.Center;
        infoText.color = Color.white;
        infoText.text = "";

        infoText.gameObject.SetActive(false);   // hidden until hovered
    }

    private void LateUpdate()
    {
        if (infoTransform == null || Camera.main == null) return;

        // Upright camera-facing billboard.
        Vector3 toCam = Camera.main.transform.position - infoTransform.position;
        if (toCam.sqrMagnitude < 0.001f) return;
        infoTransform.rotation = Quaternion.LookRotation(-toCam, Vector3.up);
    }

    // ── Public API ────────────────────────────────────────────────────────

    public void DisableInfoLabel()
    {
        showInfoLabel = false;
        infoText?.gameObject.SetActive(false);
    }

    public void SetLabelVisible(bool visible)
    {
        if (infoText == null) return;
        infoText.gameObject.SetActive(visible);
    }

    public void UpdateDisplay()
    {
        if (infoText == null) return;

        // Sign convention (matches PowerGridManager assignment):
        //   + value  → producer (injecting into grid)
        //   - value  → consumer (drawing from grid)
        //   ± net    → infrastructure (grid balance = production - consumption)
        //
        // "+0.0;-0.0;0.0" always shows an explicit sign on non-zero values.
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