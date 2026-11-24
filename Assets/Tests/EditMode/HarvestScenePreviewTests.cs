using System.Linq;
using HarvestSim.Editor;
using HarvestSim.Visualization;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HarvestSim.Tests
{
    public class HarvestScenePreviewTests
    {
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
    }
}
