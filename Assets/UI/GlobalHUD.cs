using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Persistent top-centre bar showing:
///   ⚡ Total production (MW)    〜 Grid frequency (Hz)
///   $ Money                     👥 Customer count
///
/// Fully built in code — attach to any persistent GameObject and press Play.
/// Requires TextMeshPro (Window → Package Manager → TextMeshPro).
/// </summary>
public class GlobalHUD : MonoBehaviour
{
    [SerializeField] private float refreshRate = 0.25f;

    private TextMeshProUGUI prodText, freqText, moneyText, custText;
    private float timer;

    // ── Setup ─────────────────────────────────────────────────────────────────

    void Start() => Build();

    void Build()
    {
        // Dedicated overlay canvas — always on top
        var cGO = new GameObject("GlobalHUD_Canvas");
        DontDestroyOnLoad(cGO);

        var cv = cGO.AddComponent<Canvas>();
        cv.renderMode = RenderMode.ScreenSpaceOverlay;
        cv.sortingOrder = 200;

        var scaler = cGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        cGO.AddComponent<GraphicRaycaster>();

        // ── Bar container ────────────────────────────────────────────────────
        var bar = new GameObject("Bar");
        bar.transform.SetParent(cGO.transform, false);
        var barRT = bar.AddComponent<RectTransform>();
        barRT.anchorMin = new Vector2(0.15f, 1f);
        barRT.anchorMax = new Vector2(0.85f, 1f);
        barRT.pivot = new Vector2(0.5f, 1f);
        barRT.anchoredPosition = Vector2.zero;
        barRT.sizeDelta = new Vector2(0, 46);

        var barBG = bar.AddComponent<Image>();
        barBG.color = new Color(0.04f, 0.05f, 0.07f, 0.94f);

        // Bottom accent line
        var line = new GameObject("Line");
        line.transform.SetParent(bar.transform, false);
        var lineRT = line.AddComponent<RectTransform>();
        lineRT.anchorMin = new Vector2(0, 0);
        lineRT.anchorMax = new Vector2(1, 0);
        lineRT.pivot = new Vector2(0.5f, 0);
        lineRT.anchoredPosition = Vector2.zero;
        lineRT.sizeDelta = new Vector2(0, 2);
        line.AddComponent<Image>().color = new Color(0.2f, 0.6f, 1f, 0.55f);

        // ── Four stat cells ──────────────────────────────────────────────────
        prodText = Cell(bar, 0f, "⚡  —", new Color(0.30f, 0.95f, 0.45f));
        freqText = Cell(bar, 0.25f, "〜  —", Color.cyan);
        moneyText = Cell(bar, 0.50f, "$  —", new Color(1.00f, 0.85f, 0.20f));
        custText = Cell(bar, 0.75f, "👥  —", new Color(0.80f, 0.60f, 1.00f));
    }

    TextMeshProUGUI Cell(GameObject bar, float xAnchor, string def, Color col)
    {
        var go = new GameObject($"Cell_{xAnchor:F2}");
        go.transform.SetParent(bar.transform, false);

        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(xAnchor, 0);
        rt.anchorMax = new Vector2(xAnchor + 0.25f, 1);
        rt.offsetMin = rt.offsetMax = Vector2.zero;

        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = def;
        tmp.fontSize = 15f;
        tmp.color = col;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.fontStyle = FontStyles.Bold;
        return tmp;
    }

    // ── Update ────────────────────────────────────────────────────────────────

    void Update()
    {
        timer += Time.deltaTime;
        if (timer < refreshRate) return;
        timer = 0f;

        var mgr = PowerGridManager.Instance;
        if (mgr == null) return;

        prodText.text = $"⚡  {mgr.TotalProductionMW:F1} MW";
        moneyText.text = $"$  {GameEconomy.Instance?.Money ?? 0f:N0}";
        custText.text = $"👥  {mgr.TotalCustomers:N0}";

        float freq = mgr.OverallFrequencyHz;
        freqText.text = $"〜  {freq:F2} Hz";

        // Colour-code frequency: red at limits, cyan at nominal
        float frac = Mathf.InverseLerp(PowerNetwork.MinFreqHz,
                                          PowerNetwork.MaxFreqHz, freq);
        freqText.color = Color.Lerp(Color.red, Color.cyan, frac);
    }
}