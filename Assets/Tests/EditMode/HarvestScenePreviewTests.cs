using System.Collections;
using System.Linq;
using HarvestSim.Editor;
using HarvestSim.Visualization;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace HarvestSim.Tests
{
    public class HarvestScenePreviewTests
    {
        static WorldView Preview() => Resources.FindObjectsOfTypeAll<WorldView>()
            .SingleOrDefault(view => view.gameObject.name == "Harvest preview");

        [Test]
        public void PreviewIsTemporaryAndPreservesSceneObjects()
        {
            var scene = SceneManager.GetSceneByPath(HarvestSceneBuilder.ScenePath);
            bool opened = !scene.IsValid() || !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(HarvestSceneBuilder.ScenePath, OpenSceneMode.Additive);
            try
            {
                HarvestScenePreview.Clear();
                var controller = scene.GetRootGameObjects().SelectMany(go => go.GetComponentsInChildren<PlaybackController>(true)).First();
                var world = controller.worldView;
                int cells = world.cellsRoot.childCount;
                int structures = world.structuresRoot.childCount;
                int agents = world.agentsRoot.childCount;
                var cameraPosition = world.mapCamera.transform.position;
                bool wasDirty = scene.isDirty;
                int[] roots = scene.GetRootGameObjects().Select(go => go.GetInstanceID()).OrderBy(id => id).ToArray();

                HarvestScenePreview.Build(controller);
                var preview = Resources.FindObjectsOfTypeAll<WorldView>().Single(view => view.gameObject.name == "Harvest preview");
                Assert.That(preview.cellsRoot.childCount, Is.GreaterThan(0));
                Assert.That(preview.agentsRoot.childCount, Is.GreaterThan(0));
                Assert.That(preview.gameObject.hideFlags & HideFlags.DontSaveInEditor, Is.EqualTo(HideFlags.DontSaveInEditor));
                Assert.That(world.cellsRoot.childCount, Is.EqualTo(cells));
                Assert.That(world.structuresRoot.childCount, Is.EqualTo(structures));
                Assert.That(world.agentsRoot.childCount, Is.EqualTo(agents));
                Assert.That(world.mapCamera.transform.position, Is.EqualTo(cameraPosition));
                HarvestScenePreview.Clear();
                Assert.That(scene.GetRootGameObjects().Select(go => go.GetInstanceID()).OrderBy(id => id).ToArray(), Is.EqualTo(roots));
                Assert.That(scene.isDirty, Is.EqualTo(wasDirty));
            }
            finally
            {
                HarvestScenePreview.Clear();
                if (opened) EditorSceneManager.CloseScene(scene, true);
            }
        }

        [UnityTest]
        public IEnumerator ChangedTraceRefreshesOnceAndClosingAnotherSceneKeepsPreview()
        {
            var scene = SceneManager.GetSceneByPath(HarvestSceneBuilder.ScenePath);
            bool opened = !scene.IsValid() || !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(HarvestSceneBuilder.ScenePath, OpenSceneMode.Additive);
            var controller = scene.GetRootGameObjects().SelectMany(go => go.GetComponentsInChildren<PlaybackController>(true)).First();
            var original = controller.initialTrace;
            var otherScene = default(Scene);
            try
            {
                HarvestScenePreview.Build(controller);
                var first = Preview();
                Assert.That(first, Is.Not.Null);
                int firstId = first.GetInstanceID();
                yield return null;
                yield return null;
                Assert.That(Preview().GetInstanceID(), Is.EqualTo(firstId), "No debe reconstruir sin cambios.");

                var example = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Data/Traces/trace_ejemplo.json");
                var scenario = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Data/Traces/escenario_a.json");
                controller.initialTrace = original == example ? scenario : example;
                Assert.That(controller.initialTrace, Is.Not.Null);
                Assert.That(controller.initialTrace, Is.Not.EqualTo(original));
                double deadline = EditorApplication.timeSinceStartup + 5;
                while (Preview().GetInstanceID() == firstId && EditorApplication.timeSinceStartup < deadline)
                    yield return null;
                Assert.That(Preview().GetInstanceID(), Is.Not.EqualTo(firstId));
                Assert.That(Preview().cellsRoot.childCount, Is.EqualTo(controller.initialTrace == example ? 12 : 56));
                int changedId = Preview().GetInstanceID();
                deadline = EditorApplication.timeSinceStartup + .25;
                while (EditorApplication.timeSinceStartup < deadline)
                {
                    yield return null;
                    Assert.That(Preview().GetInstanceID(), Is.EqualTo(changedId), "El nuevo trace debe reconstruirse solo una vez.");
                }

                otherScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                EditorSceneManager.CloseScene(otherScene, true);
                Assert.That(Preview().GetInstanceID(), Is.EqualTo(changedId));
            }
            finally
            {
                controller.initialTrace = original;
                HarvestScenePreview.Clear();
                if (otherScene.IsValid() && otherScene.isLoaded) EditorSceneManager.CloseScene(otherScene, true);
                if (opened) EditorSceneManager.CloseScene(scene, true);
                else HarvestScenePreview.Build(controller);
            }
        }

        [Test]
        public void FramePreviewMovesSceneViewWithoutMovingSceneCamera()
        {
            var scene = SceneManager.GetSceneByPath(HarvestSceneBuilder.ScenePath);
            bool opened = !scene.IsValid() || !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(HarvestSceneBuilder.ScenePath, OpenSceneMode.Additive);
            var view = EditorWindow.GetWindow<SceneView>();
            var oldPivot = view.pivot;
            var controller = scene.GetRootGameObjects().SelectMany(go => go.GetComponentsInChildren<PlaybackController>(true)).First();
            var cameraPosition = controller.worldView.mapCamera.transform.position;
            try
            {
                HarvestScenePreview.Build(controller);
                view.pivot = new Vector3(1000, 1000, 1000);
                HarvestScenePreview.FramePreview();
                Assert.That(Vector3.Distance(view.pivot, Vector3.zero), Is.LessThan(100));
                Assert.That(controller.worldView.mapCamera.transform.position, Is.EqualTo(cameraPosition));
            }
            finally
            {
                view.pivot = oldPivot;
                HarvestScenePreview.Clear();
                if (opened) EditorSceneManager.CloseScene(scene, true);
                else HarvestScenePreview.Build(controller);
            }
        }
    }
}
