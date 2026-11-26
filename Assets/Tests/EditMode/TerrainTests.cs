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

        [Test]
        public void MapTraceRequiresVersion23()
        {
            var json = JObject.Parse(File.ReadAllText(Ejemplo));
            json["schema_version"] = "2.2";
            Assert.Throws<System.FormatException>(() => TraceLoader.Parse(json.ToString()));
        }
    }
}
