using System.IO;
using HarvestSim.Visualization;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace HarvestSim.Editor
{
    public static class HarvestSceneBuilder
    {
        public const string ScenePath = "Assets/Scenes/HarvestPlayback.unity";
        static TMP_FontAsset font;

        [MenuItem("Harvest Sim/Crear o abrir escena MVP")]
        public static void CreateScene()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (File.Exists(ScenePath))
            {
                EditorSceneManager.OpenScene(ScenePath);
                return; // Conservar los cambios manuales de una escena ya generada.
            }
            EnsureFont();
            Directory.CreateDirectory("Assets/Prefabs/Harvest");
            Directory.CreateDirectory("Assets/Materials/Harvest");
            Directory.CreateDirectory("Assets/Scenes");
            AssetDatabase.Refresh();
            var crop = Material("Cultivo", new Color(.86f, .74f, .34f));
            var harvested = Material("Cosechado", new Color(.90f, .89f, .84f));
            var service = Material("Servicio", new Color(.70f, .78f, .80f));
            var structure = Material("Estructura", new Color(.25f, .35f, .40f));
            var obstacle = Material("Obstaculo", new Color(.32f, .33f, .31f));
            var green = Material("Cosechadora", new Color(.15f, .46f, .29f));
            var blue = Material("Carro", new Color(.15f, .43f, .70f));
            var white = Material("Frente", Color.white);
            var cell = Primitive("Cell", PrimitiveType.Cube, null, Vector3.zero, new Vector3(.95f, .1f, .95f), crop);
            var cellPrefab = SavePrefab(cell, "Cell");
            var harvester = AgentPrefab("Harvester", PrimitiveType.Cube, green, white);
            var cart = AgentPrefab("Cart", PrimitiveType.Cylinder, blue, white);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var controller = new GameObject("SimulationRoot").AddComponent<PlaybackController>();
            var world = new GameObject("WorldRoot").AddComponent<WorldView>();
            world.cellsRoot = Empty("CellsRoot", world.transform);
            world.structuresRoot = Empty("StructuresRoot", world.transform);
            world.agentsRoot = Empty("AgentsRoot", world.transform);
            world.cellPrefab = cellPrefab; world.harvesterPrefab = harvester; world.cartPrefab = cart;
            world.cropMaterial = crop; world.harvestedMaterial = harvested; world.serviceMaterial = service;
            world.structureMaterial = structure; world.obstacleMaterial = obstacle;
            var camera = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener)).GetComponent<Camera>();
            camera.tag = "MainCamera"; camera.orthographic = true; camera.orthographicSize = 6;
            camera.transform.position = new Vector3(4, 20, -3); camera.transform.rotation = Quaternion.Euler(90, 0, 0);
            camera.rect = new Rect(0, 0, .65f, 1); camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.96f, .95f, .91f);
            camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;
            world.mapCamera = camera;
            var light = new GameObject("Directional Light").AddComponent<Light>();
            light.type = LightType.Directional; light.intensity = 1;
            light.shadows = LightShadows.Soft; light.shadowStrength = .65f;
            light.transform.rotation = Quaternion.Euler(60, -30, 0);
            var hud = BuildHUD();
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            controller.worldView = world; controller.hud = hud;
            controller.initialTrace = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Data/Traces/escenario_a.json");
            if (!controller.initialTrace) throw new FileNotFoundException("Falta Assets/Data/Traces/escenario_a.json");
            controller.initialSpeed = 8;
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = controller.gameObject;
            Debug.Log("Harvest MVP listo. Pulsa Play; no hace falta posicionar objetos ni asignar scripts. La escena no altera Build Profiles.");
        }

        static void EnsureFont()
        {
            const string path = "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";
            font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (font) return;
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(TMP_Text).Assembly);
            AssetDatabase.ImportPackage(Path.Combine(package.resolvedPath, "Package Resources/TMP Essential Resources.unitypackage"), false);
            AssetDatabase.Refresh();
            font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (!font) throw new FileNotFoundException("Importa Window > TextMeshPro > Import TMP Essential Resources y vuelve a generar la escena.");
        }
        static Material Material(string name, Color color)
        {
            string path = "Assets/Materials/Harvest/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material) return material;
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (!shader) throw new System.InvalidOperationException("No se encuentra el shader URP/Lit.");
            material = new Material(shader) { name = name, color = color };
            AssetDatabase.CreateAsset(material, path); return material;
        }
        static Transform Empty(string name, Transform parent)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false); return go.transform;
        }
        static GameObject Primitive(string name, PrimitiveType type, Transform parent, Vector3 position, Vector3 scale, Material material)
        {
            var go = GameObject.CreatePrimitive(type); go.name = name; go.transform.SetParent(parent, false);
            go.transform.localPosition = position; go.transform.localScale = scale;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.GetComponent<Renderer>().sharedMaterial = material;
            return go;
        }
        static GameObject SavePrefab(GameObject instance, string name)
        {
            string path = "Assets/Prefabs/Harvest/" + name + ".prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (!prefab) prefab = PrefabUtility.SaveAsPrefabAsset(instance, path);
            Object.DestroyImmediate(instance); return prefab;
        }
        static AgentView AgentPrefab(string name, PrimitiveType type, Material body, Material front)
        {
            var root = new GameObject(name); root.AddComponent<AgentView>();
            Primitive("Body", type, root.transform, Vector3.zero, new Vector3(.6f, type == PrimitiveType.Cylinder ? .25f : .5f, .6f), body);
            Primitive("Front", PrimitiveType.Cube, root.transform, new Vector3(0, .28f, .25f), new Vector3(.16f, .12f, .25f), front);
            return SavePrefab(root, name).GetComponent<AgentView>();
        }
        static RectTransform Rect(string name, Transform parent)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false); return rect;
        }
        static void Stretch(RectTransform rect)
        { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; }
        static TMP_Text Text(string name, Transform parent, string value, float size = 20)
        {
            var text = Rect(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
            text.font = font; text.fontSize = size; text.text = value; text.richText = false;
            text.color = new Color(.1f, .19f, .15f); text.raycastTarget = false;
            return text;
        }
        static Button Button(string name, Transform parent, string value, out TMP_Text label)
        {
            var rect = Rect(name, parent);
            var image = rect.gameObject.AddComponent<Image>(); image.color = new Color(.82f, .89f, .84f);
            var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = image;
            var layout = rect.gameObject.AddComponent<LayoutElement>(); layout.minHeight = 42; layout.flexibleWidth = 1;
            label = Text("Label", rect, value, 18); Stretch(label.rectTransform); label.alignment = TextAlignmentOptions.Center;
            return button;
        }
        static RectTransform Row(string name, Transform parent)
        {
            var row = Rect(name, parent);
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 6; layout.childControlHeight = true; layout.childControlWidth = true;
            layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
            row.gameObject.AddComponent<LayoutElement>().minHeight = 44;
            return row;
        }
        static SimulationHUD BuildHUD()
        {
            var canvas = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvas.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 900); scaler.matchWidthOrHeight = .5f;
            var hud = canvas.AddComponent<SimulationHUD>();
            var panel = Rect("HUD", canvas.transform); Stretch(panel); panel.anchorMin = new Vector2(.65f, 0);
            panel.gameObject.AddComponent<Image>().color = new Color(.96f, .98f, .96f);
            var scroll = panel.gameObject.AddComponent<ScrollRect>(); scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped;
            var viewport = Rect("Viewport", panel); Stretch(viewport); viewport.gameObject.AddComponent<RectMask2D>();
            var content = Rect("Content", viewport); content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one;
            content.pivot = new Vector2(.5f, 1); content.sizeDelta = Vector2.zero;
            var layout = content.gameObject.AddComponent<VerticalLayoutGroup>(); layout.padding = new RectOffset(18, 18, 18, 18);
            layout.spacing = 12; layout.childControlWidth = layout.childControlHeight = true; layout.childForceExpandHeight = false;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = viewport; scroll.content = content;
            hud.header = Text("Header", content, "HARVEST", 24);
            hud.errorText = Text("Error", content, "", 20); hud.errorText.color = new Color(.7f, .1f, .1f); hud.errorText.gameObject.SetActive(false);
            var row = Row("Playback", content);
            hud.playButton = Button("PlayPause", row, "Reproducir", out hud.playLabel);
            hud.backButton = Button("Previous", row, "Atrás", out _);
            hud.forwardButton = Button("Next", row, "Adelante", out _);
            row = Row("Navigation", content);
            hud.restartButton = Button("Restart", row, "Reinicio", out _);
            hud.endButton = Button("End", row, "Fin", out _);
            Text("SpeedLabel", content, "Velocidad (ticks/s)", 18);
            var speed = TMP_DefaultControls.CreateDropdown(new TMP_DefaultControls.Resources());
            speed.name = "Speed"; speed.transform.SetParent(content, false);
            speed.AddComponent<LayoutElement>().minHeight = 40;
            hud.speedDropdown = speed.GetComponent<TMP_Dropdown>();
            hud.speedDropdown.ClearOptions(); hud.speedDropdown.AddOptions(new System.Collections.Generic.List<string> { "1", "4", "8", "16" });
            foreach (var text in speed.GetComponentsInChildren<TMP_Text>(true)) { text.font = font; text.fontSize = 20; text.richText = false; }
            Text("Legend", content, SimulationHUD.LegendText, 17);
            hud.worldText = Text("World", content, "");
            hud.agentsText = Text("Agents", content, "");
            hud.eventsText = Text("Events", content, "", 16);
            hud.metricsText = Text("FinalMetrics", content, "", 17);
            return hud;
        }
    }
}
