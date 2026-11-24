#if UNITY_EDITOR
using System.Collections;
using HarvestSim.Visualization;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace HarvestSim.Tests
{
    public class PlaybackSmokeTests
    {
        [UnityTest]
        public IEnumerator SceneReloadControlsAndClock()
        {
            const string scene = "Assets/Scenes/HarvestPlayback.unity";
            Assert.That(System.IO.File.Exists(scene), Is.True, "Ejecuta Harvest Sim > Crear o abrir escena MVP antes de PlayMode Tests.");
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(scene, new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            var controller = Object.FindFirstObjectByType<PlaybackController>();
            Assert.That(controller.Playback, Is.Not.Null);
            Assert.That(controller.Paused, Is.True); Assert.That(controller.Playback.State.Tick, Is.Zero);
            Assert.That(controller.worldView.agentsRoot.childCount, Is.EqualTo(controller.Trace.Agents.Count));
            controller.StepForward(); Assert.That(controller.Playback.State.Tick, Is.EqualTo(1));
            controller.StepBackward(); Assert.That(controller.Playback.State.Tick, Is.Zero);
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Data/Traces/trace_ejemplo.json");
            controller.LoadTrace(asset); yield return null;
            controller.GoToEnd(); int delivered = controller.Playback.State.Delivered;
            Assert.That(delivered, Is.GreaterThan(0));
            controller.Restart(); Assert.That(controller.Playback.State.Delivered, Is.Zero);
            Assert.That(controller.Playback.State.Harvested.Count, Is.Zero);
            foreach (Transform cell in controller.worldView.cellsRoot)
                if (!cell.name.EndsWith("_0")) Assert.That(cell.GetComponent<Renderer>().sharedMaterial, Is.EqualTo(controller.worldView.cropMaterial));
            foreach (float speed in new[] { 1f, 16f })
            {
                controller.Restart(); controller.SetSpeed(speed); controller.PlayPause();
                controller.Advance((controller.Trace.TotalTicks + 1) / speed);
                Assert.That(controller.Paused, Is.True); Assert.That(controller.Playback.State.Delivered, Is.EqualTo(delivered));
            }
            controller.Restart(); controller.Advance(100); Assert.That(controller.Playback.State.Tick, Is.Zero);
            var invalid = new TextAsset("{}"); controller.LoadTrace(invalid); yield return null;
            Assert.That(controller.Playback, Is.Null); Assert.That(controller.worldView.agentsRoot.childCount, Is.Zero);
            Assert.That(controller.hud.errorText.gameObject.activeSelf, Is.True);
            Assert.That(controller.hud.playButton.interactable, Is.False);
            controller.LoadTrace(asset); yield return null;
            Assert.That(controller.worldView.agentsRoot.childCount, Is.EqualTo(2));
            Assert.That(controller.hud.errorText.gameObject.activeSelf, Is.False);
            controller.LoadTrace(asset); yield return null;
            Assert.That(controller.worldView.agentsRoot.childCount, Is.EqualTo(2));
            Object.Destroy(invalid);
        }

        [UnityTest]
        public IEnumerator LinesSupportAgentIdsContainingColon()
        {
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Scenes/HarvestPlayback.unity", new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            var controller = Object.FindFirstObjectByType<PlaybackController>();
            var original = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Data/Traces/trace_ejemplo.json");
            var trace = new TextAsset(original.text.Replace("cosechadora_1", "cosechadora:1").Replace("carro_1", "carro:1"));
            controller.LoadTrace(trace);
            Assert.That(controller.Playback, Is.Not.Null);
            var state = controller.Playback.State;
            state.Winners.Add("cosechadora:1", "carro:1");
            controller.worldView.Render(state);
            Assert.That(controller.worldView.structuresRoot.GetComponentsInChildren<LineRenderer>().Length, Is.EqualTo(1));
            Assert.DoesNotThrow(() => controller.worldView.Interpolate(state, state, .5f));
            Object.Destroy(trace);
        }

        [UnityTest]
        public IEnumerator ScenarioRefreshKeepsLocalTraceAfterServerFailure()
        {
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Scenes/HarvestPlayback.unity", new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            var controller = Object.FindFirstObjectByType<PlaybackController>();
            var client = controller.GetComponent<TraceClient>();
            float deadline = Time.realtimeSinceStartup + 12;
            while (!controller.hud.refreshScenariosButton.interactable && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(controller.hud.refreshScenariosButton.interactable, Is.True);
            var remoteJson = JObject.Parse(controller.initialTrace.text);
            remoteJson["meta"]["escenario"] = "escenario_remoto";
            controller.LoadTraceJson(remoteJson.ToString(), "remoto");
            Assert.That(controller.Trace.Scenario, Is.EqualTo("escenario_remoto"));
            client.baseUrl = "http://127.0.0.1:1";
            controller.hud.refreshScenariosButton.onClick.Invoke();
            Assert.That(controller.hud.refreshScenariosButton.interactable, Is.False);
            Assert.That(controller.hud.simulateButton.interactable, Is.False);
            client.RefreshScenarios();
            deadline = Time.realtimeSinceStartup + 12;
            while (!controller.hud.refreshScenariosButton.interactable && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(controller.hud.refreshScenariosButton.interactable, Is.True);
            Assert.That(controller.hud.simulateButton.interactable, Is.True);
            Assert.That(controller.hud.errorText.gameObject.activeSelf, Is.True);
            Assert.That(controller.Playback, Is.Not.Null);
            Assert.That(controller.Trace.Scenario, Is.EqualTo((string)JObject.Parse(controller.initialTrace.text)["meta"]["escenario"]));
            Assert.That(controller.hud.errorText.text, Does.Contain("trace local cargado"));
        }

        [UnityTest]
        public IEnumerator AgentDisplaysAllAlertsInSameTick()
        {
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Scenes/HarvestPlayback.unity", new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            var controller = Object.FindFirstObjectByType<PlaybackController>();
            var state = controller.Playback.State;
            state.Events.Add(new JObject { ["tipo"] = "RECARGA", ["agente"] = "carro_1" });
            state.Events.Add(new JObject { ["tipo"] = "RESTRICCION", ["agente"] = "carro_1", ["regla"] = "reserva" });
            state.Events.Add(new JObject { ["tipo"] = "RESTRICCION", ["agente"] = "carro_1", ["regla"] = "pasillo" });
            controller.worldView.Render(state);
            var label = controller.worldView.agentsRoot.Find("carro_1/AgentLabel").GetComponent<TextMesh>();
            Assert.That(label.text, Does.Contain("RECARGA"));
            Assert.That(label.text, Does.Contain("RESTRICCION: reserva"));
            Assert.That(label.text, Does.Contain("RESTRICCION: pasillo"));
        }

        [UnityTest]
        public IEnumerator AdjacentAgentLabelsStaySeparateAndOffTheirBodies()
        {
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Scenes/HarvestPlayback.unity", new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            var controller = Object.FindFirstObjectByType<PlaybackController>();
            controller.LoadTrace(AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Data/Traces/trace_ejemplo.json"));
            yield return null;
            var cart = controller.worldView.agentsRoot.Find("carro_1");
            var harvester = controller.worldView.agentsRoot.Find("cosechadora_1");
            var cartLabel = cart.Find("AgentLabel").GetComponent<TextMesh>();
            var harvesterLabel = harvester.Find("AgentLabel").GetComponent<TextMesh>();
            Assert.That(cartLabel.text, Is.EqualTo("carro_1"));
            Assert.That(harvesterLabel.text, Is.EqualTo("cosechadora_1"));
            Assert.That(cartLabel.GetComponent<Renderer>().bounds.max.x,
                Is.LessThan(harvesterLabel.GetComponent<Renderer>().bounds.min.x));
            foreach (var agent in new[] { cart, harvester })
                Assert.That(agent.Find("AgentLabel").GetComponent<Renderer>().bounds.min.z,
                    Is.GreaterThan(agent.Find("Body").GetComponent<Renderer>().bounds.max.z));
            controller.worldView.Interpolate(controller.Playback.State, controller.Playback.State, .5f);
            Assert.That(cartLabel.transform.position.z - cart.position.z, Is.EqualTo(.38f).Within(.001f));
            Assert.That(harvesterLabel.transform.position.z - harvester.position.z, Is.EqualTo(.38f).Within(.001f));
        }

        [UnityTest]
        public IEnumerator InvalidServerUrlDoesNotLockControls()
        {
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Scenes/HarvestPlayback.unity", new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            var controller = Object.FindFirstObjectByType<PlaybackController>();
            var client = controller.GetComponent<TraceClient>();
            float deadline = Time.realtimeSinceStartup + 12;
            while (!controller.hud.refreshScenariosButton.interactable && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(controller.hud.refreshScenariosButton.interactable, Is.True);

            client.baseUrl = "http://[invalid";
            client.RefreshScenarios();
            yield return null;
            deadline = Time.realtimeSinceStartup + 12;
            while (!controller.hud.refreshScenariosButton.interactable && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(controller.hud.refreshScenariosButton.interactable, Is.True);
            Assert.That(controller.hud.simulateButton.interactable, Is.True);
            Assert.That(controller.hud.errorText.gameObject.activeSelf, Is.True);
            Assert.That(controller.Playback, Is.Not.Null);

            client.Simulate();
            yield return null;
            deadline = Time.realtimeSinceStartup + 12;
            while (!controller.hud.simulateButton.interactable && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(controller.hud.refreshScenariosButton.interactable, Is.True);
            Assert.That(controller.hud.simulateButton.interactable, Is.True);
            Assert.That(controller.hud.errorText.text, Does.Contain("No se pudo simular"));
            Assert.That(controller.Playback, Is.Not.Null);
        }
    }
}

#endif
