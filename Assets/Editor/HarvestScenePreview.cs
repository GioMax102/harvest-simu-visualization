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

        static HarvestScenePreview()
        {
            EditorSceneManager.sceneOpened += (_, __) => QueueRefresh();
            EditorSceneManager.sceneClosing += (_, __) => Clear();
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.ExitingEditMode) Clear();
                else if (state == PlayModeStateChange.EnteredEditMode) QueueRefresh();
            };
            AssemblyReloadEvents.beforeAssemblyReload += Clear;
            EditorApplication.quitting += Clear;
            QueueRefresh();
        }

        static void QueueRefresh() => EditorApplication.delayCall += RefreshOpenScene;

        static void RefreshOpenScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var scene = SceneManager.GetSceneByPath(HarvestSceneBuilder.ScenePath);
            if (!scene.IsValid() || !scene.isLoaded) { Clear(); return; }
            foreach (var gameObject in scene.GetRootGameObjects())
            {
                var controller = gameObject.GetComponentInChildren<PlaybackController>(true);
                if (controller) { Build(controller); return; }
            }
            Clear();
        }

        // Uses separate temporary roots so scene objects and the editor camera stay untouched.
        public static void Build(PlaybackController controller)
        {
            Clear();
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
                preview.cellPrefab = source.cellPrefab;
                preview.harvesterPrefab = source.harvesterPrefab;
                preview.cartPrefab = source.cartPrefab;
                preview.cropMaterial = source.cropMaterial;
                preview.harvestedMaterial = source.harvestedMaterial;
                preview.serviceMaterial = source.serviceMaterial;
                preview.structureMaterial = source.structureMaterial;
                preview.obstacleMaterial = source.obstacleMaterial;
                preview.Build(data);
                preview.Render(new TracePlayback(data).State);
                foreach (var child in root.GetComponentsInChildren<Transform>(true))
                    child.gameObject.hideFlags = HideFlags.HideAndDontSave;
                SceneView.RepaintAll();
            }
            catch (Exception error)
            {
                Clear();
                Debug.LogWarning("No se pudo generar la vista previa de Harvest: " + error.Message);
            }
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
        }
    }
}
