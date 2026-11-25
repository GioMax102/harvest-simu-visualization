#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using HarvestSim.Visualization;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace HarvestSim.Tests
{
    public class VehiclePrefabsTests
    {
        [UnityTest]
        public IEnumerator RealTracesRenderEachAgentWithItsVehiclePrefab()
        {
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Scenes/HarvestPlayback.unity", new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            var controller = Object.FindFirstObjectByType<PlaybackController>();
            foreach (string scenario in new[] { "d", "e", "f" })
            {
                var asset = AssetDatabase.LoadAssetAtPath<TextAsset>($"Assets/Data/Traces/entrega2/escenario_{scenario}__base__seed0.json");
                Assert.That(asset, Is.Not.Null);
                controller.LoadTrace(asset);
                yield return null;
                Assert.That(controller.Playback, Is.Not.Null, controller.hud.errorText.text);
                Assert.That(controller.Trace.Scenario, Is.EqualTo("escenario_" + scenario));
                Assert.That(controller.worldView.agentsRoot.childCount, Is.EqualTo(controller.Trace.Agents.Count));
                Assert.That(controller.Trace.Agents.Values.Select(agent => agent.Type), Does.Contain("cosechadora"));
                Assert.That(controller.Trace.Agents.Values.Select(agent => agent.Type), Does.Contain("carro"));
                foreach (var agent in controller.Trace.Agents.Values)
                {
                    var view = controller.worldView.agentsRoot.Find(agent.Id);
                    Assert.That(view, Is.Not.Null);
                    var expected = agent.Type == "carro" ? controller.worldView.cartPrefab : controller.worldView.harvesterPrefab;
                    Assert.That(view.Find("Body").GetComponent<MeshFilter>().sharedMesh,
                        Is.EqualTo(expected.transform.Find("Body").GetComponent<MeshFilter>().sharedMesh));
                }
                controller.GoToEnd();
                Assert.That(controller.Playback.State.Tick, Is.EqualTo(controller.Trace.TotalTicks));
                Assert.That(controller.Playback.State.Harvested.Count, Is.GreaterThan(0));
            }
        }

        [UnityTest]
        public IEnumerator AgentsUseDetailedMeshesAndKeepTheirColorsWithinOneCell()
        {
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Scenes/HarvestPlayback.unity", new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            var controller = Object.FindFirstObjectByType<PlaybackController>();
            Assert.That(controller.Playback, Is.Not.Null);
            foreach (Transform agent in controller.worldView.agentsRoot)
            {
                var body = agent.Find("Body");
                Assert.That(body, Is.Not.Null);
                var mesh = body.GetComponent<MeshFilter>().sharedMesh;
                var renderer = body.GetComponent<MeshRenderer>();
                Assert.That(mesh.vertexCount, Is.GreaterThan(1000));
                Assert.That(renderer.sharedMaterials.Select(m => m.name), Does.Contain("HarvestGreen"));
                Assert.That(renderer.sharedMaterials.Select(m => m.name), Does.Contain("HarvestYellow"));
                Assert.That(renderer.bounds.size.x, Is.LessThanOrEqualTo(1f));
                Assert.That(renderer.bounds.size.z, Is.LessThanOrEqualTo(1f));
                Assert.That(renderer.bounds.min.y, Is.GreaterThanOrEqualTo(-.02f));
                Assert.That(agent.GetComponentInChildren<Collider>(), Is.Null);
            }
        }
    }
}
#endif
