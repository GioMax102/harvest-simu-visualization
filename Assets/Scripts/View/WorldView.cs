using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace HarvestSim.Visualization
{
    public sealed class WorldView : MonoBehaviour
    {
        public Transform cellsRoot, structuresRoot, agentsRoot;
        public Camera mapCamera;
        public GameObject cellPrefab;
        public AgentView harvesterPrefab, cartPrefab;
        public Material cropMaterial, harvestedMaterial, serviceMaterial, structureMaterial, obstacleMaterial;
        readonly Dictionary<(int x, int y), Renderer> cells = new();
        readonly Dictionary<string, AgentView> agents = new();
        TraceData trace;

        public void Build(TraceData data)
        {
            if (!cellsRoot || !structuresRoot || !agentsRoot || !mapCamera || !cellPrefab || !harvesterPrefab || !cartPrefab ||
                !cropMaterial || !harvestedMaterial || !serviceMaterial || !structureMaterial || !obstacleMaterial)
                throw new InvalidOperationException("WorldView: faltan raíces, cámara, prefabs o materiales. Genera la escena desde Harvest Sim.");
            Clear(); trace = data;
            for (int y = 0; y < trace.Height; y++)
                for (int x = 0; x < trace.Width; x++)
                {
                    var cell = Instantiate(cellPrefab, cellsRoot);
                    cell.name = $"Cell_{x}_{y}";
                    cell.transform.localPosition = CellToWorld(x, y, -0.05f);
                    cells.Add((x, y), cell.GetComponent<Renderer>());
                }
            foreach (JProperty home in ((JObject)trace.World["bases"]).Properties())
                Structure("Base_" + home.Name, home.Value, PrimitiveType.Cylinder, new Vector3(.8f, .025f, .8f), .025f, structureMaterial);
            Structure("Bodega_B", trace.World["bodega"], PrimitiveType.Cube, new Vector3(.8f, .12f, .8f), .06f, structureMaterial);
            var label = new GameObject("B").AddComponent<TextMesh>();
            label.transform.SetParent(structuresRoot, false);
            label.transform.localPosition = CellToWorld((int)trace.World["bodega"][0], (int)trace.World["bodega"][1], .14f);
            label.transform.localRotation = Quaternion.Euler(90, 0, 0);
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.GetComponent<Renderer>().sharedMaterial = label.font.material;
            label.text = "B"; label.anchor = TextAnchor.MiddleCenter; label.fontSize = 48; label.characterSize = .12f; label.color = Color.white;
            foreach (var obstacle in (JArray)trace.World["obstaculos"])
                Structure("Obstaculo", obstacle, PrimitiveType.Cube, new Vector3(.9f, .6f, .9f), .3f, obstacleMaterial);
            foreach (var a in trace.Agents.Values)
            {
                var view = Instantiate(a.Type == "carro" ? cartPrefab : harvesterPrefab, agentsRoot);
                view.Initialize(a); agents.Add(a.Id, view);
            }
            FrameCamera(mapCamera);
        }
        void Structure(string name, JToken cell, PrimitiveType type, Vector3 scale, float height, Material material)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name; go.transform.SetParent(structuresRoot, false);
            go.transform.localPosition = CellToWorld((int)cell[0], (int)cell[1], height);
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = material;
            var collider = go.GetComponent<Collider>(); collider.enabled = false;
            if (Application.isPlaying) Destroy(collider); else DestroyImmediate(collider);
        }
        public void Render(PlaybackState state)
        {
            foreach (var cell in cells)
                cell.Value.sharedMaterial = cell.Key.y == 0 ? serviceMaterial : state.Harvested.Contains(cell.Key) ? harvestedMaterial : cropMaterial;
            foreach (var agent in agents)
            {
                var value = state.Agents[agent.Key];
                agent.Value.Render(value, CellToWorld(value.X, value.Y, .4f));
            }
            FrameCamera(mapCamera);
        }
        public Vector3 CellToWorld(int x, int y, float height) => new Vector3(x, height, -y);
        public void FrameCamera(Camera camera)
        {
            if (trace == null || !camera) return;
            camera.orthographic = true;
            camera.transform.position = new Vector3((trace.Width - 1) / 2f, 20, -(trace.Height - 1) / 2f);
            camera.transform.rotation = Quaternion.Euler(90, 0, 0);
            camera.orthographicSize = Mathf.Max(trace.Height / 2f + .7f, (trace.Width / 2f + .7f) / Mathf.Max(.1f, camera.aspect));
        }
        public void Clear()
        {
            foreach (var root in new[] { cellsRoot, structuresRoot, agentsRoot })
            {
                if (!root) continue;
                for (int i = root.childCount - 1; i >= 0; i--)
                {
                    var child = root.GetChild(i).gameObject;
                    child.SetActive(false);
                    if (Application.isPlaying) Destroy(child); else DestroyImmediate(child);
                }
            }
            cells.Clear(); agents.Clear(); trace = null;
        }
    }
}
