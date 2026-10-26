using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Eden.Domain;
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
        public IEnumerator Controls_EvolveTheBody_AndDrawEachCreatureWithItsOwnBody()
        {
            var bodyField = runner.Hud.Root.Q<DropdownField>("body-field");
            Assert.That(bodyField.value, Is.EqualTo(LabHud.BodyChoices[0]), "the fixed body is the default");
            yield return StartFromControls(LabMode.IsolatedEvolution, preset: 0, seed: 3, evolvingBody: true);
            Assert.That(bodyField.enabledSelf, Is.True, "the body applies to a new evolution");
            Assert.That(runner.EvolvingBody, Is.True);
            Assert.That(runner.Evolution.Plan.ExpressedBodyGenes, Is.EqualTo(BodyGeneSet.All));
            StringAssert.Contains("Corpo evolutivo", runner.Hud.RunText);
            var session = runner.Evolution;
            yield return Press("max-button");
            yield return WaitUntil(() => session.Training.Count > 0, EvolutionTimeoutSeconds);
            var evaluated = session.LastTraining.Individuals;

            yield return StartFromControls(LabMode.SharedWorld, preset: 0, seed: 9, brain: LabBrainKind.Evolved,
                evolvingBody: true);
            Assert.That(bodyField.enabledSelf, Is.False, "the shared world takes the bodies of the evolution");
            yield return Press("pause-button");
            var world = runner.Simulation.World;
            var sizes = new HashSet<float>();
            for (var i = 0; i < world.Creatures.Count; i++)
            {
                var creature = world.Creatures[i];
                Assert.That(creature.Genes, Is.EqualTo(evaluated[creature.Id.Value - 1].Genome.Body), "its own genes");
                Assert.That(runner.View.TryGetDrawnBody(creature.Id, out var scale, out var sensor), Is.True);
                Assert.That(scale, Is.EqualTo(creature.Body.EatRadius / world.Config.EatRadius).Within(1e-5f));
                Assert.That(sensor, Is.Zero, "only the selected creature shows its range");
                sizes.Add(scale);
            }

            Assert.That(sizes.Count, Is.GreaterThan(1), "bodies differ");
            var selected = world.Creatures[2];
            runner.Select(selected.Id);
            yield return null;
            Assert.That(runner.View.TryGetDrawnBody(selected.Id, out _, out var diameter), Is.True);
            Assert.That(diameter, Is.EqualTo(2f * selected.Body.SensorRange).Within(1e-3f));
            StringAssert.Contains("Corpo (genes", runner.Hud.InspectorText);
            StringAssert.Contains("Gasto/s", runner.Hud.InspectorText);
        }

        [UnityTest]
        public IEnumerator Controls_SaveAndLoadAnEvolutionInTheMiddleOfAnEpisode()
        {
            UseTemporaryCheckpoint();
            yield return StartFromControls(LabMode.IsolatedEvolution, preset: 0, seed: 11);
            yield return Press("max-button");
            yield return WaitUntil(() => runner.Evolution.Training.Count > 0, EvolutionTimeoutSeconds);
            yield return Press("max-button");
            yield return Press("pause-button");
            yield return Step(3);
            var saved = runner.Evolution;
            var trainedAtSave = saved.Training.Count;
            var atSave = Position();

            yield return Press("save-button");
            Assert.That(File.Exists(runner.CheckpointPath), Is.True);
            StringAssert.Contains("Checkpoint salvo", runner.Hud.CheckpointText);
            yield return Step(12);
            var later = Position();

            yield return StartFromControls(LabMode.SharedWorld, preset: 0, seed: 2);
            Assert.That(runner.Clock.IsPaused, Is.True, "a new run keeps the pause");
            yield return Press("load-button");
            StringAssert.Contains("Checkpoint carregado", runner.Hud.CheckpointText);
            Assert.That(runner.Mode, Is.EqualTo(LabMode.IsolatedEvolution));
            Assert.That(runner.Evolution, Is.Not.SameAs(saved), "a restored session");
            Assert.That(runner.Simulation, Is.SameAs(runner.Evolution.Job.Current.Simulation));
            Assert.That(Position(), Is.EqualTo(atSave));
            Assert.That(runner.Evolution.Training.Count, Is.EqualTo(trainedAtSave), "history restored");
            Assert.That(runner.Hud.Chart.PlottedGenerations, Is.EqualTo(trainedAtSave));
            var experiment = File.ReadAllLines(Path.Combine(runner.EvolutionDirectory, "experiment.txt"));
            Assert.That(experiment, Does.Contain("resumed_from=" + runner.CheckpointPath)
                .And.Contain("resumed_at_generation=" + trainedAtSave)
                .And.Contain("resumed_at_phase=" + runner.Evolution.Phase.ToString().ToLowerInvariant())
                .And.Contain("preset=minimal"), "the export says where it came from");
            Assert.That(File.Exists(Path.Combine(runner.EvolutionDirectory, "experiment.json")), Is.True);
            yield return Step(12);
            Assert.That(Position(), Is.EqualTo(later), "continues exactly like the saved run");
        }

        [UnityTest]
        public IEnumerator Controls_SaveAndLoadASharedWorld_AndRefuseABrokenFile()
        {
            UseTemporaryCheckpoint();
            yield return StartFromControls(LabMode.SharedWorld, preset: 0, seed: 4, brain: LabBrainKind.Neural);
            yield return Press("pause-button");
            yield return Step(5);
            var atSave = Position();
            yield return Press("save-button");
            yield return Step(20);
            var later = Position();

            yield return Press("load-button");
            Assert.That(runner.BrainKind, Is.EqualTo(LabBrainKind.Neural));
            Assert.That(Position(), Is.EqualTo(atSave));
            yield return Step(20);
            Assert.That(Position(), Is.EqualTo(later));

            File.WriteAllText(runner.CheckpointPath, "{\"format\": \"eden-checkpoint\", \"schemaVersion\": 7}");
            File.Delete(runner.CheckpointPath + ".bak");
            var simulation = runner.Simulation;
            yield return Press("load-button");
            StringAssert.Contains("recusado (a execução atual continua)", runner.Hud.CheckpointText);
            StringAssert.Contains("newer", runner.Hud.CheckpointText);
            Assert.That(runner.Simulation, Is.SameAs(simulation), "the run is untouched");
            Assert.That(Position(), Is.EqualTo(later));
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

        [UnityTest]
        public IEnumerator Benchmark_MeasuresTheRunAndWritesAReport()
        {
            var directory = Path.Combine(Path.GetTempPath(), "eden-benchmark-" + Guid.NewGuid().ToString("N"));
            runDirectories.Add(directory);
            runner.StartBenchmark(0.2, 1.0, directory);
            Assert.That(runner.MetricsDirectory, Is.Null, "no metrics log while measuring");

            var waited = 0f;
            while (runner.BenchmarkDirectory == null && waited < 15f)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            Assert.That(runner.BenchmarkDirectory, Is.EqualTo(directory));
            var json = File.ReadAllText(Path.Combine(directory, "performance.json"));
            StringAssert.Contains("\"build\": \"editor\"", json);
            StringAssert.Contains("\"mode\": \"shared\"", json);
            Assert.That(int.Parse(Regex.Match(json, "\"timedTicks\": ([0-9]+)").Groups[1].Value), Is.GreaterThan(0),
                "every shared world tick is timed");
            StringAssert.DoesNotContain("\"simulationAllocations\": null", json, "the Editor has the allocation recorder");
            Assert.That(File.ReadAllText(Path.Combine(directory, "performance.txt")), Does.Contain("FPS"));
            yield return null;
            Assert.That(runner.Simulation, Is.Not.Null, "the Editor keeps the lab running");
        }

        // Checkpoints of a test go to a temporary directory, never to the user's lab checkpoint.
        private void UseTemporaryCheckpoint()
        {
            var directory = Path.Combine(Path.GetTempPath(), "eden-lab-checkpoint-" + Guid.NewGuid().ToString("N"));
            runDirectories.Add(directory);
            runner.CheckpointPath = Path.Combine(directory, "lab.json");
        }

        // Exact state of what is shown: world fingerprint (agenda included) and, in evolution mode, the ticks run so far.
        private (ulong, long) Position() =>
            (runner.Simulation.ComputeFingerprint(), runner.Evolution != null && runner.Mode == LabMode.IsolatedEvolution
                ? runner.Evolution.TotalTicks
                : runner.Simulation.World.Tick);

        private IEnumerator Step(int ticks)
        {
            Assert.That(runner.Clock.IsPaused, Is.True, "steps are counted while paused");
            for (var i = 0; i < ticks; i++)
                yield return Press("step-button");
        }

        private IEnumerator StartFromControls(LabMode mode, int preset, int seed, LabBrainKind brain = LabBrainKind.Heuristic,
            bool evolvingBody = false)
        {
            var root = runner.Hud.Root;
            Assert.That(root, Is.Not.Null, "the HUD is bound");
            root.Q<DropdownField>("mode-field").index = (int)mode;
            root.Q<DropdownField>("preset-field").index = preset;
            var brainField = root.Q<DropdownField>("brain-field");
            Assert.That(brainField.choices.Count, Is.GreaterThan((int)brain), "the brain is offered");
            brainField.index = (int)brain;
            root.Q<IntegerField>("seed-field").value = seed;
            root.Q<DropdownField>("body-field").index = evolvingBody ? 1 : 0;
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
