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
