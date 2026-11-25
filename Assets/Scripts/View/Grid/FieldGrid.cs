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

        readonly CellKind[,] kinds;
        readonly Dictionary<string, Vector2Int> bases = new();
        readonly List<ObstacleGroup> obstacleGroups = new();

        public FieldGrid(int width, int height, float cellSize, Vector2Int warehouse,
            IDictionary<string, Vector2Int> agentBases, IEnumerable<Vector2Int> obstacles, IEnumerable<Vector2Int> outsideField = null)
        {
            if (width < 1 || height < 2) throw new ArgumentOutOfRangeException(nameof(width), "La cuadrícula necesita al menos 1×2 celdas.");
            if (!(cellSize > 0) || float.IsInfinity(cellSize)) throw new ArgumentOutOfRangeException(nameof(cellSize));
            Width = width; Height = height; CellSize = cellSize; Warehouse = warehouse;
            kinds = new CellKind[width, height];
            for (int x = 0; x < width; x++)
            {
                kinds[x, 0] = CellKind.Service;
                for (int y = 1; y < height; y++) kinds[x, y] = CellKind.Field;
            }
            if (outsideField != null)
                foreach (var cell in outsideField) if (InBounds(cell.x, cell.y) && cell.y > 0) kinds[cell.x, cell.y] = CellKind.OutsideField;
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
        }

        /// <summary>Crea el layout a partir de <c>mundo</c>. Usa <c>mundo.tamano_celda_m</c> si existe (trace 2.3).</summary>
        public static FieldGrid FromTrace(TraceData trace, float? cellSizeOverride = null)
        {
            var world = trace.World;
            float size = cellSizeOverride ?? (world["tamano_celda_m"] is JValue value && value.Type is JTokenType.Float or JTokenType.Integer
                ? (float)value : DefaultCellSize);
            var agentBases = new Dictionary<string, Vector2Int>();
            if (world["bases"] is JObject homes)
                foreach (var home in homes.Properties()) agentBases[home.Name] = Cell(home.Value);
            return new FieldGrid(trace.Width, trace.Height, size, Cell(world["bodega"]), agentBases,
                Cells(world["obstaculos"]), Cells(world["fuera_de_campo"]));
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

        public Vector3 CellToWorld(int x, int y, float height = 0) => new(x * CellSize, height, -y * CellSize);
        public Vector3 CellToWorld(Vector2 cell, float height = 0) => new(cell.x * CellSize, height, -cell.y * CellSize);
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
