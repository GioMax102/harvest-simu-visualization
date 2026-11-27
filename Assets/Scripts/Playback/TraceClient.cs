using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace HarvestSim.Visualization
{
    public sealed class TraceClient : MonoBehaviour
    {
        public string baseUrl = "http://127.0.0.1:8000";
        [Tooltip("Segundos de espera para una simulación: un campo de 60×40 tarda alrededor de 1.5 min en el servidor.")]
        public int simulationTimeout = 300;
        public PlaybackController controller;
        bool busy;
        bool serverHealthy;

        IEnumerator Start()
        {
            if (!controller) controller = GetComponent<PlaybackController>();
            if (!controller || !controller.hud) yield break;
            controller.hud.BindClient(this);
            yield return RefreshScenariosRoutine();
        }

        public void RefreshScenarios()
        {
            if (!busy) StartCoroutine(RefreshScenariosRoutine());
        }

        IEnumerator RefreshScenariosRoutine()
        {
            SetBusy(true);
            controller.hud.SetServerState(ServerState.Checking);
            serverHealthy = false;
            yield return CheckHealth();
            if (serverHealthy) yield return GetScenarios();
            controller.hud.SetServerState(serverHealthy ? ServerState.Online : ServerState.Offline);
            SetBusy(false);
        }

        void SetBusy(bool value)
        {
            busy = value;
            controller.hud.simulateButton.interactable = !value;
            controller.hud.refreshScenariosButton.interactable = !value;
        }

        void LoadLocalAfterError(string message)
        {
            controller.LoadTrace(controller.initialTrace);
            controller.hud.ShowStatus(message + (controller.Playback != null
                ? "; trace local cargado." : "; no se pudo cargar el trace local: " + controller.hud.errorText.text));
        }

        static IEnumerator Request(string url, int timeout, Action<string, string> completed)
        {
            UnityWebRequest request = null;
            UnityWebRequestAsyncOperation operation = null;
            string error = null;
            try
            {
                request = UnityWebRequest.Get(url);
                request.timeout = timeout;
                operation = request.SendWebRequest();
            }
            catch (Exception exception) { error = exception.Message; }
            using (request)
            {
                if (error == null)
                {
                    yield return operation;
                    if (request.result != UnityWebRequest.Result.Success) error = request.error ?? "Error de conexión";
                }
                string body = error == null ? request.downloadHandler.text : null;
                completed(body, error);
            }
        }

        IEnumerator CheckHealth()
        {
            string body = null, requestError = null;
            yield return Request((baseUrl ?? "").TrimEnd('/') + "/api/salud", 5,
                (response, error) => { body = response; requestError = error; });
            if (requestError != null)
            {
                LoadLocalAfterError("Servidor no disponible: " + requestError);
                yield break;
            }
            try
            {
                var health = JObject.Parse(body);
                serverHealthy = (string)health["estado"] == "ok" && (string)health["schema_version"] is "2.2" or "2.3" or "2.4";
                if (!serverHealthy) LoadLocalAfterError("El servidor no ofrece el contrato trace 2.2");
            }
            catch (Exception error)
            {
                LoadLocalAfterError("No se pudo leer la salud del servidor: " + error.Message);
            }
        }

        IEnumerator GetScenarios()
        {
            string body = null, requestError = null;
            yield return Request((baseUrl ?? "").TrimEnd('/') + "/api/escenarios", 5,
                (response, error) => { body = response; requestError = error; });
            if (requestError != null)
            {
                LoadLocalAfterError("Servidor no disponible: " + requestError);
                yield break;
            }
            try
            {
                var list = ParseScenarios(JToken.Parse(body));
                strategiesByScenario = list.strategies;
                controller.hud.SetScenarios(list.names);
                if (controller.hud.scenarioDropdown && !strategiesListener)
                {
                    controller.hud.scenarioDropdown.onValueChanged.AddListener(_ => SyncStrategies());
                    strategiesListener = true;
                }
                SyncStrategies();
                controller.hud.ClearError();
            }
            catch (Exception error)
            {
                LoadLocalAfterError("No se pudo leer la lista de escenarios: " + error.Message);
            }
        }

        Dictionary<string, List<string>> strategiesByScenario = new();
        bool strategiesListener;

        /// <summary>Estrategias que el servidor puede correr en el escenario elegido (M1: las aprendidas solo
        /// donde hay tabla entrenada).</summary>
        void SyncStrategies()
        {
            var dropdown = controller.hud.scenarioDropdown;
            if (!dropdown || dropdown.options.Count == 0) return;
            string scenario = dropdown.options[dropdown.value].text;
            if (strategiesByScenario.TryGetValue(scenario, out var names)) controller.hud.SetStrategies(names);
        }

        /// <summary>Lee la respuesta de /api/escenarios: nombres y, por escenario, sus estrategias (si el servidor
        /// no las da por escenario, se usa la lista general "estrategias").</summary>
        public static (List<string> names, Dictionary<string, List<string>> strategies) ParseScenarios(JToken response)
        {
            var entries = response is JArray array ? array : response["escenarios"] as JArray;
            if (entries == null) throw new FormatException("lista de escenarios inválida");
            static List<string> Texts(JToken token) =>
                token is JArray list ? list.Where(t => t.Type == JTokenType.String).Select(t => (string)t).ToList() : null;
            var general = response is JObject root ? Texts(root["estrategias"]) : null;
            var names = new List<string>();
            var strategies = new Dictionary<string, List<string>>();
            foreach (var entry in entries)
            {
                string name = entry.Type == JTokenType.String ? (string)entry : (string)entry["nombre"];
                if (string.IsNullOrWhiteSpace(name)) continue;
                names.Add(name);
                var own = entry is JObject item ? Texts(item["estrategias"]) : null;
                if ((own ?? general) is { Count: > 0 } chosen) strategies[name] = chosen;
            }
            if (names.Count == 0) throw new FormatException("lista de escenarios vacía");
            return (names, strategies);
        }

        public void Simulate()
        {
            if (busy) return;
            if (!int.TryParse(controller.hud.seedInput.text, out int seed) || seed < 0)
            {
                controller.hud.ShowStatus("La semilla debe ser un entero no negativo.");
                return;
            }
            string scenario = controller.hud.scenarioDropdown.options[controller.hud.scenarioDropdown.value].text;
            string strategy = controller.hud.strategyDropdown.options[controller.hud.strategyDropdown.value].text;
            StartCoroutine(GetTrace(scenario, strategy, seed));
        }

        IEnumerator GetTrace(string scenario, string strategy, int seed)
        {
            SetBusy(true);
            controller.hud.SetServerState(ServerState.Simulating, $"Simulando {scenario} · {strategy}…");
            string url = (baseUrl ?? "").TrimEnd('/') + "/api/escenarios/" + Uri.EscapeDataString(scenario) +
                         "/trace?seed=" + seed + "&estrategia=" + Uri.EscapeDataString(strategy);
            string body = null, requestError = null;
            yield return Request(url, Mathf.Max(30, simulationTimeout), (response, error) => { body = response; requestError = error; });
            if (requestError == null) controller.LoadTraceJson(body, $"{scenario}__{strategy}__seed{seed}");
            if (requestError != null || controller.Playback == null)
            {
                string error = requestError ?? controller.hud.errorText.text;
                LoadLocalAfterError("No se pudo simular " + scenario + ": " + error);
            }
            controller.hud.SetServerState(requestError == null ? ServerState.Online : ServerState.Offline);
            SetBusy(false);
        }
    }
}
