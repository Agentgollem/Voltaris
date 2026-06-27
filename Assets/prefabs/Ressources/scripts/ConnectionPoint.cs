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
    [Tooltip("Uncheck to hide the hover info label on this connection point (e.g. on household sockets).")]
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
        // Owner assignment
        if (owner == null)
        {
            owner = GetComponentInParent<ElectricalNode>();
            if (owner == null)
                Debug.LogError($"[ConnectionPoint] '{name}' has no ElectricalNode above it.", gameObject);
        }

        // Build the world-space label
        var labelGO = new GameObject("InfoLabel");
        labelGO.transform.SetParent(transform, false);
        labelGO.transform.localPosition = new Vector3(0, 1.8f, 0);
        print(labelGO.transform == null);
        infoTransform = labelGO.transform;

        // Optional dark background
        var bgGO = new GameObject("Bg");
        bgGO.transform.SetParent(labelGO.transform, false);
        var bg = bgGO.AddComponent<SpriteRenderer>();
        bg.sprite = Resources.Load<Sprite>("Sprites/WhiteSquare");
        bg.color = new Color(0, 0, 0, 0.6f);
        bg.transform.localScale = new Vector3(2.5f, 1.2f, 1f);

        infoText = labelGO.AddComponent<TextMeshPro>();
        infoText.fontSize = 3f;
        infoText.alignment = TextAlignmentOptions.Center;
        infoText.color = Color.white;
        infoText.text = "";

        // Hidden until hovered (or permanently if showInfoLabel is false)
        infoText.gameObject.SetActive(false);

        Debug.Log($"[ConnectionPoint] Label created for '{name}'", gameObject);
    }

    void Start() { }   // nothing needed; label starts hidden via Awake

    private void LateUpdate()
    {

        if (infoTransform == null || Camera.main == null) return;

        // Upright camera-facing billboard.
        // -toCam points the label's +Z away from the camera so the text's
        // front face (-Z for TMP world-space meshes) always faces the viewer.
        // Vector3.up keeps the label level even when the camera tilts.
        Vector3 toCam = Camera.main.transform.position - infoTransform.position;
        if (toCam.sqrMagnitude < 0.001f) return;
        infoTransform.rotation = Quaternion.LookRotation(-toCam, Vector3.up);
    }

    // ── Public API ────────────────────────────────────────────────────────

    /// <summary>
    /// Call once to permanently hide the info label on this point
    /// (used by HouseHold so its socket doesn't show stats on hover).
    /// </summary>
    public void DisableInfoLabel()
    {
        showInfoLabel = false;
        infoText?.gameObject.SetActive(false);
    }

    public void SetLabelVisible(bool visible)
    {
        // Silently ignored when this point has its label suppressed.
        if (infoText == null || !showInfoLabel) return;
        infoText.gameObject.SetActive(visible);
    }

    public void UpdateDisplay()
    {
        if (infoText == null || !showInfoLabel) return;

        string flow = $"{CurrentFlowMW:F1} MW";
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