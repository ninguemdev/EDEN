using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Eden.Tests.PlayMode
{
    public class BootstrapSceneTests
    {
        private const string BootstrapScene = "Bootstrap";

        [UnityTest]
        public IEnumerator BootstrapScene_ShowsEdenLabelThroughOrthographicCamera()
        {
            yield return SceneManager.LoadSceneAsync(BootstrapScene, LoadSceneMode.Single);
            yield return null;

            var camera = Camera.main;
            Assert.That(camera, Is.Not.Null, "Bootstrap needs a camera tagged MainCamera.");
            Assert.That(camera.orthographic, Is.True);

            Assert.That(Object.FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None), Is.Not.Empty);

            var document = Object.FindFirstObjectByType<UIDocument>();
            Assert.That(document, Is.Not.Null);
            var title = document.rootVisualElement.Q<Label>("title");
            Assert.That(title, Is.Not.Null);
            Assert.That(title.text, Is.EqualTo("EDEN"));

            yield return null;
            var panelBounds = document.rootVisualElement.panel.visualTree.worldBound;
            var titleBounds = title.worldBound;
            var message = $"title={titleBounds} panel={panelBounds} root={document.rootVisualElement.worldBound}";
            Assert.That(titleBounds.width, Is.GreaterThan(0f), message);
            Assert.That(titleBounds.height, Is.GreaterThan(0f), message);
            Assert.That(panelBounds.Contains(titleBounds.min) && panelBounds.Contains(titleBounds.max), Is.True, message);

            LabSceneTests.DeleteRunDirectory(Object.FindFirstObjectByType<Eden.Presentation.LabRunner>());
        }
    }
}
