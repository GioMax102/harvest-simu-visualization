using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace HarvestSim.Visualization
{
    /// <summary>
    /// Señales visuales de los eventos de un tick, ya tipadas. Solo lee lo que el trace dice;
    /// no infiere ganadores ni reglas (el ganador de la subasta sale de <c>PlaybackState.Winners</c>).
    /// </summary>
    public sealed class TickCues
    {
        /// <summary>Alertas por agente en el orden del trace: "RECARGA", "RESTRICCION: regla", "BLOQUEO".</summary>
        public readonly Dictionary<string, List<string>> Alerts = new();
        public readonly HashSet<string> Refueled = new();
        public readonly HashSet<string> Restricted = new();
        public readonly HashSet<string> Blocked = new();
        /// <summary>Costo de la PROPUESTA enviada en este tick, por agente que la envía (el carro).</summary>
        public readonly Dictionary<string, string> Proposals = new();
        /// <summary>Pares (cosechadora, carro) de cada SOLICITUD_SERVICIO (cfp) enviada en este tick.</summary>
        public readonly List<(string from, string to)> CallsForProposal = new();
        /// <summary>Celda → agente que la cosechó en este tick.</summary>
        public readonly Dictionary<(int x, int y), string> HarvestedBy = new();
        public readonly HashSet<string> Deposits = new();

        public static TickCues From(JArray events)
        {
            var cues = new TickCues();
            if (events == null) return cues;
            foreach (var token in events)
            {
                if (token is not JObject e) continue;
                string agent = (string)e["agente"];
                switch ((string)e["tipo"])
                {
                    case "RECARGA":
                        cues.AddAlert(agent, "RECARGA"); cues.Refueled.Add(agent); break;
                    case "RESTRICCION":
                        cues.AddAlert(agent, "RESTRICCION: " + (string)e["regla"]); cues.Restricted.Add(agent); break;
                    case "BLOQUEO":
                        cues.AddAlert(agent, "BLOQUEO"); cues.Blocked.Add(agent); break;
                    case "DEPOSITO":
                        if (agent != null) cues.Deposits.Add(agent); break;
                    case "CELDA_COSECHADA":
                        if (e["celda"] is JArray cell && cell.Count == 2) cues.HarvestedBy[((int)cell[0], (int)cell[1])] = agent;
                        break;
                    case "MENSAJE_ENVIADO":
                        string kind = (string)e["tipo_mensaje"], from = (string)e["de"], to = (string)e["a"];
                        if (kind == "PROPUESTA" && from != null) cues.Proposals[from] = e["payload"]?["costo"]?.ToString();
                        if (kind == "SOLICITUD_SERVICIO" && from != null && to != null) cues.CallsForProposal.Add((from, to));
                        break;
                }
            }
            return cues;
        }

        void AddAlert(string agent, string alert)
        {
            if (agent == null) return;
            if (!Alerts.TryGetValue(agent, out var list)) Alerts[agent] = list = new List<string>();
            list.Add(alert);
        }

        public string AlertText(string agent) =>
            Alerts.TryGetValue(agent, out var list) && list.Count > 0 ? string.Join("\n", list) : null;
    }
}
