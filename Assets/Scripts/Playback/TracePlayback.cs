using System;
using Newtonsoft.Json.Linq;

namespace HarvestSim.Visualization
{
    public sealed class TracePlayback
    {
        readonly TraceData trace;
        public PlaybackState State { get; private set; }
        public TracePlayback(TraceData trace) { this.trace = trace; Reset(); }
        public void Reset() { State = new PlaybackState(); Seek(0); }
        public void Step(int delta) => Seek((int)Math.Max(0, Math.Min(trace.TotalTicks, (long)State.Tick + delta)));
        public void Seek(int tick)
        {
            tick = Math.Max(0, Math.Min(trace.TotalTicks, tick));
            if (tick < State.Tick) State = new PlaybackState();
            while (State.Tick < tick)
            {
                State.Tick++;
                State.Events = trace.Changes.TryGetValue(State.Tick, out var changes) ? changes : new JArray();
                foreach (JObject e in State.Events) ApplyEvent(e);
            }
        }
        void ApplyEvent(JObject e)
        {
            switch ((string)e["tipo"])
            {
                case "AGENTE":
                    string id = (string)e["id"];
                    if (!State.Agents.TryGetValue(id, out var agent)) State.Agents[id] = agent = new AgentState();
                    var set = (JObject)e["set"];
                    if (set["pose"] is JObject pose)
                    { agent.X = (int)pose["x"]; agent.Y = (int)pose["y"]; agent.Orientation = (string)pose["orientacion"]; }
                    if (set["estado"] != null) agent.Status = (string)set["estado"];
                    if (set["carga"] != null) agent.Load = (int)set["carga"];
                    if (set["combustible"] != null) agent.Fuel = (float)set["combustible"];
                    break;
                case "CELDA_COSECHADA": State.Harvested.Add(((int)e["celda"][0], (int)e["celda"][1])); break;
                case "DEPOSITO": State.Delivered = (int)e["entregado_total"]; break;
                case "MENSAJE_ENVIADO":
                    State.Messages.Add((int)e["id"], e);
                    if ((string)e["tipo_mensaje"] == "ACEPTACION") State.Winners[(string)e["de"]] = (string)e["a"];
                    if ((string)e["tipo_mensaje"] == "TRANSFERENCIA_TERMINADA") State.Winners.Remove((string)e["de"]);
                    break;
                case "MENSAJE_ENTREGADO": State.Messages.Remove((int)e["id"]); break;
                case "DECISION_DESPACHO": State.Decisions[(string)e["agente"]] = e; break;
            }
        }
    }
}
