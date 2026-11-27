using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace HarvestSim.Visualization
{
    /// <summary>
    /// Dibuja el maíz con instancias (sin un GameObject por planta). El estado lógico de cosecha solo cambia en
    /// <see cref="Render"/> (al cambiar de tick); <see cref="SetCut"/> oculta plantas detrás del cabezal mientras se
    /// interpola hacia una celda que el trace marca como cosechada en el tick siguiente. Funciona también fuera de Play.
    ///
    /// Cada cámara elige por celda: plantas detalladas (cerca), plantas simples, o solo el dosel cuando una planta
    /// mediría pocos píxeles. Con campos grandes (el G del mapa real tiene ~1 800 celdas y ~140 000 tramos) dibujar
    /// todo en cada cascada de sombra colgaba la GPU; el tope de instancias y de sombras lo evita.
    ///
    /// Con la forma real de los lotes (<see cref="LotShapes"/>), las celdas de la orilla siembran solo dentro de su
    /// pedazo de lote (incluidos los que les prestan sus vecinas) y su dosel es una malla recortada; ninguna planta
    /// cae sobre la cinta de un camino.
    /// </summary>
    [ExecuteAlways]
    public sealed class CornField : MonoBehaviour
    {
        public const float RowSpacing = .76f; // maíz a 76 cm (30 in): 15 hileras por celda de 12 m
        /// <summary>Altura del dosel lejano: debajo de las hojas altas para que asomen las plantas.</summary>
        public const float CanopyHeight = 1.35f;
        /// <summary>Altura típica de una planta (m), para estimar cuánto mide en pantalla.</summary>
        public const float PlantHeight = 2.3f;
        [Tooltip("Distancia (m) hasta la que se dibujan las plantas detalladas.")]
        public float detailDistance = 70f;
        [Tooltip("Distancia (m) hasta la que se dibuja rastrojo en las celdas cosechadas.")]
        public float stubbleDistance = 90f;
        [Tooltip("Altura mínima de una planta en pantalla (px). Si mide menos, la celda se dibuja solo con el dosel.")]
        public float minPlantPixels = 4f;
        [Tooltip("Máximo de tramos de hilera por cámara y cuadro. Si hacen falta más, las celdas más lejanas quedan solo con el dosel.")]
        public int instanceBudget = 60000;
        [Tooltip("Con más tramos que esto en el cuadro, solo las plantas detalladas (cerca) proyectan sombra: cada cascada vuelve a dibujar todo lo que la proyecta.")]
        public int shadowInstanceBudget = 20000;
        public bool castShadows = true;

        struct Instance { public Matrix4x4 Matrix; public Vector3 Position; public byte Tone, Variant; }
        sealed class Cell
        {
            public Vector2Int Coord;
            public Bounds Bounds;
            public Instance[] Plants;
            public Matrix4x4[] Stubble;
            public Matrix4x4 Canopy;
            /// <summary>Triángulos de la celda en la malla del dosel de orilla; null si usa el cuadro instanciado.</summary>
            public int[] CanopyTriangles;
            public bool Harvested;
            public bool Cutting;
            public Vector3 CutPoint, CutForward;
        }

        readonly List<Cell> cells = new();
        readonly Dictionary<Vector2Int, Cell> byCoord = new();
        readonly List<Matrix4x4>[,,] batches = new List<Matrix4x4>[CornMeshFactory.Lods, CornMeshFactory.ToneBuckets, CornMeshFactory.VariantsPerBucket];
        readonly List<Matrix4x4> stubbleBatch = new();
        readonly List<Matrix4x4> canopyBatch = new();
        readonly List<Cell> borderCanopy = new();
        readonly List<Cell> borderCanopyDrawn = new();
        readonly List<int> borderIndices = new();
        readonly List<(Cell cell, float distance)> candidates = new();
        readonly Bounds[] lodBounds = new Bounds[CornMeshFactory.Lods];
        readonly bool[] lodUsed = new bool[CornMeshFactory.Lods];
        readonly Plane[] planes = new Plane[6];
        static readonly System.Comparison<(Cell cell, float distance)> Nearest = (a, b) => a.distance.CompareTo(b.distance);
        Mesh[,,] meshes;
        Mesh stubbleMesh, canopyMesh, borderCanopyMesh;
        Material material, canopy;
        Bounds fieldBounds;
        FieldGrid grid;
        long plantsInCandidates;

        public int CellCount => cells.Count;
        public int PlantInstanceCount { get; private set; }
        /// <summary>Celdas con maíz en pie según el último tick dibujado (sin contar el corte visual).</summary>
        public int StandingCellCount { get; private set; }
        /// <summary>Tramos de hilera elegidos en el último <see cref="Collect"/> (última cámara).</summary>
        public int DrawnPlantCount { get; private set; }
        /// <summary>Celdas en pie dibujadas solo con el dosel en el último <see cref="Collect"/>.</summary>
        public int CanopyOnlyCellCount { get; private set; }
        /// <summary>Si en el último <see cref="Collect"/> las plantas simples (lejos) proyectan sombra.</summary>
        public bool FarPlantsCastShadows { get; private set; }

        /// <summary>Siembra las celdas de campo. Sin <paramref name="lots"/> los calcula del trace (mundo.geo.formas).</summary>
        public void Build(FieldGrid layout, TraceData trace, Material cornMaterial, Material canopyMaterial = null, LotShapes lots = null)
        {
            Clear();
            grid = layout; material = cornMaterial; canopy = canopyMaterial;
            if (canopy) canopy.enableInstancing = true;
            if (!canopyMesh)
            {
                var quad = new MeshBuilder();
                quad.AddQuad(new Vector3(-.5f, 0, -.5f), new Vector3(-.5f, 0, .5f), new Vector3(.5f, 0, .5f), new Vector3(.5f, 0, -.5f), Vector3.up, Vector2.zero, Vector2.one);
                canopyMesh = quad.ToMesh("Dosel");
            }
            if (!material) return;
            material.enableInstancing = true;
            meshes ??= CornMeshFactory.BuildSet();
            stubbleMesh ??= CornMeshFactory.Stubble();
            for (int lod = 0; lod < CornMeshFactory.Lods; lod++)
                for (int tone = 0; tone < CornMeshFactory.ToneBuckets; tone++)
                    for (int v = 0; v < CornMeshFactory.VariantsPerBucket; v++)
                        batches[lod, tone, v] ??= new List<Matrix4x4>();

            var yields = trace.World["rendimiento"] as JArray;
            int maxYield = 0;
            if (yields != null)
                foreach (JArray row in yields) foreach (var value in row) maxYield = Mathf.Max(maxYield, (int)value);

            float size = grid.CellSize;
            int rows = Mathf.Max(1, Mathf.FloorToInt(size / RowSpacing));
            int segments = Mathf.Max(1, Mathf.RoundToInt(size / CornMeshFactory.SegmentLength));
            float segment = size / segments, rowStep = size / rows;
            var origin = transform.localToWorldMatrix;
            lots ??= LotShapes.From(grid, trace);
            var border = new MeshBuilder();
            var plants = new List<Instance>();
            var stubble = new List<Matrix4x4>();
            bool first = true;
            int total = 0;
            for (int y = 0; y < grid.Height; y++)
                for (int x = 0; x < grid.Width; x++)
                {
                    if (!grid.IsField(x, y)) continue;
                    var coord = new Vector2Int(x, y);
                    var random = new System.Random(x * 7919 + y * 104729);
                    int yield = yields != null ? (int)yields[y][x] : -1;
                    var center = grid.CellToWorld(x, y);
                    var pieces = lots.PiecesOf(coord);
                    bool nearRoad = lots.NearRoad(coord);
                    plants.Clear(); stubble.Clear();
                    float low = float.MaxValue, high = float.MinValue;

                    void Plant(float px, float pz)
                    {
                        var p = new Vector2(px, pz);
                        if (pieces != null && !lots.Contains(coord, p)) return;
                        if (nearRoad && lots.OnRoad(coord, p)) return;
                        // Cada planta sobre el relieve.
                        var local = new Vector3(px, grid.HasRelief ? grid.GroundHeight(px, pz) : center.y, pz);
                        int tone = yield < 0 ? (random.NextDouble() < .7 ? 1 : random.Next(0, 3))
                            : Mathf.Clamp(Mathf.FloorToInt(yield / (float)Mathf.Max(1, maxYield) * 3 - .01f) + (random.NextDouble() < .15 ? random.Next(-1, 2) : 0), 0, 2);
                        float heightScale = (float)(.93 + random.NextDouble() * .12);
                        var rotation = Quaternion.Euler(0, (float)(random.NextDouble() * 4 - 2), 0);
                        var matrix = origin * Matrix4x4.TRS(local, rotation, new Vector3(segment / CornMeshFactory.SegmentLength, heightScale, 1));
                        plants.Add(new Instance { Matrix = matrix, Position = origin.MultiplyPoint3x4(local), Tone = (byte)tone, Variant = (byte)random.Next(0, CornMeshFactory.VariantsPerBucket) });
                        stubble.Add(origin * Matrix4x4.TRS(local, rotation, new Vector3(segment / CornMeshFactory.SegmentLength, 1, 1)));
                        low = Mathf.Min(low, local.y); high = Mathf.Max(high, local.y);
                    }

                    Rect region;
                    if (pieces != null && lots.TryRegion(coord, out region))
                    {
                        // La misma retícula de surcos de todo el campo (filas cada rowStep, tramos de segment) sobre el
                        // rectángulo de los pedazos; solo quedan los tramos que caen dentro de ellos.
                        int j0 = Mathf.CeilToInt((region.xMin + size / 2) / segment - .5f - 1e-4f);
                        int j1 = Mathf.FloorToInt((region.xMax + size / 2) / segment - .5f + 1e-4f);
                        int k0 = Mathf.CeilToInt((region.yMin + size / 2) / rowStep - .5f - 1e-4f);
                        int k1 = Mathf.FloorToInt((region.yMax + size / 2) / rowStep - .5f + 1e-4f);
                        for (int k = k0; k <= k1; k++)
                            for (int j = j0; j <= j1; j++)
                                Plant(-size / 2 + (j + .5f) * segment, -size / 2 + (k + .5f) * rowStep);
                    }
                    else
                    {
                        region = new Rect(center.x - size / 2, center.z - size / 2, size, size);
                        for (int r = 0; r < rows; r++)
                            for (int s = 0; s < segments; s++)
                                Plant(center.x - size / 2 + (s + .5f) * segment, center.z - size / 2 + (r + .5f) * rowStep);
                    }

                    var cell = new Cell { Coord = coord, Plants = plants.ToArray(), Stubble = stubble.ToArray() };
                    total += cell.Plants.Length;
                    cell.Canopy = origin * Matrix4x4.TRS(center + Vector3.up * CanopyHeight, Quaternion.identity, new Vector3(size, 1, size));
                    if (pieces != null)
                    {
                        var tris = new List<int>();
                        float cy = center.y;
                        foreach (var piece in pieces)
                            LotShapes.AddPiece(border, piece,
                                p => new Vector3(p.x, (grid.HasRelief ? grid.GroundHeight(p.x, p.y) : cy) + CanopyHeight, p.y),
                                p => new Vector2((p.x - center.x) / size + .5f, (p.y - center.z) / size + .5f), tris);
                        cell.CanopyTriangles = tris.ToArray();
                    }
                    if (low > high) low = high = center.y;
                    var worldCenter = origin.MultiplyPoint3x4(new Vector3(region.center.x, (low + high) / 2 + 1.3f, region.center.y));
                    cell.Bounds = new Bounds(worldCenter, new Vector3(region.width + 1, high - low + 3, region.height + 1));
                    if (first) { fieldBounds = cell.Bounds; first = false; } else fieldBounds.Encapsulate(cell.Bounds);
                    cells.Add(cell); byCoord[cell.Coord] = cell;
                }
            if (border.VertexCount > 0)
            {
                borderCanopyMesh = border.ToMesh("Dosel de orilla");
                borderCanopyMesh.MarkDynamic();
                borderCanopyMesh.SetTriangles(System.Array.Empty<int>(), 0, false);
            }
            PlantInstanceCount = total;
            StandingCellCount = cells.Count;
        }

        /// <summary>Estado lógico del tick: qué celdas ya están cosechadas. Quita cualquier corte visual en curso.</summary>
        public void Render(PlaybackState state)
        {
            int standing = 0;
            foreach (var cell in cells)
            {
                cell.Harvested = state.Harvested.Contains((cell.Coord.x, cell.Coord.y));
                cell.Cutting = false;
                if (!cell.Harvested) standing++;
            }
            StandingCellCount = standing;
        }

        /// <summary>Corte visual: oculta las plantas de la celda que quedan detrás del frente del cabezal.</summary>
        public void SetCut(Vector2Int coord, Vector3 headerFront, Vector3 forward)
        {
            if (!byCoord.TryGetValue(coord, out var cell) || cell.Harvested) return;
            cell.Cutting = true; cell.CutPoint = headerFront; cell.CutForward = forward;
        }

        public void ClearCuts() { foreach (var cell in cells) cell.Cutting = false; }

        /// <summary>Número de tramos de hilera visibles en una celda (para pruebas).</summary>
        public int VisiblePlantsIn(Vector2Int coord)
        {
            if (!byCoord.TryGetValue(coord, out var cell) || cell.Harvested) return 0;
            if (!cell.Cutting) return cell.Plants.Length;
            int count = 0;
            foreach (var plant in cell.Plants) if (!IsCut(cell, plant.Position)) count++;
            return count;
        }

        static bool IsCut(Cell cell, Vector3 position) => Vector3.Dot(position - cell.CutPoint, cell.CutForward) < 0;

        void OnEnable() => RenderPipelineManager.beginCameraRendering += Draw;
        void OnDisable() => RenderPipelineManager.beginCameraRendering -= Draw;

        void Draw(ScriptableRenderContext context, Camera camera)
        {
            if (cells.Count == 0 || !material || meshes == null || camera.cameraType == CameraType.Preview) return;
            if (camera.cameraType == CameraType.Reflection) return;
            if ((camera.cullingMask & (1 << gameObject.layer)) == 0) return;
            Collect(camera);
            for (int lod = 0; lod < CornMeshFactory.Lods; lod++)
            {
                if (!lodUsed[lod]) continue;
                // Límites de las celdas de este LOD (no de todo el campo): así cada cascada descarta lo que no le toca.
                bool shadows = castShadows && (lod == 0 || FarPlantsCastShadows);
                var plants = new RenderParams(material)
                {
                    camera = camera, layer = gameObject.layer, worldBounds = lodBounds[lod], receiveShadows = true,
                    shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off
                };
                for (int tone = 0; tone < CornMeshFactory.ToneBuckets; tone++)
                    for (int v = 0; v < CornMeshFactory.VariantsPerBucket; v++)
                        Submit(plants, meshes[lod, tone, v], batches[lod, tone, v]);
            }
            var flat = new RenderParams(material)
            {
                camera = camera, layer = gameObject.layer, worldBounds = fieldBounds, receiveShadows = true,
                shadowCastingMode = ShadowCastingMode.Off
            };
            Submit(flat, stubbleMesh, stubbleBatch);
            if (canopy)
            {
                flat.material = canopy;
                Submit(flat, canopyMesh, canopyBatch);
                if (borderCanopyMesh && borderCanopy.Count > 0)
                {
                    UpdateBorderCanopy();
                    Graphics.RenderMesh(flat, borderCanopyMesh, 0, transform.localToWorldMatrix);
                }
            }
        }

        /// <summary>
        /// Elige qué dibuja una cámara y llena los lotes: por celda visible, plantas detalladas (cerca), plantas simples
        /// o solo el dosel (planta de menos de <see cref="minPlantPixels"/> o fuera de <see cref="instanceBudget"/>,
        /// empezando por las más lejanas). La celda que se está cortando siempre lleva plantas. Pública para las pruebas.
        /// </summary>
        public void Collect(Camera camera)
        {
            GeometryUtility.CalculateFrustumPlanes(camera, planes);
            foreach (var list in batches) list?.Clear();
            stubbleBatch.Clear(); canopyBatch.Clear(); borderCanopy.Clear(); candidates.Clear(); plantsInCandidates = 0;
            for (int lod = 0; lod < CornMeshFactory.Lods; lod++) lodUsed[lod] = false;
            DrawnPlantCount = 0; CanopyOnlyCellCount = 0;
            var eye = camera.transform.position;
            float stubble = stubbleDistance * stubbleDistance;
            float plantRange = PlantRangeSqr(camera.orthographic, camera.orthographicSize, camera.fieldOfView, camera.pixelHeight, minPlantPixels);
            foreach (var cell in cells)
            {
                if (!GeometryUtility.TestPlanesAABB(planes, cell.Bounds)) continue;
                float distance = cell.Bounds.SqrDistance(eye);
                if (cell.Harvested)
                {
                    if (distance < stubble) stubbleBatch.AddRange(cell.Stubble);
                    continue;
                }
                if (distance >= plantRange && !cell.Cutting) { CanopyOnly(cell); continue; }
                candidates.Add((cell, distance));
                plantsInCandidates += cell.Plants.Length;
            }

            // Tope de instancias: de la más cercana a la más lejana; en cuanto una celda ya no cabe, esa y las que
            // siguen quedan solo con el dosel (menos la que se está cortando).
            if (plantsInCandidates > instanceBudget) candidates.Sort(Nearest);
            float detail = detailDistance * detailDistance;
            long used = 0;
            bool full = false;
            foreach (var (cell, distance) in candidates)
            {
                if (!full && used + cell.Plants.Length > instanceBudget) full = true;
                if (full && !cell.Cutting) { CanopyOnly(cell); continue; }
                used += cell.Plants.Length;
                AddPlants(cell, distance < detail ? 0 : 1, distance < stubble);
            }
            FarPlantsCastShadows = DrawnPlantCount <= shadowInstanceBudget;
        }

        void CanopyOnly(Cell cell)
        {
            AddCanopy(cell);
            CanopyOnlyCellCount++;
        }

        void AddCanopy(Cell cell)
        {
            if (cell.CanopyTriangles != null) borderCanopy.Add(cell);
            else canopyBatch.Add(cell.Canopy);
        }

        /// <summary>Rearma los índices del dosel de orilla solo si cambió qué celdas lo llevan.</summary>
        void UpdateBorderCanopy()
        {
            bool same = borderCanopy.Count == borderCanopyDrawn.Count;
            for (int i = 0; same && i < borderCanopy.Count; i++) same = borderCanopy[i] == borderCanopyDrawn[i];
            if (same) return;
            borderCanopyDrawn.Clear(); borderCanopyDrawn.AddRange(borderCanopy);
            borderIndices.Clear();
            foreach (var cell in borderCanopy) borderIndices.AddRange(cell.CanopyTriangles);
            borderCanopyMesh.SetTriangles(borderIndices, 0, false);
        }

        /// <summary>Celdas de la orilla que la última llamada a <see cref="Collect"/> dejó con el dosel recortado.</summary>
        public int BorderCanopyCellCount => borderCanopy.Count;

        void AddPlants(Cell cell, int lod, bool nearStubble)
        {
            if (lod == 1 && !cell.Cutting) AddCanopy(cell);
            foreach (var plant in cell.Plants)
            {
                if (cell.Cutting && IsCut(cell, plant.Position)) continue;
                batches[lod, plant.Tone, plant.Variant].Add(plant.Matrix);
                DrawnPlantCount++;
            }
            if (cell.Cutting && nearStubble)
                for (int i = 0; i < cell.Plants.Length; i++)
                    if (IsCut(cell, cell.Plants[i].Position)) stubbleBatch.Add(cell.Stubble[i]);
            if (lodUsed[lod]) lodBounds[lod].Encapsulate(cell.Bounds);
            else { lodBounds[lod] = cell.Bounds; lodUsed[lod] = true; }
        }

        /// <summary>
        /// Distancia² hasta la que una planta mide al menos <paramref name="minPixels"/> en pantalla. En ortográfica
        /// la escala no depende de la distancia: todo o nada. Sin mínimo, infinita.
        /// </summary>
        public static float PlantRangeSqr(bool orthographic, float orthographicSize, float fieldOfView, float pixelHeight, float minPixels)
        {
            if (minPixels <= 0) return float.PositiveInfinity;
            float pixels = Mathf.Max(1, pixelHeight);
            if (orthographic)
                return PlantHeight * pixels / (2 * Mathf.Max(1e-3f, orthographicSize)) >= minPixels ? float.PositiveInfinity : 0;
            float range = PlantHeight * pixels / (2 * Mathf.Tan(Mathf.Clamp(fieldOfView, 1, 179) * Mathf.Deg2Rad / 2) * minPixels);
            return range * range;
        }

        static void Submit(RenderParams parameters, Mesh mesh, List<Matrix4x4> matrices)
        {
            const int batch = 1023;
            for (int start = 0; start < matrices.Count; start += batch)
                Graphics.RenderMeshInstanced(parameters, mesh, 0, matrices, Mathf.Min(batch, matrices.Count - start), start);
        }

        public void Clear()
        {
            cells.Clear(); byCoord.Clear(); borderCanopy.Clear(); borderCanopyDrawn.Clear();
            DestroyAsset(borderCanopyMesh); borderCanopyMesh = null;
            PlantInstanceCount = 0; StandingCellCount = 0;
            grid = null;
        }

        void OnDestroy()
        {
            if (meshes != null) foreach (var mesh in meshes) DestroyAsset(mesh);
            DestroyAsset(stubbleMesh); DestroyAsset(canopyMesh); DestroyAsset(borderCanopyMesh);
            meshes = null; stubbleMesh = null; canopyMesh = null; borderCanopyMesh = null;
        }

        static void DestroyAsset(Object asset)
        {
            if (!asset) return;
            if (Application.isPlaying) Destroy(asset); else DestroyImmediate(asset);
        }
    }
}
