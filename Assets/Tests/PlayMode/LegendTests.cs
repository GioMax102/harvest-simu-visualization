#if UNITY_EDITOR
using System.Collections;
using HarvestSim.Visualization;
using NUnit.Framework;
using TMPro;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace HarvestSim.Tests
{
    public class LegendTests
    {
        [UnityTest]
        public IEnumerator ExistingSceneShowsCurrentVehicleLegendAndCameraControls()
        {
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Scenes/HarvestPlayback.unity", new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            var hud = Object.FindFirstObjectByType<SimulationHUD>();
            var legend = hud.legendText.text;
            Assert.That(legend, Does.Contain("cabezal maicero"));
            Assert.That(legend, Does.Contain("tractor con carro tolva"));
            Assert.That(legend, Does.Contain("Clic derecho"));
            Assert.That(legend, Does.Contain("rueda"));
            Assert.That(legend, Does.Contain("Vista cenital"));
            Assert.That(legend, Does.Contain("cfp"));
        }
    }
}
#endif
