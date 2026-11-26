using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Newtonsoft.Json.Linq;

namespace HarvestSim.Visualization
{
    /// <summary>Textos del HUD a partir del trace. Solo presenta lo que el trace dice; no calcula métricas propias.</summary>
    public static class HudFormat
    {
        public const string Muted = "#9aa59c", Info = "#8fbcff", Good = "#79d98d", Warn = "#f2b33d", Bad = "#ff8a75";

        static string Color(string text, string color) => $"<color={color}>{text}</color>";

        /// <summary>Una línea legible por evento, o null si el evento no aporta al registro (por ejemplo, solo una pose).</summary>
        public static string Event(JObject e)
        {
            string agent = (string)e["agente"];
            switch ((string)e["tipo"])
            {
                case "MENSAJE_ENVIADO":
                    string kind = (string)e["tipo_mensaje"];
                    string detail = kind switch
                    {
                        "PROPUESTA" => " · costo " + e["payload"]?["costo"],
                        "RECHAZO" => " · " + e["payload"]?["motivo"],
                        "TRANSFERENCIA_TERMINADA" => " · " + e["payload"]?["cantidad"] + " u",
                        _ => ""
                    };
                    return $"{e["de"]} → {e["a"]}  {Color(kind, Info)} {Color("(" + e["performativa"] + ")", Muted)}{detail}";
                case "MENSAJE_ENTREGADO":
                    return Color($"#{e["id"]} entregado a {e["a"]}", Muted);
                case "CELDA_COSECHADA":
                    var cell = e["celda"] as JArray;
                    string amount = e["cantidad"] != null ? $" +{e["cantidad"]}" : "";
                    return $"{agent} cosechó ({cell?[0]},{cell?[1]}){amount}";
                case "DEPOSITO":
                    return $"{agent} {Color("depositó " + e["cantidad"], Good)} · total {e["entregado_total"]}";
                case "RECARGA":
                    return $"{agent} {Color("recargó " + Number(e["cantidad"]), Good)} → {Number(e["nivel"])}";
                case "RESTRICCION":
                    return $"{agent} {Color(e["regla"] + "", Bad)}";
                case "BLOQUEO":
                    return $"{agent} {Color("bloqueado", Bad)}";
                case "AGENTE":
                    string state = (string)e["set"]?["estado"];
                    return state == null ? null : $"{e["id"]} → {Color(AgentView.StatusText(state), Warn)}";
                default:
                    return null;
            }
        }

        /// <summary>Registro de los últimos ticks, del más reciente al más antiguo, con un máximo de líneas.</summary>
        public static string RecentEvents(TraceData trace, int tick, int maxLines = 14, int ticksBack = 25)
        {
            var builder = new StringBuilder();
            int lines = 0;
            for (int t = tick; t >= 0 && t > tick - ticksBack && lines < maxLines; t--)
            {
                if (!trace.Changes.TryGetValue(t, out var changes)) continue;
                var tickLines = new List<string>();
                foreach (var token in changes)
                    if (token is JObject e && Event(e) is string line) tickLines.Add(line);
                if (tickLines.Count == 0) continue;
                builder.Append(Color($"t{t}", t == tick ? Warn : Muted)).Append('\n');
                foreach (var line in tickLines)
                {
                    if (lines++ >= maxLines) break;
                    builder.Append("  ").Append(line).Append('\n');
                }
            }
            return lines == 0 ? Color("Sin eventos recientes.", Muted) : builder.ToString().TrimEnd('\n');
        }

        static readonly (string key, string label, string unit)[] MetricRows =
        {
            ("tiempo_completacion", "Ticks totales", ""),
            ("porcentaje_ocioso", "Ocio cosechadoras", "%"),
            ("grano_total_entregado", "Grano entregado", ""),
            ("porcentaje_entregado", "Entregado", "%"),
            ("distancia_total", "Distancia total", " celdas"),
            ("combustible_usado", "Combustible usado", ""),
            ("recargas", "Recargas", ""),
            ("restricciones", "Restricciones", ""),
            ("mensajes_enviados", "Mensajes", ""),
            ("colisiones", "Colisiones", ""),
            ("costo_total", "Costo J", ""),
            // M1: despacho aprendido (solo vienen con qlearning o sarsa)
            ("decisiones_despacho", "Decisiones Q", ""),
            ("pedidos_tabla", "Pedidos por la tabla", ""),
            ("pedidos_respaldo", "Pedidos por umbral", ""),
            ("cliffs_despacho", "Tolva llena sin carro", ""),
        };

        /// <summary>Rango de carga de cada tramo de la tolva (M1); el 5 es «llena».</summary>
        public static string Bracket(int tramo) => tramo >= 5 ? "llena" : $"{tramo * 20}–{tramo * 20 + 20} %";

        /// <summary>Texto corto para la placa en el tick de la decisión, solo cuando pide carro.</summary>
        public static string DecisionCue(JObject decision)
        {
            if (decision == null || (string)decision["accion"] != "pedir") return null;
            return (string)decision["origen"] switch
            {
                "tabla" => "Q: pide carro",
                "umbral" => "Umbral: pide carro",
                "forzada" => "Tolva llena: pide carro",
                "exploracion" => "Explora: pide carro",
                _ => "Pide carro"
            };
        }

        /// <summary>«Q · tolva 40–60 % · 1 libre · seguir −312 / pedir −298 → pide»: la última decisión (libres = carros
        /// sin servicio; seguir/pedir = valores Q de la tabla).</summary>
        public static string DecisionText(JObject decision)
        {
            if (decision?["estado"] is not JObject state) return null;
            int tramo = (int)state["tramo"], libres = (int)state["carros_libres"];
            string origin = (string)decision["origen"], action = (string)decision["accion"];
            string head = origin switch { "tabla" => "Q", "umbral" => "Umbral", "forzada" => "Cliff", "exploracion" => "Explora", _ => origin };
            var parts = new List<string> { head, "tolva " + Bracket(tramo),
                libres == 0 ? "sin libres" : libres == 1 ? "1 libre" : "2+ libres" };
            if ((bool)state["otra_pidio"]) parts.Add("otra pidió");
            if (decision["q"] is JObject q)
                parts.Add($"seguir {Number(q["seguir"], "0")} / pedir {Number(q["pedir"], "0")}");
            string verb = action == "pedir" ? "pide" : "sigue";
            return Color(string.Join(" · ", parts) + " → ", Muted) + (action == "pedir" ? Color(verb, Info) : verb);
        }

        public static string Number(JToken token, string format) =>
            token is JValue v && v.Type is JTokenType.Float or JTokenType.Integer
                ? ((double)v).ToString(format, CultureInfo.InvariantCulture).Replace("-", "−") : token?.ToString() ?? "";

        /// <summary>Resultados finales que trae el archivo (calculados por Python).</summary>
        public static string Metrics(JObject metrics)
        {
            if (metrics == null || !metrics.HasValues) return Color("El trace no trae métricas finales.", Muted);
            var builder = new StringBuilder();
            foreach (var (key, label, unit) in MetricRows)
            {
                if (metrics[key] is not JValue value || value.Type is not (JTokenType.Integer or JTokenType.Float)) continue;
                builder.Append(Color(label, Muted)).Append("<pos=58%>").Append(Number(value)).Append(unit).Append('\n');
            }
            return builder.Length == 0 ? Color("Sin métricas reconocidas.", Muted) : builder.ToString().TrimEnd('\n');
        }

        public static string Number(JToken token)
        {
            if (token is not JValue value) return token?.ToString() ?? "";
            return value.Type == JTokenType.Float
                ? ((double)value).ToString("0.##", CultureInfo.InvariantCulture)
                : value.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>Estrategia del nombre de la corrida "escenario__estrategia__seedN", o null si no viene.</summary>
        public static string StrategyFromLabel(string label)
        {
            if (string.IsNullOrEmpty(label)) return null;
            var parts = label.Split(new[] { "__" }, System.StringSplitOptions.None);
            return parts.Length >= 3 ? parts[1] : null;
        }

        /// <summary>"escenario_f · subasta · semilla 0". La estrategia sale del nombre de la corrida; si no, de config.politica.asignacion.</summary>
        public static string Subtitle(TraceData trace, string label = null)
        {
            string strategy = StrategyFromLabel(label) ?? (string)trace.Json["config"]?["politica"]?["asignacion"];
            string seed = trace.Json["meta"]?["seed"]?.ToString();
            var parts = new List<string> { trace.Scenario };
            if (!string.IsNullOrEmpty(strategy)) parts.Add(strategy);
            if (!string.IsNullOrEmpty(seed)) parts.Add("semilla " + seed);
            return string.Join(" · ", parts);
        }

        public static string AgentType(string type) => type == "carro" ? "Carro tolva" : type == "cosechadora" ? "Cosechadora" : type;

        public static string Legend =>
            "<b>Vehículos</b>\n" +
            "Cosechadora: máquina con cabezal maicero\nCarro: tractor con carro tolva\n" +
            "Anillo en el suelo: color propio de cada agente\n" +
            $"Anillo pulsa {Color("rojo", Bad)}: restricción · {Color("verde", Good)}: recarga\n\n" +
            "<b>Subasta</b>\n" +
            $"Arco {Color("azul", Info)}: solicitud de servicio (cfp)\n" +
            $"Arco {Color("naranja", Warn)}: carro ganador hasta terminar la transferencia\n" +
            "Oferta en la placa: costo de la propuesta\n\n" +
            "<b>Despacho aprendido (qlearning, sarsa)</b>\n" +
            $"Placa {Color("lila", "#c9b6ff")}: la cosechadora pide carro (Q, umbral o tolva llena)\n" +
            "Tarjeta: última decisión con su tolva, carros libres y valores Q\n\n" +
            "<b>Campo</b>\n" +
            "Maíz en pie: pendiente · rastrojo: cosechado\nMaíz más oscuro: más rendimiento\n" +
            "Árboles y rocas: obstáculos · silos: bodega · tanques: bases\n\n" +
            "<b>Cámara</b>\n" +
            "Clic derecho: girar · rueda: acercar · clic central o WASD: mover\n" +
            "Clic en un vehículo o en su tarjeta: seguirlo · F: encuadrar\n" +
            "Vista cenital: desde arriba";
    }
}
