using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace HarvestSim.Visualization
{
    /// <summary>
    /// Formas reales del campo (trace 2.3, <c>mundo.geo.formas</c>): caminos como franjas de terracería y el
    /// contorno de cada lote, apoyados en el relieve. Solo dibuja: la lógica (qué celda es camino o campo) la
    /// decide la simulación en Python y viene en las celdas del trace.
    ///
    /// La franja con inglete en las curvas está adaptada de <c>CultivoDesdeKML.Cinta</c> (Oswaldo,
    /// docs/cultivo/scripts); aquí cada vértice toma la altura del terreno.
    ///
    /// Las formas vienen en metros del marco de la cuadrícula (x a lo largo de los surcos, y a la derecha, origen
    /// en la esquina de la celda (0, 0)); en Unity la celda (x, y) está centrada en (x·s, 0, −y·s), así que un
    /// punto (u, v) va en (u − s/2, h, s/2 − v).
    /// </summary>
    public sealed class TerrainShapes
    {
        public const float RoadWidth = 4f, RoadLift = .08f, OutlineLift = .25f, Step = 2f;

        public int RoadCount { get; private set; }
        /// <summary>2.4: tramos de la orilla de la cabecera (uno en cada punta de cada pasada).</summary>
        public int HeadlandEdgeCount { get; private set; }
        public const float HeadlandEdgeWidth = .5f;
        public int LotCount { get; private set; }
        readonly List<Object> created = new();

        public void Build(FieldGrid grid, TraceData trace, HarvestArt art, Transform parent)
        {
            BuildHeadlandEdges(grid, trace, art, parent);
            var formas = trace.World["geo"]?["formas"] as JObject;
            if (formas == null) return;
            if (formas["caminos"] is JArray caminos)
            {
                var builder = new MeshBuilder();
                foreach (var linea in caminos)
                {
                    var puntos = Densify(ToWorld(grid, linea), Step);
                    if (puntos.Count >= 2) { Ribbon(builder, grid, puntos, RoadWidth); RoadCount++; }
                }
                if (RoadCount > 0)
                {
                    var mesh = builder.ToMesh("Caminos");
                    created.Add(mesh);
                    var go = new GameObject("Caminos (KML)");
                    go.transform.SetParent(parent, false);
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var renderer = go.AddComponent<MeshRenderer>();
                    renderer.sharedMaterial = art.roadMaterial;
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                }
            }
            if (formas["lotes"] is JObject lotes)
                foreach (var lote in lotes.Properties())
                {
                    var anillo = ToWorld(grid, lote.Value);
                    if (anillo.Count < 3) continue;
                    anillo.Add(anillo[0]);
                    var puntos = Densify(anillo, Step);
                    var go = new GameObject($"Contorno lote {lote.Name}");
                    go.transform.SetParent(parent, false);
                    var line = go.AddComponent<LineRenderer>();
                    line.useWorldSpace = false;
                    line.widthMultiplier = .45f;
                    line.sharedMaterial = art.lineMaterial;
                    line.startColor = line.endColor = new Color(.32f, .24f, .12f, .9f);
                    line.positionCount = puntos.Count;
                    for (int i = 0; i < puntos.Count; i++)
                        line.SetPosition(i, new Vector3(puntos[i].x, grid.GroundHeight(puntos[i].x, puntos[i].y) + OutlineLift, puntos[i].y));
                    LotCount++;
                }
        }

        /// <summary>
        /// 2.4 (hileras): la orilla de la cabecera, donde terminan las pasadas y se dan las vueltas. Es una franja de
        /// tierra apisonada en cada punta de cada pasada [x0, y, x1], perpendicular a las hileras y sobre el relieve.
        /// </summary>
        void BuildHeadlandEdges(FieldGrid grid, TraceData trace, HarvestArt art, Transform parent)
        {
            if (trace.World["pasadas"] is not JArray passes || passes.Count == 0) return;
            float s = grid.CellSize;
            var builder = new MeshBuilder();
            foreach (var pass in passes)
            {
                int x0 = (int)pass[0], y = (int)pass[1], x1 = (int)pass[2];
                foreach (float edge in new[] { (x0 - .5f) * s, (x1 + .5f) * s })
                {
                    var line = Densify(new List<Vector2> { new(edge, -(y - .5f) * s), new(edge, -(y + .5f) * s) }, Step);
                    Ribbon(builder, grid, line, HeadlandEdgeWidth);
                    HeadlandEdgeCount++;
                }
            }
            var mesh = builder.ToMesh("Cabecera");
            created.Add(mesh);
            var go = new GameObject("Orilla de la cabecera");
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = art.roadMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
        }

        /// <summary>(u, v) del marco de la cuadrícula → (X, Z) de Unity.</summary>
        public static Vector2 ToWorld(FieldGrid grid, float u, float v) => new(u - grid.CellSize / 2, grid.CellSize / 2 - v);

        static List<Vector2> ToWorld(FieldGrid grid, JToken lista)
        {
            var res = new List<Vector2>();
            if (lista is JArray arr)
                foreach (var p in arr)
                    if (p is JArray par && par.Count == 2) res.Add(ToWorld(grid, (float)par[0], (float)par[1]));
            return res;
        }

        /// <summary>Agrega puntos cada <paramref name="step"/> metros para que la forma siga el relieve.</summary>
        static List<Vector2> Densify(List<Vector2> pts, float step)
        {
            var res = new List<Vector2>();
            for (int i = 0; i < pts.Count - 1; i++)
            {
                int n = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(pts[i], pts[i + 1]) / step));
                for (int k = 0; k < n; k++) res.Add(Vector2.Lerp(pts[i], pts[i + 1], k / (float)n));
            }
            if (pts.Count > 0) res.Add(pts[^1]);
            return res;
        }

        static void Ribbon(MeshBuilder builder, FieldGrid grid, List<Vector2> pts, float width)
        {
            float half = width / 2, travelled = 0;
            int previousLeft = -1, previousRight = -1;
            for (int i = 0; i < pts.Count; i++)
            {
                Vector2 dirPrev = i > 0 ? (pts[i] - pts[i - 1]).normalized : Vector2.zero;
                Vector2 dirNext = i < pts.Count - 1 ? (pts[i + 1] - pts[i]).normalized : Vector2.zero;
                Vector2 dir = (dirPrev + dirNext).normalized;
                if (dir == Vector2.zero) dir = dirNext != Vector2.zero ? dirNext : dirPrev;
                var normal = new Vector2(-dir.y, dir.x);
                var reference = dirNext != Vector2.zero ? dirNext : dirPrev;
                // Inglete: ensanchar en las curvas para mantener el ancho, con límite.
                float scale = Mathf.Min(1f / Mathf.Max(Vector2.Dot(normal, new Vector2(-reference.y, reference.x)), .25f), 3f);
                if (i > 0) travelled += Vector2.Distance(pts[i], pts[i - 1]);
                var left = pts[i] + normal * half * scale;
                var right = pts[i] - normal * half * scale;
                int l = builder.AddVertex(new Vector3(left.x, grid.GroundHeight(left.x, left.y) + RoadLift, left.y), Vector3.up,
                    new Vector2(0, travelled / width));
                int r = builder.AddVertex(new Vector3(right.x, grid.GroundHeight(right.x, right.y) + RoadLift, right.y), Vector3.up,
                    new Vector2(1, travelled / width));
                if (previousLeft >= 0) { UpFacing(builder, previousLeft, previousRight, l); UpFacing(builder, previousRight, r, l); }
                previousLeft = l; previousRight = r;
            }
        }

        /// <summary>Triángulo con la cara hacia arriba (Unity dibuja la cara en sentido horario visto desde arriba).</summary>
        static void UpFacing(MeshBuilder builder, int a, int b, int c)
        {
            var v = builder.Vertices;
            if (Vector3.Cross(v[b] - v[a], v[c] - v[a]).y >= 0) builder.AddTriangle(a, b, c);
            else builder.AddTriangle(a, c, b);
        }

        public void Destroy()
        {
            foreach (var o in created)
                if (o) { if (Application.isPlaying) Object.Destroy(o); else Object.DestroyImmediate(o); }
            created.Clear();
        }
    }
}
