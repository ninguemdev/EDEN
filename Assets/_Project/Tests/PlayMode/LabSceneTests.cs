using System;
using System.Collections;
using System.IO;
using Eden.Presentation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Eden.Tests.PlayMode
{
    public class LabSceneTests
    {
        private const float TimeoutSeconds = 5f;
        private LabRunner runner;

        [UnitySetUp]
        public IEnumerator LoadLab()
        {
            yield return SceneManager.LoadSceneAsync("Bootstrap", LoadSceneMode.Single);
            yield return null;
            runner = UnityEngine.Object.FindFirstObjectByType<LabRunner>();
            Assert.That(runner, Is.Not.Null, "Bootstrap needs a LabRunner");
        }

        [TearDown]
        public void DeleteRunLog() => DeleteRunDirectory(runner);

        /// <summary>Closes and deletes the metrics directory a test run created under persistentDataPath.</summary>
        public static void DeleteRunDirectory(LabRunner lab)
        {
            if (lab == null || lab.RunDirectory == null)
                return;
            lab.CloseMetricsLog();
            if (Directory.Exists(lab.RunDirectory))
                Directory.Delete(lab.RunDirectory, true);
        }

        [UnityTest]
        public IEnumerator Lab_RunsTicksPausesAndStepsExactlyOne()
        {
            yield return WaitUntil(() => runner.Simulation.World.Tick > 0);
            Assert.That(runner.Simulation.World.Tick, Is.GreaterThan(0));

            runner.TogglePause();
            yield return null;
            var pausedAt = runner.Simulation.World.Tick;
            for (var i = 0; i < 20; i++)
                yield return null;
            Assert.That(runner.Simulation.World.Tick, Is.EqualTo(pausedAt), "paused clock must not advance");

            runner.RequestStep();
            yield return null;
            yield return null;
            Assert.That(runner.Simulation.World.Tick, Is.EqualTo(pausedAt + 1));
        }

        [UnityTest]
        public IEnumerator Lab_RendersWorldAndInspectsCreatureById()
        {
            runner.TogglePause();
            yield return null;

            var world = runner.Simulation.World;
            Assert.That(runner.View.VisibleCreatures, Is.EqualTo(world.Creatures.Count));
            Assert.That(runner.View.VisibleFood, Is.EqualTo(world.Food.Count));

            var target = world.Creatures[5].Id;
            runner.Select(target);
            yield return null;
            StringAssert.Contains($"Criatura {target}", runner.Hud.InspectorText);
            StringAssert.Contains("Sensores", runner.Hud.InspectorText);

            runner.SelectNeighbor(+1);
            Assert.That(runner.Selected, Is.EqualTo(world.Creatures[6].Id));
            runner.ClearSelection();
            Assert.That(runner.Hud.InspectorText, Is.Empty);
            StringAssert.Contains("População", runner.Hud.RunText);
        }

        [UnityTest]
        public IEnumerator Lab_RunsNeuralBrainsWithOneGenomePerCreature()
        {
            var initialRun = runner.RunDirectory;
            runner.UseBrain(LabBrainKind.Neural);
            if (initialRun != null && Directory.Exists(initialRun))
                Directory.Delete(initialRun, true);
            yield return WaitUntil(() => runner.Simulation.World.Tick > 0);

            Assert.That(runner.BrainKind, Is.EqualTo(LabBrainKind.Neural));
            Assert.That(runner.BrainName, Is.EqualTo(Eden.Brains.MlpBrain.BrainName));
            StringAssert.Contains("rede neural 12-16-3", runner.Hud.RunText);

            runner.TogglePause();
            runner.Select(runner.Simulation.World.Creatures[0].Id);
            yield return null;
            StringAssert.Contains("genoma próprio", runner.Hud.InspectorText);
            StringAssert.Contains("Sensores", runner.Hud.InspectorText);
        }

        [UnityTest]
        public IEnumerator NeuralSelection_ShowsALiveDiagramMatchingTheHeldAction()
        {
            yield return SwitchToNeural();
            runner.TogglePause();
            var creature = runner.Simulation.World.Creatures[2].Id;
            runner.Select(creature);
            var tick = runner.Simulation.World.Tick;
            runner.RequestStep();
            runner.RequestStep();
            yield return WaitUntil(() => runner.Simulation.World.Tick >= tick + 2);
            runner.RefreshNetwork();
            yield return WaitUntil(() => runner.Hud.Graph.RepaintCount > 0);

            Assert.That(runner.Hud.NetworkVisible, Is.True);
            Assert.That(runner.Network.HasData, Is.True);
            Assert.That(runner.Network.Creature, Is.EqualTo(creature));
            Assert.That(runner.TryGetHeldAction(creature, out var held), Is.True);
            Assert.That(runner.Network.Action.Thrust, Is.EqualTo(held.Thrust));
            Assert.That(runner.Network.Action.Turn, Is.EqualTo(held.Turn));
            Assert.That(runner.Network.Action.Strafe, Is.EqualTo(held.Strafe));
            Assert.That(runner.Hud.Graph.RepaintCount, Is.GreaterThan(0), "the diagram was drawn");

            runner.TogglePause();
            var captures = runner.Network.Captures;
            yield return WaitSeconds(0.35f);
            Assert.That(runner.Network.Captures, Is.GreaterThan(captures), "refreshed while running");
        }

        [UnityTest]
        public IEnumerator ClearingSelection_HidesAndStopsTheDiagram()
        {
            yield return SwitchToNeural();
            runner.Select(runner.Simulation.World.Creatures[0].Id);
            yield return WaitSeconds(0.3f);
            Assert.That(runner.Hud.NetworkVisible, Is.True);

            runner.ClearSelection();
            yield return null;
            var captures = runner.Network.Captures;
            var repaints = runner.Hud.Graph.RepaintCount;
            yield return WaitSeconds(0.4f);

            Assert.That(runner.Hud.NetworkVisible, Is.False);
            Assert.That(runner.Network.Captures, Is.EqualTo(captures), "no network work without a selection");
            Assert.That(runner.Hud.Graph.RepaintCount, Is.EqualTo(repaints), "no drawing without a selection");
        }

        [UnityTest]
        public IEnumerator WithoutSelection_TheNetworkCostsNothing()
        {
            yield return SwitchToNeural();
            yield return WaitSeconds(0.5f);

            Assert.That(runner.Simulation.World.Tick, Is.GreaterThan(0));
            Assert.That(runner.Network.Captures, Is.EqualTo(0));
            Assert.That(runner.Hud.Graph.RepaintCount, Is.EqualTo(0));
            Assert.That(runner.Hud.NetworkVisible, Is.False);
        }

        [UnityTest]
        public IEnumerator ReferenceBrains_KeepTheTextInspectorWithoutDiagram()
        {
            Assert.That(runner.BrainKind, Is.EqualTo(LabBrainKind.Heuristic));
            runner.Select(runner.Simulation.World.Creatures[0].Id);
            yield return WaitSeconds(0.3f);

            StringAssert.Contains("Sensores", runner.Hud.InspectorText);
            Assert.That(runner.Hud.NetworkVisible, Is.False);
            Assert.That(runner.Network.Captures, Is.EqualTo(0));
        }

        private IEnumerator SwitchToNeural()
        {
            var initialRun = runner.RunDirectory;
            runner.UseBrain(LabBrainKind.Neural);
            if (initialRun != null && Directory.Exists(initialRun))
                Directory.Delete(initialRun, true);
            yield return WaitUntil(() => runner.Simulation.World.Tick > 0);
        }

        private static IEnumerator WaitSeconds(float seconds)
        {
            var waited = 0f;
            while (waited < seconds)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator Lab_WritesInvariantMetricsCsvOutsideTheProject()
        {
            runner.ScaleSpeed(16f);
            var ticksPerSecond = runner.Simulation.World.Config.TicksPerSecond;
            yield return WaitUntil(() => runner.Simulation.World.Tick >= 3 * ticksPerSecond);

            var directory = runner.RunDirectory;
            Assert.That(directory, Is.Not.Null);
            StringAssert.StartsWith(Path.GetFullPath(UnityEngine.Application.persistentDataPath), Path.GetFullPath(directory));
            runner.CloseMetricsLog();

            var lines = File.ReadAllLines(Path.Combine(directory, "metrics.csv"));
            Assert.That(lines[0], Does.StartWith("tick,simulated_seconds,population"));
            Assert.That(lines.Length, Is.GreaterThanOrEqualTo(3), "header plus at least two one-second samples");
            Assert.That(lines[1], Does.StartWith($"{ticksPerSecond},1,"));
            Assert.That(File.ReadAllText(Path.Combine(directory, "run-info.txt")), Does.Contain("seed="));
        }

        private static IEnumerator WaitUntil(Func<bool> condition)
        {
            var waited = 0f;
            while (!condition() && waited < TimeoutSeconds)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
        }
    }
}
