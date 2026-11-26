using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace HarvestSim.Visualization
{
    public enum CellKind { Field, Service, Warehouse, Base, Obstacle, OutsideField }

    /// <summary>Grupo de obstáculos conectados en 4 direcciones; su forma decide cómo se dibuja.</summary>
    public sealed class ObstacleGroup
    {
        public readonly List<Vector2Int> Cells = new();
        public int MinX = int.MaxValue, MaxX = int.MinValue, MinY = int.MaxValue, MaxY = int.MinValue;
        public int SpanX => MaxX - MinX + 1;
        public int SpanY => MaxY - MinY + 1;
        /// <summary>Una fila o columna de grosor 1 y al menos 3 celdas: se dibuja como seto o línea de árboles.</summary>
        public bool IsLinear => Cells.Count >= 3 && Mathf.Min(SpanX, SpanY) == 1;
    }

    /// <summary>
    /// Traduce la cuadrícula lógica del trace al mundo de Unity. Solo presentación: no decide nada de la simulación.
    /// La celda (x, y) se centra en (x·tamaño, 0, −y·tamaño); el Norte (y−1) es +Z y el Este es +X.
    /// </summary>
    public sealed class FieldGrid
    {
        /// <summary>Una pasada de un cabezal maicero de 16 hileras (unos 12 m).</summary>
        public const float DefaultCellSize = 12f;

        public int Width { get; }
        public int Height { get; }
        public float CellSize { get; }
        public Vector2Int Warehouse { get; }
        public IReadOnlyDictionary<string, Vector2Int> Bases => bases;
        public IReadOnlyList<ObstacleGroup> ObstacleGroups => obstacleGroups;
        public int FieldCellCount { get; }
        /// <summary>True si el trace trae el mapa real (2.3): caminos, fuera de campo y lotes.</summary>
        public bool HasMap { get; }
        /// <summary>Altura de referencia (m sobre el nivel del mar): la mínima del campo. El suelo de Unity
        /// queda en (altura − BaseHeight), así que el campo empieza cerca de y = 0.</summary>
        public float BaseHeight { get; private set; }
        public bool HasRelief => heights != null;

        readonly CellKind[,] kinds;
        float[,] heights; // relativas a BaseHeight, por celda (centro)
        readonly Dictionary<string, Vector2Int> bases = new();
        readonly List<ObstacleGroup> obstacleGroups = new();

        public FieldGrid(int width, int height, float cellSize, Vector2Int warehouse,
            IDictionary<string, Vector2Int> agentBases, IEnumerable<Vector2Int> obstacles, IEnumerable<Vector2Int> outsideField = null,
            IEnumerable<Vector2Int> roads = null, float[,] absoluteHeights = null)
        {
            if (width < 1 || height < 2) throw new ArgumentOutOfRangeException(nameof(width), "La cuadrícula necesita al menos 1×2 celdas.");
            if (!(cellSize > 0) || float.IsInfinity(cellSize)) throw new ArgumentOutOfRangeException(nameof(cellSize));
            Width = width; Height = height; CellSize = cellSize; Warehouse = warehouse;
            HasMap = roads != null;
            kinds = new CellKind[width, height];
            for (int x = 0; x < width; x++)
            {
                // Sin mapa, la fila 0 es la de servicio; con mapa, el servicio son las celdas de camino.
                kinds[x, 0] = HasMap ? CellKind.Field : CellKind.Service;
                for (int y = 1; y < height; y++) kinds[x, y] = CellKind.Field;
            }
            if (roads != null)
                foreach (var cell in roads) if (InBounds(cell.x, cell.y)) kinds[cell.x, cell.y] = CellKind.Service;
            if (outsideField != null)
                foreach (var cell in outsideField) if (InBounds(cell.x, cell.y) && (HasMap || cell.y > 0)) kinds[cell.x, cell.y] = CellKind.OutsideField;
            foreach (var cell in obstacles) if (InBounds(cell.x, cell.y)) kinds[cell.x, cell.y] = CellKind.Obstacle;
            foreach (var pair in agentBases)
            {
                bases[pair.Key] = pair.Value;
                if (InBounds(pair.Value.x, pair.Value.y)) kinds[pair.Value.x, pair.Value.y] = CellKind.Base;
            }
            if (InBounds(warehouse.x, warehouse.y)) kinds[warehouse.x, warehouse.y] = CellKind.Warehouse;
            int field = 0;
            foreach (var kind in kinds) if (kind == CellKind.Field) field++;
            FieldCellCount = field;
            GroupObstacles();
            if (absoluteHeights != null) SetHeights(absoluteHeights);
        }

        /// <summary>Relieve por celda (metros, alto × ancho como en <c>mundo.altura_m</c>). La referencia es la
        /// altura mínima del campo (o de la cuadrícula si no hay campo).</summary>
        void SetHeights(float[,] absolute)
        {
            if (absolute.GetLength(0) != Height || absolute.GetLength(1) != Width)
                throw new ArgumentException("altura_m no tiene el tamaño de la cuadrícula");
            float min = float.MaxValue, minAll = float.MaxValue;
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                {
                    minAll = Mathf.Min(minAll, absolute[y, x]);
                    if (kinds[x, y] == CellKind.Field) min = Mathf.Min(min, absolute[y, x]);
                }
            BaseHeight = min < float.MaxValue ? min : minAll;
            heights = new float[Width, Height];
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++) heights[x, y] = absolute[y, x] - BaseHeight;
        }

        /// <summary>Altura del suelo (y de Unity) en un punto del mundo: interpolación bilineal entre los centros
        /// de las celdas; fuera de la cuadrícula se usa la orilla más cercana. 0 si no hay relieve.</summary>
        public float GroundHeight(float worldX, float worldZ)
        {
            if (heights == null) return 0f;
            float gx = Mathf.Clamp(worldX / CellSize, 0, Width - 1), gy = Mathf.Clamp(-worldZ / CellSize, 0, Height - 1);
            int x0 = Mathf.Min((int)gx, Width - 2 < 0 ? 0 : Width - 2), y0 = Mathf.Min((int)gy, Height - 2);
            int x1 = Mathf.Min(x0 + 1, Width - 1), y1 = Mathf.Min(y0 + 1, Height - 1);
            float tx = gx - x0, ty = gy - y0;
            float a = Mathf.Lerp(heights[x0, y0], heights[x1, y0], tx), b = Mathf.Lerp(heights[x0, y1], heights[x1, y1], tx);
            return Mathf.Lerp(a, b, ty);
        }

        public float GroundHeight(Vector3 world) => GroundHeight(world.x, world.z);
        public float HeightAt(int x, int y) => heights == null || !InBounds(x, y) ? 0f : heights[x, y];

        /// <summary>Crea el layout a partir de <c>mundo</c>. Usa <c>mundo.tamano_celda_m</c> si existe (trace 2.3).</summary>
        public static FieldGrid FromTrace(TraceData trace, float? cellSizeOverride = null)
        {
            var world = trace.World;
            float size = cellSizeOverride ?? (world["tamano_celda_m"] is JValue value && value.Type is JTokenType.Float or JTokenType.Integer
                ? (float)value : DefaultCellSize);
            var agentBases = new Dictionary<string, Vector2Int>();
            if (world["bases"] is JObject homes)
                foreach (var home in homes.Properties()) agentBases[home.Name] = Cell(home.Value);
            bool map = world["caminos"] != null || world["lotes"] != null;
            float[,] alturas = null;
            if (world["altura_m"] is JArray rows && rows.Count == trace.Height)
            {
                alturas = new float[trace.Height, trace.Width];
                for (int y = 0; y < trace.Height; y++)
                    for (int x = 0; x < trace.Width; x++) alturas[y, x] = (float)rows[y][x];
            }
            return new FieldGrid(trace.Width, trace.Height, size, Cell(world["bodega"]), agentBases,
                Cells(world["obstaculos"]), Cells(world["fuera_de_campo"]),
                map ? Cells(world["caminos"]) : null, alturas);
        }

        static Vector2Int Cell(JToken token) => token is JArray pair && pair.Count == 2 ? new Vector2Int((int)pair[0], (int)pair[1]) : Vector2Int.zero;
        static IEnumerable<Vector2Int> Cells(JToken token)
        {
            if (token is not JArray list) yield break;
            foreach (var item in list) yield return Cell(item);
        }

        public bool InBounds(int x, int y) => x >= 0 && x < Width && y >= 0 && y < Height;
        public CellKind KindAt(int x, int y) => InBounds(x, y) ? kinds[x, y] : CellKind.OutsideField;
        public bool IsField(int x, int y) => KindAt(x, y) == CellKind.Field;

        // La altura del relieve se suma sola: todo lo que se coloca con CellToWorld queda apoyado en el terreno.
        public Vector3 CellToWorld(int x, int y, float height = 0) =>
            new(x * CellSize, height + (heights != null && InBounds(x, y) ? heights[x, y] : GroundHeight(x * CellSize, -y * CellSize)), -y * CellSize);
        public Vector3 CellToWorld(Vector2 cell, float height = 0) =>
            new(cell.x * CellSize, height + GroundHeight(cell.x * CellSize, -cell.y * CellSize), -cell.y * CellSize);
        public Vector2Int WorldToCell(Vector3 position) =>
            new(Mathf.RoundToInt(position.x / CellSize), Mathf.RoundToInt(-position.z / CellSize));

        /// <summary>Rectángulo de la cuadrícula en el mundo, desde el borde exterior de las celdas.</summary>
        public Bounds WorldBounds
        {
            get
            {
                var min = new Vector3(-CellSize / 2, 0, -(Height - .5f) * CellSize);
                var max = new Vector3((Width - .5f) * CellSize, 0, CellSize / 2);
                var bounds = new Bounds(); bounds.SetMinMax(min, max); return bounds;
            }
        }

        /// <summary>Ángulo en Y (grados) de cada orientación del trace: N 0, E 90, S 180, O 270.</summary>
        public static float Heading(string orientation) => orientation switch { "E" => 90, "S" => 180, "O" => 270, _ => 0 };

        /// <summary>Paso en la cuadrícula para cada orientación (y crece hacia el Sur).</summary>
        public static Vector2Int Step(string orientation) => orientation switch
        {
            "E" => new Vector2Int(1, 0), "S" => new Vector2Int(0, 1), "O" => new Vector2Int(-1, 0), _ => new Vector2Int(0, -1)
        };

        /// <summary>Vector unitario en el mundo para un ángulo en Y.</summary>
        public static Vector3 Forward(float heading) => Quaternion.Euler(0, heading, 0) * Vector3.forward;

        void GroupObstacles()
        {
            var seen = new bool[Width, Height];
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                {
                    if (seen[x, y] || kinds[x, y] != CellKind.Obstacle) continue;
                    var group = new ObstacleGroup();
                    var pending = new Stack<Vector2Int>();
                    pending.Push(new Vector2Int(x, y)); seen[x, y] = true;
                    while (pending.Count > 0)
                    {
                        var cell = pending.Pop();
                        group.Cells.Add(cell);
                        group.MinX = Mathf.Min(group.MinX, cell.x); group.MaxX = Mathf.Max(group.MaxX, cell.x);
                        group.MinY = Mathf.Min(group.MinY, cell.y); group.MaxY = Mathf.Max(group.MaxY, cell.y);
                        foreach (var step in new[] { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down })
                        {
                            var next = cell + step;
                            if (!InBounds(next.x, next.y) || seen[next.x, next.y] || kinds[next.x, next.y] != CellKind.Obstacle) continue;
                            seen[next.x, next.y] = true; pending.Push(next);
                        }
                    }
                    group.Cells.Sort((a, b) => a.y != b.y ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x));
                    obstacleGroups.Add(group);
                }
        }
    }
}
