using System.IO;
using System.Linq;
using HarvestSim.Visualization;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace HarvestSim.Tests
{
    /// <summary>
    /// Trace 2.3 con el mapa real de El Pueblito (harvest_sim, docs/especificacion_terreno.md): caminos, fuera de
    /// campo, lotes, relieve (altura_m) y formas reales (geo.formas). Unity solo dibuja lo que trae el trace.
    /// </summary>
    public class TerrainTests
    {
        const string Ejemplo = "Assets/Data/Traces/terreno/ejemplo_23.json";
        const string G = "Assets/Data/Traces/terreno/escenario_g__anticipada_plus__seed0.json";
        static TraceData Load(string path) => TraceLoader.Parse(File.ReadAllText(path));

        [Test]
        public void Example23LoadsWithMapKinds()
        {
            var trace = Load(Ejemplo);
            var grid = FieldGrid.FromTrace(trace);
            Assert.That(grid.HasMap && grid.HasRelief, Is.True);
            Assert.That(grid.FieldCellCount, Is.EqualTo(15));
            Assert.That(grid.KindAt(0, 2), Is.EqualTo(CellKind.Service), "camino");
            Assert.That(grid.KindAt(5, 1), Is.EqualTo(CellKind.OutsideField));
            Assert.That(grid.KindAt(5, 4), Is.EqualTo(CellKind.Obstacle));
            Assert.That(grid.KindAt(3, 0), Is.EqualTo(CellKind.Service), "con mapa la fila 0 no es especial");
            Assert.That(grid.CellSize, Is.EqualTo(12f));
        }

        [Test]
        public void RealFieldHeightsMatchTheTrace()
        {
            var trace = Load(G);
            Assert.That((string)trace.Json["schema_version"], Is.EqualTo("2.3"));
            var grid = FieldGrid.FromTrace(trace);
            var alturas = (JArray)trace.World["altura_m"];
            int campo = ((JObject)trace.World["lotes"]).Properties().Sum(p => ((JArray)p.Value).Count);
            Assert.That(grid.FieldCellCount, Is.EqualTo(campo));
            foreach (var (x, y) in new[] { (20, 20), (40, 30), (60, 10), (0, 0), (75, 51) })
            {
                float real = (float)alturas[y][x];
                Assert.That(grid.CellToWorld(x, y).y + grid.BaseHeight, Is.EqualTo(real).Within(.01f), $"celda ({x}, {y})");
                var centro = grid.CellToWorld(x, y);
                Assert.That(grid.GroundHeight(centro) + grid.BaseHeight, Is.EqualTo(real).Within(.01f));
            }
            // entre dos centros, la altura está entre las dos (bilineal)
            var a = grid.CellToWorld(30, 20); var b = grid.CellToWorld(31, 20);
            float medio = grid.GroundHeight((a + b) / 2);
            Assert.That(medio, Is.InRange(Mathf.Min(a.y, b.y) - 1e-3f, Mathf.Max(a.y, b.y) + 1e-3f));
        }

        [Test]
        public void RealFieldPlaysToTheEnd()
        {
            var trace = Load(G);
            var playback = new TracePlayback(trace);
            playback.Seek(trace.TotalTicks);
            Assert.That(playback.State.Delivered, Is.EqualTo((int)trace.Json["metricas"]["grano_total_entregado"]));
            Assert.That(playback.State.Harvested.Count, Is.EqualTo(FieldGrid.FromTrace(trace).FieldCellCount));
        }

        [Test]
        public void CornOnlyOnFieldCellsAndOnTheRelief()
        {
            var trace = Load(G);
            var grid = FieldGrid.FromTrace(trace);
            var go = new GameObject("maiz-prueba");
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            try
            {
                var corn = go.AddComponent<CornField>();
                corn.Build(grid, trace, material);
                Assert.That(corn.CellCount, Is.EqualTo(grid.FieldCellCount), "sin maíz fuera de campo, en caminos ni en obstáculos");
            }
            finally { Object.DestroyImmediate(go); Object.DestroyImmediate(material); }
        }

        /// <summary>
        /// El G completo en pantalla colgaba la GPU (~140 000 tramos en la cámara y en cada cascada de sombra).
        /// Visto entero solo va el dosel; de cerca, el tope de instancias y sin sombra lejana.
        /// </summary>
        [Test]
        public void RealFieldCornStaysWithinTheBudget()
        {
            var trace = Load(G);
            var grid = FieldGrid.FromTrace(trace);
            WithCornCamera(grid, trace, (corn, camera) =>
            {
                Assert.That(corn.PlantInstanceCount, Is.GreaterThan(corn.instanceBudget), "el G pide más tramos que el tope");
                var area = grid.WorldBounds;

                TopView(camera, area);
                corn.Collect(camera);
                Assert.That(corn.DrawnPlantCount, Is.Zero, "visto completo, cada planta mediría ~3 px");
                Assert.That(corn.CanopyOnlyCellCount, Is.EqualTo(corn.CellCount));

                camera.orthographic = false; camera.fieldOfView = 60;
                camera.nearClipPlane = .3f; camera.farClipPlane = 3000;
                var rotation = Quaternion.Euler(35, 30, 0);
                camera.transform.SetPositionAndRotation(area.center - rotation * Vector3.forward * 350, rotation);
                corn.Collect(camera);
                Assert.That(corn.DrawnPlantCount, Is.InRange(1, corn.instanceBudget));
                Assert.That(corn.CanopyOnlyCellCount, Is.GreaterThan(0), "las celdas más lejanas quedan solo con el dosel");
                Assert.That(corn.FarPlantsCastShadows, Is.False);
            });
        }

        [Test]
        public void SmallFieldCornIsDrawnInFull()
        {
            var trace = TraceLoader.Parse(File.ReadAllText("Assets/Data/Traces/entrega2/escenario_f__base__seed0.json"));
            var grid = FieldGrid.FromTrace(trace);
            WithCornCamera(grid, trace, (corn, camera) =>
            {
                TopView(camera, grid.WorldBounds);
                corn.Collect(camera);
                Assert.That(corn.DrawnPlantCount, Is.EqualTo(corn.PlantInstanceCount), "en A–F nada cambia");
                Assert.That(corn.CanopyOnlyCellCount, Is.Zero);
                Assert.That(corn.FarPlantsCastShadows, Is.True);
            });
        }

        /// <summary>Encuadre cenital como <see cref="CameraRig.Frame"/> sin HUD: ortográfica a 400 m.</summary>
        static void TopView(Camera camera, Bounds area)
        {
            camera.orthographic = true;
            camera.orthographicSize = Mathf.Max(area.extents.z, area.extents.x / camera.aspect) * 1.12f + 4;
            camera.transform.SetPositionAndRotation(area.center + Vector3.up * 400, Quaternion.Euler(90, 0, 0));
            camera.nearClipPlane = 1; camera.farClipPlane = 1000;
        }

        static void WithCornCamera(FieldGrid grid, TraceData trace, System.Action<CornField, Camera> check)
        {
            var go = new GameObject("maiz-lod");
            var eye = new GameObject("camara-lod");
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            var target = new RenderTexture(1920, 1080, 16);
            try
            {
                var corn = go.AddComponent<CornField>();
                corn.Build(grid, trace, material);
                var camera = eye.AddComponent<Camera>();
                camera.enabled = false; // solo se usa para Collect, no dibuja
                camera.targetTexture = target;
                camera.aspect = 1920f / 1080f;
                check(corn, camera);
            }
            finally
            {
                Object.DestroyImmediate(go); Object.DestroyImmediate(eye);
                Object.DestroyImmediate(material); Object.DestroyImmediate(target);
            }
        }

        [Test]
        public void RealRoadsAndOutlinesFollowTheRelief()
        {
            var trace = Load(G);
            var grid = FieldGrid.FromTrace(trace);
            var art = ScriptableObject.CreateInstance<HarvestArt>();
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            art.roadMaterial = art.lineMaterial = material;
            var root = new GameObject("formas-prueba");
            var shapes = new TerrainShapes();
            try
            {
                shapes.Build(grid, trace, art, root.transform);
                var formas = (JObject)trace.World["geo"]["formas"];
                Assert.That(shapes.RoadCount, Is.EqualTo(((JArray)formas["caminos"]).Count));
                Assert.That(shapes.LotCount, Is.EqualTo(3));
                var mesh = root.GetComponentInChildren<MeshFilter>().sharedMesh;
                foreach (var v in mesh.vertices.Take(200))
                    Assert.That(v.y, Is.EqualTo(grid.GroundHeight(v.x, v.z) + TerrainShapes.RoadLift).Within(.01f));
                // el contorno del lote A pasa por su primer vértice real
                var lote = (JArray)formas["lotes"]["A"];
                var esperado = TerrainShapes.ToWorld(grid, (float)lote[0][0], (float)lote[0][1]);
                var linea = root.GetComponentsInChildren<LineRenderer>().First(l => l.name.EndsWith(" A"));
                var p = linea.GetPosition(0);
                Assert.That(new Vector2(p.x, p.z), Is.EqualTo(esperado));
            }
            finally
            {
                shapes.Destroy();
                Object.DestroyImmediate(root); Object.DestroyImmediate(material); Object.DestroyImmediate(art);
            }
        }

        /// <summary>
        /// La simulación marca campo con ≥ 50 % de la celda en el lote: los cuadros completos se salían del contorno y
        /// dejaban huecos de lote. Ahora cada celda de la orilla se recorta al lote y toma los pedazos de sus vecinas.
        /// </summary>
        [Test]
        public void LotEdgesFollowTheRealShape()
        {
            var trace = Load(G);
            var grid = FieldGrid.FromTrace(trace);
            var lots = LotShapes.From(grid, trace);
            Assert.That(lots.Lots.Count, Is.EqualTo(3));
            Assert.That(lots.BorderCellCount, Is.InRange(1, grid.FieldCellCount - 1));
            var random = new System.Random(7);
            var b = grid.WorldBounds;
            int overflow = 0, uncovered = 0, doubled = 0, before = 0;
            for (int i = 0; i < 30000; i++)
            {
                var p = new Vector2((float)(b.min.x + random.NextDouble() * b.size.x), (float)(b.min.z + random.NextDouble() * b.size.z));
                bool inLot = lots.InsideLot(p);
                var cell = grid.WorldToCell(new Vector3(p.x, 0, p.y));
                int owners = 0;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        var n = new Vector2Int(cell.x + dx, cell.y + dy);
                        if (grid.IsField(n.x, n.y) && (lots.PiecesOf(n) == null ? n == cell : lots.Contains(n, p))) owners++;
                    }
                if (owners > 0 && !inLot) overflow++;
                if (owners > 1) doubled++;
                if (inLot && owners == 0 && grid.KindAt(cell.x, cell.y) is CellKind.OutsideField or CellKind.Service) uncovered++;
                if (grid.IsField(cell.x, cell.y) != inLot) before++;
            }
            Assert.That(before, Is.GreaterThan(100), "la escalera de antes se nota");
            Assert.That(overflow, Is.Zero, "nada dibujado fuera de los lotes");
            Assert.That(doubled, Is.Zero, "ningún pedazo es de dos celdas");
            Assert.That(uncovered, Is.LessThanOrEqualTo(2), "sin huecos de lote en celdas fuera de campo o de camino");
        }

        [Test]
        public void SoilAndCornStayInsideTheLots()
        {
            var trace = Load(G);
            var grid = FieldGrid.FromTrace(trace);
            var lots = LotShapes.From(grid, trace);
            var art = ScriptableObject.CreateInstance<HarvestArt>();
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            art.grassMaterial = art.soilMaterial = art.stubbleMaterial = art.roadMaterial = art.concreteMaterial = material;
            var root = new GameObject("suelo-prueba");
            var ground = new GroundBuilder();
            var corn = new GameObject("maiz-prueba").AddComponent<CornField>();
            try
            {
                ground.Build(grid, art, root.transform, lots);
                var mesh = ground.Parcels.GetComponent<MeshFilter>().sharedMesh;
                var v = mesh.vertices;
                var soil = mesh.GetTriangles(GroundBuilder.SoilSubmesh);
                Assert.That(soil.Length, Is.GreaterThan(0));
                for (int i = 0; i < soil.Length; i += 3)
                {
                    var c = (v[soil[i]] + v[soil[i + 1]] + v[soil[i + 2]]) / 3;
                    Assert.That(lots.InsideLot(new Vector2(c.x, c.z)), Is.True, $"triángulo de tierra en ({c.x:F1}, {c.z:F1})");
                }

                corn.Build(grid, trace, material, null, lots);
                Assert.That(corn.CellCount, Is.EqualTo(grid.FieldCellCount), "la cosecha lógica no cambia");
                int sum = 0;
                for (int y = 0; y < grid.Height; y++)
                    for (int x = 0; x < grid.Width; x++)
                        if (grid.IsField(x, y)) sum += corn.VisiblePlantsIn(new Vector2Int(x, y));
                Assert.That(sum, Is.EqualTo(corn.PlantInstanceCount));
            }
            finally
            {
                ground.Destroy();
                Object.DestroyImmediate(corn.gameObject); Object.DestroyImmediate(root);
                Object.DestroyImmediate(material); Object.DestroyImmediate(art);
            }
        }

        [Test]
        public void TracesWithoutShapesKeepSquareCells()
        {
            var trace = TraceLoader.Parse(File.ReadAllText("Assets/Data/Traces/escenario_a.json"));
            var grid = FieldGrid.FromTrace(trace);
            Assert.That(LotShapes.From(grid, trace).BorderCellCount, Is.Zero);
            Assert.That(RoadRoutes.Build(grid, trace).RunCount, Is.Zero);
        }

        /// <summary>
        /// Por camino, la escalera de la cuadrícula (E, N, E, N…) se dibuja como una línea suave a velocidad constante
        /// que empieza y termina en la pose exacta del trace; en el cultivo no cambia nada.
        /// </summary>
        [Test]
        public void RoadRunsAreSmoothAndInSync()
        {
            var trace = Load(G);
            var grid = FieldGrid.FromTrace(trace);
            var routes = RoadRoutes.Build(grid, trace);
            Assert.That(routes.RunCount, Is.GreaterThan(0));
            var playback = new TracePlayback(trace);
            int checkedRuns = 0;
            foreach (var id in trace.Agents.Keys)
                for (int t = 0; t < trace.TotalTicks && checkedRuns < 12; t++)
                {
                    if (!routes.TryRun(id, t, out var points, out int start, out int end) || start != t) continue;
                    checkedRuns++;
                    foreach (int k in new[] { start, end })
                    {
                        playback.Seek(k);
                        var at = VehicleMotion.At(grid, playback.State.Agents[id]);
                        Assert.That(routes.TryPose(id, k, 0, out var pose), Is.True);
                        Assert.That((pose.Position - at.Position).magnitude, Is.LessThan(.01f), $"{id} tick {k}");
                        Assert.That(Mathf.Abs(Mathf.DeltaAngle(pose.Heading, at.Heading)), Is.LessThan(.5f));
                    }
                    var steps = new System.Collections.Generic.List<float>();
                    for (int k = start; k < end; k++)
                    {
                        playback.Seek(k);
                        var agent = playback.State.Agents[id];
                        Assert.That(grid.IsField(agent.X, agent.Y), Is.False, "solo por camino");
                        for (int sub = 0; sub < 5; sub++)
                        {
                            routes.TryPose(id, k, sub / 5f, out var a);
                            routes.TryPose(id, k, (sub + 1) / 5f, out var c);
                            steps.Add(new Vector2(a.Position.x - c.Position.x, a.Position.z - c.Position.z).magnitude);
                        }
                    }
                    float mean = steps.Average();
                    Assert.That(steps.Max() - steps.Min(), Is.LessThan(mean * .1f), $"{id} {start}–{end}: velocidad pareja");
                    t = end;
                }
            Assert.That(checkedRuns, Is.GreaterThan(0));
        }

        [Test]
        public void MapTraceRequiresVersion23()
        {
            var json = JObject.Parse(File.ReadAllText(Ejemplo));
            json["schema_version"] = "2.2";
            Assert.Throws<System.FormatException>(() => TraceLoader.Parse(json.ToString()));
        }
    }
}
