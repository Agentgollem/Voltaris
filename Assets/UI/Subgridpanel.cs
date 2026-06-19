using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>
/// Screen-space panel for one PowerNetwork.
/// Fully built in code — no prefab or scene setup required.
///
/// DRAG FIX: Uses RectTransformUtility.ScreenPointToLocalPointInRectangle
///           to convert screen coords into canvas-local coords correctly,
///           even when a CanvasScaler is active.
///
/// GRAPH FIX: UILineGraph now uses RawImage + Texture2D instead of
///            MaskableGraphic, which had cross-version rendering quirks.
/// </summary>
public class SubgridPanel : MonoBehaviour
{
    private string networkID;
    private Color netColor;
    private bool isPinned;

    private RectTransform rt;
    private TextMeshProUGUI titleText;

    // Stats
    private GameObject statsView;
    private TextMeshProUGUI freqVal, prodVal, demandVal, voltVal, stabVal;
    private Image freqBar, prodBar, demandBar, voltBar, stabBar;

    // Graph
    private GameObject graphView;
    private UILineGraph lineGraph;
    private TextMeshProUGUI graphTitle;
    private int graphMetric = 0;  // 0=Freq 1=Prod 2=Dem 3=Volt 4=Stab

    // Drag
    private Vector2 dragOffset;

    // ── Factory ───────────────────────────────────────────────────────────────

    public static SubgridPanel Open(string netID, Color color, Vector2 screenPos)
    {
        var canvas = GetOrCreateCanvas();

        var go = new GameObject($"Panel_{netID}");
        go.transform.SetParent(canvas.transform, worldPositionStays: false);

        var p = go.AddComponent<SubgridPanel>();
        p.networkID = netID;
        p.netColor = color;
        p.Build(screenPos, canvas);
        return p;
    }

    // ── Build ─────────────────────────────────────────────────────────────────

    void Build(Vector2 screenPos, Canvas canvas)
    {
        rt = gameObject.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(320, 420);
        rt.pivot = new Vector2(0f, 1f);

        // Convert screen position to canvas-local (handles CanvasScaler)
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvas.GetComponent<RectTransform>(),
                screenPos, canvas.worldCamera, out Vector2 local))
            rt.anchoredPosition = local;

        gameObject.AddComponent<Image>().color = new Color(0.07f, 0.09f, 0.12f, 0.97f);

        BuildHeader();
        BuildTabBar();
        statsView = BuildStatsView();
        graphView = BuildGraphView();
        graphView.SetActive(false);
    }

    // ── Header ────────────────────────────────────────────────────────────────

    void BuildHeader()
    {
        var hRT = Child("Header", rt, new Vector2(0, 1), new Vector2(1, 1),
                         new Vector2(0, -40), Vector2.zero);
        hRT.gameObject.AddComponent<Image>().color = Darken(netColor, 0.45f);

        // Left accent stripe
        Child("Accent", hRT, Vector2.zero, new Vector2(0, 1),
               Vector2.zero, new Vector2(5, 0))
            .gameObject.AddComponent<Image>().color = netColor;

        // Title
        var tGO = new GameObject("Title");
        tGO.transform.SetParent(hRT, false);
        titleText = tGO.AddComponent<TextMeshProUGUI>();
        titleText.text = networkID; titleText.fontSize = 13f;
        titleText.color = Color.white; titleText.fontStyle = FontStyles.Bold;
        var tRT = tGO.GetComponent<RectTransform>();
        tRT.anchorMin = new Vector2(0.04f, 0.1f); tRT.anchorMax = new Vector2(0.70f, 0.9f);
        tRT.offsetMin = tRT.offsetMax = Vector2.zero;

        Btn("PinBtn", hRT, new Vector2(0.71f, 0.1f), new Vector2(0.86f, 0.9f), "PIN", 10, TogglePin);
        Btn("CloseBtn", hRT, new Vector2(0.87f, 0.1f), new Vector2(1.00f, 0.9f), "✕", 13, () => Destroy(gameObject));

        hRT.gameObject.AddComponent<HeaderDragHandler>().panel = this;
    }

    void TogglePin()
    {
        isPinned = !isPinned;
        titleText.text = isPinned ? $"📌 {networkID}" : networkID;
    }

    // ── Tab bar ───────────────────────────────────────────────────────────────

    void BuildTabBar()
    {
        var tabRT = Child("Tabs", rt, new Vector2(0, 1), new Vector2(1, 1),
                           new Vector2(0, -70), new Vector2(0, -40));
        tabRT.gameObject.AddComponent<Image>().color = new Color(0.05f, 0.06f, 0.09f);

        Btn("StatsTab", tabRT, new Vector2(0, 0), new Vector2(0.5f, 1), "STATS", 11, ShowStats);
        Btn("GraphTab", tabRT, new Vector2(0.5f, 0), new Vector2(1f, 1), "GRAPH", 11, ShowGraph);
    }

    // ── Stats view ────────────────────────────────────────────────────────────

    GameObject BuildStatsView()
    {
        var root = Child("StatsView", rt, Vector2.zero, new Vector2(1, 1),
                          Vector2.zero, new Vector2(0, -70));
        root.gameObject.AddComponent<Image>().color = Color.clear;

        float step = 0.19f, top = 0.97f;
        (freqVal, freqBar) = StatRow(root, "Frequency", Color.cyan, top - step * 0);
        (prodVal, prodBar) = StatRow(root, "Production", new Color(0.2f, 1f, 0.4f), top - step * 1);
        (demandVal, demandBar) = StatRow(root, "Demand", new Color(1f, 0.8f, 0.2f), top - step * 2);
        (voltVal, voltBar) = StatRow(root, "Voltage", new Color(0.9f, 0.9f, 1f), top - step * 3);
        (stabVal, stabBar) = StatRow(root, "Stability", new Color(0.5f, 1f, 0.5f), top - step * 4);

        return root.gameObject;
    }

    (TextMeshProUGUI val, Image bar) StatRow(RectTransform parent,
        string label, Color color, float anchorTop)
    {
        var rowRT = Child($"Row{label}", parent,
            new Vector2(0, anchorTop - 0.16f), new Vector2(1, anchorTop),
            new Vector2(10, 3), new Vector2(-10, -3));

        // Label
        var lGO = new GameObject("Lbl");
        lGO.transform.SetParent(rowRT, false);
        var lTMP = lGO.AddComponent<TextMeshProUGUI>();
        lTMP.text = label; lTMP.fontSize = 10f;
        lTMP.color = new Color(0.65f, 0.65f, 0.65f);
        var lRT = lGO.GetComponent<RectTransform>();
        lRT.anchorMin = new Vector2(0, 0); lRT.anchorMax = new Vector2(0.32f, 1);
        lRT.offsetMin = lRT.offsetMax = Vector2.zero;

        // Value
        var vGO = new GameObject("Val");
        vGO.transform.SetParent(rowRT, false);
        var vTMP = vGO.AddComponent<TextMeshProUGUI>();
        vTMP.text = "—"; vTMP.fontSize = 11f;
        vTMP.color = color; vTMP.fontStyle = FontStyles.Bold;
        vTMP.alignment = TextAlignmentOptions.Right;
        var vRT = vGO.GetComponent<RectTransform>();
        vRT.anchorMin = new Vector2(0.32f, 0); vRT.anchorMax = new Vector2(0.62f, 1);
        vRT.offsetMin = vRT.offsetMax = Vector2.zero;

        // Bar BG
        var bgRT = Child("BarBG", rowRT, new Vector2(0.63f, 0.2f), new Vector2(1f, 0.8f),
                          Vector2.zero, Vector2.zero);
        bgRT.gameObject.AddComponent<Image>().color = new Color(0.10f, 0.10f, 0.12f);

        // Fill
        var fillGO = new GameObject("Fill");
        fillGO.transform.SetParent(bgRT, false);
        var fill = fillGO.AddComponent<Image>();
        fill.color = color; fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Horizontal; fill.fillAmount = 0f;
        var fillRT = fillGO.GetComponent<RectTransform>();
        fillRT.anchorMin = Vector2.zero; fillRT.anchorMax = Vector2.one;
        fillRT.offsetMin = fillRT.offsetMax = Vector2.zero;

        return (vTMP, fill);
    }

    // ── Graph view ────────────────────────────────────────────────────────────

    GameObject BuildGraphView()
    {
        var root = Child("GraphView", rt, Vector2.zero, new Vector2(1, 1),
                          Vector2.zero, new Vector2(0, -70));

        // Metric selector buttons row
        var btnBar = Child("MetricBar", root, new Vector2(0, 1), new Vector2(1, 1),
                            new Vector2(0, -28), Vector2.zero);
        btnBar.gameObject.AddComponent<Image>().color = new Color(0.04f, 0.05f, 0.08f);

        string[] names = { "FREQ", "PROD", "DEM", "VOLT", "STAB" };
        for (int i = 0; i < names.Length; i++)
        {
            int idx = i;
            float x0 = (float)i / names.Length, x1 = (float)(i + 1) / names.Length;
            Btn($"M{i}", btnBar, new Vector2(x0, 0), new Vector2(x1, 1),
                names[i], 8, () => { graphMetric = idx; RefreshGraph(); });
        }

        // Graph title
        var gtGO = new GameObject("GTitle");
        gtGO.transform.SetParent(root, false);
        graphTitle = gtGO.AddComponent<TextMeshProUGUI>();
        graphTitle.text = "Frequency (Hz)"; graphTitle.fontSize = 10f;
        graphTitle.color = new Color(0.5f, 0.5f, 0.55f);
        graphTitle.alignment = TextAlignmentOptions.Center;
        var gtRT = gtGO.GetComponent<RectTransform>();
        gtRT.anchorMin = new Vector2(0, 1); gtRT.anchorMax = new Vector2(1, 1);
        gtRT.offsetMin = new Vector2(4, -50); gtRT.offsetMax = new Vector2(-4, -28);

        // Graph area (UILineGraph uses RawImage, no separate background Image needed)
        var gbRT = Child("GraphArea", root, Vector2.zero, new Vector2(1, 1),
                          new Vector2(8, 8), new Vector2(-8, -54));

        var lgGO = new GameObject("LineGraph");
        lgGO.transform.SetParent(gbRT, false);
        // [RequireComponent(typeof(RawImage))] will auto-add RawImage
        lineGraph = lgGO.AddComponent<UILineGraph>();
        var lgRT = lgGO.GetComponent<RectTransform>();
        lgRT.anchorMin = Vector2.zero; lgRT.anchorMax = Vector2.one;
        lgRT.offsetMin = lgRT.offsetMax = Vector2.zero;

        return root.gameObject;
    }

    // ── Tab switching ─────────────────────────────────────────────────────────

    void ShowStats() { statsView.SetActive(true); graphView.SetActive(false); }
    void ShowGraph() { statsView.SetActive(false); graphView.SetActive(true); RefreshGraph(); }

    void RefreshGraph()
    {
        var net = GetNetwork();
        if (net == null || lineGraph == null) return;

        float[] d; string title; Color col; float? fmin = null, fmax = null;

        switch (graphMetric)
        {
            case 1: d = net.History.GetProduction(); title = "Production (MW)"; col = new Color(0.2f, 1f, 0.4f); break;
            case 2: d = net.History.GetConsumption(); title = "Demand (MW)"; col = new Color(1f, 0.8f, 0.2f); break;
            case 3: d = net.History.GetVoltage(); title = "Voltage (kV)"; col = new Color(0.9f, 0.9f, 1f); break;
            case 4:
                d = net.History.GetStability(); title = "Stability"; col = new Color(0.5f, 1f, 0.5f);
                fmin = 0f; fmax = 1f; break;
            default:
                d = net.History.GetFrequency(); title = "Frequency (Hz)"; col = Color.cyan;
                fmin = PowerNetwork.MinFreqHz - 0.5f;
                fmax = PowerNetwork.MaxFreqHz + 0.5f; break;
        }

        if (graphTitle != null) graphTitle.text = title;
        lineGraph.SetData(d, fmin, fmax, col);
    }

    // ── Per-frame update ──────────────────────────────────────────────────────

    void Update()
    {
        var net = GetNetwork();
        if (net == null) { if (!isPinned) Destroy(gameObject); return; }

        TickStats(net);
        if (graphView.activeSelf) RefreshGraph();
    }

    void TickStats(PowerNetwork net)
    {
        freqVal.text = $"{net.FrequencyHz:F2} Hz";
        prodVal.text = $"{net.ProductionMW:F1} MW";
        demandVal.text = $"{net.ConsumptionMW:F1} MW";
        voltVal.text = $"{net.VoltageKV:F1} kV";
        stabVal.text = $"{net.Stability * 100f:F0} %";

        float frac = Mathf.InverseLerp(PowerNetwork.MinFreqHz, PowerNetwork.MaxFreqHz, net.FrequencyHz);
        freqBar.fillAmount = frac;
        freqBar.color = Color.Lerp(Color.red, Color.cyan, frac);

        prodBar.fillAmount = Mathf.Clamp01(net.ProductionMW / 500f);
        demandBar.fillAmount = Mathf.Clamp01(net.ConsumptionMW / 500f);
        voltBar.fillAmount = Mathf.Clamp01(net.VoltageKV / 100f);
        stabBar.fillAmount = net.Stability;
        stabBar.color = Color.Lerp(Color.red, new Color(0.5f, 1f, 0.5f), net.Stability);
    }

    PowerNetwork GetNetwork()
    {
        if (PowerGridManager.Instance == null) return null;
        foreach (var net in PowerGridManager.Instance.Networks)
            if (net.NetworkID == networkID) return net;
        return null;
    }

    // ── Drag ─────────────────────────────────────────────────────────────────
    // Called by HeaderDragHandler. Converts screen coords to canvas-local
    // using RectTransformUtility so CanvasScaler is accounted for.

    public void BeginDrag(PointerEventData e)
    {
        Debug.Log("Begin Drag");
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                rt.parent as RectTransform,
                e.position,
                e.pressEventCamera,
                out Vector2 local))
        {
            dragOffset = rt.anchoredPosition - local;
        }
    }

    public void Drag(PointerEventData e)
    {
        Debug.Log("Dragging");
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                rt.parent as RectTransform,
                e.position,
                e.pressEventCamera,
                out Vector2 local))
        {
            rt.anchoredPosition = local + dragOffset;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    static Color Darken(Color c, float f) => new Color(c.r * f, c.g * f, c.b * f, 1f);

    static RectTransform Child(string name, RectTransform parent,
        Vector2 ancMin, Vector2 ancMax, Vector2 offMin, Vector2 offMax)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var r = go.AddComponent<RectTransform>();
        r.anchorMin = ancMin; r.anchorMax = ancMax;
        r.offsetMin = offMin; r.offsetMax = offMax;
        return r;
    }

    static void Btn(string name, RectTransform parent,
        Vector2 ancMin, Vector2 ancMax, string label, float fs, Action onClick)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var r = go.AddComponent<RectTransform>();
        r.anchorMin = ancMin; r.anchorMax = ancMax;
        r.offsetMin = r.offsetMax = Vector2.zero;

        var img = go.AddComponent<Image>();
        img.color = new Color(0.13f, 0.15f, 0.20f);
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(() => onClick());

        var tGO = new GameObject("T");
        tGO.transform.SetParent(go.transform, false);
        var tmp = tGO.AddComponent<TextMeshProUGUI>();
        tmp.text = label; tmp.fontSize = fs;
        tmp.color = Color.white; tmp.alignment = TextAlignmentOptions.Center;
        var tRT = tGO.GetComponent<RectTransform>();
        tRT.anchorMin = Vector2.zero; tRT.anchorMax = Vector2.one;
        tRT.offsetMin = tRT.offsetMax = Vector2.zero;
    }

    static Canvas GetOrCreateCanvas()
    {
        var canvas = FindObjectOfType<Canvas>();
        if (canvas != null) return canvas;

        var go = new GameObject("VoltarisUICanvas");
        var cv = go.AddComponent<Canvas>();
        cv.renderMode = RenderMode.ScreenSpaceOverlay;
        cv.sortingOrder = 50;
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        go.AddComponent<GraphicRaycaster>();

        if (FindObjectOfType<EventSystem>() == null)
        {
            var esGO = new GameObject("EventSystem");
            esGO.AddComponent<EventSystem>();
            esGO.AddComponent<StandaloneInputModule>();
        }
        return cv;
    }
}

/// <summary>
/// Placed on the header Image. Forwards drag events to the panel.
/// Only the header is draggable — not the whole panel.
/// </summary>
public class HeaderDragHandler : MonoBehaviour, IBeginDragHandler, IDragHandler
{
    public SubgridPanel panel;
    public void OnBeginDrag(PointerEventData e) => panel?.BeginDrag(e);
    public void OnDrag(PointerEventData e) => panel?.Drag(e);
}