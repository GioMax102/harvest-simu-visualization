using System.IO;
using System.Linq;
using HarvestSim.Visualization;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace HarvestSim.Tests
{
    /// <summary>
    /// H6, descarga en marcha (harvest_sim, docs/resultados_hileras.md): el carro va al lado de la cosechadora en el
    /// estado RECIBIENDO_EN_MARCHA y recibe grano mientras ella sigue cosechando. Unity solo lo reproduce.
    /// </summary>
    public class DescargaEnMarchaTests
    {
        const string G = "Assets/Data/Traces/hileras/escenario_g_en_marcha__anticipada_plus__seed0.json";

        [Test]
        public void TraceWithOnTheGoUnloadingLoadsAndPlays()
        {
            var trace = TraceLoader.Parse(File.ReadAllText(G));
            Assert.That((bool)trace.Json["config"]["politica"]["descarga_en_marcha"], Is.True);
            var playback = new TracePlayback(trace);
            bool seen = false, beside = false;
            for (int t = 1; t <= trace.TotalTicks; t++)
            {
                playback.Seek(t);
                foreach (var pair in playback.State.Agents.Where(p => p.Value.Status == "RECIBIENDO_EN_MARCHA"))
                {
                    seen = true;
                    var cart = pair.Value;
                    // a la altura de una cosechadora, en la fila vecina
                    beside |= playback.State.Agents.Values.Any(h => h != cart && h.X == cart.X && System.Math.Abs(h.Y - cart.Y) == 1);
                }
            }
            Assert.That(seen && beside, Is.True);
            Assert.That(playback.State.Delivered, Is.EqualTo((int)trace.Metrics["grano_total_entregado"]));
            Assert.That((int)trace.Metrics["grano_en_marcha"], Is.GreaterThan(0));
        }

        [Test]
        public void StatusAndLegendDescribeTheNewState()
        {
            Assert.That(AgentView.StatusText("RECIBIENDO_EN_MARCHA"), Is.EqualTo("Recibiendo en marcha"));
            StringAssert.Contains("Recibiendo en marcha", HudFormat.Legend);
        }

        [Test]
        public void UnknownCartStateIsStillRejected()
        {
            var json = JObject.Parse(File.ReadAllText(G));
            var e = json["ticks"].SelectMany(t => t["cambios"])
                .First(c => (string)c["tipo"] == "AGENTE" && ((string)c["id"]).StartsWith("carro") && c["set"]["estado"] != null);
            e["set"]["estado"] = "VOLANDO";
            Assert.Throws<System.FormatException>(() => TraceLoader.Parse(json.ToString()));
        }
    }
}
