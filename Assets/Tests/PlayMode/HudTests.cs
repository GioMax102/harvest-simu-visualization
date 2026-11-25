#if UNITY_EDITOR
using System.Collections;
using HarvestSim.Visualization;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace HarvestSim.Tests
{
    public class HudTests
    {
        static IEnumerator LoadScene()
        {
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/Scenes/HarvestPlayback.unity", new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
        }

        [UnityTest]
        public IEnumerator CardsTimelineAndFollowReflectTheTrace()
        {
            yield return LoadScene();
            var controller = Object.FindFirstObjectByType<PlaybackController>();
            var hud = controller.hud;
            controller.LoadTrace(AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Data/Traces/entrega2/escenario_f__subasta__seed0.json"));
            yield return null;
            Assert.That(controller.Playback, Is.Not.Null, hud.errorText.text);
            Assert.That(hud.Cards.Count, Is.EqualTo(controller.Trace.Agents.Count));
            Assert.That(hud.timeline.maxValue, Is.EqualTo(controller.Trace.TotalTicks));
            Assert.That(hud.timelineMarks.texture, Is.Not.Null);

            // Arrastrar la línea de tiempo salta al tick y pausa.
            controller.SetSpeed(8); controller.PlayPause();
            hud.timeline.value = 50;
            Assert.That(controller.Playback.State.Tick, Is.EqualTo(50));
            Assert.That(controller.Paused, Is.True);
            Assert.That(hud.tickText.text, Does.Contain("50"));

            // La tarjeta muestra el estado del trace y al hacer clic la cámara sigue al agente.
            var card = hud.Cards[0];
            var state = controller.Playback.State.Agents[card.AgentId];
            Assert.That(card.state.text, Does.Contain(AgentView.StatusText(state.Status)));
            card.button.onClick.Invoke();
            var view = controller.worldView.Agents[card.AgentId];
            Assert.That(controller.worldView.Rig.Mode, Is.EqualTo(CameraMode.Follow));
            Assert.That(controller.worldView.Rig.FollowTarget, Is.EqualTo(view.transform));
            Assert.That(card.highlight.enabled, Is.True);

            // Plegar el panel deja el botón para reabrirlo.
            hud.panelToggleButton.onClick.Invoke();
            Assert.That(hud.sidePanel.gameObject.activeSelf, Is.False);
            Assert.That(hud.panelOpenButton.gameObject.activeSelf, Is.True);
            hud.panelOpenButton.onClick.Invoke();
            Assert.That(hud.sidePanel.gameObject.activeSelf, Is.True);
        }

        [UnityTest]
        public IEnumerator InvalidTraceDisablesPlaybackControls()
        {
            yield return LoadScene();
            var controller = Object.FindFirstObjectByType<PlaybackController>();
            // Espera a que el cliente termine de consultar el servidor (si responde, limpia los avisos al recibir escenarios).
            float deadline = Time.realtimeSinceStartup + 12;
            while (!controller.hud.refreshScenariosButton.interactable && Time.realtimeSinceStartup < deadline) yield return null;
            var invalid = new TextAsset("{}");
            controller.LoadTrace(invalid);
            yield return null;
            Assert.That(controller.hud.timeline.interactable, Is.False);
            Assert.That(controller.hud.speedDropdown.interactable, Is.False);
            Assert.That(controller.hud.Cards.Count, Is.Zero);
            Assert.That(controller.hud.toast.activeSelf, Is.True);
            controller.LoadTrace(AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Data/Traces/trace_ejemplo.json"));
            yield return null;
            Assert.That(controller.hud.timeline.interactable, Is.True);
            Assert.That(controller.hud.Cards.Count, Is.EqualTo(2));
            Object.Destroy(invalid);
        }
    }
}
#endif
