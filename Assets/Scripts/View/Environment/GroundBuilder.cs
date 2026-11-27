using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace HarvestSim.Visualization
{
    /// <summary>
    /// Suelo del mundo: una planicie de pasto alrededor y una malla de parcelas con submallas por material
    /// (tierra bajo el maíz, rastrojo cosechado, camino de servicio y concreto). Las UV están en metros;
    /// el tamaño de cada textura se ajusta con la escala del material.
    /// Con la forma real de los lotes (<see cref="LotShapes"/>), las celdas de la orilla se dibujan recortadas al
    /// contorno y con los pedazos de lote que les prestan sus vecinas, en lugar del cuadro completo.
    /// </summary>
    public sealed class GroundBuilder
    {
        public const int SoilSubmesh = 0, StubbleSubmesh = 1, RoadSubmesh = 2, ConcreteSubmesh = 3;
        public const float TileHeight = .03f;

        public MeshRenderer Terrain { get; private set; }
        public MeshRenderer Parcels { get; private set; }
        public int StubbleCellCount { get; private set; }
        readonly Dictionary<Vector2Int, int[]> cellTriangles = new();
        readonly List<int>[] indices = { new(), new(), new(), new() };
        FieldGrid grid;
        Mesh parcelsMesh, terrainMesh;

        public void Build(FieldGrid layout, HarvestArt art, Transform parent, LotShapes lots = null)
        {
            grid = layout;
            float size = grid.CellSize;
            var bounds = grid.WorldBounds;
            float margin = Mathf.Max(art.marginCells * size, 80f);

            // Planicie exterior: se extiende mucho más allá del margen para que la niebla cierre el horizonte.
            float far = margin * 8 + Mathf.Max(bounds.size.x, bounds.size.z);
            var terrain = new MeshBuilder();
            var min = bounds.min - new Vector3(far, 0, far);
            var max = bounds.max + new Vector3(far, 0, far);
            // Con relieve (trace 2.3 con altura_m), la planicie lejana queda un poco abajo de lo más bajo y, sobre la
            // cuadrícula y su margen, una malla sigue el terreno real (DEM interpolado entre centros de celda).
            float lejos = grid.HasRelief ? LowestHeight(grid) - .5f : 0f;
            terrain.AddQuad(new Vector3(min.x, lejos, min.z), new Vector3(min.x, lejos, max.z), new Vector3(max.x, lejos, max.z), new Vector3(max.x, lejos, min.z),
                Vector3.up, new Vector2(min.x, min.z), new Vector2(max.x, max.z));
            if (grid.HasRelief) AddDrapedGround(terrain, bounds, margin, size);
            terrainMesh = terrain.ToMesh("Terreno");
            Terrain = Renderer("Terreno", terrainMesh, parent, art.grassMaterial);

            var parcels = new MeshBuilder();
            for (int y = 0; y < grid.Height; y++)
                for (int x = 0; x < grid.Width; x++)
                {
                    var kind = grid.KindAt(x, y);
                    if (kind is CellKind.Obstacle or CellKind.OutsideField) continue;
                    // Con mapa, los caminos se dibujan con su forma real (TerrainShapes), no como cuadros de 12 m.
                    if (grid.HasMap && kind == CellKind.Service) continue;
                    var coord = new Vector2Int(x, y);
                    var pieces = kind == CellKind.Field ? lots?.PiecesOf(coord) : null;
                    if (pieces != null)
                    {
                        var tris = new List<int>();
                        foreach (var piece in pieces) LotShapes.AddPiece(parcels, piece, p => Draped(p.x, p.y), p => p, tris);
                        cellTriangles[coord] = tris.ToArray();
                        continue;
                    }
                    var c = grid.CellToWorld(x, y, TileHeight);
                    float h = size / 2;
                    int v = parcels.VertexCount;
                    cellTriangles[coord] = new[] { v, v + 1, v + 2, v, v + 2, v + 3 };
                    parcels.AddQuad(Draped(c.x - h, c.z - h), Draped(c.x - h, c.z + h), Draped(c.x + h, c.z + h), Draped(c.x + h, c.z - h),
                        Vector3.up, new Vector2(c.x - h, c.z - h), new Vector2(c.x + h, c.z + h));
                }
            parcelsMesh = parcels.ToMesh("Parcelas");
            parcelsMesh.MarkDynamic();
            parcelsMesh.subMeshCount = 4;
            Parcels = Renderer("Parcelas", parcelsMesh, parent, art.soilMaterial, art.stubbleMaterial, art.roadMaterial, art.concreteMaterial);
            Render(new HashSet<(int x, int y)>());
        }

        /// <summary>Esquina de una parcela apoyada en el relieve (y = altura interpolada + grosor del mosaico).</summary>
        Vector3 Draped(float x, float z) => new(x, grid.GroundHeight(x, z) + TileHeight, z);

        static float LowestHeight(FieldGrid grid)
        {
            float low = float.MaxValue;
            for (int y = 0; y < grid.Height; y++)
                for (int x = 0; x < grid.Width; x++) low = Mathf.Min(low, grid.HeightAt(x, y));
            return low;
        }

        /// <summary>Malla del suelo que sigue el relieve sobre la cuadrícula y su margen, con un paso de una celda.
        /// La normal de cada cuadro sale de su pendiente para que la luz marque las lomas.</summary>
        void AddDrapedGround(MeshBuilder builder, Bounds bounds, float margin, float step)
        {
            float x0 = bounds.min.x - margin, x1 = bounds.max.x + margin, z0 = bounds.min.z - margin, z1 = bounds.max.z + margin;
            int nx = Mathf.CeilToInt((x1 - x0) / step), nz = Mathf.CeilToInt((z1 - z0) / step);
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < nz; j++)
                {
                    float ax = x0 + i * step, bx = ax + step, az = z0 + j * step, bz = az + step;
                    Vector3 a = Ground(ax, az), b = Ground(ax, bz), c = Ground(bx, bz), d = Ground(bx, az);
                    var normal = Vector3.Cross(b - a, d - a).normalized;
                    if (normal.y < 0) normal = -normal;
                    builder.AddQuad(a, b, c, d, normal, new Vector2(ax, az), new Vector2(bx, bz));
                }
        }

        Vector3 Ground(float x, float z) => new(x, grid.GroundHeight(x, z) - .02f, z);

        static MeshRenderer Renderer(string name, Mesh mesh, Transform parent, params Material[] materials)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = materials;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = true;
            return renderer;
        }

        /// <summary>Reasigna tierra o rastrojo según las celdas cosechadas en el tick actual.</summary>
        public void Render(HashSet<(int x, int y)> harvested)
        {
            if (!parcelsMesh) return;
            foreach (var list in indices) list.Clear();
            int stubble = 0;
            foreach (var pair in cellTriangles)
            {
                var cell = pair.Key;
                int submesh = grid.KindAt(cell.x, cell.y) switch
                {
                    CellKind.Service => RoadSubmesh,
                    CellKind.Warehouse or CellKind.Base => ConcreteSubmesh,
                    _ => harvested.Contains((cell.x, cell.y)) ? StubbleSubmesh : SoilSubmesh
                };
                if (submesh == StubbleSubmesh) stubble++;
                indices[submesh].AddRange(pair.Value);
            }
            for (int i = 0; i < indices.Length; i++) parcelsMesh.SetTriangles(indices[i], i, false);
            parcelsMesh.RecalculateBounds();
            StubbleCellCount = stubble;
        }

        public void Destroy()
        {
            DestroyObject(parcelsMesh); DestroyObject(terrainMesh);
            parcelsMesh = terrainMesh = null; cellTriangles.Clear();
        }

        static void DestroyObject(Object target)
        {
            if (!target) return;
            if (Application.isPlaying) Object.Destroy(target); else Object.DestroyImmediate(target);
        }
    }
}
