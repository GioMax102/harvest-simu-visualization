using System.IO;
using HarvestSim.Visualization;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace HarvestSim.Tests
{
    /// <summary>Textos del HUD: eventos legibles, métricas del archivo y subtítulo, sin inventar datos.</summary>
    public class HudFormatTests
    {
        [Test]
        public void EventsReadAsSentencesWithPerformatives()
        {
            var proposal = JObject.Parse(@"{""tipo"":""MENSAJE_ENVIADO"",""id"":4,""tipo_mensaje"":""PROPUESTA"",""performativa"":""propose"",
                ""de"":""carro_2"",""a"":""cosechadora_1"",""payload"":{""servicio"":""s"",""costo"":12.5},""entrega_en"":9}");
            string line = HudFormat.Event(proposal);
            Assert.That(line, Does.Contain("carro_2 → cosechadora_1"));
            Assert.That(line, Does.Contain("PROPUESTA"));
            Assert.That(line, Does.Contain("(propose)"));
            Assert.That(line, Does.Contain("costo 12.5"));

            Assert.That(HudFormat.Event(JObject.Parse(@"{""tipo"":""DEPOSITO"",""agente"":""carro_1"",""cantidad"":8,""entregado_total"":24}")),
                Does.Contain("depositó 8").And.Contain("total 24"));
            Assert.That(HudFormat.Event(JObject.Parse(@"{""tipo"":""RESTRICCION"",""agente"":""carro_1"",""regla"":""R4_intercambio"",""detalle"":""x""}")),
                Does.Contain("R4_intercambio"));
            Assert.That(HudFormat.Event(JObject.Parse(@"{""tipo"":""AGENTE"",""id"":""carro_1"",""set"":{""pose"":{""x"":1,""y"":0,""orientacion"":""N""}}}")),
                Is.Null, "Un cambio de pose solo no ensucia el registro.");
            Assert.That(HudFormat.Event(JObject.Parse(@"{""tipo"":""AGENTE"",""id"":""carro_1"",""set"":{""estado"":""DESCARGANDO""}}")),
                Does.Contain("Descargando"));
        }

        [Test]
        public void RecentEventsAreNewestFirstAndBounded()
        {
            var trace = TraceLoader.Parse(File.ReadAllText("Assets/Data/Traces/entrega2/escenario_f__subasta__seed0.json"));
            string log = HudFormat.RecentEvents(trace, 200, maxLines: 6);
            int lines = 0;
            foreach (var line in log.Split('\n')) if (line.StartsWith("  ")) lines++;
            Assert.That(lines, Is.InRange(1, 6));
            int first = log.IndexOf("t2"), last = log.LastIndexOf("t1");
            Assert.That(first, Is.GreaterThanOrEqualTo(0));
            Assert.That(HudFormat.RecentEvents(trace, 0, ticksBack: 1), Is.Not.Empty);
        }

        [Test]
        public void MetricsAndSubtitleComeFromTheFile()
        {
            var trace = TraceLoader.Parse(File.ReadAllText("Assets/Data/Traces/entrega2/escenario_f__subasta__seed0.json"));
            string metrics = HudFormat.Metrics(trace.Metrics);
            Assert.That(metrics, Does.Contain("Ticks totales"));
            Assert.That(metrics, Does.Contain(trace.Metrics["tiempo_completacion"].ToString()));
            Assert.That(metrics, Does.Contain("Costo J"));
            Assert.That(HudFormat.Metrics(new JObject()), Does.Contain("no trae métricas"));
            Assert.That(HudFormat.Subtitle(trace), Is.EqualTo("escenario_f · subasta · semilla 0"));
            Assert.That(HudFormat.Subtitle(trace, "escenario_f__anticipada_plus__seed0"), Is.EqualTo("escenario_f · anticipada_plus · semilla 0"));
            Assert.That(HudFormat.StrategyFromLabel("trace_ejemplo"), Is.Null);
            Assert.That(HudFormat.Legend, Does.Contain("cfp").And.Contain("Vista cenital"));
        }
    }
}
