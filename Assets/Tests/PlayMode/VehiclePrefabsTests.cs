#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using HarvestSim.Visualization;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace HarvestSim.Tests
{
    public class VehiclePrefabsTests
    {
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
