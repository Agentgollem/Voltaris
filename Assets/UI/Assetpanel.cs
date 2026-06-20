using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using System.Collections;

public class AssetPanel : MonoBehaviour, IDraggablePanel
{

    private Outline panelOutline;   // cached Outline component
    private static readonly Dictionary<object, AssetPanel> openPanels = new();
    private Image bgImage;               // cached in Build()
    private Coroutine highlightRoutine;
    // ── Fields ───────────────────────────────────────────────────────────────
    private RectTransform rt;
    private TextMeshProUGUI titleText;

    // Stats view
    private GameObject statsView;
    private TextMeshProUGUI statsText;

    // Graph view
    private GameObject graphView;
    private UILineGraph historyGraph;
    private TextMeshProUGUI graphTitle;
    private TextMeshProUGUI graphMinLabel, graphMaxLabel, graphCurrentLabel;
    private int graphMetric = 0;   // 0 = Production, 1 = Consumption (only for consumers)

    // Buttons (always visible, never rebuilt)
    private GameObject buttonsRoot;

    // Inspected object
    private object inspectedAsset;
    private ElectricalNode inspectedNode;
    private PowerLine inspectedLine;

    private bool isPinned;

    // History
    private readonly List<float> metricHistory = new();
    private float historyTimer;
    private float lastRecordedMetric;

    // Drag
    private Vector2 dragOffset;

    // ── Factory ───────────────────────────────────────────────────────────────
    public static AssetPanel Open(object asset, Vector2 screenPos)
    {
        // If a panel for this asset already exists, bring it to front + flash outline
        if (openPanels.TryGetValue(asset, out var existing))
        {
            existing.BringToFrontAndHighlight();
            return existing;
        }

        // --- Close all unpinned panels when a NEW asset is opened ---
        var toClose = new List<AssetPanel>();
        foreach (var kvp in openPanels)
        {
            if (!kvp.Value.isPinned)
                toClose.Add(kvp.Value);
        }
        foreach (var p in toClose)
        {
            Destroy(p.gameObject);   // OnDestroy removes it from openPanels
        }
        // -----------------------------------------------------------

        var canvas = GetOrCreateCanvas();
        var go = new GameObject("AssetPanel");
        go.transform.SetParent(canvas.transform, worldPositionStays: false);
        var panel = go.AddComponent<AssetPanel>();
        panel.inspectedAsset = asset;
        openPanels[asset] = panel;
        panel.Build(screenPos, canvas);
        return panel;
    }

    // ── Build ─────────────────────────────────────────────────────────────────
    void Build(Vector2 screenPos, Canvas canvas)
    {
        rt = gameObject.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(340, 460);
        rt.pivot = new Vector2(0f, 1f);

        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvas.GetComponent<RectTransform>(),
                screenPos, canvas.worldCamera, out Vector2 local))
            rt.anchoredPosition = local;

        // Only ONE Image component – store it for highlight
        var img = gameObject.AddComponent<Image>();
        img.color = new Color(0.07f, 0.09f, 0.12f, 0.97f);
        bgImage = img;

        BuildHeader();
        BuildTabBar();
        statsView = BuildStatsView();
        graphView = BuildGraphView();
        graphView.SetActive(false);
        BuildControlBar();

        RefreshStats();
        RefreshGraph();
    }

    // ── Header ────────────────────────────────────────────────────────────────
    void BuildHeader()
    {
        var hRT = Child("Header", rt, new Vector2(0, 1), new Vector2(1, 1),
                         new Vector2(0, -40), Vector2.zero);
        hRT.gameObject.AddComponent<Image>().color = new Color(0.05f, 0.06f, 0.09f);

        var tGO = new GameObject("Title");
        tGO.transform.SetParent(hRT, false);
        titleText = tGO.AddComponent<TextMeshProUGUI>();
        titleText.fontSize = 13;
        titleText.color = Color.white;
        titleText.fontStyle = FontStyles.Bold;
        var tRT = tGO.GetComponent<RectTransform>();
        tRT.anchorMin = new Vector2(0.04f, 0.1f); tRT.anchorMax = new Vector2(0.7f, 0.9f);
        tRT.offsetMin = tRT.offsetMax = Vector2.zero;

        Btn("PinBtn", hRT, new Vector2(0.71f, 0.1f), new Vector2(0.86f, 0.9f), "PIN", 10, TogglePin);
        Btn("CloseBtn", hRT, new Vector2(0.87f, 0.1f), new Vector2(1, 0.9f), "✕", 13, () => Destroy(gameObject));

        hRT.gameObject.AddComponent<HeaderDragHandler>().panel = this;
    }

    void AddSlider(string label, float min, float max, float initial, Action<float> onChanged)
    {
        // Container for the whole slider row
        var container = new GameObject("Slider_" + label);
        container.transform.SetParent(buttonsRoot.transform, false);
        var crt = container.AddComponent<RectTransform>();
        crt.sizeDelta = new Vector2(220, 28);
        container.AddComponent<LayoutElement>().preferredWidth = 220;

        var hLayout = container.AddComponent<HorizontalLayoutGroup>();
        hLayout.childControlWidth = true;
        hLayout.childForceExpandWidth = false;
        hLayout.spacing = 4;
        hLayout.childAlignment = TextAnchor.MiddleLeft;

        // --- Label ---
        var lblGO = new GameObject("Label");
        lblGO.transform.SetParent(container.transform, false);
        var lbl = lblGO.AddComponent<TextMeshProUGUI>();
        lbl.text = label;
        lbl.fontSize = 8;
        lbl.color = Color.white;
        lbl.alignment = TextAlignmentOptions.MidlineLeft;
        lblGO.GetComponent<RectTransform>().sizeDelta = new Vector2(50, 28);

        // --- Slider ---
        var sliderGO = new GameObject("Slider");
        sliderGO.transform.SetParent(container.transform, false);
        var slider = sliderGO.AddComponent<Slider>();
        slider.minValue = min;
        slider.maxValue = max;
        slider.value = initial;
        slider.wholeNumbers = false;
        slider.direction = Slider.Direction.LeftToRight;
        slider.interactable = true;
        slider.transition = Selectable.Transition.ColorTint;

        var colors = slider.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(0.9f, 0.9f, 0.9f);
        colors.pressedColor = new Color(0.7f, 0.7f, 0.7f);
        slider.colors = colors;

        var sliderRT = sliderGO.GetComponent<RectTransform>();
        sliderRT.sizeDelta = new Vector2(100, 20);

        // Background (full width) – no raycast
        var bgGO = new GameObject("Background");
        bgGO.transform.SetParent(sliderGO.transform, false);
        var bgImg = bgGO.AddComponent<Image>();
        bgImg.color = new Color(0.1f, 0.1f, 0.15f);
        bgImg.raycastTarget = false;
        var bgRT = bgGO.GetComponent<RectTransform>();
        bgRT.anchorMin = Vector2.zero; bgRT.anchorMax = Vector2.one;
        bgRT.sizeDelta = Vector2.zero;

        // Fill Area
        var fillArea = new GameObject("Fill Area");
        fillArea.transform.SetParent(sliderGO.transform, false);
        var faRT = fillArea.AddComponent<RectTransform>();
        faRT.anchorMin = new Vector2(0, 0.25f); faRT.anchorMax = new Vector2(1, 0.75f);
        faRT.offsetMin = Vector2.zero; faRT.offsetMax = Vector2.zero;

        var fillGO = new GameObject("Fill");
        fillGO.transform.SetParent(fillArea.transform, false);
        var fillImg = fillGO.AddComponent<Image>();
        fillImg.color = new Color(0.2f, 0.6f, 1f);
        fillImg.raycastTarget = false;
        var fillRT = fillGO.GetComponent<RectTransform>();
        fillRT.anchorMin = Vector2.zero; fillRT.anchorMax = Vector2.one;
        fillRT.sizeDelta = Vector2.zero;

        // Handle Slide Area (invisible)
        var handleSlide = new GameObject("Handle Slide Area");
        handleSlide.transform.SetParent(sliderGO.transform, false);
        var hsRT = handleSlide.AddComponent<RectTransform>();
        hsRT.anchorMin = Vector2.zero; hsRT.anchorMax = Vector2.one;
        hsRT.sizeDelta = Vector2.zero;

        // Handle (small white square)
        var handleGO = new GameObject("Handle");
        handleGO.transform.SetParent(handleSlide.transform, false);
        var handleImg = handleGO.AddComponent<Image>();
        handleImg.color = Color.white;   // raycastTarget true by default
        var handleRT = handleGO.GetComponent<RectTransform>();
        handleRT.anchorMin = handleRT.anchorMax = new Vector2(0.5f, 0.5f);
        handleRT.pivot = new Vector2(0.5f, 0.5f);
        handleRT.sizeDelta = new Vector2(12, 12);
        handleRT.anchoredPosition = Vector2.zero;

        // Link slider parts
        slider.fillRect = fillRT;
        slider.handleRect = handleRT;
        slider.targetGraphic = handleImg;

        // Debug: confirm value changes
   
        // --- Value text ---
        var valGO = new GameObject("Value");
        valGO.transform.SetParent(container.transform, false);
        var valTMP = valGO.AddComponent<TextMeshProUGUI>();
        valTMP.text = $"{initial:F1} MW";
        valTMP.fontSize = 8;
        valTMP.color = Color.white;
        valTMP.alignment = TextAlignmentOptions.MidlineRight;

        slider.onValueChanged.AddListener(v =>
   {
       // Debug.Log($"Slider value: {v}");
       valTMP.text = $"{v:F1} MW";
       onChanged(v);
   });

        valGO.GetComponent<RectTransform>().sizeDelta = new Vector2(50, 28);
    }
    void TogglePin()
    {
        isPinned = !isPinned;
        titleText.text = (isPinned ? "📌 " : "") + (inspectedNode != null ? $"{inspectedNode.GetType().Name}: {inspectedNode.name}" : "Asset");
    }

    // ── Tab bar ───────────────────────────────────────────────────────────────
    void BuildTabBar()
    {
        var tabRT = Child("Tabs", rt, new Vector2(0, 1), new Vector2(1, 1),
                           new Vector2(0, -70), new Vector2(0, -40));
        tabRT.gameObject.AddComponent<Image>().color = new Color(0.05f, 0.06f, 0.09f);

        Btn("StatsTab", tabRT, new Vector2(0, 0), new Vector2(0.5f, 1), "STATS", 11, ShowStats);
        Btn("GraphTab", tabRT, new Vector2(0.5f, 0), new Vector2(1, 1), "GRAPH", 11, ShowGraph);
    }

    // ── Stats view ────────────────────────────────────────────────────────────
    GameObject BuildStatsView()
    {
        var root = Child("StatsView", rt, Vector2.zero, new Vector2(1, 1),
                          new Vector2(8, 8), new Vector2(-8, -80));   // leave room for control bar at bottom
        root.gameObject.AddComponent<Image>().color = Color.clear;

        var go = new GameObject("StatsText");
        go.transform.SetParent(root, false);
        var stRT = go.AddComponent<RectTransform>();
        stRT.anchorMin = new Vector2(0, 0); stRT.anchorMax = new Vector2(1, 1);
        stRT.offsetMin = new Vector2(4, 4); stRT.offsetMax = new Vector2(-4, -4);

        statsText = go.AddComponent<TextMeshProUGUI>();
        statsText.fontSize = 10;
        statsText.color = new Color(0.7f, 0.7f, 0.7f);
        statsText.alignment = TextAlignmentOptions.TopLeft;

        return root.gameObject;
    }

    // ── Graph view ────────────────────────────────────────────────────────────
    GameObject BuildGraphView()
    {
        var root = Child("GraphView", rt, Vector2.zero, new Vector2(1, 1),
                           new Vector2(0, 8), new Vector2(0, -80));

        // Determine which metrics are available
        var metrics = new List<(string name, string title, string suffix, Color color)>();
        if (inspectedAsset is EnergyProducer)
        {
            metrics.Add(("PROD", "Production (MW)", " MW", new Color(0.2f, 1f, 0.4f)));
            // optional: add Voltage / Frequency later
        }
        else if (inspectedAsset is PowerConsumer)
        {
            metrics.Add(("CONS", "Consumption (MW)", " MW", new Color(1f, 0.8f, 0.2f)));
        }
        else if (inspectedAsset is ElectricalNode) // generic node (e.g. PowerLineStructure)
        {
            metrics.Add(("PROD", "Production (MW)", " MW", new Color(0.2f, 1f, 0.4f)));
            metrics.Add(("CONS", "Consumption (MW)", " MW", new Color(1f, 0.8f, 0.2f)));
        }
        // PowerLine could have a "FLOW" metric later

        // Metric selector row
        var btnBar = Child("MetricBar", root, new Vector2(0, 1), new Vector2(1, 1),
                            new Vector2(0, -28), Vector2.zero);
        btnBar.gameObject.AddComponent<Image>().color = new Color(0.04f, 0.05f, 0.08f);

        for (int i = 0; i < metrics.Count; i++)
        {
            int idx = i;
            float x0 = (float)i / metrics.Count;
            float x1 = (float)(i + 1) / metrics.Count;
            Btn($"M{i}", btnBar, new Vector2(x0, 0), new Vector2(x1, 1),
                metrics[i].name, 8, () => { graphMetric = idx; RefreshGraph(); });
        }

        // Graph title
        var gtGO = new GameObject("GTitle");
        gtGO.transform.SetParent(root, false);
        graphTitle = gtGO.AddComponent<TextMeshProUGUI>();
        graphTitle.text = metrics.Count > 0 ? metrics[0].title : "";
        graphTitle.fontSize = 10;
        graphTitle.color = new Color(0.5f, 0.5f, 0.55f);
        graphTitle.alignment = TextAlignmentOptions.Center;
        var gtRT = gtGO.GetComponent<RectTransform>();
        gtRT.anchorMin = new Vector2(0, 1); gtRT.anchorMax = new Vector2(1, 1);
        gtRT.offsetMin = new Vector2(4, -50); gtRT.offsetMax = new Vector2(-4, -28);

        // Graph area
        var gbRT = Child("GraphArea", root, Vector2.zero, new Vector2(1, 1),
                          new Vector2(8, 8), new Vector2(-8, -54));

        graphMinLabel = CreateGraphLabel(gbRT, "MinLabel", new Vector2(0, 0));
        graphMaxLabel = CreateGraphLabel(gbRT, "MaxLabel", new Vector2(0, 1));
        graphCurrentLabel = CreateGraphLabel(gbRT, "CurrentLabel", new Vector2(0, 0.5f));

        var lgGO = new GameObject("LineGraph");
        lgGO.transform.SetParent(gbRT, false);
        historyGraph = lgGO.AddComponent<UILineGraph>();
        var lgRT = lgGO.GetComponent<RectTransform>();
        lgRT.anchorMin = Vector2.zero; lgRT.anchorMax = Vector2.one;
        lgRT.offsetMin = lgRT.offsetMax = Vector2.zero;

        // Bring labels to front
        graphMinLabel.rectTransform.SetAsLastSibling();
        graphMaxLabel.rectTransform.SetAsLastSibling();
        graphCurrentLabel.rectTransform.SetAsLastSibling();

        return root.gameObject;
    }

    TextMeshProUGUI CreateGraphLabel(RectTransform parent, string name, Vector2 anchor)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, anchor.y);
        rt.anchorMax = new Vector2(0, anchor.y);
        rt.pivot = new Vector2(0, anchor.y);
        rt.sizeDelta = new Vector2(60, 18);
        rt.anchoredPosition = anchor.y > 0.5f ? new Vector2(4, -12) : (anchor.y < 0.5f ? new Vector2(4, 12) : new Vector2(4, 0));

        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.fontSize = 9;
        tmp.color = new Color(0.65f, 0.65f, 0.7f);
        tmp.alignment = TextAlignmentOptions.Left;
        return tmp;
    }

    // ── Control bar (buttons always visible) ───────────────────────────────────
    void BuildControlBar()
    {
        var barRT = Child("ControlBar", rt, new Vector2(0, 0), new Vector2(1, 0),
                           new Vector2(8, 8), new Vector2(-8, 48));
        barRT.gameObject.AddComponent<Image>().color = new Color(0.05f, 0.06f, 0.09f);

        // Use a horizontal layout group to auto-arrange buttons
        var layout = barRT.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 8;
        layout.padding = new RectOffset(6, 6, 6, 6);
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = false;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = true;

        buttonsRoot = barRT.gameObject;  // add buttons as children of this layout
    }

    // ── Tab switching ─────────────────────────────────────────────────────────
    void ShowStats() { statsView.SetActive(true); graphView.SetActive(false); }
    void ShowGraph() { statsView.SetActive(false); graphView.SetActive(true); RefreshGraph(); }

    // ── Refresh stats text ────────────────────────────────────────────────────
    void RefreshStats()
    {
        if (inspectedAsset == null) { Destroy(gameObject); return; }
        inspectedNode = inspectedAsset as ElectricalNode;
        inspectedLine = inspectedAsset as PowerLine;

        // Title
        if (inspectedNode != null)
            titleText.text = (isPinned ? "📌 " : "") + $"{inspectedNode.GetType().Name}: {inspectedNode.name}";
        else if (inspectedLine != null)
            titleText.text = $"PowerLine ({inspectedLine.startPoint?.owner?.name} → {inspectedLine.endPoint?.owner?.name})";

        // Stats text
        string stats = "";
        if (inspectedNode is EnergyProducer prod)
        {
            stats += $"Running: {(prod.IsRunning ? "YES" : "NO")}\n";
            stats += $"Output: {prod.GetProductionMW():F1} MW\n";
            stats += $"Rated: {prod.RatedVoltageKV} kV / {prod.RatedFrequencyHz} Hz\n";
            if (prod is CoalGenerator coal)
                stats += $"Coal: {coal.GetCoalStored():F1} tons\n";
        }
        else if (inspectedNode is PowerConsumer cons)
        {
            stats += $"Active: {(cons.IsActive ? "YES" : "NO")}\n";
            stats += $"Demand: {cons.DemandMW:F1} MW\n";
            stats += $"Customers: {cons.CustomerCount}\n";
        }
        else if (inspectedNode != null)
        {
            stats += $"Production: {inspectedNode.GetProductionMW():F1} MW\n";
            stats += $"Consumption: {inspectedNode.GetConsumptionMW():F1} MW\n";
        }

        if (inspectedLine != null)
        {
            stats += $"Capacity: {inspectedLine.MaxCapacityMW:F0} MW\n";
        }

        if (statsText != null) statsText.text = stats;
    }

    // ── Refresh graph ─────────────────────────────────────────────────────────
    void RefreshGraph()
    {
        if (historyGraph == null || inspectedNode == null) return;

        // Build the same metrics list as in BuildGraphView (could be cached)
        var metrics = new List<(string name, string title, string suffix, Color color)>();
        if (inspectedNode is EnergyProducer)
        {
            metrics.Add(("PROD", "Production (MW)", " MW", new Color(0.2f, 1f, 0.4f)));
        }
        else if (inspectedNode is PowerConsumer)
        {
            metrics.Add(("CONS", "Consumption (MW)", " MW", new Color(1f, 0.8f, 0.2f)));
        }
        else
        {
            metrics.Add(("PROD", "Production (MW)", " MW", new Color(0.2f, 1f, 0.4f)));
            metrics.Add(("CONS", "Consumption (MW)", " MW", new Color(1f, 0.8f, 0.2f)));
        }

        if (graphMetric < 0 || graphMetric >= metrics.Count) return;

        var metric = metrics[graphMetric];
        float currentMetric = 0f;

        if (metric.name == "PROD")
            currentMetric = inspectedNode.GetProductionMW();
        else if (metric.name == "CONS")
            currentMetric = inspectedNode.GetConsumptionMW();

        // History recording (same as before)
        if (Math.Abs(currentMetric - lastRecordedMetric) > 0.001f || Time.time - historyTimer > 1f)
        {
            metricHistory.Add(currentMetric);
            if (metricHistory.Count > 128) metricHistory.RemoveAt(0);
            lastRecordedMetric = currentMetric;
            historyTimer = Time.time;
        }

        if (metricHistory.Count > 1)
        {
            float[] data = metricHistory.ToArray();
            float min = data.Min();
            float max = data.Max();
            if (max - min < 0.001f) { min -= 1f; max += 1f; }

            if (graphTitle) graphTitle.text = metric.title;
            if (graphMinLabel) graphMinLabel.text = $"{min:F1}{metric.suffix}";
            if (graphMaxLabel) graphMaxLabel.text = $"{max:F1}{metric.suffix}";
            if (graphCurrentLabel) graphCurrentLabel.text = $"Now: {currentMetric:F1}{metric.suffix}";
            historyGraph.SetData(data, null, null, metric.color);
        }
        else
        {
            if (graphTitle) graphTitle.text = metric.title;
            if (graphMinLabel) graphMinLabel.text = "—";
            if (graphMaxLabel) graphMaxLabel.text = "—";
            if (graphCurrentLabel) graphCurrentLabel.text = "Now: —";
        }
    }

    // ── Build control buttons (called once after asset is known) ────────────
    void BuildControlButtons()
    {
        foreach (Transform t in buttonsRoot.transform) Destroy(t.gameObject);

        if (inspectedNode is EnergyProducer ep)
        {
            AddButton("Toggle On/Off", () => { ep.ToggleRunning(); RefreshStats(); });
            if (inspectedNode is CoalGenerator coalGen)
                AddButton("Add 50 Coal", () => { coalGen.AddCoal(50); RefreshStats(); });

            if (ep.IsPilotable)
            {
                AddSlider("Output MW", 0f, ep.GetMaxPowerOutputMW(), ep.CurrentOutputMW,
                    (v) => ep.SetOutputMW(v));
            }
        }
        if (inspectedNode is PowerConsumer)
        {
            AddButton("Toggle Active", () => { ((PowerConsumer)inspectedNode).ToggleActive(); RefreshStats(); });
        }
        
        // (Optional: add line controls here)
    }

    void AddButton(string label, Action onClick)
    {
        var go = new GameObject(label);
        go.transform.SetParent(buttonsRoot.transform, false);
        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(label.Length * 8 + 20, 28); // auto‑width

        var img = go.AddComponent<Image>();
        img.color = new Color(0.2f, 0.2f, 0.25f);
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(() => onClick());

        var tGO = new GameObject("T");
        tGO.transform.SetParent(go.transform, false);
        var tmp = tGO.AddComponent<TextMeshProUGUI>();
        tmp.text = label; tmp.fontSize = 9;
        tmp.color = Color.white; tmp.alignment = TextAlignmentOptions.Center;
        var tRT = tGO.GetComponent<RectTransform>();
        tRT.anchorMin = Vector2.zero; tRT.anchorMax = Vector2.one;
        tRT.offsetMin = tRT.offsetMax = Vector2.zero;
    }

    // ── Per‑frame update ─────────────────────────────────────────────────────
    void Update()
    {
        if (inspectedNode == null && inspectedLine == null) return;

        RefreshStats();                         // update text values
        if (graphView.activeSelf) RefreshGraph(); // update graph line
    }

    // ── Drag ─────────────────────────────────────────────────────────────────
    public void BeginDrag(PointerEventData e)
    {
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                rt.parent as RectTransform, e.position, e.pressEventCamera, out Vector2 local))
            dragOffset = rt.anchoredPosition - local;
    }

    public void Drag(PointerEventData e)
    {
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                rt.parent as RectTransform, e.position, e.pressEventCamera, out Vector2 local))
            rt.anchoredPosition = local + dragOffset;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    static RectTransform Child(string name, RectTransform parent, Vector2 ancMin, Vector2 ancMax, Vector2 offMin, Vector2 offMax)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var r = go.AddComponent<RectTransform>();
        r.anchorMin = ancMin; r.anchorMax = ancMax;
        r.offsetMin = offMin; r.offsetMax = offMax;
        return r;
    }

    static void Btn(string name, RectTransform parent, Vector2 ancMin, Vector2 ancMax, string label, float fs, Action onClick)
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
        const string name = "VoltarisUICanvas";
        var found = GameObject.Find(name);
        if (found != null)
        {
            var cv = found.GetComponent<Canvas>();
            if (cv != null) return cv;          // valid existing canvas
        }

        // Not found or no Canvas component – create a fresh one
        var go = new GameObject(name);
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;

        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        go.AddComponent<GraphicRaycaster>();

        EnsureEventSystem();
        return canvas;
    }

    static void EnsureEventSystem()
    {
        var es = FindObjectOfType<EventSystem>();
        if (es == null)
        {
            var esGO = new GameObject("EventSystem");
            es = esGO.AddComponent<EventSystem>();
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
            esGO.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
            esGO.AddComponent<StandaloneInputModule>();
#endif
            return;
        }

#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        var legacy = es.GetComponent<StandaloneInputModule>();
        if (legacy != null && es.GetComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>() == null)
        {
            Destroy(legacy);
            es.gameObject.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
        }
#endif
    }

    // ── Called from AssetInspector when first opening ─────────────────────────
    // Already set via factory, but we can call BuildControlButtons after the asset is set.
    // Actually, we need to call BuildControlButtons after inspectedNode is known.
    // We'll do it at the end of RefreshStats() on the first time.
    private bool buttonsBuilt = false;
    void LateUpdate()
    {
        if (!buttonsBuilt && (inspectedNode != null || inspectedLine != null))
        {
            BuildControlButtons();
            buttonsBuilt = true;
        }
    }

    public void BringToFrontAndHighlight()
    {
        transform.SetAsLastSibling();
        if (highlightRoutine != null)
            StopCoroutine(highlightRoutine);
        highlightRoutine = StartCoroutine(FlashHighlight());
    }

    IEnumerator FlashHighlight()
    {
        // Ensure we have an Image to attach the outline to
        if (bgImage == null) yield break;

        // Get or add an Outline component on the panel's background Image
        var outline = bgImage.gameObject.GetComponent<Outline>();
        if (outline == null)
            outline = bgImage.gameObject.AddComponent<Outline>();

        outline.effectColor = new Color(0.9f, 0.85f, 0.2f, 1f);   // bright yellow/gold
        outline.effectDistance = new Vector2(3, -3);                // diagonal outline

        yield return new WaitForSeconds(0.15f);

        // Remove the outline after the flash
        if (outline != null) Destroy(outline);
    }

    void OnDestroy()
    {
        if (inspectedAsset != null)
            openPanels.Remove(inspectedAsset);
    }
}