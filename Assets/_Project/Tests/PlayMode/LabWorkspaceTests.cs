using System;
using System.Collections;
using System.IO;
using System.Linq;
using Eden.Application;
using Eden.Brains;
using Eden.Domain;
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
        public IEnumerator RandomNetworkHonorsBodyAlgorithmAndSensesInPreviewRunAndCheckpoint()
        {
            Press(Root, "new-experiment"); Press(Root, "config-tab-2");
            Root.Q<DropdownField>("mode-field").index = (int)LabMode.SharedWorld;
            Root.Q<DropdownField>("brain-field").index = (int)LabBrainKind.Neural;
            foreach (var name in new[] { "body-field", "algorithm-field", "senses-field" })
                Assert.That(Root.Q<DropdownField>(name).enabledInHierarchy, Is.True, name);
            Root.Q<DropdownField>("preset-field").index = 0;
            Root.Q<DropdownField>("body-field").index = 1;
            Root.Q<DropdownField>("senses-field").index = 2;
            Assert.That(Root.Q<DropdownField>("algorithm-field").index, Is.EqualTo(1));
            Press(Root, "refresh-preview");
            var preview = runner.Workspace.Preview.World;
            Assert.That(preview, Is.Not.Null);
            runner.Workspace.BeginExperiment();
            Assert.That(runner.Workspace.Screen, Is.EqualTo("simulation"));
            Assert.That(runner.BrainName, Is.EqualTo(NeatBrain.BrainName));
            Assert.That(runner.EvolvingEyes && runner.EvolvingBody && runner.Neat, Is.True);
            var founders = runner.Simulation.World.Creatures;
            foreach (var creature in founders)
            {
                var genome = runner.SharedGenomes.GenomeOf(creature.Id);
                Assert.That(genome.Neat, Is.Not.Null);
                Assert.That(genome.Body.IsNeutral, Is.False);
                Assert.That(genome.Eyes, Is.EqualTo(EyeSet.Founder));
                Assert.That(creature.Genes, Is.EqualTo(genome.Body));
                Assert.That(creature.Eyes, Is.EqualTo(genome.Eyes));
                Assert.That(preview.Creatures[creature.Id.Value - 1].Genes, Is.EqualTo(creature.Genes));
                Assert.That(preview.Creatures[creature.Id.Value - 1].Position, Is.EqualTo(creature.Position));
            }
            var first = runner.SharedGenomes.GenomeOf(founders[0].Id);
            runner.Select(founders[0].Id); yield return null;
            yield return Steps(2);
            runner.RefreshNetwork();
            Assert.That(runner.Hud.NeatNetworkShown, Is.True);
            var fingerprint = runner.Simulation.ComputeFingerprint();
            Assert.That(runner.SaveCheckpoint(), Is.True);
            var saved = CheckpointFile.Load(runner.CheckpointPath, out var info);
            Assert.That(info.SchemaVersion, Is.EqualTo(11));
            Assert.That(saved.SharedWorld.RandomNetwork.EvolvingEyes, Is.True);
            yield return Steps(1);
            var continued = runner.Simulation.ComputeFingerprint();
            Assert.That(runner.LoadCheckpoint(), Is.True);
            Assert.That(runner.Simulation.ComputeFingerprint(), Is.EqualTo(fingerprint));
            Assert.That(runner.SharedGenomes.GenomeOf(new CreatureId(1)).HasSameContent(first), Is.True);
            Assert.That(Root.Q<DropdownField>("algorithm-field").index, Is.EqualTo(1));
            Assert.That(Root.Q<DropdownField>("senses-field").index, Is.EqualTo(2));
            Assert.That(Root.Q<DropdownField>("body-field").index, Is.EqualTo(1));
            yield return Steps(1);
            Assert.That(runner.Simulation.ComputeFingerprint(), Is.EqualTo(continued), "resumes the same decisions");
        }

        [UnityTest]
        public IEnumerator RandomNetworkSupportsLegacyNeatFixedEyesAndMlpControl()
        {
            Press(Root, "new-experiment");
            Root.Q<DropdownField>("brain-field").index = (int)LabBrainKind.Neural;
            // A training profile left in the draft must not lock the shared world's algorithm.
            runner.Hud.ShowChoice(new LabChoice(LabMode.SharedWorld, 0, 1, LabBrainKind.Neural,
                profile: new ProfileChoice { Kind = ProfileKind.SearchActive }), false);
            foreach (var senses in new[] { 0, 1 })
            {
                Root.Q<DropdownField>("algorithm-field").index = 1;
                Root.Q<DropdownField>("senses-field").index = senses;
                runner.StartExperiment(runner.Hud.ReadChoice());
                var genome = runner.SharedGenomes.GenomeOf(new CreatureId(1));
                Assert.That(genome.Neat, Is.Not.Null);
                Assert.That(genome.Contract.HasEyes, Is.EqualTo(senses == 1));
                Assert.That(genome.Body.IsNeutral, Is.True);
            }
            Root.Q<DropdownField>("algorithm-field").index = 0;
            Assert.That(Root.Q<DropdownField>("senses-field").index, Is.Zero);
            runner.StartExperiment(runner.Hud.ReadChoice());
            var config = runner.Simulation.World.Config;
            Assert.That(runner.SharedGenomes.GenomeOf(new CreatureId(1))
                .HasSameContent(InitialGenomes.RandomFor(config, new CreatureId(1))), Is.True,
                "the original fixed MLP stream remains compatible");
            Assert.That(runner.BrainName, Is.EqualTo(MlpBrain.BrainName));
            Root.Q<DropdownField>("brain-field").index = (int)LabBrainKind.Heuristic;
            Assert.That(Root.Q<DropdownField>("algorithm-field").enabledSelf, Is.False);
            yield return null;
        }

        [UnityTest]
        public IEnumerator TrainingChoicesEnableEvolutionWithoutChangingTheLiveSession()
        {
            Press(Root, "new-experiment");
            Root.Q<DropdownField>("brain-field").index = (int)LabBrainKind.Neural;
            Root.Q<DropdownField>("body-field").index = 1;
            Root.Q<DropdownField>("senses-field").index = 1;
            var live = runner.Simulation;
            Press(Root, "config-tab-3");
            Assert.That(Root.Q<DropdownField>("training-field").enabledInHierarchy, Is.True);
            Assert.That(Root.Q<DropdownField>("profile-field").enabledInHierarchy, Is.True);
            Root.Q<DropdownField>("training-field").index = 1;
            var choice = runner.Hud.ReadChoice();
            Assert.That(choice.Mode, Is.EqualTo(LabMode.IsolatedEvolution));
            Assert.That(choice.ParallelTraining && choice.EvolvingBody && choice.Neat && choice.Eyes, Is.True);
            Assert.That(runner.Simulation, Is.SameAs(live), "editing does not start training");
            Root.Q<DropdownField>("mode-field").index = (int)LabMode.SharedWorld;
            Root.Q<DropdownField>("profile-field").index = (int)ProfileKind.Resources;
            Assert.That(runner.Hud.ReadChoice().Mode, Is.EqualTo(LabMode.IsolatedEvolution));
            runner.Workspace.BeginExperiment();
            Assert.That(runner.Mode, Is.EqualTo(LabMode.IsolatedEvolution));
            Assert.That(runner.Evolution.EvaluationWorkers, Is.EqualTo(EvaluationJob.AutomaticWorkerCount));
            Assert.That(ProfileChoice.Read(runner.Evolution.Evolution.Settings).Kind, Is.EqualTo(ProfileKind.Resources));
            Assert.That(runner.Neat && runner.Eyes && runner.EvolvingBody, Is.True);
            Assert.That(runner.Clock.IsPaused, Is.True);
            yield return null;
        }

        [UnityTest]
        public IEnumerator RemovePoolClearsDraftAndInvalidPreviewWithoutDeletingExports()
        {
            Press(Root, "new-experiment");
            Root.Q<DropdownField>("mode-field").index = (int)LabMode.IsolatedEvolution;
            runner.GenomeExportDirectory = directory;
            Directory.CreateDirectory(directory);
            var marker = Path.Combine(directory, "keep.txt"); File.WriteAllText(marker, "export preserved");
            Press(Root, "use-export-directory-button");
            Assert.That(runner.Hud.ReadChoice().GenomeDirectory, Is.EqualTo(directory));
            Press(Root, "refresh-preview");
            Assert.That(runner.Workspace.Preview.World, Is.Null, "the directory has no valid genome");
            Press(Root, "clear-genomes-directory-button");
            Assert.That(runner.Hud.ReadChoice().GenomeDirectory, Is.Empty);
            Assert.That(Root.Q<Button>("clear-genomes-directory-button").enabledSelf, Is.False);
            Press(Root, "refresh-preview");
            Assert.That(runner.Workspace.Preview.World, Is.Not.Null);
            Assert.That(File.Exists(marker), Is.True);
            runner.Hud.SetSeedGenomesDirectory(directory);
            Root.Q<DropdownField>("mode-field").index = (int)LabMode.SharedWorld;
            Assert.That(Root.Q<TextField>("seed-genomes-field").enabledSelf, Is.False);
            Press(Root, "clear-genomes-directory-button");
            Assert.That(runner.Hud.SeedGenomesDirectory, Is.Empty, "removal also works after switching modes");
            runner.Workspace.Open("home"); runner.Workspace.Open("configure");
            Assert.That(runner.Hud.ReadChoice().GenomeDirectory, Is.Empty);
            yield return null;
        }

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
