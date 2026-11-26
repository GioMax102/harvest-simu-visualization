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
    /// </summary>
    [ExecuteAlways]
    public sealed class CornField : MonoBehaviour
    {
        public const float RowSpacing = 1.2f;
        /// <summary>Altura del dosel lejano: debajo de las hojas altas para que asomen las plantas.</summary>
        public const float CanopyHeight = 1.35f;
        [Tooltip("Distancia (m) hasta la que se dibujan las plantas detalladas.")]
        public float detailDistance = 70f;
        [Tooltip("Distancia (m) hasta la que se dibuja rastrojo en las celdas cosechadas.")]
        public float stubbleDistance = 90f;
        public bool castShadows = true;

        struct Instance { public Matrix4x4 Matrix; public Vector3 Position; public byte Tone, Variant; }
        sealed class Cell
        {
            public Vector2Int Coord;
            public Bounds Bounds;
            public Instance[] Plants;
            public Matrix4x4[] Stubble;
            public Matrix4x4 Canopy;
            public bool Harvested;
            public bool Cutting;
            public Vector3 CutPoint, CutForward;
        }

        readonly List<Cell> cells = new();
        readonly Dictionary<Vector2Int, Cell> byCoord = new();
        readonly List<Matrix4x4>[,,] batches = new List<Matrix4x4>[CornMeshFactory.Lods, CornMeshFactory.ToneBuckets, CornMeshFactory.VariantsPerBucket];
        readonly List<Matrix4x4> stubbleBatch = new();
        readonly List<Matrix4x4> canopyBatch = new();
        readonly Plane[] planes = new Plane[6];
        Mesh[,,] meshes;
        Mesh stubbleMesh, canopyMesh;
        Material material, canopy;
        Bounds fieldBounds;
        FieldGrid grid;

        public int CellCount => cells.Count;
        public int PlantInstanceCount { get; private set; }
        /// <summary>Celdas con maíz en pie según el último tick dibujado (sin contar el corte visual).</summary>
        public int StandingCellCount { get; private set; }

        public void Build(FieldGrid layout, TraceData trace, Material cornMaterial, Material canopyMaterial = null)
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
            float segment = size / segments;
            var origin = transform.localToWorldMatrix;
            bool first = true;
            for (int y = 0; y < grid.Height; y++)
                for (int x = 0; x < grid.Width; x++)
                {
                    if (!grid.IsField(x, y)) continue;
                    var random = new System.Random(x * 7919 + y * 104729);
                    int yield = yields != null ? (int)yields[y][x] : -1;
                    var center = grid.CellToWorld(x, y);
                    var cell = new Cell { Coord = new Vector2Int(x, y), Plants = new Instance[rows * segments], Stubble = new Matrix4x4[rows * segments] };
                    int i = 0;
                    for (int r = 0; r < rows; r++)
                        for (int s = 0; s < segments; s++)
                        {
                            var local = center + new Vector3(-size / 2 + (s + .5f) * segment, 0, -size / 2 + (r + .5f) * size / rows);
                            if (grid.HasRelief) local.y = grid.GroundHeight(local.x, local.z); // cada planta sobre el relieve
                            int tone = yield < 0 ? (random.NextDouble() < .7 ? 1 : random.Next(0, 3))
                                : Mathf.Clamp(Mathf.FloorToInt(yield / (float)Mathf.Max(1, maxYield) * 3 - .01f) + (random.NextDouble() < .15 ? random.Next(-1, 2) : 0), 0, 2);
                            float heightScale = (float)(.93 + random.NextDouble() * .12);
                            var rotation = Quaternion.Euler(0, (float)(random.NextDouble() * 4 - 2), 0);
                            var matrix = origin * Matrix4x4.TRS(local, rotation, new Vector3(segment / CornMeshFactory.SegmentLength, heightScale, 1));
                            cell.Plants[i] = new Instance { Matrix = matrix, Position = origin.MultiplyPoint3x4(local), Tone = (byte)tone, Variant = (byte)random.Next(0, CornMeshFactory.VariantsPerBucket) };
                            cell.Stubble[i] = origin * Matrix4x4.TRS(local, rotation, new Vector3(segment / CornMeshFactory.SegmentLength, 1, 1));
                            i++;
                        }
                    cell.Canopy = origin * Matrix4x4.TRS(center + Vector3.up * CanopyHeight, Quaternion.identity, new Vector3(size, 1, size));
                    var worldCenter = origin.MultiplyPoint3x4(center + Vector3.up * 1.3f);
                    cell.Bounds = new Bounds(worldCenter, new Vector3(size + 1, 3, size + 1));
                    if (first) { fieldBounds = cell.Bounds; first = false; } else fieldBounds.Encapsulate(cell.Bounds);
                    cells.Add(cell); byCoord[cell.Coord] = cell;
                }
            PlantInstanceCount = cells.Count * rows * segments;
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
            GeometryUtility.CalculateFrustumPlanes(camera, planes);
            foreach (var list in batches) list?.Clear();
            stubbleBatch.Clear();
            canopyBatch.Clear();
            var eye = camera.transform.position;
            float detail = detailDistance * detailDistance, stubble = stubbleDistance * stubbleDistance;
            foreach (var cell in cells)
            {
                if (!GeometryUtility.TestPlanesAABB(planes, cell.Bounds)) continue;
                float distance = cell.Bounds.SqrDistance(eye);
                if (cell.Harvested)
                {
                    if (distance < stubble) stubbleBatch.AddRange(cell.Stubble);
                    continue;
                }
                int lod = distance < detail ? 0 : 1;
                if (lod == 1 && !cell.Cutting) canopyBatch.Add(cell.Canopy);
                foreach (var plant in cell.Plants)
                {
                    if (cell.Cutting && IsCut(cell, plant.Position)) continue;
                    batches[lod, plant.Tone, plant.Variant].Add(plant.Matrix);
                }
                if (cell.Cutting && distance < stubble)
                    for (int i = 0; i < cell.Plants.Length; i++)
                        if (IsCut(cell, cell.Plants[i].Position)) stubbleBatch.Add(cell.Stubble[i]);
            }
            var parameters = new RenderParams(material)
            {
                camera = camera, layer = gameObject.layer, worldBounds = fieldBounds, receiveShadows = true,
                shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off
            };
            for (int lod = 0; lod < CornMeshFactory.Lods; lod++)
                for (int tone = 0; tone < CornMeshFactory.ToneBuckets; tone++)
                    for (int v = 0; v < CornMeshFactory.VariantsPerBucket; v++)
                        Submit(parameters, meshes[lod, tone, v], batches[lod, tone, v]);
            parameters.shadowCastingMode = ShadowCastingMode.Off;
            Submit(parameters, stubbleMesh, stubbleBatch);
            if (canopy)
            {
                var canopyParameters = parameters; canopyParameters.material = canopy;
                Submit(canopyParameters, canopyMesh, canopyBatch);
            }
        }

        static void Submit(RenderParams parameters, Mesh mesh, List<Matrix4x4> matrices)
        {
            const int batch = 1023;
            for (int start = 0; start < matrices.Count; start += batch)
                Graphics.RenderMeshInstanced(parameters, mesh, 0, matrices, Mathf.Min(batch, matrices.Count - start), start);
        }

        public void Clear()
        {
            cells.Clear(); byCoord.Clear();
            PlantInstanceCount = 0; StandingCellCount = 0;
            grid = null;
        }

        void OnDestroy()
        {
            if (meshes != null) foreach (var mesh in meshes) DestroyAsset(mesh);
            DestroyAsset(stubbleMesh); DestroyAsset(canopyMesh);
            meshes = null; stubbleMesh = null; canopyMesh = null;
        }

        static void DestroyAsset(Object asset)
        {
            if (!asset) return;
            if (Application.isPlaying) Destroy(asset); else DestroyImmediate(asset);
        }
    }
}
