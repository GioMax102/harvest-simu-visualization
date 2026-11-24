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
            var legend = hud.speedDropdown.transform.parent.Find("Legend").GetComponent<TMP_Text>().text;
            Assert.That(legend, Does.Contain("Cosechadora: cabezal"));
            Assert.That(legend, Does.Contain("tractor y remolque"));
            Assert.That(legend, Does.Contain("clic derecho"));
            Assert.That(legend, Does.Contain("Rueda"));
            Assert.That(legend, Does.Contain("Vista cenital"));
        }
    }
}
#endif
