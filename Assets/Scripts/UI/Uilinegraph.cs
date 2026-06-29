using System.Linq;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Line graph rendered into a RawImage texture using Bresenham's line algorithm.
/// Replaces the previous MaskableGraphic approach which had cross-version quirks.
///
/// Call SetData() any time the dataset changes.
/// The texture is created once and repainted in place — no allocations per frame.
/// </summary>
[RequireComponent(typeof(RawImage))]
public class UILineGraph : MonoBehaviour
{
    // Texture resolution — trade-off between quality and memory
    private const int W = 512, H = 256;

    private RawImage ri;
    private Texture2D tex;
    private Color[] pixels; // reused buffer, no alloc per repaint

    private static readonly Color Bg = new Color(0.04f, 0.05f, 0.08f, 1f);
    private static readonly Color Grid = new Color(0.18f, 0.22f, 0.28f, 1f);

    // ── Unity ─────────────────────────────────────────────────────────────────

    void Awake()
    {
        ri = GetComponent<RawImage>();

        tex = new Texture2D(W, H, TextureFormat.RGBA32, mipChain: false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        ri.texture = tex;

        pixels = new Color[W * H];
        Clear();
    }

    void OnDestroy()
    {
        if (tex != null) Destroy(tex);
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <param name="values">Data points to plot, oldest first.</param>
    /// <param name="fixedMin">Pin y-axis minimum (null = auto from data).</param>
    /// <param name="fixedMax">Pin y-axis maximum (null = auto from data).</param>
    /// <param name="color">Line colour.</param>
    public void SetData(float[] values,
                        float? fixedMin = null,
                        float? fixedMax = null,
                        Color? color = null)
    {
        Color lineCol = color ?? new Color(0.30f, 0.85f, 1.00f);

        // 1. Background
        Fill(Bg);

        // 2. Dashed horizontal grid lines at 25 / 50 / 75 %
        for (int g = 1; g <= 3; g++)
        {
            int y = Mathf.RoundToInt(g / 4f * H);
            for (int x = 0; x < W; x++)
                if (x % 6 < 4)   // 4-on, 2-off dashes
                    Set(x, y, Grid);
        }

        // 3. Data line
        if (values != null && values.Length >= 2)
        {
            float min = fixedMin ?? values.Min();
            float max = fixedMax ?? values.Max();
            if (max - min < 0.001f) { min -= 1f; max += 1f; }

            for (int i = 0; i < values.Length - 1; i++)
            {
                float t0 = (float)i / (values.Length - 1);
                float t1 = (float)(i + 1) / (values.Length - 1);

                int x0 = Mathf.RoundToInt(t0 * (W - 1));
                int x1 = Mathf.RoundToInt(t1 * (W - 1));
                int y0 = Mathf.RoundToInt(Mathf.InverseLerp(min, max, values[i]) * (H - 1));
                int y1 = Mathf.RoundToInt(Mathf.InverseLerp(min, max, values[i + 1]) * (H - 1));

                Bresenham(x0, y0, x1, y1, lineCol, thickness: 2);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply(updateMipmaps: false);
    }

    // ── Raster helpers ────────────────────────────────────────────────────────

    void Fill(Color c)
    {
        for (int i = 0; i < pixels.Length; i++) pixels[i] = c;
    }

    void Clear() { Fill(Bg); tex.SetPixels(pixels); tex.Apply(false); }

    void Set(int x, int y, Color c)
    {
        if (x < 0 || x >= W || y < 0 || y >= H) return;
        pixels[y * W + x] = c;
    }

    // Bresenham's line with square thickness brush
    void Bresenham(int x0, int y0, int x1, int y1, Color c, int thickness)
    {
        int dx = Mathf.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
        int dy = -Mathf.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
        int err = dx + dy;
        int h = thickness / 2;

        while (true)
        {
            for (int ky = -h; ky <= h; ky++)
                for (int kx = -h; kx <= h; kx++)
                    Set(x0 + kx, y0 + ky, c);

            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }
}