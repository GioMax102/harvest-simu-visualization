using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace HarvestSim.Visualization
{
    /// <summary>
    /// Suelo del mundo: una planicie de pasto alrededor y una malla de parcelas con submallas por material
    /// (tierra bajo el maíz, rastrojo cosechado, camino de servicio y concreto). Las UV están en metros;
    /// el tamaño de cada textura se ajusta con la escala del material.
    /// </summary>
    public sealed class GroundBuilder
    {
        public const int SoilSubmesh = 0, StubbleSubmesh = 1, RoadSubmesh = 2, ConcreteSubmesh = 3;
        public const float TileHeight = .03f;

        public MeshRenderer Terrain { get; private set; }
        public MeshRenderer Parcels { get; private set; }
        public int StubbleCellCount { get; private set; }
        readonly Dictionary<Vector2Int, int> quadStart = new();
        readonly List<int>[] indices = { new(), new(), new(), new() };
        FieldGrid grid;
        Mesh parcelsMesh, terrainMesh;

        public void Build(FieldGrid layout, HarvestArt art, Transform parent)
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
            terrain.AddQuad(new Vector3(min.x, 0, min.z), new Vector3(min.x, 0, max.z), new Vector3(max.x, 0, max.z), new Vector3(max.x, 0, min.z),
                Vector3.up, new Vector2(min.x, min.z), new Vector2(max.x, max.z));
            terrainMesh = terrain.ToMesh("Terreno");
            Terrain = Renderer("Terreno", terrainMesh, parent, art.grassMaterial);

            var parcels = new MeshBuilder();
            for (int y = 0; y < grid.Height; y++)
                for (int x = 0; x < grid.Width; x++)
                {
                    var kind = grid.KindAt(x, y);
                    if (kind is CellKind.Obstacle or CellKind.OutsideField) continue;
                    var c = grid.CellToWorld(x, y, TileHeight);
                    float h = size / 2;
                    quadStart[new Vector2Int(x, y)] = parcels.VertexCount;
                    parcels.AddQuad(c + new Vector3(-h, 0, -h), c + new Vector3(-h, 0, h), c + new Vector3(h, 0, h), c + new Vector3(h, 0, -h),
                        Vector3.up, new Vector2(c.x - h, c.z - h), new Vector2(c.x + h, c.z + h));
                }
            parcelsMesh = parcels.ToMesh("Parcelas");
            parcelsMesh.MarkDynamic();
            parcelsMesh.subMeshCount = 4;
            Parcels = Renderer("Parcelas", parcelsMesh, parent, art.soilMaterial, art.stubbleMaterial, art.roadMaterial, art.concreteMaterial);
            Render(new HashSet<(int x, int y)>());
        }

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
            foreach (var pair in quadStart)
            {
                var cell = pair.Key;
                int submesh = grid.KindAt(cell.x, cell.y) switch
                {
                    CellKind.Service => RoadSubmesh,
                    CellKind.Warehouse or CellKind.Base => ConcreteSubmesh,
                    _ => harvested.Contains((cell.x, cell.y)) ? StubbleSubmesh : SoilSubmesh
                };
                if (submesh == StubbleSubmesh) stubble++;
                int v = pair.Value;
                indices[submesh].AddRange(new[] { v, v + 1, v + 2, v, v + 2, v + 3 });
            }
            for (int i = 0; i < indices.Length; i++) parcelsMesh.SetTriangles(indices[i], i, false);
            parcelsMesh.RecalculateBounds();
            StubbleCellCount = stubble;
        }

        public void Destroy()
        {
            DestroyObject(parcelsMesh); DestroyObject(terrainMesh);
            parcelsMesh = terrainMesh = null; quadStart.Clear();
        }

        static void DestroyObject(Object target)
        {
            if (!target) return;
            if (Application.isPlaying) Object.Destroy(target); else Object.DestroyImmediate(target);
        }
    }
}
