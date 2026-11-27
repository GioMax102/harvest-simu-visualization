using System.IO;
using System.Linq;
using HarvestSim.Visualization;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace HarvestSim.Tests
{
    /// <summary>
    /// Plan de hileras y rutas (harvest_sim, docs/especificacion_terreno.md §14): el trace 2.4 trae la cabecera, las
    /// pasadas a lo largo de las hileras y la curva de Dubins de cada vuelta (TRAYECTORIA). Unity solo la dibuja.
    /// </summary>
    public class HilerasTests
    {
        const string G = "Assets/Data/Traces/hileras/escenario_g_hileras__anticipada_plus__seed0.json";
        static TraceData Load() => TraceLoader.Parse(File.ReadAllText(G));

        static (int tick, JObject e) FirstTurn(TraceData trace) =>
            trace.Changes.OrderBy(p => p.Key).SelectMany(p => p.Value.OfType<JObject>()
                .Where(e => (string)e["tipo"] == "TRAYECTORIA").Select(e => (p.Key, e))).First();

        [Test]
        public void Trace24LoadsAndPlaysToTheEnd()
        {
            var trace = Load();
            Assert.That((string)trace.Json["schema_version"], Is.EqualTo("2.4"));
            var grid = FieldGrid.FromTrace(trace);
            int cells = ((JArray)trace.World["cabecera"]).Count + ((JArray)trace.World["pasadas"]).Sum(p => (int)p[2] - (int)p[0] + 1);
            Assert.That(cells, Is.EqualTo(grid.FieldCellCount), "cabecera + pasadas = campo");
            var playback = new TracePlayback(trace);
            playback.Seek(trace.TotalTicks);
            Assert.That(playback.State.Harvested.Count, Is.EqualTo(grid.FieldCellCount));
            Assert.That(playback.State.Trajectories, Is.Empty, "al final no queda ninguna vuelta activa");
        }

        [Test]
        public void TurnIsActiveOnlyDuringItsTicks()
        {
            var trace = Load();
            var (tick, turn) = FirstTurn(trace);
            string agent = (string)turn["agente"];
            int ticks = (int)turn["ticks"];
            var playback = new TracePlayback(trace);
            playback.Seek(tick);
            Assert.That(playback.State.Trajectories.ContainsKey(agent), Is.True);
            playback.Seek(tick + ticks);
            Assert.That(playback.State.Trajectories.ContainsKey(agent), Is.True);
            playback.Seek(tick + ticks + 1);
            Assert.That(playback.State.Trajectories.ContainsKey(agent), Is.False);
            playback.Seek(tick - 1);
            Assert.That(playback.State.Trajectories.ContainsKey(agent), Is.False, "al retroceder se reconstruye");
        }

        [Test]
        public void VehicleFollowsTheDubinsCurve()
        {
            var trace = Load();
            var grid = FieldGrid.FromTrace(trace);
            var (_, turn) = FirstTurn(trace);
            var points = (JArray)turn["puntos"];
            var start = VehicleMotion.AlongTrajectory(grid, points, 0);
            var end = VehicleMotion.AlongTrajectory(grid, points, 1);
            Vector2 first = new((float)points[0][0], -(float)points[0][1]);
            Vector2 last = new((float)points.Last()[0], -(float)points.Last()[1]);
            Assert.That(Vector2.Distance(new Vector2(start.Position.x, start.Position.z), first), Is.LessThan(.01f));
            Assert.That(Vector2.Distance(new Vector2(end.Position.x, end.Position.z), last), Is.LessThan(.01f));
            // sale y entra a lo largo de las hileras (eje x de la cuadrícula: rumbo 90° o 270° en Unity)
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(start.Heading, 90)) < 5 || Mathf.Abs(Mathf.DeltaAngle(start.Heading, 270)) < 5, Is.True, $"{start.Heading}");
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(end.Heading, 90)) < 5 || Mathf.Abs(Mathf.DeltaAngle(end.Heading, 270)) < 5, Is.True, $"{end.Heading}");
            // media vuelta: el rumbo final es opuesto al inicial
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(start.Heading, end.Heading)), Is.GreaterThan(170));
            // en algún momento de la vuelta (U u Ω) va de lado, perpendicular a las hileras
            float maxTurn = Enumerable.Range(0, 51).Max(i =>
                Mathf.Abs(Mathf.DeltaAngle(VehicleMotion.AlongTrajectory(grid, points, i / 50f).Heading, start.Heading)));
            Assert.That(maxTurn, Is.GreaterThanOrEqualTo(80f));
        }

        [Test]
        public void HeadlandEdgesAreDrawnAtEveryPassEnd()
        {
            var trace = Load();
            var grid = FieldGrid.FromTrace(trace);
            var art = ScriptableObject.CreateInstance<HarvestArt>();
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            art.roadMaterial = art.lineMaterial = material;
            var root = new GameObject("hileras-prueba");
            var shapes = new TerrainShapes();
            try
            {
                shapes.Build(grid, trace, art, root.transform);
                Assert.That(shapes.HeadlandEdgeCount, Is.EqualTo(2 * ((JArray)trace.World["pasadas"]).Count));
                var edge = root.transform.Find("Orilla de la cabecera");
                Assert.That(edge, Is.Not.Null);
                foreach (var v in edge.GetComponent<MeshFilter>().sharedMesh.vertices.Take(100))
                    Assert.That(v.y, Is.EqualTo(grid.GroundHeight(v.x, v.z) + TerrainShapes.RoadLift).Within(.01f));
            }
            finally { shapes.Destroy(); Object.DestroyImmediate(root); Object.DestroyImmediate(material); Object.DestroyImmediate(art); }
        }

        [Test]
        public void RowsRequireVersion24()
        {
            var json = JObject.Parse(File.ReadAllText(G));
            json["schema_version"] = "2.3";
            var error = Assert.Throws<System.FormatException>(() => TraceLoader.Parse(json.ToString()));
            StringAssert.Contains("2.4", error.Message);
        }

        [Test]
        public void CornRowsAtRealSpacing()
        {
            Assert.That(CornField.RowSpacing, Is.EqualTo(.76f).Within(1e-4f));
            Assert.That(Mathf.FloorToInt(12f / CornField.RowSpacing), Is.EqualTo(15));
        }
    }
}
