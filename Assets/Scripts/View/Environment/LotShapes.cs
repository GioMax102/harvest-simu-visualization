using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace HarvestSim.Visualization
{
    /// <summary>
    /// Forma real de los lotes (trace 2.3, <c>mundo.geo.formas.lotes</c>) recortada por celda. Solo dibuja.
    ///
    /// La simulación marca una celda como campo si al menos la mitad cae en un lote, así que el cuadro completo se
    /// sale del contorno y, en la misma orilla, quedan pedazos de lote en celdas fuera de campo. Aquí cada celda de
    /// campo se dibuja como (su cuadro ∩ lotes) más los pedazos de lote de sus vecinas (8) fuera de campo o de camino
    /// que le quedan más cerca (Voronoi entre centros de celdas de campo). La cosecha no cambia: lo prestado se
    /// cosecha junto con su dueña. Obstáculos, bodega y bases no prestan nada.
    ///
    /// Los lotes se triangulan una vez (orejas) y cada triángulo se recorta contra ventanas convexas, así que cada
    /// pedazo es un polígono convexo. Sin <c>formas.lotes</c> (traces 2.1/2.2) no hay pedazos y todo queda como antes.
    /// </summary>
    public sealed class LotShapes
    {
        /// <summary>Distancia extra (m) a la orilla de la cinta del camino dentro de la cual no se siembra.</summary>
        public const float RoadClearance = .8f;

        public bool HasLots => lots.Count > 0;
        public IReadOnlyList<Vector2[]> Lots => lots;
        /// <summary>Celdas de campo que no son un cuadro completo dentro del lote (recortadas o con préstamo).</summary>
        public int BorderCellCount => pieces.Count;

        readonly FieldGrid grid;
        readonly List<Vector2[]> lots = new();
        readonly List<Vector2[]> triangles = new();
        readonly List<Rect> triangleRects = new();
        readonly List<(Vector2 a, Vector2 b)> roads = new();
        readonly Dictionary<Vector2Int, List<Vector2[]>> pieces = new();
        readonly Dictionary<Vector2Int, Rect> regions = new();
        readonly Dictionary<Vector2Int, List<int>> roadsNear = new();

        LotShapes(FieldGrid grid) { this.grid = grid; }

        public static LotShapes From(FieldGrid grid, TraceData trace)
        {
            var shapes = new LotShapes(grid);
            if (grid == null || trace?.World?["geo"] is not JObject geo || geo["formas"] is not JObject formas) return shapes;
            if (formas["lotes"] is JObject lotes)
                foreach (var lote in lotes.Properties())
                {
                    var ring = Clean(Points(grid, lote.Value));
                    if (ring.Count < 3) continue;
                    shapes.lots.Add(ring.ToArray());
                    var tri = Triangulate(ring);
                    for (int i = 0; i + 2 < tri.Count; i += 3)
                    {
                        var t = new[] { ring[tri[i]], ring[tri[i + 1]], ring[tri[i + 2]] };
                        shapes.triangles.Add(t);
                        shapes.triangleRects.Add(Bounds(t));
                    }
                }
            if (formas["caminos"] is JArray caminos)
                foreach (var linea in caminos)
                {
                    var pts = Points(grid, linea);
                    for (int i = 0; i + 1 < pts.Count; i++) shapes.roads.Add((pts[i], pts[i + 1]));
                }
            if (shapes.HasLots) shapes.Cut();
            return shapes;
        }

        /// <summary>Pedazos convexos (X, Z del mundo) de una celda de campo, o null si es un cuadro completo
        /// dentro del lote sin préstamos (se dibuja como siempre).</summary>
        public IReadOnlyList<Vector2[]> PiecesOf(Vector2Int cell) => pieces.TryGetValue(cell, out var list) ? list : null;

        /// <summary>Rectángulo que cubre los pedazos de la celda (si tiene).</summary>
        public bool TryRegion(Vector2Int cell, out Rect region) => regions.TryGetValue(cell, out region);

        /// <summary>True si el punto cae en algún pedazo de la celda.</summary>
        public bool Contains(Vector2Int cell, Vector2 point)
        {
            if (!pieces.TryGetValue(cell, out var list)) return false;
            foreach (var piece in list) if (InConvex(piece, point)) return true;
            return false;
        }

        /// <summary>True si alguna cinta de camino pasa cerca de la celda (o de sus pedazos).</summary>
        public bool NearRoad(Vector2Int cell) => roadsNear.ContainsKey(cell);

        /// <summary>True si el punto cae sobre la cinta de un camino cercano a la celda (más el margen).</summary>
        public bool OnRoad(Vector2Int cell, Vector2 point)
        {
            if (!roadsNear.TryGetValue(cell, out var near)) return false;
            float limit = TerrainShapes.RoadWidth / 2 + RoadClearance;
            foreach (int i in near)
                if (DistanceToSegment(point, roads[i].a, roads[i].b) < limit) return true;
            return false;
        }

        /// <summary>True si el punto está dentro de algún lote.</summary>
        public bool InsideLot(Vector2 point)
        {
            foreach (var lot in lots) if (InPolygon(lot, point)) return true;
            return false;
        }

        /// <summary>Área total dibujada para la celda (m²): los pedazos, o el cuadro completo.</summary>
        public float DrawnArea(Vector2Int cell)
        {
            if (!pieces.TryGetValue(cell, out var list)) return grid.CellSize * grid.CellSize;
            float sum = 0;
            foreach (var piece in list) sum += Mathf.Abs(SignedArea(piece));
            return sum;
        }

        void Cut()
        {
            float s = grid.CellSize, full = s * s;
            var neighbors = new List<Vector2Int>();
            for (int y = 0; y < grid.Height; y++)
                for (int x = 0; x < grid.Width; x++)
                {
                    if (!grid.IsField(x, y)) continue;
                    var cell = new Vector2Int(x, y);
                    var list = new List<Vector2[]>();
                    float own = Clip(Square(cell), list);
                    bool changed = Mathf.Abs(own - full) > full * 1e-3f;
                    foreach (var n in Around(cell))
                    {
                        var kind = grid.KindAt(n.x, n.y);
                        if (kind is not (CellKind.OutsideField or CellKind.Service) || !grid.InBounds(n.x, n.y)) continue;
                        // Parte de la vecina que le queda más cerca a esta celda que a cualquier otra de campo.
                        var window = Square(n);
                        var me = Center(cell);
                        foreach (var other in Around(n))
                        {
                            if (other == cell || !grid.IsField(other.x, other.y)) continue;
                            var them = Center(other);
                            window = ClipHalfPlane(window, (me + them) / 2, them - me);
                            if (window.Count < 3) break;
                        }
                        if (window.Count < 3) continue;
                        if (Clip(window, list) > full * 1e-4f) changed = true;
                    }
                    if (changed)
                    {
                        pieces[cell] = list;
                        regions[cell] = Union(list, cell);
                    }
                    var area = regions.TryGetValue(cell, out var r) ? r : SquareRect(cell);
                    float reach = TerrainShapes.RoadWidth / 2 + RoadClearance;
                    var grown = new Rect(area.xMin - reach, area.yMin - reach, area.width + 2 * reach, area.height + 2 * reach);
                    List<int> near = null;
                    for (int i = 0; i < roads.Count; i++)
                        if (grown.Overlaps(Bounds(roads[i].a, roads[i].b))) (near ??= new List<int>()).Add(i);
                    if (near != null) roadsNear[cell] = near;
                }
        }

        /// <summary>Recorta cada triángulo de lote contra la ventana convexa; agrega los pedazos y devuelve su área.</summary>
        float Clip(List<Vector2> window, List<Vector2[]> into)
        {
            var box = Bounds(window);
            float area = 0;
            for (int i = 0; i < triangles.Count; i++)
            {
                if (!triangleRects[i].Overlaps(box)) continue;
                var piece = ClipConvex(triangles[i], window);
                if (piece.Count < 3) continue;
                float a = Mathf.Abs(SignedArea(piece));
                if (a < 1e-3f) continue;
                into.Add(piece.ToArray());
                area += a;
            }
            return area;
        }

        Rect Union(List<Vector2[]> list, Vector2Int cell)
        {
            if (list.Count == 0) return SquareRect(cell);
            var rect = Bounds(list[0]);
            foreach (var piece in list)
            {
                var b = Bounds(piece);
                rect = Rect.MinMaxRect(Mathf.Min(rect.xMin, b.xMin), Mathf.Min(rect.yMin, b.yMin), Mathf.Max(rect.xMax, b.xMax), Mathf.Max(rect.yMax, b.yMax));
            }
            return rect;
        }

        IEnumerable<Vector2Int> Around(Vector2Int c)
        {
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                    if ((dx != 0 || dy != 0) && grid.InBounds(c.x + dx, c.y + dy)) yield return new Vector2Int(c.x + dx, c.y + dy);
        }

        Vector2 Center(Vector2Int c) => new(c.x * grid.CellSize, -c.y * grid.CellSize);
        Rect SquareRect(Vector2Int c) { var m = Center(c); float h = grid.CellSize / 2; return Rect.MinMaxRect(m.x - h, m.y - h, m.x + h, m.y + h); }
        List<Vector2> Square(Vector2Int c)
        {
            var r = SquareRect(c);
            return new List<Vector2> { new(r.xMin, r.yMin), new(r.xMin, r.yMax), new(r.xMax, r.yMax), new(r.xMax, r.yMin) };
        }

        static List<Vector2> Points(FieldGrid grid, JToken token)
        {
            var res = new List<Vector2>();
            if (token is JArray arr)
                foreach (var p in arr)
                    if (p is JArray par && par.Count == 2) res.Add(TerrainShapes.ToWorld(grid, (float)par[0], (float)par[1]));
            return res;
        }

        /// <summary>Quita el cierre repetido y los puntos duplicados seguidos.</summary>
        static List<Vector2> Clean(List<Vector2> ring)
        {
            var res = new List<Vector2>();
            foreach (var p in ring)
                if (res.Count == 0 || (p - res[^1]).sqrMagnitude > 1e-6f) res.Add(p);
            while (res.Count > 1 && (res[0] - res[^1]).sqrMagnitude <= 1e-6f) res.RemoveAt(res.Count - 1);
            return res;
        }

        // ---- Geometría 2D ---------------------------------------------------------------------------------

        public static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        public static float SignedArea(IReadOnlyList<Vector2> poly)
        {
            float sum = 0;
            for (int i = 0; i < poly.Count; i++) sum += Cross(poly[i], poly[(i + 1) % poly.Count]);
            return sum / 2;
        }

        static Rect Bounds(IReadOnlyList<Vector2> poly)
        {
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            foreach (var p in poly) { x0 = Mathf.Min(x0, p.x); y0 = Mathf.Min(y0, p.y); x1 = Mathf.Max(x1, p.x); y1 = Mathf.Max(y1, p.y); }
            return Rect.MinMaxRect(x0, y0, x1, y1);
        }

        static Rect Bounds(Vector2 a, Vector2 b) => Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));

        /// <summary>Se queda con la parte del polígono convexo donde (p − punto)·normal ≤ 0.</summary>
        public static List<Vector2> ClipHalfPlane(IReadOnlyList<Vector2> poly, Vector2 point, Vector2 normal)
        {
            var res = new List<Vector2>();
            for (int i = 0; i < poly.Count; i++)
            {
                var a = poly[i]; var b = poly[(i + 1) % poly.Count];
                float da = Vector2.Dot(a - point, normal), db = Vector2.Dot(b - point, normal);
                if (da <= 0) res.Add(a);
                if ((da < 0 && db > 0) || (da > 0 && db < 0)) res.Add(a + (b - a) * (da / (da - db)));
            }
            return res;
        }

        /// <summary>Intersección de un polígono convexo con una ventana convexa (Sutherland–Hodgman).</summary>
        public static List<Vector2> ClipConvex(IReadOnlyList<Vector2> subject, IReadOnlyList<Vector2> window)
        {
            var res = new List<Vector2>(subject);
            float sign = SignedArea(window) >= 0 ? 1 : -1;
            for (int i = 0; i < window.Count && res.Count >= 3; i++)
            {
                var a = window[i]; var b = window[(i + 1) % window.Count];
                var edge = b - a;
                // Lado interior: sign·Cross(edge, p − a) ≥ 0 ⇔ (p − a)·normal ≤ 0 con normal = sign·(edge.y, −edge.x).
                res = ClipHalfPlane(res, a, new Vector2(edge.y, -edge.x) * sign);
            }
            return res;
        }

        public static bool InConvex(IReadOnlyList<Vector2> poly, Vector2 p)
        {
            float sign = SignedArea(poly) >= 0 ? 1 : -1;
            for (int i = 0; i < poly.Count; i++)
                if (sign * Cross(poly[(i + 1) % poly.Count] - poly[i], p - poly[i]) < -1e-4f) return false;
            return true;
        }

        public static bool InPolygon(IReadOnlyList<Vector2> poly, Vector2 p)
        {
            bool inside = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
                if ((poly[i].y > p.y) != (poly[j].y > p.y) &&
                    p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x)
                    inside = !inside;
            return inside;
        }

        public static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float t = ab.sqrMagnitude > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0;
            return Vector2.Distance(p, a + ab * t);
        }

        /// <summary>Triangulación por orejas de un polígono simple (cóncavo o convexo). Índices de a tres.</summary>
        public static List<int> Triangulate(IReadOnlyList<Vector2> poly)
        {
            var result = new List<int>();
            var idx = new List<int>();
            for (int i = 0; i < poly.Count; i++) idx.Add(i);
            if (idx.Count < 3) return result;
            float sign = SignedArea(poly) >= 0 ? 1 : -1;
            int guard = poly.Count * poly.Count + 10;
            while (idx.Count > 3 && guard-- > 0)
            {
                bool cut = false;
                for (int i = 0; i < idx.Count && !cut; i++)
                {
                    int a = idx[(i + idx.Count - 1) % idx.Count], b = idx[i], c = idx[(i + 1) % idx.Count];
                    if (sign * Cross(poly[b] - poly[a], poly[c] - poly[b]) <= 1e-6f) continue; // reflejo o colineal
                    bool blocked = false;
                    foreach (int j in idx)
                    {
                        if (j == a || j == b || j == c) continue;
                        if (InTriangle(poly[j], poly[a], poly[b], poly[c])) { blocked = true; break; }
                    }
                    if (blocked) continue;
                    result.Add(a); result.Add(b); result.Add(c);
                    idx.RemoveAt(i);
                    cut = true;
                }
                if (cut) continue;
                // Sin oreja: quita un vértice colineal (no cambia el área) o se rinde.
                int flat = -1;
                for (int i = 0; i < idx.Count && flat < 0; i++)
                {
                    int a = idx[(i + idx.Count - 1) % idx.Count], b = idx[i], c = idx[(i + 1) % idx.Count];
                    if (Mathf.Abs(Cross(poly[b] - poly[a], poly[c] - poly[b])) <= 1e-6f) flat = i;
                }
                if (flat < 0) break;
                idx.RemoveAt(flat);
            }
            if (idx.Count == 3 && Mathf.Abs(Cross(poly[idx[1]] - poly[idx[0]], poly[idx[2]] - poly[idx[0]])) > 1e-6f)
                result.AddRange(idx);
            return result;
        }

        static bool InTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = Cross(b - a, p - a), d2 = Cross(c - b, p - b), d3 = Cross(a - c, p - c);
            bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(neg && pos);
        }

        /// <summary>
        /// Agrega un pedazo convexo como abanico de triángulos con la cara hacia arriba (Unity: horario visto desde
        /// arriba). <paramref name="place"/> da el vértice 3D y <paramref name="uv"/> su coordenada de textura.
        /// </summary>
        public static void AddPiece(MeshBuilder builder, IReadOnlyList<Vector2> piece, System.Func<Vector2, Vector3> place,
            System.Func<Vector2, Vector2> uv, List<int> triangles)
        {
            if (piece.Count < 3) return;
            int first = builder.VertexCount;
            foreach (var p in piece) builder.AddVertex(place(p), Vector3.up, uv(p));
            var v = builder.Vertices;
            for (int i = 1; i + 1 < piece.Count; i++)
            {
                int a = first, b = first + i, c = first + i + 1;
                if (Vector3.Cross(v[b] - v[a], v[c] - v[a]).y >= 0) { triangles.Add(a); triangles.Add(b); triangles.Add(c); }
                else { triangles.Add(a); triangles.Add(c); triangles.Add(b); }
            }
        }
    }
}
