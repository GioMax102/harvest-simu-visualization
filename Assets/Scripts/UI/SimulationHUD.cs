using System.Linq;
using System.Text;
using Newtonsoft.Json;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HarvestSim.Visualization
{
    public sealed class SimulationHUD : MonoBehaviour
    {
        public TMP_Text header, agentsText, worldText, eventsText, metricsText, errorText, playLabel;
        public Button playButton, backButton, forwardButton, restartButton, endButton;
        public TMP_Dropdown speedDropdown;
        public TMP_Dropdown scenarioDropdown, strategyDropdown;
        public TMP_InputField seedInput;
        public Button simulateButton, refreshScenariosButton;
        public Button perspectiveButton, topButton;
        PlaybackController controller;
        readonly float[] speeds = { 1, 4, 8, 16 };
        public void Bind(PlaybackController value)
        {
            Unbind(); controller = value;
            playButton.onClick.AddListener(controller.PlayPause);
            backButton.onClick.AddListener(controller.StepBackward);
            forwardButton.onClick.AddListener(controller.StepForward);
            restartButton.onClick.AddListener(controller.Restart);
            endButton.onClick.AddListener(controller.GoToEnd);
            speedDropdown.onValueChanged.AddListener(ChangeSpeed);
        }
        void Unbind()
        {
            if (!controller) return;
            playButton.onClick.RemoveListener(controller.PlayPause);
            backButton.onClick.RemoveListener(controller.StepBackward);
            forwardButton.onClick.RemoveListener(controller.StepForward);
            restartButton.onClick.RemoveListener(controller.Restart);
            endButton.onClick.RemoveListener(controller.GoToEnd);
            speedDropdown.onValueChanged.RemoveListener(ChangeSpeed);
            controller = null;
        }
        void OnDestroy() => Unbind();
        void ChangeSpeed(int index) => controller.SetSpeed(speeds[index]);
        public void BindClient(TraceClient client)
        {
            if (!scenarioDropdown)
            {
                var parent = speedDropdown.transform.parent;
                scenarioDropdown = Instantiate(speedDropdown, parent);
                scenarioDropdown.name = "Escenario";
                scenarioDropdown.onValueChanged.RemoveAllListeners();
                scenarioDropdown.ClearOptions();
                scenarioDropdown.AddOptions(new System.Collections.Generic.List<string> { "escenario_a" });
                strategyDropdown = Instantiate(speedDropdown, parent);
                strategyDropdown.name = "Estrategia";
                strategyDropdown.onValueChanged.RemoveAllListeners();
                strategyDropdown.ClearOptions();
                strategyDropdown.AddOptions(new System.Collections.Generic.List<string> { "base", "subasta", "anticipada", "anticipada_plus" });
                var field = TMP_DefaultControls.CreateInputField(new TMP_DefaultControls.Resources());
                field.name = "Seed"; field.transform.SetParent(parent, false);
                field.AddComponent<LayoutElement>().minHeight = 40;
                seedInput = field.GetComponent<TMP_InputField>();
                seedInput.contentType = TMP_InputField.ContentType.IntegerNumber;
                seedInput.text = "0";
                foreach (var text in field.GetComponentsInChildren<TMP_Text>(true)) text.font = header.font;
                if (seedInput.placeholder is TMP_Text placeholder) placeholder.text = "Semilla";
                simulateButton = Instantiate(playButton, parent);
                simulateButton.name = "Simular";
                simulateButton.onClick.RemoveAllListeners();
                simulateButton.GetComponentInChildren<TMP_Text>().text = "Simular";
                refreshScenariosButton = Instantiate(playButton, parent);
                refreshScenariosButton.name = "ActualizarEscenarios";
                refreshScenariosButton.GetComponentInChildren<TMP_Text>().text = "Actualizar escenarios";
                perspectiveButton = Instantiate(playButton, parent);
                perspectiveButton.name = "Perspectiva";
                perspectiveButton.GetComponentInChildren<TMP_Text>().text = "Perspectiva 3D";
                topButton = Instantiate(playButton, parent);
                topButton.name = "Cenital";
                topButton.GetComponentInChildren<TMP_Text>().text = "Vista cenital";
                var title = Instantiate(header, parent); title.text = "SERVIDOR / SIMULACIÓN"; title.fontSize = 18;
                var scenarioLabel = Instantiate(header, parent); scenarioLabel.text = "Escenario"; scenarioLabel.fontSize = 16;
                var strategyLabel = Instantiate(header, parent); strategyLabel.text = "Estrategia"; strategyLabel.fontSize = 16;
                var seedLabel = Instantiate(header, parent); seedLabel.text = "Semilla"; seedLabel.fontSize = 16;
                int position = speedDropdown.transform.GetSiblingIndex() - 1;
                foreach (var element in new Transform[] { title.transform, scenarioLabel.transform, scenarioDropdown.transform,
                             strategyLabel.transform, strategyDropdown.transform, seedLabel.transform, seedInput.transform,
                             simulateButton.transform, refreshScenariosButton.transform, perspectiveButton.transform, topButton.transform })
                    element.SetSiblingIndex(position++);
            }
            simulateButton.onClick.RemoveAllListeners();
            simulateButton.onClick.AddListener(client.Simulate);
            refreshScenariosButton.onClick.RemoveAllListeners();
            refreshScenariosButton.onClick.AddListener(client.RefreshScenarios);
            perspectiveButton.onClick.RemoveAllListeners();
            perspectiveButton.onClick.AddListener(controller.worldView.SetPerspective);
            topButton.onClick.RemoveAllListeners();
            topButton.onClick.AddListener(controller.worldView.ResetCamera);
        }
        public void SetScenarios(System.Collections.Generic.List<string> names)
        {
            scenarioDropdown.ClearOptions(); scenarioDropdown.AddOptions(names);
        }
        public void ShowStatus(string message)
        {
            errorText.gameObject.SetActive(true); errorText.text = message;
        }
        public void Render(TraceData trace, PlaybackState state, bool paused, float speed)
        {
            header.text = $"HARVEST / {trace.Scenario}\nTick {state.Tick} / {trace.TotalTicks} · {(paused ? "Pausa" : "Reproduciendo")} · {speed:g} ticks/s";
            playLabel.text = paused ? "Reproducir" : "Pausar";
            playButton.interactable = state.Tick < trace.TotalTicks;
            backButton.interactable = state.Tick > 0;
            forwardButton.interactable = state.Tick < trace.TotalTicks;
            restartButton.interactable = endButton.interactable = speedDropdown.interactable = true;
            int speedIndex = System.Array.IndexOf(speeds, speed);
            if (speedIndex >= 0) speedDropdown.SetValueWithoutNotify(speedIndex);
            var agents = new StringBuilder("AGENTES\n");
            foreach (var a in trace.Agents.Values)
            {
                var s = state.Agents[a.Id];
                agents.AppendLine($"{a.Id} ({a.Type})\n{s.Status}\nCarga {s.Load}/{a.Capacity} · ({s.X},{s.Y}) {s.Orientation}\n");
                if (s.Fuel.HasValue)
                {
                    var tank = trace.Json["config"]?["combustible"]?[a.Type == "carro" ? "tanque_carro" : "tanque_cosechadora"];
                    float capacity = tank == null ? 0 : (float)tank;
                    int filled = capacity <= 0 ? 0 : Mathf.Clamp(Mathf.RoundToInt(10 * s.Fuel.Value / capacity), 0, 10);
                    agents.AppendLine($"Combustible {s.Fuel.Value:0.##} L " +
                                      (capacity > 0 ? $"[{new string('|', filled)}{new string('.', 10 - filled)}] {capacity:0.##} L" : "") + "\n");
                }
            }
            agentsText.text = agents.ToString();
            int field = trace.Width * (trace.Height - 1) - ((Newtonsoft.Json.Linq.JArray)trace.World["obstaculos"]).Count;
            float percent = field == 0 ? 0 : 100f * state.Harvested.Count / field;
            worldText.text = $"CAMPO Y BODEGA\nCosechadas: {state.Harvested.Count}/{field} ({percent:0.0}%)\nGrano entregado: {state.Delivered}";
            var events = new StringBuilder("EVENTOS DEL TICK\n");
            if (state.Events.Count == 0) events.AppendLine("Sin eventos.");
            foreach (var e in state.Events) events.AppendLine(e.ToString(Formatting.None));
            events.AppendLine($"\nMENSAJES EN TRÁNSITO ({state.Messages.Count})");
            foreach (var e in state.Messages.OrderBy(p => p.Key).Select(p => p.Value))
                events.AppendLine($"#{e["id"]} {e["tipo_mensaje"]} ({e["performativa"]})\n{e["de"]} -> {e["a"]} · entrega t={e["entrega_en"]}");
            eventsText.text = events.ToString();
            var metrics = new StringBuilder("RESULTADOS FINALES DEL ARCHIVO\n");
            foreach (string name in new[] { "tiempo_completacion", "tiempo_ocioso_cosechadora", "porcentaje_ocioso" })
                if (trace.Metrics[name] != null) metrics.AppendLine(name + ": " + trace.Metrics[name]);
            if (!trace.Metrics.HasValues) metrics.AppendLine("Sin métricas finales.");
            metricsText.text = metrics.ToString();
        }
        public void ShowError(string message)
        {
            errorText.gameObject.SetActive(true); errorText.text = message;
            header.text = "No se pudo cargar el trace";
            agentsText.text = worldText.text = eventsText.text = metricsText.text = "";
            foreach (var button in new[] { playButton, backButton, forwardButton, restartButton, endButton }) button.interactable = false;
            speedDropdown.interactable = false;
        }
        public void ClearError() { errorText.text = ""; errorText.gameObject.SetActive(false); }
    }
}
