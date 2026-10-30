using System;
using System.Collections;
using System.IO;
using System.Linq;
using Eden.Persistence;
using Eden.Presentation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Eden.Tests.PlayMode
{
    public class LabWorkspaceTests
    {
        private LabRunner runner;
        private string directory;
        private VisualElement Root => runner.Hud.Root;
        [UnitySetUp]
        public IEnumerator Setup()
        {
            yield return SceneManager.LoadSceneAsync("Bootstrap", LoadSceneMode.Single); yield return null;
            runner = UnityEngine.Object.FindFirstObjectByType<LabRunner>();
            directory = Path.Combine(Path.GetTempPath(), "eden-workspace-" + Guid.NewGuid().ToString("N"));
            runner.CheckpointPath = Path.Combine(directory, "lab.json");
        }
        [TearDown]
        public void Cleanup()
        {
            runner.CloseLogs(); LabSceneTests.DeleteRunDirectory(runner);
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
        private static void Press(VisualElement root, string name)
        { var button = root.Q<Button>(name); using (var submit = NavigationSubmitEvent.GetPooled()) { submit.target = button; button.SendEvent(submit); } }
        private static IEnumerator Until(Func<bool> condition)
        { var timeout = Time.realtimeSinceStartup + 30; while (!condition() && Time.realtimeSinceStartup < timeout) yield return null; Assert.That(condition(), Is.True); }
        private IEnumerator Steps(int count)
        { for (var i = 0; i < count; i++) { runner.RequestStep(); yield return null; } }
        [UnityTest]
        public IEnumerator NavigationPreservesDraftAndPreviewDoesNotChangeLiveWorldOrRng()
        {
            Assert.That(runner.Workspace.Screen, Is.EqualTo("home")); Assert.That(runner.Clock.IsPaused, Is.True);
            foreach (var name in new[] { "inspector-tabs", "analysis-toolbar", "chart-filters" })
                Assert.That(Root.Query<VisualElement>(name).ToList().Count, Is.EqualTo(1), name);
            var before = runner.Simulation.ComputeFingerprint();
            Press(Root, "new-experiment"); Root.Q<IntegerField>("seed-field").value = 987;
            Press(Root, "refresh-preview"); var preview = runner.Workspace.Preview.World;
            Assert.That(preview, Is.Not.Null); Assert.That(preview.Tick, Is.Zero);
            var creature = preview.Creatures[0].Position;
            Press(Root, "refresh-preview"); Assert.That(runner.Workspace.Preview.World.Creatures[0].Position, Is.EqualTo(creature));
            Press(Root, "nav-home"); Press(Root, "nav-configure");
            Assert.That(Root.Q<IntegerField>("seed-field").value, Is.EqualTo(987));
            Assert.That(runner.Simulation.ComputeFingerprint(), Is.EqualTo(before)); yield return null;
            Assert.That(runner.Simulation.World.Tick, Is.Zero);
        }
        [UnityTest]
        public IEnumerator LibrarySelectsMiddleCheckpointAndCreatesIndependentManualSaves()
        {
            var paths = new string[3]; var ticks = new long[3];
            for (var i = 0; i < 3; i++)
            {
                yield return Steps(3); ticks[i] = runner.Simulation.World.Tick;
                runner.RequestCheckpoint(); yield return Until(() => !runner.CheckpointIsBusy);
                paths[i] = runner.LastManualCheckpointPath; Assert.That(File.Exists(paths[i]), Is.True);
            }
            Assert.That(paths.Distinct().Count(), Is.EqualTo(3));
            Press(Root, "open-checkpoints"); var list = Root.Q<ListView>("library-list");
            yield return Until(() => list.itemsSource?.Count == 3);
            var index = Enumerable.Range(0, list.itemsSource.Count).First(i => ((CheckpointCatalogEntry)list.itemsSource[i]).Path == paths[1]);
            list.SetSelection(index); var live = runner.Simulation;
            Assert.That(runner.Simulation, Is.SameAs(live)); Press(Root, "load-selected");
            Assert.That(runner.Workspace.IsModal, Is.True); Press(Root, "confirm-discard"); yield return Until(() => !runner.Workspace.IsLoading);
            Assert.That(runner.Simulation.World.Tick, Is.EqualTo(ticks[1])); Assert.That(runner.Clock.IsPaused, Is.True);
            runner.RequestCheckpoint(); yield return Until(() => !runner.CheckpointIsBusy);
            Assert.That(CheckpointCatalog.List(directory).Length, Is.EqualTo(4));
            foreach (var path in paths) Assert.That(File.Exists(path), Is.True);
        }
        [UnityTest]
        public IEnumerator MenuAndReplacementCancelPreserveSessionAndViewportExcludesControls()
        {
            runner.Workspace.Open("simulation"); runner.Clock.Resume(); yield return null;
            Press(Root, "nav-configure"); yield return Steps(3); Assert.That(runner.Clock.IsPaused, Is.True);
            var live = runner.Simulation; var tick = live.World.Tick;
            runner.Workspace.BeginExperiment(); Assert.That(runner.Workspace.IsModal, Is.True);
            Press(Root, "confirm-cancel"); Assert.That(runner.Simulation, Is.SameAs(live)); Assert.That(live.World.Tick, Is.EqualTo(tick));
            Press(Root, "nav-simulation"); yield return null;
            var arena = Root.Q("arena-viewport").worldBound;
            Assert.That(arena.width, Is.GreaterThan(100));
            Assert.That(Camera.main.rect.width, Is.LessThan(1)); Assert.That(Camera.main.rect.height, Is.LessThan(1));
            Assert.That(Root.Q<Button>("pause-button").worldBound.yMax, Is.LessThan(arena.yMin));
            Assert.That(runner.Hud.IsPointerOverControls(new Vector2(arena.center.x, Screen.height - arena.center.y)), Is.False);
            var pause = Root.Q<Button>("pause-button").worldBound.center;
            Assert.That(runner.Hud.IsPointerOverControls(new Vector2(pause.x, Screen.height - pause.y)), Is.True);
        }
    }
}
