using System;
using System.Collections;
using System.Collections.Generic;
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
                serverHealthy = (string)health["estado"] == "ok" && (string)health["schema_version"] == "2.2";
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
                JToken response = JToken.Parse(body);
                var entries = response is JArray array ? array : response["escenarios"] as JArray;
                if (entries == null) throw new FormatException("lista de escenarios inválida");
                var names = new List<string>();
                foreach (var entry in entries)
                {
                    string name = entry.Type == JTokenType.String ? (string)entry : (string)entry["nombre"];
                    if (!string.IsNullOrWhiteSpace(name)) names.Add(name);
                }
                if (names.Count == 0) throw new FormatException("lista de escenarios vacía");
                controller.hud.SetScenarios(names);
                controller.hud.ClearError();
            }
            catch (Exception error)
            {
                LoadLocalAfterError("No se pudo leer la lista de escenarios: " + error.Message);
            }
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
