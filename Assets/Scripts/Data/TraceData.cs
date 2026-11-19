using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace HarvestSim.Visualization
{
    public sealed class TraceData
    {
        public JObject Json { get; }
        public string Scenario => (string)Json["meta"]["escenario"];
        public int TotalTicks => (int)Json["meta"]["total_ticks"];
        public int Width => (int)Json["mundo"]["ancho"];
        public int Height => (int)Json["mundo"]["alto"];
        public JObject World => (JObject)Json["mundo"];
        public JObject Metrics => (JObject)Json["metricas"];
        public readonly Dictionary<string, AgentData> Agents = new();
        public readonly Dictionary<int, JArray> Changes = new();
        public TraceData(JObject json) { Json = json; }
    }

    public sealed class AgentData
    {
        public string Id;
        public string Type;
        public int Capacity;
    }

    public sealed class AgentState
    {
        public int X, Y, Load;
        public string Orientation, Status;
    }

    public sealed class PlaybackState
    {
        public int Tick = -1;
        public int Delivered;
        public readonly Dictionary<string, AgentState> Agents = new();
        public readonly HashSet<(int x, int y)> Harvested = new();
        public readonly Dictionary<int, JObject> Messages = new();
        public JArray Events = new();
    }
}
