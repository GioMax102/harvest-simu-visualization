using System;
using System.Collections.Generic;
using System.IO;
using HarvestSim.Visualization;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace HarvestSim.Editor
{
    /// <summary>
    /// Construye el HUD superpuesto (Canvas + SimulationHUD) y genera sus sprites (íconos y rectángulo redondeado)
    /// como PNG en Assets/Art/UI. Estilo: paneles de vidrio oscuro translúcido sobre la vista 3D, acento ámbar.
    /// </summary>
    public static class HarvestHudBuilder
    {
        public const string UiFolder = "Assets/Art/UI";
        public const float PanelWidth = 380, Margin = 12, TopHeight = 64, BottomHeight = 92;

        static readonly Color Glass = new(.07f, .09f, .08f, .84f);
        static readonly Color GlassSolid = new(.09f, .11f, .10f, .97f);
        static readonly Color Card = new(1, 1, 1, .06f);
        static readonly Color Control = new(1, 1, 1, .1f);
        static readonly Color TextMain = new(.94f, .95f, .92f);
        static readonly Color TextMuted = new(.64f, .68f, .64f);
        static readonly Color Accent = new(.95f, .70f, .24f);
        static readonly Color AccentText = new(.12f, .1f, .06f);
        static readonly Color Track = new(1, 1, 1, .12f);

        static TMP_FontAsset font;
        static Sprite rounded, circle;
        static readonly Dictionary<string, Sprite> icons = new();

        public static SimulationHUD Build(TMP_FontAsset fontAsset)
        {
            font = fontAsset;
            EnsureSprites();
            var canvasObject = new GameObject("HUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 900);
            scaler.matchWidthOrHeight = .5f;
            var hud = canvasObject.AddComponent<SimulationHUD>();
            var root = canvasObject.transform;

            BuildTopBar(hud, root);
            BuildToast(hud, root);
            BuildSidePanel(hud, root);
            BuildBottomBar(hud, root);
            BuildCameraBar(hud, root);
            BuildTooltip(hud, root);
            hud.ClearError();
            hud.SetServerState(ServerState.Unknown);
            return hud;
        }

        // ---------- Secciones ----------

        static void BuildTopBar(SimulationHUD hud, Transform root)
        {
            var bar = Panel("BarraSuperior", root, Glass);
            var rect = bar.rectTransform;
            rect.anchorMin = new Vector2(0, 1); rect.anchorMax = new Vector2(1, 1); rect.pivot = new Vector2(.5f, 1);
            rect.offsetMin = new Vector2(Margin, -Margin - TopHeight); rect.offsetMax = new Vector2(-Margin, -Margin);
            hud.topBar = rect;
            var row = HLayout(bar.gameObject, new RectOffset(18, 14, 8, 8), 12);

            var title = VLayout(Rect("Titulo", row).gameObject, new RectOffset(0, 0, 2, 2), 0);
            Layout(title.gameObject, minWidth: 250, flexibleWidth: 1);
            Label("Nombre", title, "HARVEST SIM", 18, Accent, FontStyles.Bold);
            hud.header = Label("Subtitulo", title, "", 13, TextMuted);

            hud.scenarioDropdown = Field(row, "Escenario", 190, parent => Dropdown("Escenario", parent, new List<string> { "escenario_a" }));
            hud.strategyDropdown = Field(row, "Estrategia", 160, parent => Dropdown("Estrategia", parent, new List<string> { "base", "subasta", "anticipada", "anticipada_plus", "qlearning", "sarsa" }));
            hud.seedInput = Field(row, "Semilla", 70, parent => Input("Semilla", parent, "0"));
            hud.simulateButton = TextButton("Simular", row, "Simular", Accent, AccentText, 110);
            hud.refreshScenariosButton = IconButton("ActualizarEscenarios", row, Icon("refresh"), 36, Control, out _);
            Tooltip(hud.refreshScenariosButton.gameObject, "Actualizar escenarios");

            var chip = Rect("Servidor", row);
            Layout(chip.gameObject, minWidth: 200, preferredHeight: 36);
            var chipRow = HLayout(chip.gameObject, new RectOffset(10, 10, 0, 0), 8);
            var dot = Image("Punto", chipRow, circle, new Color(.6f, .62f, .6f));
            Layout(dot.gameObject, preferredWidth: 12, preferredHeight: 12);
            hud.serverDot = dot;
            hud.serverText = Label("Estado", chipRow, "Servidor", 13, TextMain);
        }

        static void BuildToast(SimulationHUD hud, Transform root)
        {
            var toast = Panel("Aviso", root, new Color(.36f, .1f, .08f, .94f));
            var rect = toast.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, 1); rect.pivot = new Vector2(.5f, 1);
            rect.anchoredPosition = new Vector2(-PanelWidth / 2, -Margin * 2 - TopHeight);
            rect.sizeDelta = new Vector2(720, 0);
            var row = HLayout(toast.gameObject, new RectOffset(16, 10, 10, 10), 10);
            row.childAlignment = TextAnchor.UpperLeft;
            toast.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            hud.errorText = Label("Mensaje", row, "", 14, TextMain);
            hud.errorText.textWrappingMode = TextWrappingModes.Normal;
            Layout(hud.errorText.gameObject, flexibleWidth: 1);
            hud.toastClose = IconButton("Cerrar", row, Icon("close"), 26, new Color(1, 1, 1, .12f), out _);
            hud.toast = toast.gameObject;
        }

        static void BuildSidePanel(SimulationHUD hud, Transform root)
        {
            var panel = Panel("PanelLateral", root, Glass);
            var rect = panel.rectTransform;
            rect.anchorMin = new Vector2(1, 0); rect.anchorMax = new Vector2(1, 1); rect.pivot = new Vector2(1, .5f);
            rect.offsetMin = new Vector2(-Margin - PanelWidth, Margin); rect.offsetMax = new Vector2(-Margin, -Margin * 2 - TopHeight);
            hud.sidePanel = rect;
            var column = VLayout(panel.gameObject, new RectOffset(14, 10, 10, 12), 8);
            column.childForceExpandHeight = false;

            var headerRow = HLayout(Rect("Encabezado", column).gameObject, new RectOffset(4, 0, 0, 0), 6);
            Layout(headerRow.gameObject, preferredHeight: 30);
            var heading = Label("Titulo", headerRow, "SIMULACIÓN", 13, TextMuted, FontStyles.Bold);
            heading.characterSpacing = 4;
            Layout(heading.gameObject, flexibleWidth: 1);
            hud.panelToggleButton = IconButton("Plegar", headerRow, Icon("chevronRight"), 28, Control, out _);
            Tooltip(hud.panelToggleButton.gameObject, "Ocultar panel");

            // Contenido desplazable.
            var scrollRect = Rect("Desplazamiento", column);
            Layout(scrollRect.gameObject, flexibleHeight: 1, flexibleWidth: 1);
            var scroll = scrollRect.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 30;
            var viewport = Rect("Viewport", scrollRect); Stretch(viewport); viewport.gameObject.AddComponent<RectMask2D>();
            var content = Rect("Contenido", viewport);
            content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one; content.pivot = new Vector2(.5f, 1); content.sizeDelta = Vector2.zero;
            var contentLayout = VLayout(content.gameObject, new RectOffset(0, 6, 0, 8), 14);
            contentLayout.childForceExpandHeight = false;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = viewport; scroll.content = content;

            Section(content, "AGENTES");
            var cards = VLayout(Rect("Tarjetas", content).gameObject, new RectOffset(0, 0, 0, 0), 6);
            cards.childForceExpandHeight = false;
            hud.agentCards = (RectTransform)cards.transform;
            hud.cardTemplate = AgentCardTemplate(cards.transform);
            hud.agentsText = null;

            Section(content, "CAMPO");
            hud.harvestFill = BarRow(content, "Cosechado", Accent);
            hud.deliveredFill = BarRow(content, "Entregado", new Color(.4f, .78f, .52f));
            hud.worldText = Label("Campo", content, "", 13, TextMain);
            hud.worldText.textWrappingMode = TextWrappingModes.Normal;

            Section(content, "EVENTOS RECIENTES");
            hud.eventsText = Label("Eventos", content, "", 12.5f, TextMain);
            hud.eventsText.textWrappingMode = TextWrappingModes.Normal;
            hud.eventsText.lineSpacing = -6;

            Section(content, "RESULTADOS DEL ARCHIVO");
            hud.metricsText = Label("Metricas", content, "", 13, TextMain);

            Section(content, "LEYENDA");
            hud.legendText = Label("Legend", content, SimulationHUD.LegendText, 12, TextMain);
            hud.legendText.textWrappingMode = TextWrappingModes.Normal;

            // Botón para volver a abrir el panel plegado.
            var open = IconButton("AbrirPanel", root, Icon("chevronLeft"), 34, Glass, out _);
            var openRect = (RectTransform)open.transform;
            openRect.anchorMin = openRect.anchorMax = new Vector2(1, .5f); openRect.pivot = new Vector2(1, .5f);
            openRect.anchoredPosition = new Vector2(-Margin, 0); openRect.sizeDelta = new Vector2(34, 72);
            Tooltip(open.gameObject, "Mostrar panel");
            open.gameObject.SetActive(false);
            hud.panelOpenButton = open;
        }

        static void BuildBottomBar(SimulationHUD hud, Transform root)
        {
            var bar = Panel("BarraInferior", root, Glass);
            var rect = bar.rectTransform;
            rect.anchorMin = new Vector2(0, 0); rect.anchorMax = new Vector2(1, 0); rect.pivot = new Vector2(.5f, 0);
            rect.offsetMin = new Vector2(Margin + 150, Margin); rect.offsetMax = new Vector2(-Margin * 2 - PanelWidth, Margin + BottomHeight);
            hud.bottomBar = rect;
            var column = VLayout(bar.gameObject, new RectOffset(18, 18, 12, 10), 8);
            column.childForceExpandHeight = false;

            var timelineArea = Rect("LineaDeTiempo", column);
            Layout(timelineArea.gameObject, preferredHeight: 22, flexibleWidth: 1);
            hud.timeline = Timeline(timelineArea, out hud.timelineMarks);

            var row = HLayout(Rect("Controles", column).gameObject, new RectOffset(0, 0, 0, 0), 8);
            Layout(row.gameObject, preferredHeight: 40);
            hud.restartButton = IconButton("Reinicio", row, Icon("start"), 36, Control, out _);
            hud.backButton = IconButton("Atras", row, Icon("prev"), 36, Control, out _);
            hud.playButton = IconButton("PlayPause", row, Icon("play"), 42, Accent, out hud.playIcon);
            hud.playIcon.color = AccentText;
            hud.forwardButton = IconButton("Adelante", row, Icon("next"), 36, Control, out _);
            hud.endButton = IconButton("Fin", row, Icon("end"), 36, Control, out _);
            Tooltip(hud.restartButton.gameObject, "Reinicio (Inicio)"); Tooltip(hud.backButton.gameObject, "Tick anterior (,)");
            Tooltip(hud.playButton.gameObject, "Reproducir o pausar (Espacio)"); Tooltip(hud.forwardButton.gameObject, "Tick siguiente (.)");
            Tooltip(hud.endButton.gameObject, "Fin (Fin)");
            hud.playSprite = Icon("play"); hud.pauseSprite = Icon("pause");
            hud.playLabel = null;
            hud.tickText = Label("Tick", row, "", 15, TextMain);
            Layout(hud.tickText.gameObject, flexibleWidth: 1);
            hud.tickText.margin = new Vector4(10, 0, 0, 0);
            var speedLabel = Label("VelocidadEtiqueta", row, "Velocidad", 12, TextMuted);
            speedLabel.alignment = TextAlignmentOptions.MidlineRight;
            Layout(speedLabel.gameObject, preferredWidth: 70);
            var options = new List<string>();
            foreach (float speed in SimulationHUD.Speeds) options.Add(speed + " ticks/s");
            hud.speedDropdown = Dropdown("Velocidad", row, options);
            Layout(hud.speedDropdown.gameObject, preferredWidth: 120, preferredHeight: 32);
        }

        static void BuildCameraBar(SimulationHUD hud, Transform root)
        {
            var bar = Panel("Camara", root, Glass);
            var rect = bar.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0, 0); rect.pivot = new Vector2(0, 0);
            rect.anchoredPosition = new Vector2(Margin, Margin);
            rect.sizeDelta = new Vector2(150 - Margin, BottomHeight);
            var grid = rect.gameObject.AddComponent<GridLayoutGroup>();
            grid.padding = new RectOffset(8, 8, 8, 8); grid.spacing = new Vector2(6, 6);
            grid.cellSize = new Vector2(55, 35);
            hud.perspectiveButton = IconButton("Perspectiva", rect, Icon("orbit"), 35, Control, out _);
            hud.topButton = IconButton("Cenital", rect, Icon("top"), 35, Control, out _);
            hud.followButton = IconButton("Seguir", rect, Icon("follow"), 35, Control, out _);
            hud.frameButton = IconButton("Encuadrar", rect, Icon("frame"), 35, Control, out _);
            Tooltip(hud.perspectiveButton.gameObject, "Perspectiva 3D"); Tooltip(hud.topButton.gameObject, "Vista cenital");
            Tooltip(hud.followButton.gameObject, "Seguir agente"); Tooltip(hud.frameButton.gameObject, "Encuadrar campo (F)");
        }

        // ---------- Piezas ----------

        static HudAgentCard AgentCardTemplate(Component parent)
        {
            var card = Panel("TarjetaPlantilla", parent, Card);
            Layout(card.gameObject, minHeight: 92);
            var component = card.gameObject.AddComponent<HudAgentCard>();
            component.background = card;
            component.button = card.gameObject.AddComponent<Button>();
            component.button.targetGraphic = card;
            var colors = component.button.colors;
            colors.normalColor = Color.white; colors.highlightedColor = new Color(1.6f, 1.6f, 1.6f); colors.pressedColor = new Color(2f, 2f, 2f);
            colors.colorMultiplier = 2; component.button.colors = colors;

            var highlight = Image("Seleccion", card.transform, rounded, new Color(Accent.r, Accent.g, Accent.b, .18f));
            Stretch(highlight.rectTransform); highlight.raycastTarget = false; highlight.enabled = false;
            component.highlight = highlight;
            var bar = Image("Color", card.transform, rounded, Color.white);
            bar.rectTransform.anchorMin = new Vector2(0, 0); bar.rectTransform.anchorMax = new Vector2(0, 1);
            bar.rectTransform.offsetMin = new Vector2(6, 8); bar.rectTransform.offsetMax = new Vector2(11, -8);
            bar.pixelsPerUnitMultiplier = 6; bar.raycastTarget = false;
            component.colorBar = bar;

            var content = Rect("Contenido", card.transform); Stretch(content);
            var column = VLayout(content.gameObject, new RectOffset(20, 12, 8, 8), 3);
            column.childForceExpandHeight = false;
            var titleRow = HLayout(Rect("Titulo", column).gameObject, new RectOffset(0, 0, 0, 0), 6);
            component.title = Label("Id", titleRow, "agente", 14, TextMain, FontStyles.Bold);
            Layout(component.title.gameObject, flexibleWidth: 1);
            component.subtitle = Label("Tipo", titleRow, "", 12, TextMuted);
            component.subtitle.alignment = TextAlignmentOptions.MidlineRight;
            component.state = Label("Estado", column, "", 13, TextMain);
            component.loadFill = MiniBar(column, "Carga", Accent, out component.loadText, out _);
            component.fuelFill = MiniBar(column, "Combustible", new Color(.4f, .78f, .52f), out component.fuelText, out component.fuelFillImage);
            card.gameObject.SetActive(false);
            return component;
        }

        static RectTransform MiniBar(Component parent, string name, Color color, out TMP_Text text, out Image fillImage)
        {
            var row = HLayout(Rect(name, parent).gameObject, new RectOffset(0, 0, 0, 0), 8);
            row.childAlignment = TextAnchor.MiddleLeft;
            Layout(row.gameObject, preferredHeight: 16);
            text = Label("Texto", row, name, 11.5f, TextMuted);
            Layout(text.gameObject, preferredWidth: 140);
            var track = Image("Barra", row, rounded, Track);
            track.pixelsPerUnitMultiplier = 5; track.raycastTarget = false;
            Layout(track.gameObject, flexibleWidth: 1, preferredHeight: 6);
            fillImage = Image("Relleno", track.transform, rounded, color);
            fillImage.pixelsPerUnitMultiplier = 5; fillImage.raycastTarget = false;
            Stretch(fillImage.rectTransform);
            return fillImage.rectTransform;
        }

        static RectTransform BarRow(Component parent, string name, Color color)
        {
            var fill = MiniBar(parent, name, color, out var text, out _);
            text.fontSize = 12.5f; text.color = TextMain;
            return fill;
        }

        static void Section(Component parent, string title)
        {
            var label = Label("Seccion_" + title, parent, title, 11.5f, TextMuted, FontStyles.Bold);
            label.characterSpacing = 6;
            label.margin = new Vector4(0, 6, 0, 0);
        }

        static T Field<T>(Component row, string caption, float width, Func<Transform, T> create) where T : Component
        {
            var group = VLayout(Rect("Campo_" + caption, row).gameObject, new RectOffset(0, 0, 0, 0), 2);
            group.childForceExpandHeight = false;
            Layout(group.gameObject, preferredWidth: width, flexibleWidth: 0);
            Label("Etiqueta", group, caption, 10.5f, TextMuted);
            var control = create(group.transform);
            Layout(control.gameObject, preferredHeight: 28, preferredWidth: width);
            return control;
        }

        static Slider Timeline(Component parent, out RawImage marks)
        {
            var go = DefaultControls.CreateSlider(UiResources());
            go.name = "Linea";
            go.transform.SetParent(parent.transform, false);
            Stretch((RectTransform)go.transform);
            var slider = go.GetComponent<Slider>();
            slider.wholeNumbers = true; slider.minValue = 0; slider.maxValue = 1;
            var background = go.transform.Find("Background").GetComponent<Image>();
            background.color = Track; background.pixelsPerUnitMultiplier = 4;
            ((RectTransform)background.transform).anchorMin = new Vector2(0, .3f);
            ((RectTransform)background.transform).anchorMax = new Vector2(1, .7f);
            var fillArea = (RectTransform)go.transform.Find("Fill Area");
            fillArea.anchorMin = new Vector2(0, .3f); fillArea.anchorMax = new Vector2(1, .7f);
            var fill = fillArea.Find("Fill").GetComponent<Image>();
            fill.color = new Color(Accent.r, Accent.g, Accent.b, .55f); fill.pixelsPerUnitMultiplier = 4;
            var markObject = new GameObject("Marcas", typeof(RectTransform), typeof(RawImage));
            markObject.transform.SetParent(go.transform, false);
            markObject.transform.SetSiblingIndex(fillArea.GetSiblingIndex() + 1);
            var markRect = (RectTransform)markObject.transform;
            markRect.anchorMin = new Vector2(0, .1f); markRect.anchorMax = new Vector2(1, .9f);
            markRect.offsetMin = new Vector2(5, 0); markRect.offsetMax = new Vector2(-5, 0);
            marks = markObject.GetComponent<RawImage>(); marks.raycastTarget = false;
            marks.color = new Color(1, 1, 1, .9f);
            var handle = go.transform.Find("Handle Slide Area/Handle").GetComponent<Image>();
            handle.sprite = circle; handle.color = Color.white;
            ((RectTransform)handle.transform).sizeDelta = new Vector2(16, 0);
            return slider;
        }

        static TMP_Dropdown Dropdown(string name, Component parent, List<string> options)
        {
            var go = TMP_DefaultControls.CreateDropdown(TmpResources());
            go.name = name; go.transform.SetParent(parent.transform, false);
            var dropdown = go.GetComponent<TMP_Dropdown>();
            dropdown.ClearOptions(); dropdown.AddOptions(options);
            foreach (var text in go.GetComponentsInChildren<TMP_Text>(true))
            {
                text.font = font; text.fontSize = 13; text.color = TextMain; text.richText = false;
            }
            go.GetComponent<Image>().color = Control;
            var arrow = go.transform.Find("Arrow").GetComponent<Image>();
            arrow.sprite = Icon("chevronDown"); arrow.color = TextMuted;
            var template = go.transform.Find("Template");
            template.GetComponent<Image>().color = GlassSolid;
            var item = template.Find("Viewport/Content/Item");
            var toggle = item.GetComponent<Toggle>();
            var colors = toggle.colors;
            colors.normalColor = new Color(1, 1, 1, 0); colors.highlightedColor = new Color(1, 1, 1, .12f);
            colors.pressedColor = new Color(1, 1, 1, .2f); colors.selectedColor = new Color(1, 1, 1, .08f);
            toggle.colors = colors;
            item.Find("Item Background").GetComponent<Image>().color = Color.white;
            item.Find("Item Checkmark").GetComponent<Image>().color = Accent;
            var scrollbar = template.Find("Scrollbar");
            if (scrollbar)
            {
                scrollbar.GetComponent<Image>().color = new Color(1, 1, 1, .05f);
                scrollbar.Find("Sliding Area/Handle").GetComponent<Image>().color = new Color(1, 1, 1, .3f);
            }
            return dropdown;
        }

        static TMP_InputField Input(string name, Component parent, string value)
        {
            var go = TMP_DefaultControls.CreateInputField(TmpResources());
            go.name = name; go.transform.SetParent(parent.transform, false);
            go.GetComponent<Image>().color = Control;
            var field = go.GetComponent<TMP_InputField>();
            foreach (var text in go.GetComponentsInChildren<TMP_Text>(true)) { text.font = font; text.fontSize = 13; text.color = TextMain; }
            if (field.placeholder is TMP_Text placeholder) { placeholder.text = "Semilla"; placeholder.color = TextMuted; }
            field.contentType = TMP_InputField.ContentType.IntegerNumber;
            field.text = value;
            field.caretColor = Accent; field.selectionColor = new Color(Accent.r, Accent.g, Accent.b, .35f);
            return field;
        }

        static Button TextButton(string name, Component parent, string text, Color background, Color textColor, float width)
        {
            var image = Panel(name, parent, background);
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            Layout(image.gameObject, preferredWidth: width, preferredHeight: 36);
            var label = Label("Texto", image.transform, text, 15, textColor, FontStyles.Bold);
            label.alignment = TextAlignmentOptions.Center;
            Stretch(label.rectTransform);
            return button;
        }

        static Button IconButton(string name, Component parent, Sprite icon, float size, Color background, out Image iconImage)
        {
            var image = Panel(name, parent, background);
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.highlightedColor = new Color(1.3f, 1.3f, 1.3f); colors.pressedColor = new Color(.8f, .8f, .8f);
            colors.disabledColor = new Color(1, 1, 1, .35f); colors.colorMultiplier = 1.5f;
            button.colors = colors;
            Layout(image.gameObject, preferredWidth: size, preferredHeight: size);
            ((RectTransform)image.transform).sizeDelta = new Vector2(size, size);
            iconImage = Image("Icono", image.transform, icon, TextMain);
            iconImage.raycastTarget = false; iconImage.preserveAspect = true;
            var r = iconImage.rectTransform; r.anchorMin = new Vector2(.2f, .2f); r.anchorMax = new Vector2(.8f, .8f);
            r.offsetMin = r.offsetMax = Vector2.zero;
            return button;
        }

        static void Tooltip(GameObject go, string text) => go.AddComponent<HudTooltip>().text = text;

        static void BuildTooltip(SimulationHUD hud, Transform root)
        {
            var panel = Panel("Tooltip", root, GlassSolid);
            var rect = panel.rectTransform;
            rect.anchorMin = rect.anchorMax = Vector2.zero; rect.pivot = new Vector2(.5f, 0);
            panel.raycastTarget = false;
            var row = HLayout(panel.gameObject, new RectOffset(10, 10, 5, 5), 0);
            var fitter = panel.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            hud.tooltipText = Label("Texto", row, "", 12.5f, TextMain);
            hud.tooltip = rect;
            panel.gameObject.SetActive(false);
        }

        static Image Panel(string name, Component parent, Color color)
        {
            var image = Image(name, parent, rounded, color);
            image.type = UnityEngine.UI.Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = 1.4f;
            return image;
        }

        static Image Image(string name, Component parent, Sprite sprite, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent.transform, false);
            var image = go.GetComponent<Image>();
            image.sprite = sprite; image.color = color;
            if (sprite && sprite.border != Vector4.zero) image.type = UnityEngine.UI.Image.Type.Sliced;
            return image;
        }

        static TMP_Text Label(string name, Component parent, string text, float size, Color color, FontStyles style = FontStyles.Normal)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent.transform, false);
            var label = go.AddComponent<TextMeshProUGUI>();
            label.font = font; label.fontSize = size; label.color = color; label.fontStyle = style;
            label.text = text; label.richText = true; label.raycastTarget = false;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            // Overflow: con Ellipsis, TMP oculta el renglón entero si el alto calculado queda apenas por debajo de la línea.
            label.overflowMode = TextOverflowModes.Overflow;
            label.alignment = TextAlignmentOptions.MidlineLeft;
            return label;
        }

        static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        static RectTransform Rect(string name, Component parent) => Rect(name, parent.transform);

        static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        static HorizontalLayoutGroup HLayout(GameObject go, RectOffset padding, float spacing)
        {
            var layout = go.AddComponent<HorizontalLayoutGroup>();
            layout.padding = padding; layout.spacing = spacing;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            return layout;
        }

        static VerticalLayoutGroup VLayout(GameObject go, RectOffset padding, float spacing)
        {
            var layout = go.AddComponent<VerticalLayoutGroup>();
            layout.padding = padding; layout.spacing = spacing;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
            return layout;
        }

        static LayoutElement Layout(GameObject go, float minWidth = -1, float minHeight = -1, float preferredWidth = -1,
            float preferredHeight = -1, float flexibleWidth = -1, float flexibleHeight = -1)
        {
            if (!go.TryGetComponent<LayoutElement>(out var element)) element = go.AddComponent<LayoutElement>();
            element.minWidth = minWidth; element.minHeight = minHeight;
            element.preferredWidth = preferredWidth; element.preferredHeight = preferredHeight;
            element.flexibleWidth = flexibleWidth; element.flexibleHeight = flexibleHeight;
            return element;
        }

        static TMP_DefaultControls.Resources TmpResources() => new()
        {
            standard = rounded, background = rounded, inputField = rounded, knob = circle,
            checkmark = Icon("check"), dropdown = Icon("chevronDown"), mask = rounded
        };

        static DefaultControls.Resources UiResources() => new()
        {
            standard = rounded, background = rounded, inputField = rounded, knob = circle,
            checkmark = Icon("check"), dropdown = Icon("chevronDown"), mask = rounded
        };

        // ---------- Sprites generados ----------

        static Sprite Icon(string name) => icons.TryGetValue(name, out var sprite) ? sprite : null;

        public static void EnsureSprites()
        {
            Directory.CreateDirectory(UiFolder);
            rounded = SaveSprite("Redondeado", RoundedRect(64, 16), new Vector4(20, 20, 20, 20));
            circle = SaveSprite("Circulo", Raster(64, p => (p - new Vector2(.5f, .5f)).magnitude <= .46f), Vector4.zero);
            foreach (var pair in IconShapes())
                icons[pair.Key] = SaveSprite("Icono_" + pair.Key, Raster(64, pair.Value), Vector4.zero);
        }

        static Sprite SaveSprite(string name, Texture2D texture, Vector4 border)
        {
            string path = $"{UiFolder}/{name}.png";
            File.WriteAllBytes(path, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.spriteBorder = border;
            importer.spritePixelsPerUnit = 100;
            importer.filterMode = FilterMode.Bilinear;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        static Texture2D RoundedRect(int size, float radius)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float half = size / 2f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    var p = new Vector2(Mathf.Abs(x + .5f - half), Mathf.Abs(y + .5f - half));
                    var q = p - new Vector2(half - radius, half - radius);
                    float d = new Vector2(Mathf.Max(q.x, 0), Mathf.Max(q.y, 0)).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0) - radius;
                    texture.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(.5f - d)));
                }
            texture.Apply();
            return texture;
        }

        static Texture2D Raster(int size, Func<Vector2, bool> inside)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            const int samples = 4;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    int hits = 0;
                    for (int sy = 0; sy < samples; sy++)
                        for (int sx = 0; sx < samples; sx++)
                            if (inside(new Vector2((x + (sx + .5f) / samples) / size, (y + (sy + .5f) / samples) / size))) hits++;
                    texture.SetPixel(x, y, new Color(1, 1, 1, hits / (float)(samples * samples)));
                }
            texture.Apply();
            return texture;
        }

        static Func<Vector2, bool> Poly(params float[] xy)
        {
            var points = new Vector2[xy.Length / 2];
            for (int i = 0; i < points.Length; i++) points[i] = new Vector2(xy[i * 2], xy[i * 2 + 1]);
            return p =>
            {
                bool inside = false;
                for (int i = 0, j = points.Length - 1; i < points.Length; j = i++)
                    if ((points[i].y > p.y) != (points[j].y > p.y) &&
                        p.x < (points[j].x - points[i].x) * (p.y - points[i].y) / (points[j].y - points[i].y) + points[i].x)
                        inside = !inside;
                return inside;
            };
        }

        static Func<Vector2, bool> Box(float x0, float y0, float x1, float y1) => p => p.x >= x0 && p.x <= x1 && p.y >= y0 && p.y <= y1;
        static Func<Vector2, bool> Ring(float inner, float outer) => p => { float d = (p - new Vector2(.5f, .5f)).magnitude; return d >= inner && d <= outer; };
        static Func<Vector2, bool> Disc(float radius) => p => (p - new Vector2(.5f, .5f)).magnitude <= radius;
        static Func<Vector2, bool> Union(params Func<Vector2, bool>[] shapes) => p => { foreach (var s in shapes) if (s(p)) return true; return false; };
        static Func<Vector2, bool> Mirror(Func<Vector2, bool> shape) => p => shape(new Vector2(1 - p.x, p.y));

        static Dictionary<string, Func<Vector2, bool>> IconShapes()
        {
            var next = Union(Poly(.24f, .22f, .24f, .78f, .62f, .5f), Box(.64f, .22f, .76f, .78f));
            var end = Union(Poly(.14f, .24f, .14f, .76f, .45f, .5f), Poly(.43f, .24f, .43f, .76f, .74f, .5f), Box(.76f, .24f, .86f, .76f));
            var chevronLeft = Poly(.62f, .12f, .71f, .21f, .42f, .5f, .71f, .79f, .62f, .88f, .24f, .5f);
            return new Dictionary<string, Func<Vector2, bool>>
            {
                ["play"] = Poly(.3f, .18f, .3f, .82f, .82f, .5f),
                ["pause"] = Union(Box(.26f, .2f, .42f, .8f), Box(.58f, .2f, .74f, .8f)),
                ["next"] = next,
                ["prev"] = Mirror(next),
                ["end"] = end,
                ["start"] = Mirror(end),
                ["chevronLeft"] = chevronLeft,
                ["chevronRight"] = Mirror(chevronLeft),
                ["chevronDown"] = Poly(.12f, .64f, .21f, .73f, .5f, .44f, .79f, .73f, .88f, .64f, .5f, .26f),
                ["check"] = Poly(.14f, .52f, .24f, .42f, .42f, .6f, .78f, .22f, .88f, .32f, .42f, .8f),
                ["close"] = Union(Poly(.22f, .3f, .3f, .22f, .78f, .7f, .7f, .78f), Poly(.7f, .22f, .78f, .3f, .3f, .78f, .22f, .7f)),
                ["orbit"] = Union(Disc(.13f), p =>
                {
                    var d = p - new Vector2(.5f, .5f);
                    float outer = d.x * d.x / (.44f * .44f) + d.y * d.y / (.2f * .2f);
                    float inner = d.x * d.x / (.36f * .36f) + d.y * d.y / (.13f * .13f);
                    return outer <= 1 && inner >= 1;
                }),
                ["top"] = Union(Box(.18f, .18f, .82f, .26f), Box(.18f, .74f, .82f, .82f), Box(.18f, .18f, .26f, .82f), Box(.74f, .18f, .82f, .82f),
                    Box(.46f, .18f, .54f, .82f), Box(.18f, .46f, .82f, .54f)),
                ["follow"] = Union(Ring(.2f, .28f), Disc(.08f), Box(.47f, .04f, .53f, .2f), Box(.47f, .8f, .53f, .96f),
                    Box(.04f, .47f, .2f, .53f), Box(.8f, .47f, .96f, .53f)),
                ["frame"] = Union(Box(.14f, .14f, .38f, .21f), Box(.14f, .14f, .21f, .38f), Box(.62f, .14f, .86f, .21f), Box(.79f, .14f, .86f, .38f),
                    Box(.14f, .79f, .38f, .86f), Box(.14f, .62f, .21f, .86f), Box(.62f, .79f, .86f, .86f), Box(.79f, .62f, .86f, .86f)),
                ["refresh"] = Union(p =>
                {
                    var d = p - new Vector2(.5f, .5f);
                    float r = d.magnitude, angle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
                    return r >= .24f && r <= .33f && !(angle > 15 && angle < 75);
                }, Poly(.62f, .9f, .9f, .66f, .6f, .56f)),
            };
        }
    }
}
