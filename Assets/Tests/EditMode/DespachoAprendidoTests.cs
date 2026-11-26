using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarvestSim.Visualization;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace HarvestSim.Tests
{
    /// <summary>
    /// M1, despacho aprendido (harvest_sim, docs/especificacion_mejoras.md §M1): el trace trae cada decisión de la
    /// tabla Q (evento DECISION_DESPACHO) y Unity solo la muestra: placa en el tick en que pide, tarjeta con la última
    /// decisión, métricas y estrategias del servidor.
    /// </summary>
    public class DespachoAprendidoTests
    {
        const string Dir = "Assets/Data/Traces/mejoras/";
        static TraceData Load(string name) => TraceLoader.Parse(File.ReadAllText(Dir + name + ".json"));
        static JObject Json(string name) => JObject.Parse(File.ReadAllText(Dir + name + ".json"));

        static IEnumerable<(int tick, JObject e)> Decisions(TraceData trace) =>
            trace.Changes.OrderBy(p => p.Key).SelectMany(p => p.Value.OfType<JObject>()
                .Where(e => (string)e["tipo"] == "DECISION_DESPACHO").Select(e => (p.Key, e)));

        [TestCase("escenario_d__qlearning__seed0")]
        [TestCase("escenario_d__sarsa__seed0")]
        [TestCase("escenario_g__qlearning__seed0")]
        [TestCase("escenario_g__sarsa__seed0")]
        public void LearnedTracesLoadAndPlayToTheEnd(string name)
        {
            var trace = Load(name);
            Assert.That(Decisions(trace).Count(), Is.EqualTo((int)trace.Metrics["decisiones_despacho"]));
            var playback = new TracePlayback(trace);
            playback.Seek(trace.TotalTicks);
            Assert.That(playback.State.Delivered, Is.EqualTo((int)trace.Metrics["grano_total_entregado"]));
            Assert.That(playback.State.Decisions.Keys, Is.EquivalentTo(trace.Agents.Values.Where(a => a.Type == "cosechadora").Select(a => a.Id)));
        }

        [Test]
        public void FixedStrategyTracesHaveNoDecisions()
        {
            var trace = Load("escenario_g__anticipada__seed0");
            Assert.That(Decisions(trace), Is.Empty);
            Assert.That(trace.Metrics["decisiones_despacho"], Is.Null);
        }

        [Test]
        public void LastDecisionPersistsAndRewinds()
        {
            var trace = Load("escenario_g__sarsa__seed0");
            var all = Decisions(trace).ToList();
            var (first, firstEvent) = all[0];
            var playback = new TracePlayback(trace);
            playback.Seek(first - 1);
            Assert.That(playback.State.Decisions, Is.Empty, "antes de la primera decisión no hay nada");
            playback.Seek(first + 5);
            string agent = (string)firstEvent["agente"];
            var shown = playback.State.Decisions[agent];
            Assert.That(JToken.DeepEquals(shown, all.Last(d => d.tick <= first + 5 && (string)d.e["agente"] == agent).e), Is.True);
            playback.Seek(first - 1);
            Assert.That(playback.State.Decisions, Is.Empty, "al retroceder se reconstruye desde 0");
        }

        [Test]
        public void PlateShowsOnlyTheTickWhenItCalls()
        {
            var trace = Load("escenario_g__sarsa__seed0");
            var (tick, call) = Decisions(trace).First(d => (string)d.e["accion"] == "pedir" && (string)d.e["origen"] == "tabla");
            var cues = TickCues.From(trace.Changes[tick]);
            string agent = (string)call["agente"];
            Assert.That(HudFormat.DecisionCue(cues.Decisions[agent]), Is.EqualTo("Q: pide carro"));
            Assert.That(cues.CallsForProposal.Any(c => c.from == agent), Is.True, "la llamada sale en el mismo tick");
            var (keepTick, keep) = Decisions(trace).First(d => (string)d.e["accion"] == "seguir");
            Assert.That(HudFormat.DecisionCue(TickCues.From(trace.Changes[keepTick]).Decisions[(string)keep["agente"]]), Is.Null);
            var forced = Decisions(trace).First(d => (string)d.e["origen"] == "forzada").e;
            Assert.That(HudFormat.DecisionCue(forced), Is.EqualTo("Tolva llena: pide carro"));
        }

        [Test]
        public void CardTextExplainsTheDecision()
        {
            var decision = JObject.Parse("{'tipo':'DECISION_DESPACHO','agente':'cosechadora_1','estado':{'tramo':2,'carros_libres':1,'distancia':1,'otra_pidio':true},'accion':'pedir','origen':'tabla','q':{'seguir':-312.4,'pedir':-298.6}}");
            string text = System.Text.RegularExpressions.Regex.Replace(HudFormat.DecisionText(decision), "<[^>]+>", "");
            Assert.That(text, Is.EqualTo("Q · tolva 40–60 % · 1 libre · otra pidió · seguir −312 / pedir −299 → pide"));
            decision["origen"] = "umbral"; decision["accion"] = "seguir"; decision.Remove("q");
            text = System.Text.RegularExpressions.Regex.Replace(HudFormat.DecisionText(decision), "<[^>]+>", "");
            Assert.That(text, Is.EqualTo("Umbral · tolva 40–60 % · 1 libre · otra pidió → sigue"));
            Assert.That(HudFormat.DecisionText(null), Is.Null);
        }

        [Test]
        public void MetricsPanelShowsLearningRows()
        {
            var trace = Load("escenario_g__sarsa__seed0");
            string text = HudFormat.Metrics(trace.Metrics);
            StringAssert.Contains("Decisiones Q", text);
            StringAssert.Contains("Tolva llena sin carro", text);
            StringAssert.DoesNotContain("Decisiones Q", HudFormat.Metrics(Load("escenario_g__anticipada__seed0").Metrics));
        }

        [TestCase("accion", "esperar", "accion debe ser")]
        [TestCase("origen", "intuicion", "origen desconocido")]
        [TestCase("agente", "carro_1", "solo una cosechadora")]
        public void MalformedDecisionIsRejected(string field, string value, string message)
        {
            var json = Json("escenario_d__sarsa__seed0");
            var e = json["ticks"].SelectMany(t => t["cambios"]).First(c => (string)c["tipo"] == "DECISION_DESPACHO");
            e[field] = value;
            var error = Assert.Throws<System.FormatException>(() => TraceLoader.Parse(json.ToString()));
            StringAssert.Contains(message, error.Message);
        }

        [Test]
        public void ForcedDecisionMustBeAFullHopperCall()
        {
            var json = Json("escenario_d__sarsa__seed0");
            var e = json["ticks"].SelectMany(t => t["cambios"]).First(c => (string)c["tipo"] == "DECISION_DESPACHO");
            e["origen"] = "forzada";
            Assert.Throws<System.FormatException>(() => TraceLoader.Parse(json.ToString()));
        }

        [Test]
        public void ServerListsStrategiesPerScenario()
        {
            var response = JToken.Parse("{'escenarios':[{'nombre':'escenario_a','estrategias':['base','subasta']},{'nombre':'escenario_g','estrategias':['base','qlearning','sarsa']},{'nombre':'escenario_x'}],'estrategias':['base','subasta','qlearning']}");
            var (names, strategies) = TraceClient.ParseScenarios(response);
            Assert.That(names, Is.EqualTo(new[] { "escenario_a", "escenario_g", "escenario_x" }));
            Assert.That(strategies["escenario_g"], Is.EqualTo(new[] { "base", "qlearning", "sarsa" }));
            Assert.That(strategies["escenario_x"], Is.EqualTo(new[] { "base", "subasta", "qlearning" }), "sin lista propia usa la general");
            var (old, oldStrategies) = TraceClient.ParseScenarios(JToken.Parse("['escenario_a']"));
            Assert.That(old, Is.EqualTo(new[] { "escenario_a" }));
            Assert.That(oldStrategies, Is.Empty);
        }
    }
}
