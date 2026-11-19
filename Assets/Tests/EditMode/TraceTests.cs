using System;
using System.IO;
using System.Linq;
using HarvestSim.Visualization;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace HarvestSim.Tests
{
    public class TraceTests
    {
        static JObject Fixture() => JObject.Parse(File.ReadAllText("Assets/Data/Traces/trace_ejemplo.json"));
        static TraceData Load(string name = "trace_ejemplo") => TraceLoader.Parse(File.ReadAllText("Assets/Data/Traces/" + name + ".json"));
        static void Same(PlaybackState actual, JToken expected)
        {
            Assert.That(actual.Tick, Is.EqualTo((int)expected["tick"]));
            Assert.That(actual.Delivered, Is.EqualTo((int)expected["entregado_total"]));
            Assert.That(actual.Agents.Count, Is.EqualTo(((JObject)expected["agentes"]).Count));
            foreach (var a in actual.Agents)
            {
                var e = expected["agentes"][a.Key];
                Assert.That(a.Value.X, Is.EqualTo((int)e["pose"]["x"]), a.Key + " x");
                Assert.That(a.Value.Y, Is.EqualTo((int)e["pose"]["y"]), a.Key + " y");
                Assert.That(a.Value.Orientation, Is.EqualTo((string)e["pose"]["orientacion"]));
                Assert.That(a.Value.Status, Is.EqualTo((string)e["estado"]));
                Assert.That(a.Value.Load, Is.EqualTo((int)e["carga"]));
            }
            CollectionAssert.AreEquivalent(((JArray)expected["celdas_cosechadas"]).Select(c => ((int)c[0], (int)c[1])), actual.Harvested);
            CollectionAssert.AreEquivalent(((JArray)expected["mensajes_en_transito"]).Select(v => (int)v), actual.Messages.Keys);
            Assert.That(JToken.DeepEquals(actual.Events, expected["eventos"]), Is.True, "eventos del tick " + actual.Tick);
        }

        [TestCase("trace_ejemplo")]
        [TestCase("escenario_a")]
        [TestCase("escenario_b")]
        [TestCase("escenario_c")]
        public void EveryTickAndBackwardSeekMatchPython(string name)
        {
            var expected = (JArray)JObject.Parse(File.ReadAllText("Assets/Tests/EditMode/Fixtures/expected_states.json"))[name];
            var trace = Load(name); var playback = new TracePlayback(trace);
            for (int tick = 0; tick <= trace.TotalTicks; tick++) { playback.Seek(tick); Same(playback.State, expected[tick]); }
            foreach (int tick in new[] { trace.TotalTicks / 2, 0, trace.TotalTicks, 1, 0 })
            { playback.Seek(tick); Same(playback.State, expected[tick]); }
            playback.Reset(); Same(playback.State, expected[0]);
        }

        [Test]
        public void EmptyTicksPreserveStateAndExplicitZeroReplacesLoad()
        {
            var playback = new TracePlayback(Load());
            playback.Seek(11); var load = playback.State.Agents["cosechadora_1"].Load;
            playback.Seek(12); Assert.That(playback.State.Events.Count, Is.Zero);
            Assert.That(playback.State.Agents["cosechadora_1"].Load, Is.EqualTo(load));
            playback.Seek(13); Assert.That(playback.State.Events.Count, Is.Zero);
            playback.Seek(14); Assert.That(playback.State.Agents["cosechadora_1"].Load, Is.Zero);
            Assert.That(playback.State.Messages.Count, Is.EqualTo(1));
            var x = playback.State.Agents["cosechadora_1"].X;
            playback.Seek(15); Assert.That(playback.State.Messages.Count, Is.Zero);
            Assert.That(playback.State.Agents["cosechadora_1"].X, Is.EqualTo(x));
        }
        [Test]
        public void MissingFinalEntriesAndZeroDurationAreSupported()
        {
            var json = Fixture(); json["meta"]["total_ticks"] = 60;
            var playback = new TracePlayback(TraceLoader.Parse(json.ToString()));
            playback.Seek(100); Assert.That(playback.State.Tick, Is.EqualTo(60)); Assert.That(playback.State.Events.Count, Is.Zero);
            playback.Step(int.MinValue); Assert.That(playback.State.Tick, Is.Zero);
            json["meta"]["total_ticks"] = 0; json["ticks"] = new JArray(json["ticks"][0].DeepClone());
            playback = new TracePlayback(TraceLoader.Parse(json.ToString()));
            playback.Step(1); Assert.That(playback.State.Tick, Is.Zero); Assert.That(playback.State.Agents.Count, Is.EqualTo(2));
        }
        [TestCase("schema")]
        [TestCase("initial")]
        [TestCase("duplicateTick")]
        [TestCase("unorderedTick")]
        [TestCase("coordinate")]
        [TestCase("load")]
        [TestCase("agent")]
        [TestCase("event")]
        [TestCase("orientation")]
        [TestCase("missingField")]
        [TestCase("messageRecipient")]
        [TestCase("booleanInteger")]
        [TestCase("largeInteger")]
        public void MalformedContractsFailBeforeSceneCreation(string mutation)
        {
            var json = Fixture();
            switch (mutation)
            {
                case "schema": json["schema_version"] = "3"; break;
                case "initial": ((JArray)json["ticks"]).RemoveAt(0); break;
                case "duplicateTick": json["ticks"][1]["tick"] = 0; break;
                case "unorderedTick": json["ticks"][2]["tick"] = 1; break;
                case "coordinate": json["ticks"][0]["cambios"][0]["set"]["pose"]["x"] = -1; break;
                case "load": json["ticks"][0]["cambios"][0]["set"]["carga"] = 500; break;
                case "agent": json["ticks"][0]["cambios"][0]["id"] = "fantasma"; break;
                case "event": json["ticks"][0]["cambios"][0]["tipo"] = "NUEVO"; break;
                case "orientation": json["ticks"][0]["cambios"][0]["set"]["pose"]["orientacion"] = "W"; break;
                case "missingField": ((JObject)json["ticks"][0]["cambios"][0]["set"]).Remove("carga"); break;
                case "messageRecipient": json["ticks"][7]["cambios"][0]["a"] = "cosechadora_1"; break;
                case "booleanInteger": json["meta"]["total_ticks"] = true; break;
                case "largeInteger": json["meta"]["total_ticks"] = long.MaxValue; break;
            }
            Assert.Throws<FormatException>(() => TraceLoader.Parse(json.ToString()));
        }
        [Test]
        public void InvalidJsonAndDuplicateKeysAreRejected()
        {
            Assert.Throws<FormatException>(() => TraceLoader.Parse("{"));
            Assert.Throws<FormatException>(() => TraceLoader.Parse("{\"schema_version\":\"2.1\",\"schema_version\":\"2.1\"}"));
        }
    }
}
