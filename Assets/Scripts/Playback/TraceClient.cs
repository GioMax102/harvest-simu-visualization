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
        public PlaybackController controller;
        bool busy;
        bool serverHealthy;

        IEnumerator Start()
        {
            if (!controller) controller = GetComponent<PlaybackController>();
            if (!controller || !controller.hud) yield break;
            controller.hud.BindClient(this);
            yield return CheckHealth();
            if (serverHealthy) yield return GetScenarios();
        }

        IEnumerator CheckHealth()
        {
            using var request = UnityWebRequest.Get(baseUrl.TrimEnd('/') + "/api/salud");
            request.timeout = 5;
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success)
            {
                controller.hud.ShowStatus("Servidor no disponible; trace local cargado. " + request.error);
                yield break;
            }
            try
            {
                var health = JObject.Parse(request.downloadHandler.text);
                serverHealthy = (string)health["estado"] == "ok" && (string)health["schema_version"] == "2.2";
                if (!serverHealthy) controller.hud.ShowStatus("El servidor no ofrece el contrato trace 2.2; trace local cargado.");
            }
            catch (Exception error)
            {
                controller.hud.ShowStatus("No se pudo leer la salud del servidor: " + error.Message);
            }
        }

        IEnumerator GetScenarios()
        {
            using var request = UnityWebRequest.Get(baseUrl.TrimEnd('/') + "/api/escenarios");
            request.timeout = 5;
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success)
            {
                controller.hud.ShowStatus("Servidor no disponible; trace local cargado. " + request.error);
                yield break;
            }
            try
            {
                JToken response = JToken.Parse(request.downloadHandler.text);
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
                controller.hud.ShowStatus("No se pudo leer la lista de escenarios: " + error.Message);
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
            busy = true;
            controller.hud.simulateButton.interactable = false;
            string url = baseUrl.TrimEnd('/') + "/api/escenarios/" + Uri.EscapeDataString(scenario) +
                         "/trace?seed=" + seed + "&estrategia=" + Uri.EscapeDataString(strategy);
            using var request = UnityWebRequest.Get(url);
            request.timeout = 30;
            yield return request.SendWebRequest();
            if (request.result == UnityWebRequest.Result.Success)
                controller.LoadTraceJson(request.downloadHandler.text, scenario);
            if (request.result != UnityWebRequest.Result.Success || controller.Playback == null)
            {
                string error = request.result == UnityWebRequest.Result.Success ? "trace inválido" : request.error;
                controller.LoadTrace(controller.initialTrace);
                controller.hud.ShowStatus("No se pudo simular " + scenario + ": " + error + ". Trace local cargado.");
            }
            controller.hud.simulateButton.interactable = true;
            busy = false;
        }
    }
}
