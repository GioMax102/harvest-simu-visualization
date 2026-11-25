using System.Collections.Generic;
using UnityEngine;

namespace HarvestSim.Visualization
{
    /// <summary>
    /// Arcos de la subasta: azul para cada SOLICITUD_SERVICIO (cfp) del tick y naranja fijo para el ganador
    /// (de ACEPTACION a TRANSFERENCIA_TERMINADA, según <c>PlaybackState.Winners</c>). No decide ganadores.
    /// </summary>
    public sealed class CoordinationArcs
    {
        public static readonly Color CallColor = new(.35f, .62f, 1f);
        public static readonly Color WinnerColor = new(1f, .62f, .12f);
        const int Points = 20;

        readonly Dictionary<(string from, string to), LineRenderer> lines = new();
        readonly Dictionary<(string from, string to), bool> winner = new();
        Transform root;
        Material material;
        float cellSize = 1;

        public int ActiveCount { get; private set; }
        public IEnumerable<LineRenderer> Lines => lines.Values;

        public void Build(Transform parent, Material lineMaterial, float cell)
        {
            root = parent; material = lineMaterial; cellSize = cell;
        }

        public void Render(TickCues cues, PlaybackState state, IReadOnlyDictionary<string, AgentView> agents)
        {
            foreach (var line in lines.Values) line.gameObject.SetActive(false);
            winner.Clear();
            ActiveCount = 0;
            foreach (var call in cues.CallsForProposal) Show(call, CallColor, false, agents);
            foreach (var pair in state.Winners) Show((pair.Key, pair.Value), WinnerColor, true, agents);
            Follow(agents);
        }

        void Show((string from, string to) key, Color color, bool isWinner, IReadOnlyDictionary<string, AgentView> agents)
        {
            if (!material || !root || !agents.ContainsKey(key.from) || !agents.ContainsKey(key.to)) return;
            if (!lines.TryGetValue(key, out var line))
            {
                var go = new GameObject("Coordinacion_" + key.from + "_" + key.to);
                go.transform.SetParent(root, false);
                line = go.AddComponent<LineRenderer>();
                line.sharedMaterial = material;
                line.positionCount = Points;
                line.numCapVertices = 4;
                line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                line.receiveShadows = false;
                lines.Add(key, line);
            }
            if (!line.gameObject.activeSelf) ActiveCount++;
            line.startColor = line.endColor = color;
            line.widthMultiplier = cellSize * (isWinner ? .022f : .015f);
            line.gameObject.SetActive(true);
            winner[key] = isWinner;
        }

        /// <summary>Recalcula la curva de cada arco activo con la posición actual (interpolada) de los vehículos.</summary>
        public void Follow(IReadOnlyDictionary<string, AgentView> agents)
        {
            foreach (var pair in lines)
            {
                var line = pair.Value;
                if (!line.gameObject.activeSelf) continue;
                var a = agents[pair.Key.from].transform.position + Vector3.up * cellSize * .35f;
                var b = agents[pair.Key.to].transform.position + Vector3.up * cellSize * .35f;
                float lift = Mathf.Max(cellSize * .5f, Vector3.Distance(a, b) * .35f);
                var control = (a + b) / 2 + Vector3.up * lift;
                for (int i = 0; i < Points; i++)
                {
                    float t = i / (float)(Points - 1);
                    line.SetPosition(i, (1 - t) * (1 - t) * a + 2 * (1 - t) * t * control + t * t * b);
                }
            }
        }

        public void Clear()
        {
            foreach (var line in lines.Values)
                if (line) { if (Application.isPlaying) Object.Destroy(line.gameObject); else Object.DestroyImmediate(line.gameObject); }
            lines.Clear(); winner.Clear(); ActiveCount = 0;
        }
    }
}
