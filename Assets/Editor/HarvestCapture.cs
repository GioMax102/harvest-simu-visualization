using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarvestSim.Visualization;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace HarvestSim.Editor
{
    /// <summary>
    /// Capturas PNG de la vista realista sin abrir el editor (modo batch), para revisar cambios visuales.
    /// Uso: Unity -batchmode -projectPath ... -executeMethod HarvestSim.Editor.HarvestCapture.CaptureFromCommandLine
    ///      -captureDir RUTA [-captureTrace Assets/...json] [-captureTick N] [-captureFraction 0..1] -quit
    /// </summary>
    public static class HarvestCapture
    {
        public static void CaptureFromCommandLine()
        {
            var args = Environment.GetCommandLineArgs();
            string Arg(string name, string fallback)
            {
                int i = Array.IndexOf(args, name);
                return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
            }
            string dir = Arg("-captureDir", "Temp/Capturas");
            string trace = Arg("-captureTrace", "Assets/Data/Traces/entrega2/escenario_f__subasta__seed0.json");
            int tick = int.Parse(Arg("-captureTick", "120"));
            float fraction = float.Parse(Arg("-captureFraction", ".5"), System.Globalization.CultureInfo.InvariantCulture);
            int width = int.Parse(Arg("-captureWidth", "1600")), height = int.Parse(Arg("-captureHeight", "900"));
            HarvestArtBuilder.Build();
            if (Array.IndexOf(args, "-captureHud") >= 0) CaptureHud(dir, trace, tick, width, height);
            else Capture(dir, trace, tick, fraction, width, height);
        }

        public static List<string> Capture(string dir, string tracePath, int tick, float fraction, int width, int height)
        {
            Directory.CreateDirectory(dir);
            var scene = EditorSceneManager.OpenScene(HarvestSceneBuilder.ScenePath);
            HarvestScenePreview.Clear();
            var controller = UnityEngine.Object.FindFirstObjectByType<PlaybackController>();
            var world = controller.worldView;
            var data = TraceLoader.Parse(File.ReadAllText(tracePath));
            var current = new TracePlayback(data); current.Seek(tick);
            var next = new TracePlayback(data); next.Seek(tick + 1);
            world.Build(data);
            world.Render(current.State);
            if (fraction > 0 && next.State.Tick > current.State.Tick) world.Interpolate(current.State, next.State, fraction);

            foreach (var agent in world.Agents.Values)
            {
                var body = agent.Body ? agent.Body.GetComponent<Renderer>() : null;
                Debug.Log($"Vehículo {agent.AgentId}: body={(body ? body.bounds.ToString() : "sin Body")} activo={(body ? body.enabled && body.gameObject.activeInHierarchy : false)} pos={agent.transform.position}");
            }
            var camera = world.mapCamera;
            camera.rect = new Rect(0, 0, 1, 1);
            camera.aspect = width / (float)height;
            var rig = world.Rig;
            var files = new List<string>();
            string name = Path.GetFileNameWithoutExtension(tracePath);

            rig.ResetView(CameraMode.Orbit);
            files.Add(Shot(camera, world, width, height, Path.Combine(dir, $"{name}_t{tick}_panorama.png")));

            var harvester = world.Agents.Values.FirstOrDefault(a => a.AgentType == "cosechadora");
            if (harvester)
            {
                var forward = harvester.transform.forward;
                float yaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg + 35;
                rig.SetOrbit(harvester.transform.position, yaw, 24, world.Grid.CellSize * 3.2f);
                files.Add(Shot(camera, world, width, height, Path.Combine(dir, $"{name}_t{tick}_cosechadora.png")));
                rig.SetOrbit(harvester.transform.position + Vector3.up * 2, yaw + 150, 14, world.Grid.CellSize * 2.2f);
                files.Add(Shot(camera, world, width, height, Path.Combine(dir, $"{name}_t{tick}_ras.png")));
            }
            var warehouse = world.Grid.CellToWorld(world.Grid.Warehouse.x, world.Grid.Warehouse.y);
            rig.SetOrbit(warehouse + new Vector3(-6, 4, 10), 135, 22, world.Grid.CellSize * 5);
            files.Add(Shot(camera, world, width, height, Path.Combine(dir, $"{name}_t{tick}_bodega.png")));

            rig.ResetView(CameraMode.Top);
            files.Add(Shot(camera, world, width, height, Path.Combine(dir, $"{name}_t{tick}_cenital.png")));

            world.Clear();
            EditorSceneManager.OpenScene(scene.path);
            foreach (var file in files) Debug.Log("Captura: " + file);
            return files;
        }

        /// <summary>Captura con el HUD: carga el trace por el controlador real y dibuja el canvas sobre la cámara.</summary>
        public static List<string> CaptureHud(string dir, string tracePath, int tick, int width, int height)
        {
            Directory.CreateDirectory(dir);
            var scene = EditorSceneManager.OpenScene(HarvestSceneBuilder.ScenePath);
            HarvestScenePreview.Clear();
            var controller = UnityEngine.Object.FindFirstObjectByType<PlaybackController>();
            var world = controller.worldView;
            var hud = controller.hud;
            var camera = world.mapCamera;
            camera.rect = new Rect(0, 0, 1, 1);
            var target = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            camera.targetTexture = target;
            camera.aspect = width / (float)height;
            hud.Bind(controller);
            controller.LoadTraceJson(File.ReadAllText(tracePath), Path.GetFileNameWithoutExtension(tracePath));
            controller.SeekTo(tick);
            hud.SetServerState(ServerState.Online);
            var canvas = hud.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 1;
            Canvas.ForceUpdateCanvases();
            world.Rig.overlayInsets = hud.CameraInsets();
            world.Rig.ResetView(CameraMode.Orbit);
            controller.SeekTo(tick);
            canvas.planeDistance = camera.nearClipPlane * 1.5f;
            Canvas.ForceUpdateCanvases();
            camera.targetTexture = null;
            RenderTexture.ReleaseTemporary(target);
            var files = new List<string>();
            string name = Path.GetFileNameWithoutExtension(tracePath);
            files.Add(Shot(camera, world, width, height, Path.Combine(dir, $"{name}_t{tick}_hud.png")));
            var harvester = world.Agents.Values.FirstOrDefault(a => a.AgentType == "cosechadora");
            if (harvester)
            {
                world.FollowAgent(harvester.AgentId);
                world.Rig.Snap();
                controller.SeekTo(tick);
                canvas.planeDistance = camera.nearClipPlane * 1.5f;
                Canvas.ForceUpdateCanvases();
                files.Add(Shot(camera, world, width, height, Path.Combine(dir, $"{name}_t{tick}_hud_seguir.png")));
            }
            controller.LoadTrace(null);
            EditorSceneManager.OpenScene(scene.path);
            foreach (var file in files) Debug.Log("Captura: " + file);
            return files;
        }

        static string Shot(Camera camera, WorldView world, int width, int height, string path)
        {
            foreach (var agent in world.Agents.Values) agent.FaceCamera();
            var target = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var previous = camera.targetTexture;
            camera.targetTexture = target;
            camera.Render();
            var active = RenderTexture.active;
            RenderTexture.active = target;
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            image.Apply();
            RenderTexture.active = active;
            camera.targetTexture = previous;
            RenderTexture.ReleaseTemporary(target);
            File.WriteAllBytes(path, image.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(image);
            return path;
        }
    }
}
