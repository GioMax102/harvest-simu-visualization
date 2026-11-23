using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

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
        readonly Dictionary<string, LineRenderer> lines = new();
        TraceData trace;
        Material lineMaterial;
        int maxYield;
        bool perspective;
        float yaw = 35, pitch = 55, zoom = 1;

        public void Build(TraceData data)
        {
            if (!cellsRoot || !structuresRoot || !agentsRoot || !mapCamera || !cellPrefab || !harvesterPrefab || !cartPrefab ||
                !cropMaterial || !harvestedMaterial || !serviceMaterial || !structureMaterial || !obstacleMaterial)
                throw new InvalidOperationException("WorldView: faltan raíces, cámara, prefabs o materiales. Genera la escena desde Harvest Sim.");
            Clear(); trace = data;
            if (trace.World["rendimiento"] is JArray rendimiento)
                foreach (JArray row in rendimiento)
                    foreach (var value in row) maxYield = Mathf.Max(maxYield, (int)value);
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
            var variants = new Dictionary<string, int>();
            foreach (var a in trace.Agents.Values)
            {
                var view = Instantiate(a.Type == "carro" ? cartPrefab : harvesterPrefab, agentsRoot);
                variants.TryGetValue(a.Type, out int variant);
                view.Initialize(a, variant); agents.Add(a.Id, view);
                variants[a.Type] = variant + 1;
            }
            var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Unlit");
            if (shader) lineMaterial = new Material(shader);
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
            {
                cell.Value.sharedMaterial = cell.Key.y == 0 ? serviceMaterial : state.Harvested.Contains(cell.Key) ? harvestedMaterial : cropMaterial;
                var properties = new MaterialPropertyBlock();
                if (cell.Key.y > 0 && !state.Harvested.Contains(cell.Key) && maxYield > 0 && trace.World["rendimiento"] is JArray rendimiento)
                {
                    float ratio = (int)rendimiento[cell.Key.y][cell.Key.x] / (float)maxYield;
                    properties.SetColor("_BaseColor", Color.Lerp(new Color(.86f, .74f, .34f), new Color(.55f, .38f, .08f), ratio * .65f));
                }
                cell.Value.SetPropertyBlock(properties);
            }
            var alerts = new Dictionary<string, string>();
            var proposals = new Dictionary<string, string>();
            foreach (JObject e in state.Events)
            {
                string type = (string)e["tipo"];
                if (type == "RECARGA") alerts[(string)e["agente"]] = "RECARGA";
                if (type == "RESTRICCION") alerts[(string)e["agente"]] = "RESTRICCION: " + (string)e["regla"];
                if (type == "MENSAJE_ENVIADO" && (string)e["tipo_mensaje"] == "PROPUESTA")
                    proposals[(string)e["de"]] = e["payload"]?["costo"]?.ToString();
            }
            foreach (var agent in agents)
            {
                var value = state.Agents[agent.Key];
                alerts.TryGetValue(agent.Key, out string alert);
                proposals.TryGetValue(agent.Key, out string proposal);
                agent.Value.Render(value, CellToWorld(value.X, value.Y, .4f), alert, proposal);
            }
            foreach (var line in lines.Values) line.gameObject.SetActive(false);
            foreach (JObject e in state.Events)
                if ((string)e["tipo"] == "MENSAJE_ENVIADO" && (string)e["tipo_mensaje"] == "SOLICITUD_SERVICIO")
                    ShowLine((string)e["de"], (string)e["a"], new Color(.3f, .55f, .9f));
            foreach (var winner in state.Winners)
                ShowLine(winner.Key, winner.Value, new Color(1f, .65f, .1f));
            FrameCamera(mapCamera);
        }
        void ShowLine(string from, string to, Color color)
        {
            if (!lineMaterial || !agents.ContainsKey(from) || !agents.ContainsKey(to)) return;
            string key = from + ":" + to;
            if (!lines.TryGetValue(key, out var line))
            {
                var go = new GameObject("Coordinacion_" + key);
                go.transform.SetParent(structuresRoot, false);
                line = go.AddComponent<LineRenderer>();
                line.material = lineMaterial; line.positionCount = 2; line.startWidth = line.endWidth = .045f;
                lines.Add(key, line);
            }
            var source = agents[from].transform.position;
            var target = agents[to].transform.position;
            line.SetPosition(0, source + Vector3.up * .45f);
            line.SetPosition(1, target + Vector3.up * .45f);
            line.startColor = line.endColor = color;
            line.gameObject.SetActive(true);
        }
        public Vector3 CellToWorld(int x, int y, float height) => new Vector3(x, height, -y);
        public void Interpolate(PlaybackState current, PlaybackState next, float fraction)
        {
            foreach (var agent in agents)
            {
                var from = current.Agents[agent.Key];
                var to = next.Agents[agent.Key];
                agent.Value.Interpolate(from, to, CellToWorld(from.X, from.Y, .4f), CellToWorld(to.X, to.Y, .4f), fraction);
            }
            foreach (var pair in lines)
            {
                if (!pair.Value.gameObject.activeSelf) continue;
                var ids = pair.Key.Split(':');
                pair.Value.SetPosition(0, agents[ids[0]].transform.position + Vector3.up * .45f);
                pair.Value.SetPosition(1, agents[ids[1]].transform.position + Vector3.up * .45f);
            }
        }
        public void SetPerspective() { perspective = true; FrameCamera(mapCamera); }
        public void ResetCamera() { perspective = false; yaw = 35; pitch = 55; zoom = 1; FrameCamera(mapCamera); }
        void Update()
        {
            var mouse = Mouse.current;
            if (mouse == null || !mapCamera || !mapCamera.pixelRect.Contains(mouse.position.ReadValue())) return;
            if (mouse.scroll.ReadValue().y != 0)
            {
                zoom = Mathf.Clamp(zoom * (mouse.scroll.ReadValue().y > 0 ? .9f : 1.1f), .35f, 4f);
                FrameCamera(mapCamera);
            }
            if (perspective && mouse.rightButton.isPressed)
            {
                var delta = mouse.delta.ReadValue();
                yaw += delta.x * .3f; pitch = Mathf.Clamp(pitch - delta.y * .3f, 20, 85);
                FrameCamera(mapCamera);
            }
        }
        public void FrameCamera(Camera camera)
        {
            if (trace == null || !camera) return;
            var center = new Vector3((trace.Width - 1) / 2f, 0, -(trace.Height - 1) / 2f);
            camera.orthographic = !perspective;
            if (perspective)
            {
                camera.fieldOfView = 60;
                camera.transform.position = center + Quaternion.Euler(pitch, yaw, 0) * Vector3.back * Mathf.Max(trace.Width, trace.Height) * 1.8f * zoom;
                camera.transform.LookAt(center);
            }
            else
            {
                camera.transform.position = center + Vector3.up * 20;
                camera.transform.rotation = Quaternion.Euler(90, 0, 0);
                camera.orthographicSize = Mathf.Max(trace.Height / 2f + .7f, (trace.Width / 2f + .7f) / Mathf.Max(.1f, camera.aspect)) * zoom;
            }
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
            cells.Clear(); agents.Clear(); lines.Clear(); trace = null; maxYield = 0;
            if (lineMaterial)
            {
                if (Application.isPlaying) Destroy(lineMaterial); else DestroyImmediate(lineMaterial);
                lineMaterial = null;
            }
        }
    }
}
