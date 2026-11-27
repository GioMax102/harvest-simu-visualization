#if UNITY_EDITOR
using System.Collections;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
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
        public IEnumerator PerspectiveFramesFieldAtDifferentAspectRatiosAndEnablesShadows()
        {
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Scenes/HarvestPlayback.unity", new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            var controller = Object.FindFirstObjectByType<PlaybackController>();
            var world = controller.worldView;
            var camera = world.mapCamera;
            world.SetPerspective();
            foreach (float aspect in new[] { .4f, 1f, 2f })
            {
                camera.aspect = aspect;
                world.FrameCamera(camera);
                foreach (int x in new[] { 0, controller.Trace.Width - 1 })
                    foreach (int y in new[] { 0, controller.Trace.Height - 1 })
                    {
                        var point = camera.WorldToViewportPoint(world.CellToWorld(x, y, 0));
                        Assert.That(point.z, Is.GreaterThan(0));
                        Assert.That(point.x, Is.InRange(0f, 1f));
                        Assert.That(point.y, Is.InRange(0f, 1f));
                    }
            }
            var sun = Object.FindFirstObjectByType<Light>();
            Assert.That(sun, Is.Not.Null);
            Assert.That(sun.shadows, Is.Not.EqualTo(LightShadows.None));
            camera.ResetAspect();
            world.ResetCamera();
            Assert.That(camera.orthographic, Is.True);
        }

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
            Assert.That(controller.worldView.Corn.StandingCellCount, Is.EqualTo(controller.worldView.Grid.FieldCellCount));
            Assert.That(controller.worldView.Ground.StubbleCellCount, Is.Zero);
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
            var label = controller.worldView.agentsRoot.Find("carro_1/AgentLabel").GetComponent<TMPro.TMP_Text>();
            Assert.That(label.text, Does.Contain("RECARGA"));
            Assert.That(label.text, Does.Contain("RESTRICCION: reserva"));
            Assert.That(label.text, Does.Contain("RESTRICCION: pasillo"));
        }

        [UnityTest]
        public IEnumerator AgentLabelsFloatAboveVehiclesAndFaceTheCamera()
        {
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Scenes/HarvestPlayback.unity", new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            var controller = Object.FindFirstObjectByType<PlaybackController>();
            controller.LoadTrace(AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Data/Traces/trace_ejemplo.json"));
            yield return null;
            var camera = controller.worldView.mapCamera;
            foreach (var id in new[] { "carro_1", "cosechadora_1" })
            {
                var agent = controller.worldView.agentsRoot.Find(id);
                var label = agent.Find("AgentLabel").GetComponent<TMPro.TMP_Text>();
                Assert.That(label.GetParsedText(), Does.StartWith(id));
                Assert.That(label.transform.position.y, Is.GreaterThan(agent.Find("Body").GetComponent<Renderer>().bounds.max.y));
                Assert.That(Quaternion.Angle(label.transform.rotation, camera.transform.rotation), Is.LessThan(1f));
            }
            controller.worldView.Interpolate(controller.Playback.State, controller.Playback.State, .5f);
            var cart = controller.worldView.agentsRoot.Find("carro_1");
            Assert.That(cart.Find("AgentLabel").position.y - cart.position.y, Is.GreaterThanOrEqualTo(AgentView.LabelHeight - .001f), "La placa puede subir para no encimarse, nunca bajar.");
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

        [UnityTest]
        public IEnumerator InvalidServerTracePreservesRemoteAndLocalErrors()
        {
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Scenes/HarvestPlayback.unity", new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            var controller = Object.FindFirstObjectByType<PlaybackController>();
            var client = controller.GetComponent<TraceClient>();
            float deadline = Time.realtimeSinceStartup + 12;
            while (!controller.hud.simulateButton.interactable && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(controller.hud.simulateButton.interactable, Is.True);

            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            client.baseUrl = "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port;
            var responses = Task.Run(() =>
            {
                for (int i = 0; i < 2; i++)
                {
                    using var connection = listener.AcceptTcpClient();
                    using var stream = connection.GetStream();
                    using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true);
                    while (!string.IsNullOrEmpty(reader.ReadLine())) { }
                    byte[] body = Encoding.UTF8.GetBytes("{\"schema_version\":\"3\"}");
                    byte[] headers = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: " + body.Length + "\r\nConnection: close\r\n\r\n");
                    stream.Write(headers, 0, headers.Length);
                    stream.Write(body, 0, body.Length);
                }
            });
            var original = controller.initialTrace;
            var invalid = new TextAsset("{}");
            try
            {
                for (int i = 0; i < 2; i++)
                {
                    if (i == 1) controller.initialTrace = invalid;
                    client.Simulate();
                    deadline = Time.realtimeSinceStartup + 12;
                    while (!controller.hud.simulateButton.interactable && Time.realtimeSinceStartup < deadline) yield return null;
                    Assert.That(controller.hud.simulateButton.interactable, Is.True);
                    Assert.That(controller.hud.errorText.text, Does.Contain("solo se admite 2.1, 2.2, 2.3 o 2.4"));
                    if (i == 0)
                    {
                        Assert.That(controller.Playback, Is.Not.Null);
                        Assert.That(controller.hud.errorText.text, Does.Contain("trace local cargado"));
                    }
                    else
                    {
                        Assert.That(controller.Playback, Is.Null);
                        Assert.That(controller.hud.errorText.text, Does.Contain("no se pudo cargar el trace local"));
                        Assert.That(controller.hud.errorText.text, Does.Contain("se requiere String"));
                    }
                }
                Assert.That(responses.IsFaulted, Is.False);
            }
            finally
            {
                listener.Stop();
                controller.initialTrace = original;
                Object.Destroy(invalid);
            }
        }
    }
}

#endif
