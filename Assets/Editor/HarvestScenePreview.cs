using System;
using HarvestSim.Visualization;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HarvestSim.Editor
{
    [InitializeOnLoad]
    public static class HarvestScenePreview
    {
        static GameObject root;
        static WorldView preview;
        static PlaybackController sourceController;
        static TextAsset shownTrace;
        static bool refreshQueued, frameOnRefresh;

        static HarvestScenePreview()
        {
            EditorSceneManager.sceneOpened += (scene, _) =>
            {
                if (scene.path == HarvestSceneBuilder.ScenePath) QueueRefresh(true);
            };
            EditorSceneManager.sceneClosing += (scene, _) =>
            {
                if (scene.path == HarvestSceneBuilder.ScenePath) Clear();
            };
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.ExitingEditMode) Clear();
                else if (state == PlayModeStateChange.EnteredEditMode) QueueRefresh();
            };
            AssemblyReloadEvents.beforeAssemblyReload += Clear;
            EditorApplication.quitting += Clear;
            EditorApplication.update += RefreshChangedTrace;
            QueueRefresh();
        }

        static void QueueRefresh(bool frame = false)
        {
            frameOnRefresh |= frame;
            if (refreshQueued) return;
            refreshQueued = true;
            EditorApplication.delayCall += RefreshOpenScene;
        }

        static void RefreshChangedTrace()
        {
            if (sourceController && sourceController.initialTrace != shownTrace)
                QueueRefresh();
        }

        static void RefreshOpenScene()
        {
            refreshQueued = false;
            bool frame = frameOnRefresh;
            frameOnRefresh = false;
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var scene = SceneManager.GetSceneByPath(HarvestSceneBuilder.ScenePath);
            if (!scene.IsValid() || !scene.isLoaded) { Clear(); return; }
            foreach (var gameObject in scene.GetRootGameObjects())
            {
                var controller = gameObject.GetComponentInChildren<PlaybackController>(true);
                if (controller)
                {
                    if (!root || sourceController != controller || shownTrace != controller.initialTrace)
                        Build(controller);
                    if (frame) FramePreview();
                    return;
                }
            }
            Clear();
        }

        // Uses separate temporary roots so scene objects and the editor camera stay untouched.
        public static void Build(PlaybackController controller)
        {
            Clear();
            sourceController = controller;
            shownTrace = controller ? controller.initialTrace : null;
            if (!controller || !controller.worldView || !controller.initialTrace) return;
            try
            {
                var source = controller.worldView;
                var data = TraceLoader.Parse(controller.initialTrace.text);
                root = new GameObject("Harvest preview") { hideFlags = HideFlags.HideAndDontSave };
                SceneManager.MoveGameObjectToScene(root, controller.gameObject.scene);
                preview = root.AddComponent<WorldView>();
                preview.cellsRoot = Child("Cells");
                preview.structuresRoot = Child("Structures");
                preview.agentsRoot = Child("Agents");
                var cameraObject = new GameObject("Preview camera", typeof(Camera));
                cameraObject.transform.SetParent(root.transform, false);
                preview.mapCamera = cameraObject.GetComponent<Camera>();
                preview.mapCamera.enabled = false;
                preview.art = source.art;
                preview.Build(data);
                preview.Render(new TracePlayback(data).State);
                foreach (var child in root.GetComponentsInChildren<Transform>(true))
                    child.gameObject.hideFlags = HideFlags.HideAndDontSave;
                SceneView.RepaintAll();
            }
            catch (Exception error)
            {
                Clear();
                sourceController = controller;
                shownTrace = controller ? controller.initialTrace : null;
                Debug.LogWarning("No se pudo generar la vista previa de Harvest: " + error.Message);
            }
        }

        [MenuItem("Harvest Sim/Enfocar vista previa")]
        public static void FramePreview()
        {
            var view = SceneView.lastActiveSceneView;
            if (!root || !view) return;
            var renderers = root.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            view.Frame(bounds, true);
            view.Repaint();
        }

        static Transform Child(string name)
        {
            var child = new GameObject(name).transform;
            child.SetParent(root.transform, false);
            return child;
        }

        public static void Clear()
        {
            if (preview) preview.Clear();
            if (root) UnityEngine.Object.DestroyImmediate(root);
            preview = null;
            root = null;
            sourceController = null;
            shownTrace = null;
        }
    }
}
