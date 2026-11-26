using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace HarvestSim.Visualization
{
    public enum ServerState { Unknown, Checking, Online, Offline, Simulating }

    /// <summary>
    /// HUD superpuesto a la vista 3D. Barra superior: servidor y simulación. Barra inferior: reproducción y línea de tiempo.
    /// Panel lateral plegable: agentes, campo, eventos, métricas finales y leyenda. Solo muestra el trace; no decide nada.
    /// La jerarquía se genera con Harvest Sim &gt; Actualizar escena realista (HarvestHudBuilder).
    /// </summary>
    public sealed class SimulationHUD : MonoBehaviour
    {
        public static string LegendText => HudFormat.Legend;
        public static readonly float[] Speeds = { 1, 4, 8, 16, 32 };

        [Header("Barra superior")]
        public RectTransform topBar;
        public TMP_Text header;
        public TMP_Dropdown scenarioDropdown, strategyDropdown;
        public TMP_InputField seedInput;
        public Button simulateButton, refreshScenariosButton;
        public Image serverDot;
        public TMP_Text serverText;

        [Header("Aviso")]
        public GameObject toast;
        public TMP_Text errorText;
        public Button toastClose;

        [Header("Reproducción")]
        public RectTransform bottomBar;
        public Button playButton, backButton, forwardButton, restartButton, endButton;
        public Image playIcon;
        public Sprite playSprite, pauseSprite;
        public TMP_Text playLabel, tickText;
        public TMP_Dropdown speedDropdown;
        public Slider timeline;
        public RawImage timelineMarks;

        [Header("Cámara")]
        public Button perspectiveButton, topButton, followButton, frameButton;

        [Header("Panel lateral")]
        public RectTransform sidePanel;
        public Button panelToggleButton, panelOpenButton;
        public RectTransform agentCards;
        public HudAgentCard cardTemplate;
        public TMP_Text agentsText, worldText, eventsText, metricsText, legendText;
        public RectTransform harvestFill, deliveredFill;

        [Header("Tooltip")]
        public RectTransform tooltip;
        public TMP_Text tooltipText;

        PlaybackController controller;
        TraceData shownTrace;
        Texture2D marksTexture;
        readonly List<HudAgentCard> cards = new();
        bool updatingTimeline;
        Vector4 lastInsets = new(-1, -1, -1, -1);

        public IReadOnlyList<HudAgentCard> Cards => cards;
        public ServerState Server { get; private set; } = ServerState.Unknown;

        public void Bind(PlaybackController value)
        {
            Unbind(); controller = value;
            Listen(playButton, controller.PlayPause);
            Listen(backButton, controller.StepBackward);
            Listen(forwardButton, controller.StepForward);
            Listen(restartButton, controller.Restart);
            Listen(endButton, controller.GoToEnd);
            if (speedDropdown) speedDropdown.onValueChanged.AddListener(ChangeSpeed);
            if (timeline) timeline.onValueChanged.AddListener(OnTimeline);
            Listen(perspectiveButton, () => controller.worldView.SetPerspective());
            Listen(topButton, () => controller.worldView.ResetCamera());
            Listen(frameButton, () => controller.worldView.FrameCamera(controller.worldView.mapCamera));
            Listen(followButton, FollowNext);
            Listen(panelToggleButton, () => SetPanel(false));
            Listen(panelOpenButton, () => SetPanel(true));
            Listen(toastClose, ClearError);
            if (legendText) legendText.text = LegendText;
        }

        void Listen(Button button, UnityEngine.Events.UnityAction action)
        {
            if (!button) return;
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(action);
        }

        void Unbind()
        {
            if (!controller) return;
            if (speedDropdown) speedDropdown.onValueChanged.RemoveListener(ChangeSpeed);
            if (timeline) timeline.onValueChanged.RemoveListener(OnTimeline);
            controller = null;
        }

        void OnDestroy()
        {
            Unbind();
            if (marksTexture) Destroy(marksTexture);
        }

        void ChangeSpeed(int index)
        {
            if (controller && index >= 0 && index < Speeds.Length) controller.SetSpeed(Speeds[index]);
        }

        void OnTimeline(float value)
        {
            if (updatingTimeline || !controller || controller.Playback == null) return;
            controller.SeekTo(Mathf.RoundToInt(value));
        }

        public void BindClient(TraceClient client)
        {
            Listen(simulateButton, client.Simulate);
            Listen(refreshScenariosButton, client.RefreshScenarios);
        }

        /// <summary>Opciones del selector de estrategia (las que el servidor puede correr en el escenario elegido).</summary>
        public void SetStrategies(List<string> names)
        {
            if (!strategyDropdown || names == null || names.Count == 0) return;
            string current = strategyDropdown.options.Count > 0 ? strategyDropdown.options[strategyDropdown.value].text : null;
            strategyDropdown.ClearOptions(); strategyDropdown.AddOptions(names);
            int index = current == null ? -1 : names.IndexOf(current);
            strategyDropdown.SetValueWithoutNotify(index >= 0 ? index : 0);
        }

        public void SetScenarios(List<string> names)
        {
            if (!scenarioDropdown) return;
            string current = scenarioDropdown.options.Count > 0 ? scenarioDropdown.options[scenarioDropdown.value].text : null;
            scenarioDropdown.ClearOptions(); scenarioDropdown.AddOptions(names);
            int index = current == null ? -1 : names.IndexOf(current);
            if (index >= 0) scenarioDropdown.SetValueWithoutNotify(index);
        }

        public void SetServerState(ServerState state, string detail = null)
        {
            Server = state;
            if (serverDot)
                serverDot.color = state switch
                {
                    ServerState.Online => new Color(.4f, .85f, .5f),
                    ServerState.Offline => new Color(.95f, .36f, .3f),
                    ServerState.Simulating or ServerState.Checking => new Color(.95f, .7f, .24f),
                    _ => new Color(.6f, .62f, .6f)
                };
            if (serverText)
                serverText.text = detail ?? state switch
                {
                    ServerState.Online => "Servidor conectado",
                    ServerState.Offline => "Sin servidor · trace local",
                    ServerState.Simulating => "Simulando…",
                    ServerState.Checking => "Conectando…",
                    _ => "Servidor"
                };
        }

        public void ShowStatus(string message)
        {
            if (toast) toast.SetActive(true);
            errorText.gameObject.SetActive(true); errorText.text = message;
        }

        public void ClearError()
        {
            errorText.text = ""; errorText.gameObject.SetActive(false);
            if (toast) toast.SetActive(false);
        }

        public void Render(TraceData trace, PlaybackState state, bool paused, float speed)
        {
            if (trace != shownTrace) OnTraceChanged(trace);
            var world = controller ? controller.worldView : null;
            if (header) header.text = HudFormat.Subtitle(trace, controller ? controller.TraceLabel : null);
            if (tickText) tickText.text = $"Tick <b>{state.Tick}</b> / {trace.TotalTicks}  <color={HudFormat.Muted}>{(paused ? "En pausa" : "Reproduciendo")} · {speed:g} ticks/s</color>";
            if (playLabel) playLabel.text = paused ? "Reproducir" : "Pausar";
            if (playIcon && playSprite && pauseSprite) playIcon.sprite = paused ? playSprite : pauseSprite;
            SetInteractable(true);
            playButton.interactable = state.Tick < trace.TotalTicks;
            backButton.interactable = state.Tick > 0;
            forwardButton.interactable = state.Tick < trace.TotalTicks;
            int speedIndex = System.Array.IndexOf(Speeds, speed);
            if (speedDropdown && speedIndex >= 0) speedDropdown.SetValueWithoutNotify(speedIndex);
            if (timeline)
            {
                updatingTimeline = true;
                timeline.maxValue = Mathf.Max(1, trace.TotalTicks);
                timeline.SetValueWithoutNotify(state.Tick);
                updatingTimeline = false;
            }

            var followed = world && world.Rig ? world.Rig.FollowTarget : null;
            var cues = world ? world.Cues : TickCues.From(state.Events);
            foreach (var card in cards)
            {
                if (!trace.Agents.TryGetValue(card.AgentId, out var agent) || !state.Agents.TryGetValue(card.AgentId, out var value)) continue;
                bool isFollowed = followed && world.Agents.TryGetValue(card.AgentId, out var view) && view.transform == followed;
                state.Decisions.TryGetValue(card.AgentId, out var decision);
                card.Render(value, agent.Capacity, Tank(trace, agent.Type), cues.AlertText(card.AgentId), isFollowed,
                    HudFormat.DecisionText(decision));
            }
            if (agentsText) agentsText.text = "";

            int field = world && world.Grid != null ? world.Grid.FieldCellCount
                : trace.Width * (trace.Height - 1) - ((JArray)trace.World["obstaculos"]).Count;
            float harvested = field == 0 ? 0 : state.Harvested.Count / (float)field;
            float expected = trace.Metrics?["grano_esperado_total"] is JValue e && e.Type is JTokenType.Integer or JTokenType.Float ? (float)e : 0;
            SetFill(harvestFill, harvested);
            SetFill(deliveredFill, expected > 0 ? Mathf.Clamp01(state.Delivered / expected) : 0);
            if (worldText)
            {
                string transit = state.Messages.Count == 0 ? "" : "\n" + $"<color={HudFormat.Muted}>Mensajes en tránsito ({state.Messages.Count}):</color> " +
                    string.Join(", ", state.Messages.OrderBy(p => p.Key).Take(4).Select(p => $"{p.Value["tipo_mensaje"]} → {p.Value["a"]}")) +
                    (state.Messages.Count > 4 ? "…" : "");
                // Trace 2.3: atribución de las fuentes del mapa real (trazo del KML y relieve Copernicus DEM).
                string fuentes = trace.World["geo"]?["atribucion"] is JValue atribucion
                    ? $"\n<size=80%><color={HudFormat.Muted}>{atribucion}</color></size>" : "";
                worldText.text = $"Cosechadas <b>{state.Harvested.Count}</b>/{field} ({harvested * 100:0}%)\n" +
                                 $"Grano entregado <b>{state.Delivered}</b>" + (expected > 0 ? $"/{expected:0}" : "") + transit + fuentes;
            }
            if (eventsText) eventsText.text = HudFormat.RecentEvents(trace, state.Tick);
        }

        static float Tank(TraceData trace, string type)
        {
            var tank = trace.Json["config"]?["combustible"]?[type == "carro" ? "tanque_carro" : "tanque_cosechadora"];
            return tank is JValue value && value.Type is JTokenType.Integer or JTokenType.Float ? (float)value : 0;
        }

        static void SetFill(RectTransform fill, float value)
        {
            if (!fill) return;
            fill.anchorMin = Vector2.zero; fill.anchorMax = new Vector2(Mathf.Clamp01(value), 1);
            fill.offsetMin = fill.offsetMax = Vector2.zero;
        }

        void OnTraceChanged(TraceData trace)
        {
            shownTrace = trace;
            if (metricsText) metricsText.text = HudFormat.Metrics(trace.Metrics);
            RebuildCards(trace);
            BuildMarks(trace);
            SyncSelectors(trace);
        }

        /// <summary>Los selectores muestran la corrida cargada, para volver a simularla o cambiar solo un parámetro.</summary>
        void SyncSelectors(TraceData trace)
        {
            static void Select(TMP_Dropdown dropdown, string value)
            {
                if (!dropdown || value == null) return;
                int index = dropdown.options.FindIndex(o => o.text == value);
                if (index >= 0) dropdown.SetValueWithoutNotify(index);
            }
            Select(scenarioDropdown, trace.Scenario);
            Select(strategyDropdown, HudFormat.StrategyFromLabel(controller ? controller.TraceLabel : null));
            string seed = trace.Json["meta"]?["seed"]?.ToString();
            if (seedInput && !string.IsNullOrEmpty(seed)) seedInput.SetTextWithoutNotify(seed);
        }

        void RebuildCards(TraceData trace)
        {
            foreach (var card in cards) if (card) Destroy(card.gameObject);
            cards.Clear();
            if (!cardTemplate || !agentCards) return;
            var world = controller ? controller.worldView : null;
            foreach (var agent in trace.Agents.Values.OrderBy(a => a.Type == "carro" ? 1 : 0).ThenBy(a => a.Id))
            {
                var card = Instantiate(cardTemplate, agentCards);
                card.gameObject.SetActive(true);
                var identity = world && world.Agents.TryGetValue(agent.Id, out var view) ? view.Identity : Color.white;
                card.Setup(agent, identity, Follow);
                cards.Add(card);
            }
        }

        /// <summary>Marcas en la línea de tiempo: restricciones (rojo), depósitos en la bodega (ámbar) y recargas (verde).</summary>
        void BuildMarks(TraceData trace)
        {
            if (!timelineMarks) return;
            if (marksTexture) Destroy(marksTexture);
            const int width = 1024;
            marksTexture = new Texture2D(width, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "MarcasTiempo" };
            var pixels = new Color32[width];
            var priority = new int[width];
            int total = Mathf.Max(1, trace.TotalTicks);
            foreach (var pair in trace.Changes)
                foreach (var token in pair.Value)
                {
                    string type = (string)token["tipo"];
                    int rank = type switch { "RESTRICCION" => 3, "DEPOSITO" => 2, "RECARGA" => 1, _ => 0 };
                    if (rank == 0) continue;
                    int x = Mathf.Clamp(Mathf.RoundToInt(pair.Key / (float)total * (width - 1)), 0, width - 1);
                    if (rank <= priority[x]) continue;
                    priority[x] = rank;
                    pixels[x] = rank switch { 3 => new Color32(255, 110, 90, 255), 2 => new Color32(242, 179, 61, 255), _ => new Color32(110, 210, 130, 200) };
                }
            marksTexture.SetPixels32(pixels);
            marksTexture.Apply(false, false);
            timelineMarks.texture = marksTexture;
        }

        void Follow(string id)
        {
            if (controller && controller.worldView) controller.worldView.FollowAgent(id);
            if (controller && controller.Playback != null) Render(controller.Trace, controller.Playback.State, controller.Paused, controller.Speed);
        }

        /// <summary>Botón "Seguir": recorre los agentes en el orden de las tarjetas.</summary>
        void FollowNext()
        {
            if (cards.Count == 0 || !controller || !controller.worldView) return;
            var world = controller.worldView;
            var followed = world.Rig ? world.Rig.FollowTarget : null;
            int current = cards.FindIndex(c => world.Agents.TryGetValue(c.AgentId, out var v) && v.transform == followed);
            Follow(cards[(current + 1) % cards.Count].AgentId);
        }

        public void ShowTooltip(string text, RectTransform target)
        {
            if (!tooltip || !tooltipText || string.IsNullOrEmpty(text)) return;
            tooltipText.text = text;
            tooltip.gameObject.SetActive(true);
            tooltip.SetAsLastSibling();
            var corners = new Vector3[4];
            target.GetWorldCorners(corners);
            var canvas = tooltip.GetComponentInParent<Canvas>();
            float scale = canvas ? canvas.scaleFactor : 1;
            // Sobre el botón si hay espacio; si no (barra superior), debajo.
            bool below = corners[1].y + 40 * scale > Screen.height;
            tooltip.pivot = new Vector2(.5f, below ? 1 : 0);
            var anchor = below ? (corners[0] + corners[3]) / 2 + Vector3.down * 6 * scale : (corners[1] + corners[2]) / 2 + Vector3.up * 6 * scale;
            tooltip.anchoredPosition = anchor / scale;
        }

        public void HideTooltip() { if (tooltip) tooltip.gameObject.SetActive(false); }

        void SetPanel(bool open)
        {
            if (bottomBar && sidePanel)
            {
                float panelSpace = sidePanel.rect.width + 2 * Mathf.Abs(sidePanel.offsetMax.x);
                bottomBar.offsetMax = new Vector2(open ? -panelSpace : -Mathf.Abs(sidePanel.offsetMax.x), bottomBar.offsetMax.y);
            }
            if (toast) ((RectTransform)toast.transform).anchoredPosition = new Vector2(open && sidePanel ? -sidePanel.rect.width / 2 : 0,
                ((RectTransform)toast.transform).anchoredPosition.y);
            if (sidePanel) sidePanel.gameObject.SetActive(open);
            if (panelOpenButton) panelOpenButton.gameObject.SetActive(!open);
            lastInsets = new Vector4(-1, -1, -1, -1);
        }

        void SetInteractable(bool value)
        {
            foreach (var button in new[] { playButton, backButton, forwardButton, restartButton, endButton })
                if (button) button.interactable = value;
            if (speedDropdown) speedDropdown.interactable = value;
            if (timeline) timeline.interactable = value;
        }

        public void ShowError(string message)
        {
            ShowStatus(message);
            if (header) header.text = "No se pudo cargar el trace";
            if (tickText) tickText.text = "";
            foreach (var text in new[] { agentsText, worldText, eventsText, metricsText }) if (text) text.text = "";
            foreach (var card in cards) if (card) Destroy(card.gameObject);
            cards.Clear(); shownTrace = null;
            SetFill(harvestFill, 0); SetFill(deliveredFill, 0);
            SetInteractable(false);
        }

        void Update()
        {
            if (!controller || controller.Playback == null || UiFocus.Typing) return;
            var keyboard = Keyboard.current;
            if (keyboard == null) return;
            if (keyboard.spaceKey.wasPressedThisFrame) controller.PlayPause();
            else if (keyboard.periodKey.wasPressedThisFrame) controller.StepForward();
            else if (keyboard.commaKey.wasPressedThisFrame) controller.StepBackward();
            else if (keyboard.homeKey.wasPressedThisFrame) controller.Restart();
            else if (keyboard.endKey.wasPressedThisFrame) controller.GoToEnd();
        }

        void LateUpdate()
        {
            // La cámara encuadra el campo en el área libre que dejan las barras y el panel.
            var rig = controller && controller.worldView ? controller.worldView.Rig : null;
            if (!rig || Screen.width <= 0 || Screen.height <= 0) return;
            var insets = CameraInsets();
            if ((insets - lastInsets).sqrMagnitude < 1e-6f) return;
            bool first = lastInsets.x < 0;
            lastInsets = insets;
            rig.overlayInsets = insets;
            if (first) rig.Frame();
        }

        /// <summary>Fracción de pantalla que tapan las barras y el panel (izquierda, derecha, arriba, abajo).</summary>
        public Vector4 CameraInsets()
        {
            var canvas = GetComponentInParent<Canvas>();
            var eye = canvas && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            float width = eye ? eye.pixelWidth : Screen.width, height = eye ? eye.pixelHeight : Screen.height;
            if (width <= 0 || height <= 0) return Vector4.zero;
            float Edge(RectTransform rect, int corner, bool horizontal)
            {
                var corners = new Vector3[4];
                rect.GetWorldCorners(corners);
                var point = RectTransformUtility.WorldToScreenPoint(eye, corners[corner]);
                return horizontal ? point.x : point.y;
            }
            float top = topBar && topBar.gameObject.activeInHierarchy ? Mathf.Clamp01((height - Edge(topBar, 0, false)) / height) : 0;
            float bottom = bottomBar && bottomBar.gameObject.activeInHierarchy ? Mathf.Clamp01(Edge(bottomBar, 1, false) / height) : 0;
            float right = sidePanel && sidePanel.gameObject.activeInHierarchy ? Mathf.Clamp01((width - Edge(sidePanel, 0, true)) / width) : 0;
            return new Vector4(0, right, top, bottom);
        }
    }
}
