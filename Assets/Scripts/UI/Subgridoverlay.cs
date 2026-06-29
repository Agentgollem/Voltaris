using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using TMPro;

/// <summary>
/// World-space visual for one PowerNetwork.
/// Draws a colored convex-hull outline around all member nodes
/// and places a billboard label at the centroid.
///
/// Created and managed by SubgridVisualizer.
/// </summary>
public class SubgridOverlay : MonoBehaviour
{
    public PowerNetwork Network { get; private set; }

    // Exposed so SubgridVisualizer can configure before first UpdateVisual()
    public float Padding = 2.5f;
    public float OutlineY = 0.1f;
    public float LabelHeight = 4f;

    private LineRenderer outlineLr;
    private TextMeshPro labelTmp;
    private List<Vector2> hull = new();

    // ── Factory ───────────────────────────────────────────────────────────────

    public static SubgridOverlay Create(PowerNetwork network, Color color, Transform parent)
    {
        var go = new GameObject($"Overlay_{network.NetworkID}");
        go.transform.SetParent(parent, worldPositionStays: true);
        var ov = go.AddComponent<SubgridOverlay>();
        ov.Init(network, color);
        return ov;
    }

    // ── Init ──────────────────────────────────────────────────────────────────

    void Init(PowerNetwork net, Color color)
    {
        Network = net;

        // ── LineRenderer outline ──────────────────────────────────────────────
        outlineLr = gameObject.AddComponent<LineRenderer>();
        outlineLr.useWorldSpace = true;
        outlineLr.loop = true;
        outlineLr.startWidth = 0.15f;
        outlineLr.endWidth = 0.15f;
        outlineLr.sortingOrder = 2;

        var mat = new Material(Shader.Find("Unlit/Color"));
        mat.color = color;
        outlineLr.material = mat;

        // ── TextMeshPro label ─────────────────────────────────────────────────
        var labelGO = new GameObject("Label");
        labelGO.transform.SetParent(transform, worldPositionStays: false);

        labelTmp = labelGO.AddComponent<TextMeshPro>();
        labelTmp.text = net.NetworkID;
        labelTmp.fontSize = 3.5f;
        labelTmp.color = color;
        labelTmp.fontStyle = FontStyles.Bold;
        labelTmp.alignment = TextAlignmentOptions.Center;
        labelTmp.sortingOrder = 3;
    }

    // ── Network swap (called by SubgridVisualizer after rebuild) ──────────────

    public void UpdateNetwork(PowerNetwork newNet)
    {
        Network = newNet;
        if (labelTmp != null) labelTmp.text = newNet.NetworkID;
    }

    // ── Visual update (called every frame by SubgridVisualizer) ──────────────

    public void UpdateVisual()
    {
        if (Network == null) { outlineLr.positionCount = 0; return; }

        var xzPositions = NodePositionsXZ();
        if (xzPositions.Count == 0) { outlineLr.positionCount = 0; return; }

        var centroid = Centroid(xzPositions);
        hull = ExpandedHull(xzPositions, Padding);

        // Apply to LineRenderer (draws at OutlineY world height)
        outlineLr.positionCount = hull.Count;
        for (int i = 0; i < hull.Count; i++)
            outlineLr.SetPosition(i, new Vector3(hull[i].x, OutlineY, hull[i].y));

        // Move label to centroid
        labelTmp.transform.position =
            new Vector3(centroid.x, LabelHeight, centroid.y);
    }

    void LateUpdate()
    {
        // Billboard: always face the camera
        if (Camera.main != null && labelTmp != null)
            labelTmp.transform.forward = Camera.main.transform.forward;
    }

    // ── Hit testing ──────────────────────────────────────────────────────────

    /// Returns true if worldPos projected onto XZ is inside this overlay's hull.
    public bool ContainsPoint(Vector3 worldPos) =>
        PointInPolygon(new Vector2(worldPos.x, worldPos.z), hull);

    // ── Geometry helpers ──────────────────────────────────────────────────────

    List<Vector2> NodePositionsXZ()
    {
        var list = new List<Vector2>();
        foreach (var node in Network.Nodes)
            if (node != null)
                list.Add(new Vector2(node.transform.position.x, node.transform.position.z));
        return list;
    }

    static Vector2 Centroid(List<Vector2> pts)
    {
        Vector2 s = Vector2.zero;
        foreach (var p in pts) s += p;
        return s / pts.Count;
    }

    List<Vector2> ExpandedHull(List<Vector2> pts, float pad)
    {
        if (pts.Count == 1)
        {
            // Regular octagon around a single node
            var oct = new List<Vector2>(8);
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI * 2f / 8f;
                oct.Add(pts[0] + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * pad);
            }
            return oct;
        }

        if (pts.Count == 2)
        {
            // Capsule rectangle for exactly two nodes
            Vector2 dir = (pts[1] - pts[0]).normalized;
            Vector2 perp = new Vector2(-dir.y, dir.x) * pad;
            return new List<Vector2>
            {
                pts[0] - dir * pad + perp,
                pts[1] + dir * pad + perp,
                pts[1] + dir * pad - perp,
                pts[0] - dir * pad - perp,
            };
        }

        // 3+ nodes: convex hull, then expand each vertex outward from centroid
        var raw = ConvexHull(pts);
        var centroid = Centroid(raw);
        return raw.Select(v => v + (v - centroid).normalized * pad).ToList();
    }

    // Jarvis March (gift-wrapping) O(nh), fine for small node counts
    static List<Vector2> ConvexHull(List<Vector2> pts)
    {
        int n = pts.Count;
        if (n < 3) return new List<Vector2>(pts);

        int l = 0;
        for (int i = 1; i < n; i++)
            if (pts[i].x < pts[l].x) l = i;

        var hull = new List<Vector2>();
        int p = l;
        do
        {
            hull.Add(pts[p]);
            int q = (p + 1) % n;
            for (int i = 0; i < n; i++)
                if (Cross(pts[p], pts[q], pts[i]) < 0f) q = i;
            p = q;
        } while (p != l && hull.Count <= n);

        return hull;
    }

    static float Cross(Vector2 o, Vector2 a, Vector2 b) =>
        (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);

    // Ray-casting point-in-polygon
    static bool PointInPolygon(Vector2 pt, List<Vector2> poly)
    {
        int n = poly.Count;
        bool inside = false;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            if ((poly[i].y > pt.y) != (poly[j].y > pt.y) &&
                pt.x < (poly[j].x - poly[i].x) * (pt.y - poly[i].y) /
                        (poly[j].y - poly[i].y) + poly[i].x)
                inside = !inside;
        }
        return inside;
    }
}