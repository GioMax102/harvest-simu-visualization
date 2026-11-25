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
            // Respaldo con primitivas si no existen los prefabs con modelos.
            if (!AssetDatabase.LoadAssetAtPath<AgentView>("Assets/Prefabs/Harvest/Harvester.prefab"))
                AgentPrefab("Harvester", PrimitiveType.Cube, Material("Cosechadora", new Color(.15f, .46f, .29f)), Material("Frente", Color.white));
            if (!AssetDatabase.LoadAssetAtPath<AgentView>("Assets/Prefabs/Harvest/Cart.prefab"))
                AgentPrefab("Cart", PrimitiveType.Cylinder, Material("Carro", new Color(.15f, .43f, .70f)), Material("Frente", Color.white));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var controller = new GameObject("SimulationRoot").AddComponent<PlaybackController>();
            var world = new GameObject("WorldRoot").AddComponent<WorldView>();
            world.cellsRoot = Empty("CellsRoot", world.transform);
            world.structuresRoot = Empty("StructuresRoot", world.transform);
            world.agentsRoot = Empty("AgentsRoot", world.transform);
            var camera = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener)).GetComponent<Camera>();
            camera.tag = "MainCamera";
            world.mapCamera = camera;
            var light = new GameObject("Directional Light").AddComponent<Light>();
            light.type = LightType.Directional;
            var hud = BuildHUD();
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            controller.worldView = world; controller.hud = hud;
            controller.initialTrace = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Data/Traces/escenario_a.json");
            if (!controller.initialTrace) throw new FileNotFoundException("Falta Assets/Data/Traces/escenario_a.json");
            controller.initialSpeed = 8;
            ConfigureWorld(world, camera, light);
            EditorSceneManager.SaveScene(scene, ScenePath);
            BakeEnvironment();
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = controller.gameObject;
            Debug.Log("Harvest listo. Pulsa Play; no hace falta posicionar objetos ni asignar scripts. La escena no altera Build Profiles.");
        }

        /// <summary>Aplica la vista realista a la escena existente sin tocar el HUD ni el controlador.</summary>
        [MenuItem("Harvest Sim/Actualizar escena realista")]
        public static void UpgradeScene()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (!File.Exists(ScenePath)) { CreateScene(); return; }
            var scene = EditorSceneManager.OpenScene(ScenePath);
            var world = Object.FindFirstObjectByType<WorldView>();
            if (!world || !world.mapCamera) throw new System.InvalidOperationException("La escena no tiene WorldRoot con cámara asignada.");
            Light sun = null;
            foreach (var light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (light.type == LightType.Directional) { sun = light; break; }
            if (!sun) { sun = new GameObject("Directional Light").AddComponent<Light>(); sun.type = LightType.Directional; }
            ConfigureWorld(world, world.mapCamera, sun);
            ReplaceHud(Object.FindFirstObjectByType<PlaybackController>());
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            BakeEnvironment();
            AssetDatabase.SaveAssets();
            Debug.Log("Escena actualizada con la vista realista.");
        }

        /// <summary>Sustituye el HUD de la escena por el HUD superpuesto y lo conecta al controlador.</summary>
        static void ReplaceHud(PlaybackController controller)
        {
            EnsureFont();
            foreach (var old in Object.FindObjectsByType<SimulationHUD>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(old.gameObject);
            var hud = BuildHUD();
            if (controller) { controller.hud = hud; EditorUtility.SetDirty(controller); }
            if (!Object.FindFirstObjectByType<EventSystem>())
                new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        }

        static void ConfigureWorld(WorldView world, Camera camera, Light sun)
        {
            var art = HarvestArtBuilder.Build();
            world.art = art;
            EditorUtility.SetDirty(world);

            camera.orthographic = false;
            camera.rect = new Rect(0, 0, 1, 1);
            camera.fieldOfView = 45;
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.nearClipPlane = .5f; camera.farClipPlane = 3000;
            if (!camera.GetComponent<CameraRig>()) camera.gameObject.AddComponent<CameraRig>();
            var cameraData = camera.GetUniversalAdditionalCameraData();
            cameraData.renderPostProcessing = true;
            cameraData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            cameraData.antialiasingQuality = AntialiasingQuality.High;
            EditorUtility.SetDirty(camera);

            var (sunDirection, horizon) = HarvestArtBuilder.AnalyzeSky();
            // El sol del HDRI puede quedar muy alto o muy bajo; se limita para que las sombras se lean sobre el campo.
            var flat = new Vector3(sunDirection.x, 0, sunDirection.z);
            if (flat.sqrMagnitude < 1e-4f) flat = new Vector3(-.5f, 0, -.8f);
            float elevation = Mathf.Clamp(Mathf.Asin(Mathf.Clamp(sunDirection.y, -1, 1)) * Mathf.Rad2Deg, 28, 58);
            var direction = (flat.normalized * Mathf.Cos(elevation * Mathf.Deg2Rad) + Vector3.up * Mathf.Sin(elevation * Mathf.Deg2Rad)).normalized;
            sun.transform.rotation = Quaternion.LookRotation(-direction);
            sun.intensity = 2.1f;
            sun.color = new Color(1f, .95f, .87f);
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = .88f;
            sun.shadowBias = .25f; sun.shadowNormalBias = .4f;
            sun.name = "Sol";
            EditorUtility.SetDirty(sun);

            var skybox = AssetDatabase.LoadAssetAtPath<Material>(HarvestArtBuilder.MaterialsFolder + "/Cielo.mat");
            if (!skybox)
            {
                skybox = new Material(Shader.Find("Skybox/Panoramic")) { name = "Cielo" };
                AssetDatabase.CreateAsset(skybox, HarvestArtBuilder.MaterialsFolder + "/Cielo.mat");
            }
            skybox.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(HarvestArtBuilder.SkyPath));
            skybox.SetFloat("_Mapping", 1);
            skybox.SetFloat("_ImageType", 0);
            skybox.SetFloat("_Exposure", .95f);
            skybox.SetFloat("_Rotation", 0);
            EditorUtility.SetDirty(skybox);
            RenderSettings.skybox = skybox;
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Skybox;
            RenderSettings.ambientIntensity = 1f;
            RenderSettings.defaultReflectionMode = UnityEngine.Rendering.DefaultReflectionMode.Skybox;
            RenderSettings.reflectionIntensity = .8f;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = Color.Lerp(horizon, new Color(.78f, .8f, .82f), .35f);
            RenderSettings.fogStartDistance = 350;
            RenderSettings.fogEndDistance = 1900;

            var volumeObject = GameObject.Find("Global Volume");
            if (!volumeObject) volumeObject = new GameObject("Global Volume");
            var volume = volumeObject.GetComponent<UnityEngine.Rendering.Volume>();
            if (!volume) volume = volumeObject.AddComponent<UnityEngine.Rendering.Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.VolumeProfile>(HarvestArtBuilder.VolumePath);
            EditorUtility.SetDirty(volume);

            ConfigurePipeline();
        }

        /// <summary>Sombras para una escena de cientos de metros (la cámara orbita el campo completo).</summary>
        static void ConfigurePipeline()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:UniversalRenderPipelineAsset", new[] { "Assets/Settings" }))
            {
                var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(AssetDatabase.GUIDToAssetPath(guid));
                if (!asset) continue;
                asset.shadowDistance = 450;
                asset.shadowCascadeCount = 4;
                asset.cascade4Split = new Vector3(.05f, .15f, .38f);
                asset.mainLightShadowmapResolution = 2048;
                EditorUtility.SetDirty(asset);
            }
        }

        /// <summary>Genera la luz ambiental y el reflejo del cielo (sin lightmaps) para que la escena no se vea plana.</summary>
        static void BakeEnvironment()
        {
            LightingSettings settings = null;
            try { settings = Lightmapping.lightingSettings; } catch (System.Exception) { }
            if (!settings || !AssetDatabase.Contains(settings))
            {
                string path = HarvestArtBuilder.GeneratedFolder + "/HarvestLighting.lighting";
                settings = AssetDatabase.LoadAssetAtPath<LightingSettings>(path);
                if (!settings) { settings = new LightingSettings { name = "HarvestLighting" }; AssetDatabase.CreateAsset(settings, path); }
                Lightmapping.lightingSettings = settings;
            }
            settings.bakedGI = false;
            settings.realtimeGI = false;
            settings.autoGenerate = false;
            EditorUtility.SetDirty(settings);
            Lightmapping.Bake();
            EditorSceneManager.SaveOpenScenes();
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
        static SimulationHUD BuildHUD() => HarvestHudBuilder.Build(font);
    }
}
