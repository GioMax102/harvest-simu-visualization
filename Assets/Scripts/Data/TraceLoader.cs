using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HarvestSim.Visualization
{
    public static class TraceLoader
    {
        public static TraceData Parse(string json)
        {
            try
            {
                var trace = new TraceData(JObject.Parse(json, new JsonLoadSettings
                { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error }));
                Validate(trace);
                foreach (JObject a in (JArray)trace.Json["agentes"])
                {
                    var agent = new AgentData { Id = (string)a["id"], Type = (string)a["tipo"], Capacity = (int)a["capacidad"] };
                    trace.Agents.Add(agent.Id, agent);
                }
                foreach (JObject t in (JArray)trace.Json["ticks"])
                    trace.Changes.Add((int)t["tick"], (JArray)t["cambios"]);
                return trace;
            }
            catch (JsonException e) { throw new FormatException("JSON inválido: " + e.Message, e); }
        }

        static void Require(bool condition, string path, string reason)
        {
            if (!condition) throw new FormatException(path + ": " + reason);
        }
        static JToken Field(JToken parent, string name, JTokenType type)
        {
            var value = (parent as JObject)?[name];
            Require(value != null && value.Type == type, parent?.Path + "." + name, "se requiere " + type);
            return value;
        }
        static int Integer(JToken parent, string name, int min = 0, int max = int.MaxValue)
        {
            var token = Field(parent, name, JTokenType.Integer);
            Require(long.TryParse(token.ToString(), out long n) && n >= min && n <= max, token.Path, "entero fuera de rango");
            return (int)n;
        }
        static string Text(JToken parent, string name)
        {
            var token = Field(parent, name, JTokenType.String);
            Require(!string.IsNullOrWhiteSpace((string)token), token.Path, "texto vacío");
            return (string)token;
        }
        static double Number(JToken parent, string name)
        {
            var token = (parent as JObject)?[name];
            Require(token != null && (token.Type == JTokenType.Integer || token.Type == JTokenType.Float),
                    parent?.Path + "." + name, "se requiere número");
            double value = (double)token;
            Require(!double.IsNaN(value) && !double.IsInfinity(value) && value >= 0, token.Path, "número no negativo requerido");
            return value;
        }
        static (int x, int y) Cell(JToken token, int width, int height)
        {
            Require(token is JArray a && a.Count == 2, token?.Path ?? "celda", "se requiere [x,y]");
            var pair = (JArray)token;
            Require(pair.All(v => v.Type == JTokenType.Integer), token.Path, "coordenadas enteras requeridas");
            Require(long.TryParse(pair[0].ToString(), out long x) && x >= 0 && x < width, token.Path, "x fuera del mapa");
            Require(long.TryParse(pair[1].ToString(), out long y) && y >= 0 && y < height, token.Path, "y fuera del mapa");
            return ((int)x, (int)y);
        }
        static string Agent(JToken e, string field, Dictionary<string, int> agents)
        {
            string id = Text(e, field);
            Require(agents.ContainsKey(id), e.Path + "." + field, "agente desconocido: " + id);
            return id;
        }

        public static void Validate(TraceData trace)
        {
            var root = trace.Json;
            string version = Text(root, "schema_version");
            Require(version == "2.1" || version == "2.2", "schema_version", "solo se admite 2.1 o 2.2");
            var meta = Field(root, "meta", JTokenType.Object);
            Text(meta, "escenario");
            int total = Integer(meta, "total_ticks");
            Field(root, "config", JTokenType.Object);
            Field(root, "metricas", JTokenType.Object);
            var world = Field(root, "mundo", JTokenType.Object);
            int width = Integer(world, "ancho", 1), height = Integer(world, "alto", 1);
            Integer(world, "grano_por_celda", 1);
            Cell(world["bodega"], width, height);
            var bases = (JObject)Field(world, "bases", JTokenType.Object);
            foreach (var entry in bases.Properties()) Cell(entry.Value, width, height);
            var obstacles = new HashSet<(int, int)>();
            foreach (var token in (JArray)Field(world, "obstaculos", JTokenType.Array))
            {
                var cell = Cell(token, width, height);
                Require(cell.y > 0 && obstacles.Add(cell), token.Path, "obstáculo repetido o en servicio");
            }
            if (world["rendimiento"] != null)
            {
                var rows = (JArray)Field(world, "rendimiento", JTokenType.Array);
                Require(rows.Count == height, rows.Path, "alto de rendimiento distinto al mapa");
                for (int y = 0; y < height; y++)
                {
                    Require(rows[y] is JArray row && row.Count == width, rows[y].Path, "ancho de rendimiento distinto al mapa");
                    for (int x = 0; x < width; x++)
                    {
                        var value = rows[y][x];
                        long n = 0;
                        Require(value.Type == JTokenType.Integer && long.TryParse(value.ToString(), out n) && n >= 0,
                                value.Path, "rendimiento entero no negativo requerido");
                        Require((y > 0 && !obstacles.Contains((x, y))) || n == 0, value.Path, "rendimiento fuera del campo");
                    }
                }
            }
            var agents = new Dictionary<string, int>();
            var agentTypes = new Dictionary<string, string>();
            foreach (var a in (JArray)Field(root, "agentes", JTokenType.Array))
            {
                string id = Text(a, "id"), type = Text(a, "tipo");
                Require(type == "cosechadora" || type == "carro", a.Path, "tipo de agente no soportado");
                Require(!agents.ContainsKey(id), a.Path, "id repetido");
                agents.Add(id, Integer(a, "capacidad", 1)); agentTypes.Add(id, type);
                var home = Cell(a["base"], width, height);
                Require(bases[id] != null && home == Cell(bases[id], width, height) && home.y == 0, a.Path, "base inconsistente");
            }
            Require(agents.Count > 0, "agentes", "catálogo vacío");
            foreach (var entry in bases.Properties()) Require(agents.ContainsKey(entry.Name), entry.Path, "base de agente desconocido");
            var initialized = new HashSet<string>();
            var harvested = new HashSet<(int, int)>();
            var sent = new HashSet<int>();
            var pending = new Dictionary<int, string>();
            int previous = -1;
            foreach (var t in (JArray)Field(root, "ticks", JTokenType.Array))
            {
                int tick = Integer(t, "tick", 0, total);
                Require(tick > previous && (previous >= 0 || tick == 0), t.Path, "ticks crecientes, únicos, empezando en 0");
                previous = tick;
                string checksum = Text(t, "checksum");
                Require(checksum.Length == 16 && checksum.All(Uri.IsHexDigit), t.Path, "checksum debe tener 16 dígitos hex");
                // shortcut: checksum solo estructural; validar integridad con tools/validar_trace.py antes de importar.
                foreach (var e in (JArray)Field(t, "cambios", JTokenType.Array))
                {
                    string type = Text(e, "tipo");
                    switch (type)
                    {
                        case "AGENTE":
                            string id = Agent(e, "id", agents);
                            var set = (JObject)Field(e, "set", JTokenType.Object);
                            if (tick == 0)
                            {
                                Require(set["pose"] != null && set["estado"] != null && set["carga"] != null, e.Path, "estado inicial incompleto");
                                Require(initialized.Add(id), e.Path, "estado inicial duplicado");
                            }
                            if (set["pose"] != null)
                            {
                                var p = Field(set, "pose", JTokenType.Object);
                                int x = Integer(p, "x", 0, width - 1), y = Integer(p, "y", 0, height - 1);
                                Require(!obstacles.Contains((x, y)), p.Path, "pose sobre obstáculo");
                                Require(new[] { "N", "E", "S", "O" }.Contains(Text(p, "orientacion")), p.Path, "orientación desconocida");
                            }
                            if (set["carga"] != null) Integer(set, "carga", 0, agents[id]);
                            if (set["combustible"] != null) Number(set, "combustible");
                            if (set["estado"] != null)
                            {
                                string status = Text(set, "estado");
                                string[] valid = agentTypes[id] == "carro"
                                    ? new[] { "EN_BASE", "YENDO_A_COSECHADORA", "ESPERANDO_TRANSFERENCIA", "YENDO_A_BODEGA", "DESCARGANDO", "REGRESANDO_A_BASE" }
                                    : new[] { "EN_BASE", "YENDO_A_CAMPO", "COSECHANDO", "LLENA_ESPERANDO", "TRANSFIRIENDO", "REGRESANDO_A_BASE", "FIN" };
                                Require(valid.Contains(status), set.Path, "estado desconocido");
                            }
                            break;
                        case "CELDA_COSECHADA":
                            Agent(e, "agente", agents);
                            var cell = Cell(e["celda"], width, height);
                            Require(cell.y > 0 && !obstacles.Contains(cell) && harvested.Add(cell), e.Path, "celda no cosechable o repetida");
                            if (version == "2.2") Integer(e, "cantidad", 1);
                            break;
                        case "DEPOSITO":
                            Agent(e, "agente", agents); Integer(e, "cantidad"); Integer(e, "entregado_total"); break;
                        case "BLOQUEO": Agent(e, "agente", agents); break;
                        case "RECARGA":
                            Require(version == "2.2", e.Path, "RECARGA requiere trace 2.2");
                            Agent(e, "agente", agents); Number(e, "cantidad"); Number(e, "nivel"); break;
                        case "RESTRICCION":
                            Require(version == "2.2", e.Path, "RESTRICCION requiere trace 2.2");
                            Agent(e, "agente", agents); Text(e, "regla"); Text(e, "detalle"); break;
                        case "MENSAJE_ENVIADO":
                            int message = Integer(e, "id");
                            Require(sent.Add(message), e.Path, "id de mensaje repetido");
                            Agent(e, "de", agents); pending.Add(message, Agent(e, "a", agents));
                            string messageType = Text(e, "tipo_mensaje"), act = Text(e, "performativa");
                            var acts = new Dictionary<string, string> {
                                { "LLENA", "request" }, { "TRANSFERENCIA_TERMINADA", "inform" },
                                { "SOLICITUD_SERVICIO", "cfp" }, { "PROPUESTA", "propose" },
                                { "RECHAZO", "refuse" }, { "ACEPTACION", "accept-proposal" },
                                { "RECHAZO_PROPUESTA", "reject-proposal" } };
                            Require(acts.TryGetValue(messageType, out string expectedAct) && act == expectedAct &&
                                    (version == "2.2" || messageType == "LLENA" || messageType == "TRANSFERENCIA_TERMINADA"),
                                    e.Path, "mensaje/performativa no soportados");
                            Field(e, "payload", JTokenType.Object); Integer(e, "entrega_en", tick + 1);
                            break;
                        case "MENSAJE_ENTREGADO":
                            int delivered = Integer(e, "id");
                            string recipient = Agent(e, "a", agents);
                            Require(pending.TryGetValue(delivered, out string expected) && expected == recipient, e.Path, "entrega sin envío pendiente compatible");
                            pending.Remove(delivered); break;
                        default: throw new FormatException(e.Path + ": tipo de evento no soportado: " + type);
                    }
                }
                if (tick == 0) Require(initialized.Count == agents.Count, t.Path, "faltan agentes en tick 0");
            }
            Require(previous >= 0, "ticks", "falta tick 0");
        }
    }
}
