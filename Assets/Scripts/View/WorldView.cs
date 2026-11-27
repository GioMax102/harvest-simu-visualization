using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HarvestSim.Visualization
{
    /// <summary>
    /// Orquesta la vista realista: suelo, paisaje, maíz, vehículos, arcos de la subasta y cámara.
    /// Solo lee <see cref="PlaybackState"/>: el estado lógico cambia en <see cref="Render"/> (un tick) y
    /// <see cref="Interpolate"/> mueve únicamente lo visual entre dos ticks.
    /// </summary>
    public sealed class WorldView : MonoBehaviour
    {
        public Transform cellsRoot, structuresRoot, agentsRoot;
        public Camera mapCamera;
        public HarvestArt art;

        public FieldGrid Grid { get; private set; }
        public CornField Corn { get; private set; }
        public GroundBuilder Ground { get; private set; }
        public SceneryBuilder Scenery { get; private set; }
        public TerrainShapes Shapes { get; private set; }
        /// <summary>Forma real de los lotes recortada por celda (orillas del suelo y del maíz).</summary>
        public LotShapes Lots { get; private set; }
        /// <summary>Trayectorias suaves por los caminos (sin la escalera de la cuadrícula).</summary>
        public RoadRoutes Routes { get; private set; }
        public CoordinationArcs Arcs { get; private set; }
        public CameraRig Rig { get; private set; }
        public IReadOnlyDictionary<string, AgentView> Agents => agents;
        public TickCues Cues { get; private set; } = new();

        static readonly float[] HarvesterHues = { .11f, .02f, .86f, .16f, .95f, .07f };
        static readonly float[] CartHues = { .56f, .47f, .70f, .52f, .62f, .44f };

        readonly Dictionary<string, AgentView> agents = new();
        TraceData trace;
        TickCues nextCues;
        int nextCuesTick = -1;

        public void Build(TraceData data)
        {
            if (!cellsRoot || !structuresRoot || !agentsRoot || !mapCamera || !art || !art.harvesterPrefab || !art.cartPrefab)
                throw new InvalidOperationException("WorldView: faltan raíces, cámara o el arte (HarvestArt). Ejecuta Harvest Sim > Actualizar escena.");
            Clear(); trace = data;
            Grid = FieldGrid.FromTrace(data, data.World["tamano_celda_m"] == null ? art.cellSize : null);

            Lots = LotShapes.From(Grid, data);
            Routes = RoadRoutes.Build(Grid, data);
            Ground = new GroundBuilder();
            Ground.Build(Grid, art, cellsRoot, Lots);
            var cornObject = new GameObject("Maiz");
            cornObject.transform.SetParent(cellsRoot, false);
            Corn = cornObject.AddComponent<CornField>();
            Corn.Build(Grid, data, art.cornMaterial, art.canopyMaterial, Lots);

            Scenery = new SceneryBuilder();
            Scenery.Build(Grid, art, structuresRoot);
            // Trace 2.3: caminos y contornos reales (mundo.geo.formas) sobre el relieve.
            Shapes = new TerrainShapes();
            Shapes.Build(Grid, data, art, structuresRoot);
            Arcs = new CoordinationArcs();
            Arcs.Build(structuresRoot, art.lineMaterial, Grid.CellSize);

            var variants = new Dictionary<string, int>();
            foreach (var a in data.Agents.Values)
            {
                bool cart = a.Type == "carro";
                var view = Instantiate(cart ? art.cartPrefab : art.harvesterPrefab, agentsRoot);
                variants.TryGetValue(a.Type, out int variant);
                var hues = cart ? CartHues : HarvesterHues;
                var identity = Color.HSVToRGB(hues[variant % hues.Length], .78f, .97f);
                view.Initialize(a, identity, art, Grid.CellSize, mapCamera);
                agents.Add(a.Id, view);
                variants[a.Type] = variant + 1;
            }

            Rig = mapCamera.GetComponent<CameraRig>();
            if (!Rig) Rig = mapCamera.gameObject.AddComponent<CameraRig>();
            Rig.minDistance = Grid.CellSize;
            Rig.SetArea(Grid.WorldBounds, reset: true);
        }

        public void Render(PlaybackState state)
        {
            if (trace == null) return;
            Cues = TickCues.From(state.Events);
            Ground.Render(state.Harvested);
            Corn.Render(state);
            foreach (var pair in agents)
            {
                var value = state.Agents[pair.Key];
                Cues.Proposals.TryGetValue(pair.Key, out string proposal);
                Cues.Decisions.TryGetValue(pair.Key, out var decision);
                // 2.4: vuelta de cabecera sobre la curva de Dubins; por camino, el tramo suave; si no, la celda del trace.
                var pose = VehicleMotion.OnTrajectory(Grid, state, pair.Key, 0)
                           ?? (Routes.TryPose(pair.Key, state.Tick, 0, out var smooth) ? smooth : VehicleMotion.At(Grid, value));
                pair.Value.Render(value, pose, Cues.AlertText(pair.Key), proposal, HudFormat.DecisionCue(decision));
            }
            Arcs.Render(Cues, state, agents);
        }

        /// <summary>Pose visual entre el tick actual y el siguiente. No cambia cargas, cosecha ni estados.</summary>
        public void Interpolate(PlaybackState current, PlaybackState next, float fraction)
        {
            if (trace == null) return;
            fraction = Mathf.Clamp01(fraction);
            var poses = new Dictionary<string, VehiclePose>();
            bool advancing = next.Tick == current.Tick + 1;
            foreach (var pair in agents)
            {
                // 2.4: en una vuelta de cabecera sigue la curva de Dubins del trace; por camino, la trayectoria suave del
                // tramo; en el cultivo (y al entrar a él), recto celda a celda.
                var pose = VehicleMotion.OnTrajectory(Grid, current, pair.Key, fraction)
                           ?? (advancing && Routes.TryPose(pair.Key, current.Tick, fraction, out var smooth)
                               ? smooth
                               : VehicleMotion.Sample(Grid, current.Agents[pair.Key], next.Agents[pair.Key], fraction));
                pair.Value.SetPose(pose);
                poses[pair.Key] = pose;
            }
            if (nextCuesTick != next.Tick) { nextCues = TickCues.From(next.Events); nextCuesTick = next.Tick; }
            Corn.ClearCuts();
            if (next.Tick == current.Tick + 1)
                foreach (var cut in nextCues.HarvestedBy)
                {
                    if (current.Harvested.Contains(cut.Key) || cut.Value == null || !poses.TryGetValue(cut.Value, out var pose)) continue;
                    var forward = FieldGrid.Forward(pose.Heading);
                    Corn.SetCut(new Vector2Int(cut.Key.x, cut.Key.y), pose.Position + forward * Grid.CellSize * .55f, forward);
                }
            Arcs.Follow(agents);
        }

        public Vector3 CellToWorld(int x, int y, float height) =>
            Grid != null ? Grid.CellToWorld(x, y, height) : new Vector3(x * art.cellSize, height, -y * art.cellSize);

        public void SetPerspective()
        {
            if (!Rig) return;
            Rig.SetMode(CameraMode.Orbit);
            Rig.Frame(snap: true);
        }

        public void ResetCamera() { if (Rig) Rig.ResetView(CameraMode.Top); }

        public void FollowAgent(string id)
        {
            if (Rig && agents.TryGetValue(id, out var view)) Rig.SetMode(CameraMode.Follow, view.transform);
        }

        /// <summary>Clic izquierdo sobre un vehículo (fuera del HUD): la cámara lo sigue.</summary>
        void Update()
        {
            if (!Application.isPlaying || trace == null || !Rig || !mapCamera) return;
            var mouse = Mouse.current;
            if (mouse == null || !mouse.leftButton.wasPressedThisFrame || UiFocus.PointerOverUi) return;
            var ray = mapCamera.ScreenPointToRay(mouse.position.ReadValue());
            AgentView best = null;
            float bestDistance = Grid.CellSize * .55f;
            foreach (var view in agents.Values)
            {
                var center = view.transform.position + Vector3.up * 2;
                float distance = Vector3.Cross(ray.direction, center - ray.origin).magnitude;
                if (distance < bestDistance) { best = view; bestDistance = distance; }
            }
            if (best) FollowAgent(best.AgentId);
        }

        /// <summary>Despeja placas que se enciman en pantalla: la de más abajo se queda y las demás suben.</summary>
        void LateUpdate()
        {
            if (trace == null || !mapCamera || agents.Count < 2) return;
            var items = new List<(AgentView view, Vector3 screen, float metersPerPixel)>();
            foreach (var view in agents.Values)
            {
                if (!view.Label) continue;
                var anchor = view.LabelAnchor;
                var screen = mapCamera.WorldToScreenPoint(anchor);
                if (screen.z <= 0) { view.SetLabelOffset(Vector3.zero); continue; }
                float metersPerPixel = mapCamera.orthographic
                    ? 2 * mapCamera.orthographicSize / Mathf.Max(1, mapCamera.pixelHeight)
                    : 2 * screen.z * Mathf.Tan(mapCamera.fieldOfView * Mathf.Deg2Rad / 2) / Mathf.Max(1, mapCamera.pixelHeight);
                items.Add((view, screen, metersPerPixel));
            }
            items.Sort((a, b) => a.screen.y.CompareTo(b.screen.y));
            var placed = new List<(float x, float y, float width, float height)>();
            foreach (var (view, screen, metersPerPixel) in items)
            {
                var size = view.Label.GetRenderedValues(false);
                float scale = view.Label.transform.lossyScale.x / Mathf.Max(1e-5f, metersPerPixel);
                float width = Mathf.Max(60, size.x * scale), height = Mathf.Max(20, size.y * scale);
                float y = screen.y;
                bool moved = true;
                for (int guard = 0; moved && guard < 8; guard++)
                {
                    moved = false;
                    foreach (var other in placed)
                        if (Mathf.Abs(screen.x - other.x) < (width + other.width) / 2 && y < other.y + other.height && y + height > other.y)
                        { y = other.y + other.height + 2; moved = true; }
                }
                placed.Add((screen.x, y, width, height));
                view.SetLabelOffset(mapCamera.transform.up * ((y - screen.y) * metersPerPixel));
            }
        }

        public void FrameCamera(Camera camera)
        {
            if (Rig && camera == mapCamera) Rig.Frame(snap: true);
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
            Ground?.Destroy(); Scenery?.Destroy(); Shapes?.Destroy(); Arcs?.Clear();
            Ground = null; Scenery = null; Shapes = null; Arcs = null; Corn = null; Grid = null; Lots = null; Routes = null;
            agents.Clear(); trace = null; nextCues = null; nextCuesTick = -1; Cues = new TickCues();
        }

        void OnDestroy() { Ground?.Destroy(); Scenery?.Destroy(); Shapes?.Destroy(); }
    }
}
