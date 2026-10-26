using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Eden.Presentation;
using Eden.Simulation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Eden.Tests.PlayMode
{
    /// <summary>The main lab flows driven through the real controls: pick, start, observe, pause, inspect, resume.</summary>
    public class LabExperimentTests
    {
        private const float EvolutionTimeoutSeconds = 90f;
        private readonly HashSet<string> runDirectories = new HashSet<string>();
        private LabRunner runner;

        [UnitySetUp]
        public IEnumerator LoadLab()
        {
            yield return SceneManager.LoadSceneAsync("Bootstrap", LoadSceneMode.Single);
            yield return null;
            runner = UnityEngine.Object.FindFirstObjectByType<LabRunner>();
            Assert.That(runner, Is.Not.Null, "Bootstrap needs a LabRunner");
            Track();
        }

        [TearDown]
        public void DeleteRunDirectories()
        {
            Track();
            runner?.CloseLogs();
            foreach (var directory in runDirectories)
                if (Directory.Exists(directory))
                    Directory.Delete(directory, true);
            runDirectories.Clear();
        }

        [UnityTest]
        public IEnumerator Controls_StartAnIsolatedEvolutionAndChartItsGenerations()
        {
            yield return StartFromControls(LabMode.IsolatedEvolution, preset: 0, seed: 7);

            Assert.That(runner.Mode, Is.EqualTo(LabMode.IsolatedEvolution));
            Assert.That(runner.Evolution.Plan.ExperimentSeed, Is.EqualTo(7UL));
            Assert.That(runner.Evolution.Evolution.Rules.PopulationSize, Is.EqualTo(32), "preset Mínimo");
            Assert.That(runner.Simulation.World.Creatures.Count, Is.EqualTo(1), "the world shown is an isolated episode");
            StringAssert.Contains("AVALIAÇÃO ISOLADA", runner.Hud.RunText);
            StringAssert.Contains("não seleção natural", runner.Hud.RunText);
            Assert.That(runner.Hud.ChartVisible, Is.True);
            Assert.That(runner.Hud.Chart.PlottedGenerations, Is.Zero);

            yield return Press("max-button");
            Assert.That(runner.Clock.IsUnpaced, Is.True);
            yield return WaitUntil(() => runner.Evolution.Validation.Count > 0, EvolutionTimeoutSeconds);
            yield return WaitSeconds(0.6f);
            var effectiveSpeed = runner.Clock.EffectiveSpeed;
            yield return Press("pause-button");

            Assert.That(runner.Evolution.Training.Count, Is.GreaterThanOrEqualTo(1));
            Assert.That(runner.Hud.Chart.PlottedGenerations, Is.EqualTo(runner.Evolution.Training.Count));
            Assert.That(runner.Hud.Chart.RepaintCount, Is.GreaterThan(0), "the chart was drawn");
            StringAssert.Contains("Treino g", runner.Hud.RunText);
            StringAssert.Contains("Referências na validação: aleatório", runner.Hud.RunText);
            StringAssert.Contains("heurístico", runner.Hud.RunText);
            StringAssert.Contains("efetiva", runner.Hud.RunText);
            Assert.That(effectiveSpeed, Is.GreaterThan(FixedStepClock.MaxSpeed),
                "maximum speed runs faster than the paced maximum");
            Assert.That(File.Exists(Path.Combine(runner.EvolutionDirectory, "generations.csv")), Is.True);
        }

        [UnityTest]
        public IEnumerator Controls_PauseStepAndInspectTheEvaluatedIndividual()
        {
            yield return StartFromControls(LabMode.IsolatedEvolution, preset: 0, seed: 3);
            yield return Press("pause-button");
            Assert.That(runner.Clock.IsPaused, Is.True);
            var episode = runner.Evolution.Job.Current;
            var tick = episode.Tick;
            yield return WaitSeconds(0.3f);
            Assert.That(episode.Tick, Is.EqualTo(tick), "paused");
            StringAssert.Contains("PAUSADO", runner.Hud.RunText);

            yield return Press("step-button");
            yield return null;
            Assert.That(episode.Tick, Is.EqualTo(tick + 1), "exactly one tick");

            runner.SelectNeighbor(+1);
            yield return null;
            var inspector = runner.Hud.InspectorText;
            StringAssert.Contains("Energia", inspector);
            StringAssert.Contains("Idade", inspector);
            StringAssert.Contains("Linhagem", inspector);
            StringAssert.Contains($"Indivíduo #{runner.Evolution.CurrentIndividual.Id}", inspector);
            StringAssert.Contains("Fundador", inspector);
            StringAssert.Contains("Episódio isolado: treino", inspector);
            Assert.That(runner.Hud.NetworkVisible, Is.True, "evaluated genomes are networks");

            yield return Press("pause-button");
            Assert.That(runner.Clock.IsPaused, Is.False);
        }

        [UnityTest]
        public IEnumerator Controls_ShowTheEvolvedPopulationInTheSharedWorldAndResume()
        {
            yield return StartFromControls(LabMode.IsolatedEvolution, preset: 0, seed: 5);
            var session = runner.Evolution;
            yield return Press("max-button");
            yield return WaitUntil(() => session.Training.Count > 0, EvolutionTimeoutSeconds);
            var generations = session.Training.Count;
            var evaluated = session.LastTraining.Individuals;

            yield return StartFromControls(LabMode.SharedWorld, preset: 0, seed: 9, brain: LabBrainKind.Evolved);
            Assert.That(runner.Mode, Is.EqualTo(LabMode.SharedWorld));
            Assert.That(runner.BrainKind, Is.EqualTo(LabBrainKind.Evolved));
            Assert.That(runner.Evolution, Is.SameAs(session), "the evolution is kept");
            Assert.That(runner.Simulation.World.Creatures.Count, Is.EqualTo(evaluated.Count));
            StringAssert.Contains("população evoluída", runner.Hud.RunText);
            StringAssert.Contains("Mundo compartilhado", runner.Hud.RunText);

            yield return Press("pause-button");
            runner.Select(runner.Simulation.World.Creatures[3].Id);
            yield return null;
            StringAssert.Contains("Linhagem", runner.Hud.InspectorText);
            StringAssert.Contains($"Indivíduo #{evaluated[3].Id}", runner.Hud.InspectorText);
            var trained = session.TotalTicks;
            yield return WaitSeconds(0.2f);
            Assert.That(session.TotalTicks, Is.EqualTo(trained), "the evolution waits while the shared world runs");

            var resume = runner.Hud.Root.Q<Button>("resume-evolution-button");
            Assert.That(resume.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
            yield return Press("resume-evolution-button");
            Assert.That(runner.Mode, Is.EqualTo(LabMode.IsolatedEvolution));
            Assert.That(runner.Evolution, Is.SameAs(session));
            Assert.That(session.Training.Count, Is.GreaterThanOrEqualTo(generations));
            Assert.That(runner.Simulation, Is.SameAs(session.Job.Current.Simulation));
        }

        [UnityTest]
        public IEnumerator Controls_KeepTheChoiceTheUserIsEditing()
        {
            var modeField = runner.Hud.Root.Q<DropdownField>("mode-field");
            var seedField = runner.Hud.Root.Q<IntegerField>("seed-field");
            modeField.index = (int)LabMode.IsolatedEvolution;
            seedField.value = 123;
            yield return WaitSeconds(0.6f);

            Assert.That(runner.Mode, Is.EqualTo(LabMode.SharedWorld), "nothing starts before Iniciar");
            Assert.That(modeField.index, Is.EqualTo((int)LabMode.IsolatedEvolution), "HUD refreshes keep the edit");
            Assert.That(seedField.value, Is.EqualTo(123));
            seedField.value = -4;
            Assert.That(seedField.value, Is.Zero, "seeds are not negative");
        }

        private IEnumerator StartFromControls(LabMode mode, int preset, int seed, LabBrainKind brain = LabBrainKind.Heuristic)
        {
            var root = runner.Hud.Root;
            Assert.That(root, Is.Not.Null, "the HUD is bound");
            root.Q<DropdownField>("mode-field").index = (int)mode;
            root.Q<DropdownField>("preset-field").index = preset;
            var brainField = root.Q<DropdownField>("brain-field");
            Assert.That(brainField.choices.Count, Is.GreaterThan((int)brain), "the brain is offered");
            brainField.index = (int)brain;
            root.Q<IntegerField>("seed-field").value = seed;
            yield return Press("start-button");
        }

        private IEnumerator Press(string buttonName)
        {
            var button = runner.Hud.Root.Q<Button>(buttonName);
            Assert.That(button, Is.Not.Null, buttonName);
            using (var submit = NavigationSubmitEvent.GetPooled())
            {
                submit.target = button;
                button.SendEvent(submit);
            }

            yield return null;
            yield return null;
            Track();
        }

        private void Track()
        {
            if (runner.MetricsDirectory != null)
                runDirectories.Add(runner.MetricsDirectory);
            if (runner.EvolutionDirectory != null)
                runDirectories.Add(runner.EvolutionDirectory);
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

        private static IEnumerator WaitUntil(Func<bool> condition, float timeout)
        {
            var started = Time.realtimeSinceStartup;
            while (!condition() && Time.realtimeSinceStartup - started < timeout)
                yield return null;
            Assert.That(condition(), Is.True, $"not reached within {timeout} s");
        }
    }
}
