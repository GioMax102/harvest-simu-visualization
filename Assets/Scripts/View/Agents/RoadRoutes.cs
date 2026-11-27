using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace HarvestSim.Visualization
{
    /// <summary>
    /// Trayectorias suaves por los caminos. Solo presentación: la simulación mueve a los agentes celda a celda en 4
    /// direcciones, así que un camino diagonal se recorre en escalera (E, N, E, N…). Aquí, cada tramo de ticks
    /// seguidos en que el agente avanza una celda por camino (o patio de bodega y bases) y cambia de dirección se
    /// dibuja como una línea suave sobre la línea central del camino del KML (o, sin ella, la escalera simplificada).
    ///
    /// Sincronía: el tramo empieza y termina en el centro exacto de su primera y última celda, en los mismos ticks
    /// que el trace, y la velocidad es constante dentro del tramo (la distancia total entre los ticks del tramo).
    /// Fuera de un tramo, o al entrar al cultivo, manda <see cref="VehicleMotion"/> (recto hacia la celda).
    /// </summary>
    public sealed class RoadRoutes
    {
        /// <summary>Distancia máxima (en celdas) de un centro de celda a la línea del camino para pegarse a ella.</summary>
        public const float SnapCells = .75f;
        /// <summary>Tolerancia de simplificación (en celdas) sobre la línea del camino y sobre los centros de celda.</summary>
        public const float SnappedTolerance = .25f, CenterTolerance = .6f;
        /// <summary>Separación mínima (en celdas) entre vértices de la trayectoria antes de redondear.</summary>
        public const float MinSpacing = .5f;

        sealed class Run
        {
            public int StartTick, EndTick;
            public Vector2[] Points;
            public float[] Along;
            public float Length;
            public float StartHeading, EndHeading;
        }

        readonly FieldGrid grid;
        readonly Dictionary<string, List<Run>> runs = new();
        readonly List<(Vector2 a, Vector2 b)> centerlines = new();

        public int RunCount { get; private set; }

        RoadRoutes(FieldGrid grid) { this.grid = grid; }

        public static RoadRoutes Build(FieldGrid grid, TraceData trace)
        {
            var routes = new RoadRoutes(grid);
            if (grid == null || trace == null) return routes;
            if (trace.World?["geo"] is JObject geo && geo["formas"] is JObject formas && formas["caminos"] is JArray caminos)
                foreach (var linea in caminos)
                {
                    if (linea is not JArray pts) continue;
                    Vector2? previous = null;
                    foreach (var p in pts)
                    {
                        if (p is not JArray par || par.Count != 2) continue;
                        var point = TerrainShapes.ToWorld(grid, (float)par[0], (float)par[1]);
                        if (previous.HasValue) routes.centerlines.Add((previous.Value, point));
                        previous = point;
                    }
                }
            foreach (var pair in Timeline(trace)) routes.FindRuns(pair.Key, pair.Value);
            return routes;
        }

        /// <summary>Pose de cada agente en cada tick (0..total), igual que <see cref="TracePlayback"/>.</summary>
        static Dictionary<string, (Vector2Int cell, string orientation, bool known)[]> Timeline(TraceData trace)
        {
            int total = trace.TotalTicks;
            var result = new Dictionary<string, (Vector2Int, string, bool)[]>();
            var current = new Dictionary<string, (Vector2Int cell, string orientation, bool known)>();
            for (int tick = 0; tick <= total; tick++)
            {
                if (trace.Changes.TryGetValue(tick, out var changes))
                    foreach (var token in changes)
                    {
                        if (token is not JObject e || (string)e["tipo"] != "AGENTE" || e["set"]?["pose"] is not JObject pose) continue;
                        current[(string)e["id"]] = (new Vector2Int((int)pose["x"], (int)pose["y"]), (string)pose["orientacion"], true);
                    }
                foreach (var pair in current)
                {
                    if (!result.TryGetValue(pair.Key, out var list)) result[pair.Key] = list = new (Vector2Int, string, bool)[total + 1];
                    list[tick] = pair.Value;
                }
            }
            return result;
        }

        bool IsRoad(Vector2Int c) => grid.KindAt(c.x, c.y) is CellKind.Service or CellKind.Warehouse or CellKind.Base;

        bool RoadStep((Vector2Int cell, string orientation, bool known) a, (Vector2Int cell, string orientation, bool known) b) =>
            a.known && b.known && Mathf.Abs(a.cell.x - b.cell.x) + Mathf.Abs(a.cell.y - b.cell.y) == 1 && IsRoad(a.cell) && IsRoad(b.cell);

        void FindRuns(string id, (Vector2Int cell, string orientation, bool known)[] poses)
        {
            var list = new List<Run>();
            int t = 0;
            while (t < poses.Length - 1)
            {
                if (!RoadStep(poses[t], poses[t + 1])) { t++; continue; }
                int start = t;
                while (t < poses.Length - 1 && RoadStep(poses[t], poses[t + 1])) t++;
                int end = t;
                // Un tramo recto (sin cambio de dirección) ya se ve bien con VehicleMotion: se deja igual.
                bool turns = false;
                for (int k = start + 1; k < end && !turns; k++) turns = poses[k + 1].cell - poses[k].cell != poses[k].cell - poses[k - 1].cell;
                if (!turns) continue;
                var cells = new List<Vector2Int>();
                for (int k = start; k <= end; k++) cells.Add(poses[k].cell);
                var run = MakeRun(cells, FieldGrid.Heading(poses[start].orientation), FieldGrid.Heading(poses[end].orientation));
                run.StartTick = start; run.EndTick = end;
                list.Add(run);
            }
            if (list.Count > 0) { runs[id] = list; RunCount += list.Count; }
        }

        Run MakeRun(List<Vector2Int> cells, float startHeading, float endHeading)
        {
            float s = grid.CellSize;
            var centers = new List<Vector2>();
            foreach (var c in cells) centers.Add(new Vector2(c.x * s, -c.y * s));
            var path = new List<Vector2> { centers[0] };
            bool snapped = false;
            for (int i = 1; i < centers.Count - 1; i++)
            {
                var p = centers[i];
                if (TrySnap(p, out var q)) { p = q; snapped = true; }
                // Sin retrocesos: el punto tiene que avanzar en la dirección local de la escalera.
                var forward = centers[i + 1] - centers[i - 1];
                if (Vector2.Dot(p - path[^1], forward) <= 1e-3f) continue;
                path.Add(p);
            }
            path.Add(centers[^1]);
            path = Simplify(path, (snapped ? SnappedTolerance : CenterTolerance) * s);
            // En los cruces, celdas seguidas se pegan a caminos distintos y dejan ganchos de pocos metros: entre
            // vértices debe haber al menos media celda.
            path = Spread(path, MinSpacing * s);
            path = Chaikin(path, 3);
            var along = new float[path.Count];
            for (int i = 1; i < path.Count; i++) along[i] = along[i - 1] + Vector2.Distance(path[i - 1], path[i]);
            return new Run { Points = path.ToArray(), Along = along, Length = along[^1], StartHeading = startHeading, EndHeading = endHeading };
        }

        bool TrySnap(Vector2 p, out Vector2 snapped)
        {
            snapped = p;
            float best = SnapCells * grid.CellSize;
            bool found = false;
            foreach (var (a, b) in centerlines)
            {
                var ab = b - a;
                float t = ab.sqrMagnitude > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0;
                var q = a + ab * t;
                float d = Vector2.Distance(p, q);
                if (d < best) { best = d; snapped = q; found = true; }
            }
            return found;
        }

        /// <summary>Douglas–Peucker: conserva los extremos y los puntos que se apartan más de la tolerancia.</summary>
        public static List<Vector2> Simplify(List<Vector2> pts, float tolerance)
        {
            if (pts.Count < 3) return new List<Vector2>(pts);
            var keep = new bool[pts.Count];
            keep[0] = keep[^1] = true;
            var stack = new Stack<(int a, int b)>();
            stack.Push((0, pts.Count - 1));
            while (stack.Count > 0)
            {
                var (a, b) = stack.Pop();
                float worst = 0; int index = -1;
                for (int i = a + 1; i < b; i++)
                {
                    float d = LotShapes.DistanceToSegment(pts[i], pts[a], pts[b]);
                    if (d > worst) { worst = d; index = i; }
                }
                if (index < 0 || worst <= tolerance) continue;
                keep[index] = true;
                stack.Push((a, index)); stack.Push((index, b));
            }
            var res = new List<Vector2>();
            for (int i = 0; i < pts.Count; i++) if (keep[i]) res.Add(pts[i]);
            return res;
        }

        /// <summary>Quita vértices interiores a menos de <paramref name="spacing"/> del anterior (o del final).</summary>
        public static List<Vector2> Spread(List<Vector2> pts, float spacing)
        {
            if (pts.Count < 3) return new List<Vector2>(pts);
            var res = new List<Vector2> { pts[0] };
            for (int i = 1; i < pts.Count - 1; i++)
                if (Vector2.Distance(pts[i], res[^1]) >= spacing && Vector2.Distance(pts[i], pts[^1]) >= spacing) res.Add(pts[i]);
            res.Add(pts[^1]);
            return res;
        }

        /// <summary>Redondea las esquinas (corte de Chaikin a 1/4 y 3/4) sin mover los extremos.</summary>
        public static List<Vector2> Chaikin(List<Vector2> pts, int iterations)
        {
            for (int it = 0; it < iterations && pts.Count >= 3; it++)
            {
                var res = new List<Vector2> { pts[0] };
                for (int i = 0; i < pts.Count - 1; i++)
                {
                    var a = pts[i]; var b = pts[i + 1];
                    if (i > 0) res.Add(Vector2.Lerp(a, b, .25f));
                    if (i < pts.Count - 2) res.Add(Vector2.Lerp(a, b, .75f));
                }
                res.Add(pts[^1]);
                pts = res;
            }
            return pts;
        }

        /// <summary>
        /// Pose en <paramref name="tick"/> + <paramref name="fraction"/> si cae dentro de un tramo suave del agente.
        /// En los ticks de inicio y fin coincide con <see cref="VehicleMotion.At"/>.
        /// </summary>
        public bool TryPose(string id, int tick, float fraction, out VehiclePose pose)
        {
            pose = default;
            if (id == null || !runs.TryGetValue(id, out var list)) return false;
            float time = tick + Mathf.Clamp01(fraction);
            int lo = 0, hi = list.Count - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                var run = list[mid];
                if (time < run.StartTick) hi = mid - 1;
                else if (time > run.EndTick) lo = mid + 1;
                else { pose = Sample(run, (time - run.StartTick) / (run.EndTick - run.StartTick)); return true; }
            }
            return false;
        }

        /// <summary>Polilínea (X, Z) y ticks del tramo suave que contiene <paramref name="tick"/>, si hay uno.</summary>
        public bool TryRun(string id, int tick, out IReadOnlyList<Vector2> points, out int startTick, out int endTick)
        {
            points = null; startTick = endTick = -1;
            if (id == null || !runs.TryGetValue(id, out var list)) return false;
            foreach (var run in list)
                if (tick >= run.StartTick && tick <= run.EndTick)
                {
                    points = run.Points; startTick = run.StartTick; endTick = run.EndTick;
                    return true;
                }
            return false;
        }

        VehiclePose Sample(Run run, float u)
        {
            float d = Mathf.Clamp01(u) * run.Length;
            var p = PointAt(run, d);
            // Rumbo: la cuerda entre un poco antes y un poco después (sin saltos en los vértices de la polilínea).
            float look = Mathf.Min(1.5f, run.Length / 4);
            var ahead = PointAt(run, Mathf.Min(run.Length, d + look)) - PointAt(run, Mathf.Max(0, d - look));
            float heading = ahead.sqrMagnitude > 1e-8f ? Mathf.Atan2(ahead.x, ahead.y) * Mathf.Rad2Deg : run.EndHeading;
            // Al entrar y salir del tramo se mezcla con la orientación del trace para no dar un salto.
            float blend = Mathf.Min(grid.CellSize / 2, run.Length / 4);
            if (blend > 1e-4f)
            {
                if (d < blend) heading = Mathf.LerpAngle(run.StartHeading, heading, Smooth(d / blend));
                else if (d > run.Length - blend) heading = Mathf.LerpAngle(run.EndHeading, heading, Smooth((run.Length - d) / blend));
            }
            return new VehiclePose { Position = new Vector3(p.x, grid.GroundHeight(p.x, p.y), p.y), Heading = heading };
        }

        static float Smooth(float t) => t * t * (3 - 2 * t);

        static Vector2 PointAt(Run run, float d)
        {
            var along = run.Along;
            if (d <= 0) return run.Points[0];
            if (d >= run.Length) return run.Points[^1];
            int lo = 0, hi = along.Length - 1;
            while (hi - lo > 1) { int mid = (lo + hi) / 2; if (along[mid] <= d) lo = mid; else hi = mid; }
            float span = along[hi] - along[lo];
            return Vector2.Lerp(run.Points[lo], run.Points[hi], span > 1e-6f ? (d - along[lo]) / span : 0);
        }
    }
}
