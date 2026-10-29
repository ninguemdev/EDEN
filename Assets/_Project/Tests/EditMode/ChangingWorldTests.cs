using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Eden.Application;
using Eden.Domain;
using Eden.Evolution;
using Eden.Persistence;
using Eden.Simulation;
using NUnit.Framework;
using static Eden.Tests.EditMode.ResumeChecks;

namespace Eden.Tests.EditMode
{
    /// <summary>A world that changes during a life (food moves halfway) and the plasticity comparison built on it.</summary>
    public class ChangingWorldTests
    {
        private string root;

        [SetUp]
        public void CreateDirectory()
        {
            root = Path.Combine(Path.GetTempPath(), "eden-plasticity-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
        }

        [TearDown]
        public void DeleteDirectory()
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }

        [Test]
        public void Food_LivesInTheLeftHalf_ThenMovesToTheRightHalfAtTheShift()
        {
            var settings = new SimulationSettings
            {
                Seed = 9, InitialPopulation = 0, InitialFood = 30, FoodShiftSeconds = 2f, FoodRespawnSeconds = 0.5f
            };
            var config = new SimulationConfig(settings);
            Assert.That(config.FoodShiftTicks, Is.EqualTo(40));
            var world = WorldFactory.CreateInitial(config);
            var half = config.ArenaWidth / 2f;
            Assert.That(world.Food.All(f => f.Position.X <= half), Is.True, "before the shift: left half");

            var eater = world.AddCreature(world.Food[0].Position, 0f, 50f);
            var simulation = new WorldSimulation(world);
            var idle = new ConstantController(CreatureAction.Idle);
            var respawnedLeft = 0;
            for (var t = 0; t < config.FoodShiftTicks; t++)
            {
                simulation.Step(idle);
                respawnedLeft += simulation.LastFoodRespawned;
            }

            Assert.That(respawnedLeft, Is.EqualTo(1), "the eaten item came back before the shift");
            Assert.That(world.Food.All(f => f.Position.X <= half), Is.True, "respawns before the shift stay left");
            var ids = world.Food.Select(f => f.Id.Value).ToArray();

            simulation.Step(idle);
            Assert.That(world.Food.Select(f => f.Id.Value), Is.EqualTo(ids), "the same items move");
            Assert.That(world.Food.All(f => f.Position.X >= half), Is.True, "after the shift: right half");

            eater.Position = world.Food[0].Position;
            for (var t = 0; t < 30; t++)
                simulation.Step(idle);
            Assert.That(world.Food.Count, Is.EqualTo(30));
            Assert.That(world.Food.All(f => f.Position.X >= half), Is.True, "respawns after the shift go right");
        }

        [Test]
        public void Shift_IsPartOfTheRun_ItResumesExactly_AndNoShiftChangesNothing()
        {
            var settings = new SimulationSettings { Seed = 4, InitialPopulation = 10, InitialFood = 40, FoodShiftSeconds = 3f };
            var config = new SimulationConfig(settings);
            var original = new WorldSimulation(WorldFactory.CreateInitial(config));
            var brains = BrainController.Shared(config, new Eden.Brains.HeuristicBrain());
            Run(original, brains, 30);
            var saved = original.Capture();
            var savedBrains = brains.Capture();
            Run(original, brains, 100);

            var checkpoint = new Checkpoint("shared", "minimal", 4, "heuristic", null,
                new SharedWorldSnapshot(settings, saved, savedBrains));
            var text = CheckpointCodec.Encode(checkpoint, "0.1.0", "test", new DateTime(2026, 10, 27, 0, 0, 0, DateTimeKind.Utc));
            Assert.That(text, Does.Contain("\"foodShiftSeconds\": 3"));
            var loaded = CheckpointCodec.Decode(text, out _).SharedWorld;
            var restored = WorldSimulation.Restore(new SimulationConfig(loaded.Settings), loaded.World);
            var restoredBrains = BrainController.Shared(config, new Eden.Brains.HeuristicBrain());
            restoredBrains.Restore(loaded.Brains);
            Run(restored, restoredBrains, 100);
            Assert.That(restored.ComputeFingerprint(), Is.EqualTo(original.ComputeFingerprint()));

            var plain = new SimulationSettings { Seed = 4, InitialPopulation = 10, InitialFood = 40 };
            var plainText = CheckpointCodec.Encode(new Checkpoint("shared", "minimal", 4, "heuristic", null,
                new SharedWorldSnapshot(plain, new WorldSimulation(WorldFactory.CreateInitial(new SimulationConfig(plain)))
                    .Capture(), Array.Empty<BrainSlotState>())), "0.1.0", "test", DateTime.UtcNow);
            Assert.That(plainText, Does.Not.Contain("foodShiftSeconds"), "no shift is written as before");
            Assert.That(() => new SimulationConfig(new SimulationSettings { FoodShiftSeconds = -1f }), Throws.ArgumentException);

            var evolution = Small(1);
            evolution.World.FoodShiftSeconds = 2.5f;
            Assert.That(new EvaluationPlan(evolution).EpisodeConfig(3).FoodShiftTicks, Is.EqualTo(50), "episodes change too");
        }

        [Test]
        public void Comparison_RunsFourCellsOnOneBudget_AndTheLesionMatchesAFixedPopulation()
        {
            var template = Small(5);
            template.EpisodeSeconds = 5f;
            var battery = new BatterySettings { Runs = 2, Generations = 2, ValidationInterval = 1 };
            var comparison = new PlasticityComparison(template, battery, 2.5f);
            var finals = new Dictionary<string, GenerationReport[]>();
            var result = comparison.Run((cell, run, report) =>
            {
                if (report.Evaluation.Mode != EvaluationMode.Training || report.Evaluation.Generation != battery.Generations - 1)
                    return;
                if (!finals.TryGetValue(cell.Name, out var reports))
                    finals.Add(cell.Name, reports = new GenerationReport[battery.Runs]);
                reports[run] = report;
            });

            Assert.That(result.Outcomes.Select(o => o.Cell.Name),
                Is.EqualTo(new[] { "static-fixed", "static-plastic", "changing-fixed", "changing-plastic" }));
            Assert.That(result.Outcomes.Select(o => o.TrainingEpisodes).Distinct().Single(), Is.EqualTo(2L * 2 * 6 * 2));
            Assert.That(result.Outcomes.Select(o => o.ValidationEpisodes).Distinct().Single(), Is.EqualTo(2L * 2 * 6 * 3));
            Assert.That(result.Get(false, false).Lesioned, Is.Null);
            Assert.That(result.Get(true, true).Lesioned, Is.Not.Null);
            Assert.That(comparison.Settings(new PlasticityCell(true, true)).World.FoodShiftSeconds, Is.EqualTo(2.5f));
            Assert.That(comparison.Settings(new PlasticityCell(false, true)).Plasticity, Is.True);

            var plain = new EvolutionBattery(comparison.Settings(new PlasticityCell(false, false)), battery).Run();
            for (var r = 0; r < battery.Runs; r++)
                Assert.That(Bits(result.Get(false, false).Result.Runs[r].Final.Validation.Median),
                    Is.EqualTo(Bits(plain.Runs[r].Final.Validation.Median)), "a cell is the plain battery");

            // Switching learning off in a population that never learned changes nothing: the lesion reproduces its
            // own validation exactly.
            var fixedLesion = PlasticityComparison.Lesion(comparison.Settings(new PlasticityCell(false, false)),
                finals["static-fixed"]);
            for (var r = 0; r < battery.Runs; r++)
                Assert.That(Bits(fixedLesion[r]), Is.EqualTo(Bits(result.Get(false, false).Result.Runs[r].Final.Validation.Median)));

            var culture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("pt-BR");
                PlasticityComparisonReport.Write(Path.Combine(root, "plasticity"), comparison, result, "0.1.0",
                    new DateTime(2026, 10, 27, 0, 0, 0, DateTimeKind.Utc));
            }
            finally
            {
                CultureInfo.CurrentCulture = culture;
            }

            var directory = Path.Combine(root, "plasticity");
            var rows = File.ReadAllLines(Path.Combine(directory, "cells.csv"));
            Assert.That(rows, Has.Length.EqualTo(5));
            Assert.That(rows[0], Is.EqualTo(PlasticityComparisonReport.CellsHeader));
            Assert.That(rows.Select(r => r.Split(',').Length), Is.All.EqualTo(rows[0].Split(',').Length), "invariant numbers");
            Assert.That(rows[4], Does.StartWith("changing-plastic,changing,plastic,2,2,48,72,"));
            var json = JsonText.Parse(File.ReadAllText(Path.Combine(directory, "comparison.json")));
            Assert.That(json["format"].String(), Is.EqualTo(PlasticityComparisonReport.Format));
            Assert.That(json["cells"].Count, Is.EqualTo(4));
            Assert.That(json["effects"]["changing"]["withMinusWithoutLearningSeparated"].Kind, Is.EqualTo(JsonKind.Boolean));
            var text = File.ReadAllText(Path.Combine(directory, "comparison.txt"));
            StringAssert.Contains("PRELIMINAR", text);
            StringAssert.Contains("Lesão", text);
            StringAssert.Contains("mesmo orçamento", text);
            StringAssert.Contains("Conclusão: ", text);
            Assert.That(File.Exists(Path.Combine(directory, "changing-plastic", "report.txt")), Is.True);
        }
    }
}
