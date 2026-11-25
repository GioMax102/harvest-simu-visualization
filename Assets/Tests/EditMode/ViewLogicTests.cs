using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarvestSim.Visualization;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace HarvestSim.Tests
{
    /// <summary>Lógica pura de la vista realista: cuadrícula en metros, movimiento entre ticks y señales del tick.</summary>
    public class ViewLogicTests
    {
        static TraceData Load(string path) => TraceLoader.Parse(File.ReadAllText(path));

        [Test]
        public void GridMapsCellsToMetersAndBack()
        {
            var trace = Load("Assets/Data/Traces/entrega2/escenario_f__subasta__seed0.json");
            var grid = FieldGrid.FromTrace(trace);
            Assert.That(grid.CellSize, Is.EqualTo(FieldGrid.DefaultCellSize));
            for (int y = 0; y < grid.Height; y++)
                for (int x = 0; x < grid.Width; x++)
                {
                    var world = grid.CellToWorld(x, y);
                    Assert.That(world, Is.EqualTo(new Vector3(x * 12, 0, -y * 12)));
                    Assert.That(grid.WorldToCell(world + new Vector3(5.9f, 0, -5.9f)), Is.EqualTo(new Vector2Int(x, y)));
                }
            // El Norte (y−1) es +Z y el Este es +X.
            Assert.That(FieldGrid.Forward(FieldGrid.Heading("N")).z, Is.EqualTo(1).Within(1e-5));
            Assert.That(FieldGrid.Forward(FieldGrid.Heading("E")).x, Is.EqualTo(1).Within(1e-5));
            Assert.That(FieldGrid.Step("O"), Is.EqualTo(new Vector2Int(-1, 0)));
            Assert.That(FieldGrid.FromTrace(trace, 9).CellToWorld(2, 1), Is.EqualTo(new Vector3(18, 0, -9)));
        }

        [Test]
        public void GridClassifiesCellsAndObstacleShapes()
        {
            var e = FieldGrid.FromTrace(Load("Assets/Data/Traces/entrega2/escenario_e__base__seed0.json"));
            Assert.That(e.KindAt(0, 0), Is.EqualTo(CellKind.Warehouse));
            Assert.That(e.KindAt(1, 0), Is.EqualTo(CellKind.Base));
            Assert.That(e.KindAt(8, 0), Is.EqualTo(CellKind.Service));
            Assert.That(e.KindAt(4, 3), Is.EqualTo(CellKind.Field), "El hueco del muro de E es campo.");
            Assert.That(e.KindAt(3, 3), Is.EqualTo(CellKind.Obstacle));
            Assert.That(e.FieldCellCount, Is.EqualTo(9 * 7 - 8));
            Assert.That(e.ObstacleGroups.Count, Is.EqualTo(2));
            Assert.That(e.ObstacleGroups.All(g => g.IsLinear), "Las dos mitades del muro son lineales (setos).");

            var f = FieldGrid.FromTrace(Load("Assets/Data/Traces/entrega2/escenario_f__base__seed0.json"));
            Assert.That(f.ObstacleGroups.Count, Is.EqualTo(2));
            Assert.That(f.ObstacleGroups.All(g => !g.IsLinear && g.Cells.Count == 4), "Los bloques de 2×2 de F son arboledas.");
            Assert.That(f.KindAt(-1, 0), Is.EqualTo(CellKind.OutsideField));
        }

        [Test]
        public void GridReadsOptionalCellSizeAndOutsideCells()
        {
            var json = JObject.Parse(File.ReadAllText("Assets/Data/Traces/trace_ejemplo.json"));
            json["mundo"]["tamano_celda_m"] = 9.5;
            json["mundo"]["fuera_de_campo"] = new JArray { new JArray(3, 2) };
            var grid = FieldGrid.FromTrace(new TraceData(json));
            Assert.That(grid.CellSize, Is.EqualTo(9.5f));
            Assert.That(grid.KindAt(3, 2), Is.EqualTo(CellKind.OutsideField));
            Assert.That(grid.FieldCellCount, Is.EqualTo(4 * 2 - 1));
        }

        static AgentState Pose(int x, int y, string o) => new() { X = x, Y = y, Orientation = o, Status = "COSECHANDO" };

        [Test]
        public void MotionMatchesTraceAtTickBoundaries()
        {
            var grid = new FieldGrid(5, 5, 12, Vector2Int.zero, new Dictionary<string, Vector2Int>(), new Vector2Int[0]);
            var cases = new[]
            {
                (Pose(2, 2, "E"), Pose(3, 2, "E")), // recto
                (Pose(2, 2, "N"), Pose(3, 2, "E")), // giro de 90°
                (Pose(2, 2, "S"), Pose(2, 1, "N")), // reversa de 180°
                (Pose(2, 2, "E"), Pose(2, 2, "E")), // quieto
            };
            foreach (var (from, to) in cases)
            {
                var start = VehicleMotion.Sample(grid, from, to, 0);
                var end = VehicleMotion.Sample(grid, from, to, 1);
                Assert.That(start.Position, Is.EqualTo(grid.CellToWorld(from.X, from.Y)));
                Assert.That(start.Heading, Is.EqualTo(FieldGrid.Heading(from.Orientation)));
                Assert.That(end.Position, Is.EqualTo(grid.CellToWorld(to.X, to.Y)));
                Assert.That(end.Heading, Is.EqualTo(FieldGrid.Heading(to.Orientation)));
                // Continuidad: la pose cerca de los extremos se acerca a la del trace.
                Assert.That(Vector3.Distance(VehicleMotion.Sample(grid, from, to, .999f).Position, end.Position), Is.LessThan(.1f));
                Assert.That(Mathf.Abs(Mathf.DeltaAngle(VehicleMotion.Sample(grid, from, to, .999f).Heading, end.Heading)), Is.LessThan(2f));
            }
        }

        [Test]
        public void MotionStaysInsideTheTwoCellsItCrosses()
        {
            var grid = new FieldGrid(5, 5, 12, Vector2Int.zero, new Dictionary<string, Vector2Int>(), new Vector2Int[0]);
            foreach (string before in new[] { "N", "E", "S", "O" })
                foreach (string move in new[] { "N", "E", "S", "O" })
                {
                    var step = FieldGrid.Step(move);
                    var from = Pose(2, 2, before);
                    var to = Pose(2 + step.x, 2 + step.y, move);
                    var a = grid.CellToWorld(from.X, from.Y); var b = grid.CellToWorld(to.X, to.Y);
                    var area = new Bounds((a + b) / 2, new Vector3(Mathf.Abs(a.x - b.x) + 12, 1, Mathf.Abs(a.z - b.z) + 12));
                    for (int i = 0; i <= 50; i++)
                    {
                        var pose = VehicleMotion.Sample(grid, from, to, i / 50f);
                        Assert.That(area.Contains(pose.Position), $"{before}->{move} t={i / 50f} sale de las dos celdas: {pose.Position}");
                    }
                    if (before == Opposite(move))
                        Assert.That(VehicleMotion.Sample(grid, from, to, VehicleMotion.PivotFraction * .5f).Position, Is.EqualTo(a),
                            "En la reversa gira en su celda antes de avanzar.");
                }
        }

        static string Opposite(string o) => o switch { "N" => "S", "S" => "N", "E" => "O", _ => "E" };

        [Test]
        public void TickCuesReadEventsWithoutInferringLogic()
        {
            var events = JArray.Parse(@"[
                {""tipo"":""RECARGA"",""agente"":""carro_1"",""cantidad"":10,""nivel"":50},
                {""tipo"":""RESTRICCION"",""agente"":""carro_1"",""regla"":""R3_celda_ocupada"",""detalle"":""x""},
                {""tipo"":""CELDA_COSECHADA"",""celda"":[3,4],""agente"":""cosechadora_2""},
                {""tipo"":""MENSAJE_ENVIADO"",""id"":1,""tipo_mensaje"":""SOLICITUD_SERVICIO"",""performativa"":""cfp"",""de"":""cosechadora_1"",""a"":""carro_2"",""payload"":{},""entrega_en"":3},
                {""tipo"":""MENSAJE_ENVIADO"",""id"":2,""tipo_mensaje"":""PROPUESTA"",""performativa"":""propose"",""de"":""carro_2"",""a"":""cosechadora_1"",""payload"":{""servicio"":""s"",""costo"":12.5},""entrega_en"":3},
                {""tipo"":""DEPOSITO"",""agente"":""carro_2"",""cantidad"":8,""entregado_total"":8}
            ]");
            var cues = TickCues.From(events);
            Assert.That(cues.AlertText("carro_1"), Is.EqualTo("RECARGA\nRESTRICCION: R3_celda_ocupada"));
            Assert.That(cues.Refueled, Does.Contain("carro_1"));
            Assert.That(cues.Restricted, Does.Contain("carro_1"));
            Assert.That(cues.HarvestedBy[(3, 4)], Is.EqualTo("cosechadora_2"));
            Assert.That(cues.CallsForProposal, Is.EqualTo(new[] { ("cosechadora_1", "carro_2") }));
            Assert.That(cues.Proposals["carro_2"], Is.EqualTo("12.5"));
            Assert.That(cues.Deposits, Does.Contain("carro_2"));
            Assert.That(cues.AlertText("cosechadora_1"), Is.Null);
            Assert.That(TickCues.From(null).Alerts, Is.Empty);
        }

        [Test]
        public void CornMeshesStayWithinTheirRowSegment()
        {
            var set = CornMeshFactory.BuildSet();
            foreach (var mesh in set)
            {
                Assert.That(mesh.vertexCount, Is.GreaterThan(0));
                Assert.That(mesh.bounds.min.y, Is.GreaterThanOrEqualTo(-.2f));
                Assert.That(mesh.bounds.max.y, Is.LessThan(3.5f));
                Assert.That(mesh.bounds.extents.x, Is.LessThan(CornMeshFactory.SegmentLength + 1f), "Las hojas pueden salir del tramo, pero poco.");
                Object.DestroyImmediate(mesh);
            }
            Object.DestroyImmediate(CornMeshFactory.Stubble());
        }
    }
}
