using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Eden.Brains;
using Eden.Domain;
using Eden.Application;
using Eden.Evolution;
using Eden.Persistence;
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
        public IEnumerator Controls_RunANeatEvolution_InspectItsNetwork_SaveLoadAndShowItInTheSharedWorld()
        {
            UseTemporaryCheckpoint();
            var algorithmField = runner.Hud.Root.Q<DropdownField>("algorithm-field");
            Assert.That(algorithmField.value, Is.EqualTo(LabHud.AlgorithmChoices[0]), "the MLP control is the default");
            yield return StartFromControls(LabMode.IsolatedEvolution, preset: 0, seed: 5, neat: true);
            Assert.That(algorithmField.enabledSelf, Is.True, "the algorithm applies to a new evolution");
            Assert.That(runner.Neat, Is.True);
            Assert.That(runner.Evolution.Plan.Algorithm, Is.EqualTo(EvolutionAlgorithm.Neat));
            Assert.That(runner.BrainName, Is.EqualTo(NeatBrain.BrainName));
            StringAssert.Contains("NEAT: redes começam mínimas", runner.Hud.RunText);

            var session = runner.Evolution;
            yield return Press("max-button");
            // Past generation 0 and back to training genomes (not the reference brains of the first validation).
            yield return WaitUntil(() => session.Training.Count > 0 &&
                                         session.Phase == Eden.Application.EvolutionPhase.Training,
                EvolutionTimeoutSeconds);
            yield return Press("max-button");
            yield return Press("pause-button");
            Assert.That(session.CurrentIndividual, Is.Not.Null, "a genome is being evaluated");
            StringAssert.Contains("espécie(s)", runner.Hud.RunText);

            runner.SelectNeighbor(+1);
            runner.RefreshNetwork();
            yield return null;
            yield return null;
            var inspector = runner.Hud.InspectorText;
            StringAssert.Contains("rede NEAT com genoma próprio", inspector);
            StringAssert.Contains("Rede NEAT:", inspector);
            StringAssert.Contains("Espécie", inspector);
            Assert.That(runner.Hud.NetworkVisible, Is.True);
            Assert.That(runner.Hud.NeatNetworkShown, Is.True, "a NEAT network is not drawn as 12-16-3");
            Assert.That(runner.Hud.NeatGraph.RepaintCount, Is.GreaterThan(0), "the NEAT diagram was drawn");
            Assert.That(runner.Evolution.Job.Current.Controller.TryGetBrain(runner.Selected.Value, out var selectedBrain), Is.True);
            var brain = (NeatBrain)selectedBrain;
            Assert.That(runner.NeatNetwork.NodeCount, Is.EqualTo(brain.NodeIds.Count), "the diagram shows the real network");
            Assert.That(runner.NeatNetwork.ConnectionCount, Is.EqualTo(brain.Genome.Neat.Connections.Count));

            yield return Press("save-button");
            StringAssert.Contains("Checkpoint salvo", runner.Hud.CheckpointText);
            var atSave = Position();
            yield return StartFromControls(LabMode.IsolatedEvolution, preset: 0, seed: 6);
            Assert.That(runner.Evolution.Plan.Algorithm, Is.EqualTo(EvolutionAlgorithm.Mlp));
            yield return Press("load-button");
            StringAssert.Contains("Checkpoint carregado", runner.Hud.CheckpointText);
            Assert.That(runner.Evolution.Plan.Algorithm, Is.EqualTo(EvolutionAlgorithm.Neat));
            Assert.That(algorithmField.value, Is.EqualTo(LabHud.AlgorithmChoices[1]), "the controls show the loaded choice");
            Assert.That(Position(), Is.EqualTo(atSave));

            yield return StartFromControls(LabMode.SharedWorld, preset: 0, seed: 9, brain: LabBrainKind.Evolved, neat: true);
            Assert.That(algorithmField.enabledSelf, Is.False, "the shared world takes the networks of the evolution");
            Assert.That(runner.BrainName, Is.EqualTo(NeatBrain.BrainName));
            runner.Select(runner.Simulation.World.Creatures[1].Id);
            runner.RefreshNetwork();
            yield return null;
            Assert.That(runner.Hud.NeatNetworkShown, Is.True);
            StringAssert.Contains("Rede NEAT:", runner.Hud.InspectorText);

            yield return StartFromControls(LabMode.IsolatedEvolution, preset: 0, seed: 4);
            if (!runner.Clock.IsPaused)
                yield return Press("pause-button");
            yield return Press("step-button");
            runner.SelectNeighbor(+1);
            runner.RefreshNetwork();
            yield return null;
            Assert.That(runner.Hud.NetworkVisible && !runner.Hud.NeatNetworkShown, Is.True, "the MLP keeps its diagram");
        }

        [UnityTest]
        public IEnumerator Controls_EvolveWithEyes_DrawTheCones_AndInspectWhatTheEyesGave()
        {
            var sensesField = runner.Hud.Root.Q<DropdownField>("senses-field");
            var algorithmField = runner.Hud.Root.Q<DropdownField>("algorithm-field");
            Assert.That(sensesField.value, Is.EqualTo(LabHud.SensesChoices[0]), "the legacy senses are the default");
            sensesField.index = 1;
            Assert.That(algorithmField.index, Is.EqualTo(1), "eyes pick NEAT");
            algorithmField.index = 0;
            Assert.That(sensesField.index, Is.EqualTo(0), "the MLP control goes back to the legacy senses");

            var root = runner.Hud.Root;
            root.Q<DropdownField>("mode-field").index = (int)LabMode.IsolatedEvolution;
            root.Q<DropdownField>("preset-field").index = 0;
            root.Q<IntegerField>("seed-field").value = 8;
            sensesField.index = 1;
            yield return Press("start-button");
            Assert.That(runner.Eyes, Is.True);
            Assert.That(runner.Evolution.Plan.Senses, Is.EqualTo(Senses.Eyes));
            Assert.That(runner.Evolution.Plan.Algorithm, Is.EqualTo(EvolutionAlgorithm.Neat));
            Assert.That(runner.Simulation.World.Creatures[0].Eyes, Is.EqualTo(EyeSet.Founder));

            if (!runner.Clock.IsPaused)
                yield return Press("pause-button");
            runner.SelectNeighbor(+1);
            yield return null;
            StringAssert.Contains("Olhos (1 de 4", runner.Hud.InspectorText);
            StringAssert.Contains("olho 0: direção 0°  abertura 120°", runner.Hud.InspectorText);
            StringAssert.Contains("olho 1: ausente", runner.Hud.InspectorText);
            StringAssert.Contains("Leitura atual dos olhos", runner.Hud.InspectorText, "before any decision");
            Assert.That(runner.View.DrawnEyeCones, Is.EqualTo(1), "the selected creature's cone");
            Assert.That(runner.View.TryGetDrawnBody(runner.Selected.Value, out _, out var disc), Is.True);
            Assert.That(disc, Is.Zero, "no circular sensor disc for eyes");

            yield return Press("step-button");
            yield return Press("step-button");
            Assert.That(runner.Evolution.Job.Current.Tick, Is.EqualTo(2));
            runner.RefreshNetwork();
            yield return WaitSeconds(0.4f); // the inspector text refreshes at 4 Hz
            StringAssert.Contains("Entrada do cérebro na última decisão", runner.Hud.InspectorText);
            StringAssert.Contains("+ olhos", runner.Hud.InspectorText, "the eyes' upkeep in the costs");
            Assert.That(runner.Hud.NeatNetworkShown, Is.True, "an eye brain is a NEAT network");
            Assert.That(runner.NeatNetwork.NodeCount, Is.EqualTo(1 + 40 + 3), "bias, 40 eye-contract inputs, 3 outputs");
        }

        [UnityTest]
        public IEnumerator Controls_ASolidArenaWithPillars_IsDrawnAndInspected_AndTheEvolutionUsesTheSameArena()
        {
            var arenaField = runner.Hud.Root.Q<DropdownField>("arena-field");
            Assert.That(arenaField.value, Is.EqualTo(LabHud.ArenaChoices[0]), "the open arena is the default");
            arenaField.index = 2;
            yield return StartFromControls(LabMode.SharedWorld, preset: 2, seed: 5);

            var world = runner.Simulation.World;
            Assert.That(runner.SolidBodies, Is.True);
            Assert.That(runner.Obstacles, Is.EqualTo(ObstacleLayout.Pillars));
            Assert.That(world.Config.SolidBodies, Is.True);
            Assert.That(world.Config.Obstacles.Count, Is.EqualTo(9));
            Assert.That(runner.View.DrawnObstacles, Is.EqualTo(9));
            StringAssert.Contains("Arena: corpos sólidos (raio ", runner.Hud.RunText);
            StringAssert.Contains("pilares (9 obstáculos)", runner.Hud.RunText);
            StringAssert.Contains("Contatos no último tick", runner.Hud.RunText);
            Assert.That(File.ReadAllLines(Path.Combine(runner.MetricsDirectory, "run-info.txt")),
                Does.Contain("obstacle_layout=pillars"));

            // At full speed the heuristic crowd meets at the food: contacts are found and drawn.
            var pairs = new List<TouchingPair>();
            for (var attempt = 0; attempt < 10 && new PhysicalInspection().FindPairs(world, pairs) == 0; attempt++)
            {
                yield return Press("max-button");
                yield return WaitUntil(() => runner.View.DrawnContacts > 0, 30f);
                yield return Press("max-button");
                if (!runner.Clock.IsPaused)
                    yield return Press("pause-button");
                yield return null;
            }

            Assert.That(new PhysicalInspection().FindPairs(world, pairs), Is.GreaterThan(0), "paused while bodies touch");
            Assert.That(runner.View.DrawnContacts, Is.EqualTo(new PhysicalInspection().FindPairs(world, pairs)));
            foreach (var creature in world.Creatures)
            {
                foreach (var obstacle in world.Config.Obstacles)
                    Assert.That(obstacle.DistanceTo(creature.Position), Is.GreaterThanOrEqualTo(creature.Body.Radius - 1e-3f),
                        $"{creature.Id} inside an obstacle");
            }

            runner.Select(world.Creatures[pairs[0].First].Id);
            yield return null;
            StringAssert.Contains("Arena física", runner.Hud.InspectorText);
            StringAssert.Contains("Corpo sólido: disco de raio", runner.Hud.InspectorText);
            StringAssert.Contains("Encostada em: criatura", runner.Hud.InspectorText);
            var selected = runner.Selected.Value;
            Assert.That(runner.View.TryGetDrawnBody(selected, out var scale, out _), Is.True);
            Assert.That(scale, Is.EqualTo(2f * world.Creatures[pairs[0].First].Body.Radius).Within(1e-5f),
                "the body drawn is the solid disc");
            Assert.That(runner.View.TryGetDrawnReach(selected, out var reach), Is.True);
            Assert.That(reach, Is.EqualTo(2f * world.Config.EatRadius).Within(1e-5f), "the mouth's reach around it");

            yield return StartFromControls(LabMode.IsolatedEvolution, preset: 0, seed: 3);
            Assert.That(arenaField.index, Is.EqualTo(2), "the arena choice stays");
            var episode = runner.Simulation.World.Config;
            Assert.That(episode.SolidBodies, Is.True);
            Assert.That(episode.Obstacles.Count, Is.EqualTo(9), "the episodes have the same arena");
            Assert.That(runner.View.DrawnObstacles, Is.EqualTo(9));
            StringAssert.Contains("Arena dos episódios: corpos sólidos", runner.Hud.RunText);
            Assert.That(File.ReadAllLines(Path.Combine(runner.EvolutionDirectory, "experiment.txt")),
                Does.Contain("obstacle_layout=pillars"));

            arenaField.index = 0;
            yield return StartFromControls(LabMode.SharedWorld, preset: 0, seed: 5);
            Assert.That(runner.Simulation.World.Config.SolidBodies, Is.False);
            Assert.That(runner.View.DrawnObstacles, Is.Zero);
            Assert.That(runner.View.DrawnContacts, Is.Zero);
            StringAssert.Contains("Arena: aberta", runner.Hud.RunText);
        }

        [UnityTest]
        public IEnumerator Controls_ALargerScenario_AppliesToBothModes_AndShowsItsSupply()
        {
            var root = runner.Hud.Root;
            Assert.That(root.Q<DropdownField>("scenario-size-field").value, Is.EqualTo(LabHud.SizeChoices[0]),
                "the 50 × 50 control is the default");
            root.Q<DropdownField>("scenario-size-field").index = 1;
            root.Q<IntegerField>("scenario-food-field").value = 10;
            root.Q<FloatField>("scenario-food-energy-field").value = 4f;
            root.Q<DropdownField>("scenario-water-field").index = 1;
            root.Q<FloatField>("scenario-episode-field").value = 30f;
            var maxAge = root.Q<FloatField>("scenario-max-age-field");
            maxAge.value = -5f;
            yield return null;
            Assert.That(maxAge.value, Is.EqualTo(180f), "a value the simulation would refuse goes back");
            yield return StartFromControls(LabMode.SharedWorld, preset: 0, seed: 5);

            var config = runner.Simulation.World.Config;
            Assert.That((config.ArenaWidth, config.ArenaHeight), Is.EqualTo((100f, 100f)));
            Assert.That(config.InitialFood, Is.EqualTo(40), "10 in the 50 × 50 control, kept dense");
            Assert.That(config.FoodEnergy, Is.EqualTo(4f));
            Assert.That(config.Water.Count, Is.EqualTo(1));
            Assert.That(config.SolidBodies, Is.True, "water implies solid bodies");
            StringAssert.Contains("Cenário 100×100", runner.Hud.RunText);
            StringAssert.Contains("Oferta de comida: até", runner.Hud.RunText);
            StringAssert.Contains("Oferta de água", runner.Hud.RunText);
            Assert.That(File.ReadAllLines(Path.Combine(runner.MetricsDirectory, "run-info.txt")),
                Does.Contain("scenario=arena 100x100;resources keep density (x4 of the 50x50 control)")
                    .And.Some.StartsWith("supply=creatures 32;"));

            yield return StartFromControls(LabMode.IsolatedEvolution, preset: 0, seed: 3);
            var episode = runner.Simulation.World.Config;
            Assert.That(episode.ArenaWidth, Is.EqualTo(100f), "the episodes have the same scenario");
            Assert.That(episode.InitialFood, Is.EqualTo(40), "the food given replaces the episodes' own");
            Assert.That(runner.Evolution.Plan.EpisodeSeconds, Is.EqualTo(30f));
            Assert.That(root.Q<DropdownField>("scenario-size-field").index, Is.EqualTo(1), "the scenario stays");
            StringAssert.Contains("Cenário 100×100", runner.Hud.RunText);
            Assert.That(File.ReadAllLines(Path.Combine(runner.EvolutionDirectory, "experiment.txt")),
                Does.Contain("episode_food=40").And.Some.StartsWith("scenario=arena 100x100"));

            root.Q<DropdownField>("scenario-size-field").index = 0;
            root.Q<IntegerField>("scenario-food-field").value = 0;
            root.Q<DropdownField>("scenario-water-field").index = 0;
            yield return StartFromControls(LabMode.IsolatedEvolution, preset: 0, seed: 3);
            episode = runner.Simulation.World.Config;
            Assert.That((episode.ArenaWidth, episode.InitialFood, episode.Water.Count), Is.EqualTo((50f, 32, 0)),
                "back to the control and the episodes' own food");
        }

        [UnityTest]
        public IEnumerator Controls_AComposition_PutsExactCopiesInTheArena_AndARefusalSaysWhy()
        {
            var directory = Path.Combine(Path.GetTempPath(), "eden-composition-" + Guid.NewGuid().ToString("N"));
            runDirectories.Add(directory);
            Directory.CreateDirectory(directory);
            var a = InitialGenomes.RandomFor(SimulationConfig.CreateDefault(1), new CreatureId(1));
            var b = new Genome(NeatNetwork.CreateMinimal(NeuralContract.Legacy, new Pcg32(3, 1), 1f),
                new BodyGenes(0.5f, 0f, 0f, 0f));
            GenomeFile.Save(Path.Combine(directory, "a.json"), new GenomeRecord(a, 1f), "0.1.0", DateTime.UtcNow);
            GenomeFile.Save(Path.Combine(directory, "b.json"), new GenomeRecord(b, 2f), "0.1.0", DateTime.UtcNow);

            var root = runner.Hud.Root;
            root.Q<DropdownField>("brain-field").value = LabHud.BrainChoices[(int)LabBrainKind.Composition];
            yield return null;
            Assert.That(root.Q<VisualElement>("composition-panel").resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
            root.Q<TextField>("seed-genomes-field").value = directory;
            root.Q<TextField>("composition-field").value = "a*20; #1*5";
            root.Q<DropdownField>("arena-field").index = 2;
            yield return Press("start-button");

            var world = runner.Simulation.World;
            Assert.That(runner.BrainKind, Is.EqualTo(LabBrainKind.Composition));
            Assert.That(world.Creatures.Count, Is.EqualTo(25));
            Assert.That(world.Config.Reproduction, Is.False, "frozen by default");
            Assert.That(world.Creatures.Count(c => c.Genes.Equals(b.Body)), Is.EqualTo(5), "#1 is b, the best");
            StringAssert.Contains("Composição: a.json ×20 + b.json ×5 = 25 criaturas", runner.Hud.RunText);
            StringAssert.Contains("congelados", runner.Hud.RunText);
            runner.Select(new CreatureId(22));
            yield return null;
            StringAssert.Contains("Composição: b.json, cópia 2 de 5", runner.Hud.InspectorText);

            root.Q<TextField>("composition-field").value = "c*3";
            yield return Press("start-button");
            Assert.That(runner.BrainKind, Is.EqualTo(LabBrainKind.Heuristic), "a composition that cannot be read is not used");
            StringAssert.Contains("Composição não usada", runner.GenomeExportStatus);
            StringAssert.Contains("no genome named c", runner.GenomeExportStatus);
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
        public IEnumerator SharedWorld_WithReproduction_ChildrenGetGenomesBrainsAndSurviveASaveAndLoad()
        {
            var rules = runner.WorldTemplate;
            rules.Reproduction = true;
            rules.MaturitySeconds = 2f;
            rules.ReproductionEnergyFraction = 0.3f;
            UseTemporaryCheckpoint();
            yield return StartFromControls(LabMode.SharedWorld, preset: 0, seed: 4, brain: LabBrainKind.Neural);
            yield return Press("pause-button");
            Assert.That(runner.Simulation.World.Config.Reproduction, Is.True);
            yield return Step(45);

            var world = runner.Simulation.World;
            var child = world.Creatures.FirstOrDefault(c => c.Parent.HasValue);
            Assert.That(child, Is.Not.Null, "founders give birth once mature");
            Assert.That(child.Generation, Is.EqualTo(1));
            Assert.That(runner.SharedGenomes.GenomeOf(child.Id), Is.Not.Null);
            yield return Step(2);
            Assert.That(runner.TryGetHeldAction(child.Id, out _), Is.True, "the child got its own brain");
            runner.Select(child.Id);
            yield return null;
            StringAssert.Contains($"filha de {child.Parent.Value}", runner.Hud.InspectorText);
            StringAssert.Contains("Reprodução local ligada", runner.Hud.RunText);

            yield return Press("save-button");
            yield return Step(30);
            var later = runner.Simulation.ComputeFingerprint();
            var born = runner.SharedGenomes.BornCount;
            yield return Press("load-button");
            StringAssert.DoesNotContain("recusado", runner.Hud.CheckpointText);
            Assert.That(runner.Simulation.World.Config.Reproduction, Is.True);
            yield return Step(30);
            Assert.That(runner.Simulation.ComputeFingerprint(), Is.EqualTo(later), "the loaded run continues identically");
            Assert.That(runner.SharedGenomes.BornCount, Is.EqualTo(born));
        }

        [UnityTest]
        public IEnumerator SharedWorld_Extinction_StopsTheRun_IsRecorded_AndOnlyARestartGoesOn()
        {
            var rules = runner.WorldTemplate;
            rules.Reproduction = true;
            rules.MaturitySeconds = 30f;
            rules.MaxAgeSeconds = 2f;
            yield return StartFromControls(LabMode.SharedWorld, preset: 0, seed: 5, brain: LabBrainKind.Heuristic);
            Assert.That(runner.Genealogy, Is.Not.Null);
            yield return Press("max-button");
            yield return WaitUntil(() => runner.ExtinctionTick.HasValue, 30f);
            var directory = runner.RunDirectory;
            var tick = runner.Simulation.World.Tick;

            Assert.That(runner.Clock.IsPaused, Is.True, "extinction pauses the lab");
            StringAssert.Contains($"EXTINÇÃO no tick {runner.ExtinctionTick}", runner.Hud.RunText);
            StringAssert.Contains("ninguém é reposto", runner.Hud.RunText);
            yield return Press("pause-button");
            yield return WaitSeconds(0.3f);
            yield return Press("step-button");
            Assert.That(runner.Simulation.World.Tick, Is.EqualTo(tick), "an extinct world does not go on");
            Assert.That(runner.Simulation.World.Creatures, Is.Empty);

            runner.Select(new CreatureId(1));
            yield return null;
            StringAssert.Contains("Genealogia (geração 0, fundadora)", runner.Hud.InspectorText);
            StringAssert.Contains("morreu de idade", runner.Hud.InspectorText);

            runner.CloseLogs();
            var metrics = File.ReadAllLines(Path.Combine(directory, "metrics.csv"));
            Assert.That(metrics.Last(), Does.EndWith(",1"), "the extinction closes a metrics window");
            var genealogy = File.ReadAllLines(Path.Combine(directory, "genealogy.csv"));
            Assert.That(genealogy.Last(), Is.EqualTo($"{runner.ExtinctionTick},extinction,,,,"));
            Assert.That(genealogy.Count(r => r.Contains(",founder,")), Is.EqualTo(32));

            yield return Press("start-button");
            Assert.That(runner.Simulation.World.Creatures.Count, Is.EqualTo(32), "a restart is a new world");
            Assert.That(runner.ExtinctionTick, Is.Null);
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
            StringAssert.Contains("\"arena\": \"50x50\"", json);
            StringAssert.Contains("\"bodies\": \"points\"", json);
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
            bool evolvingBody = false, bool neat = false)
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
            root.Q<DropdownField>("algorithm-field").index = neat ? 1 : 0;
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
