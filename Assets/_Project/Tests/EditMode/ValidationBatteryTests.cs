using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using Eden.Application;
using Eden.Domain;
using Eden.Evolution;
using Eden.Persistence;
using NUnit.Framework;

namespace Eden.Tests.EditMode
{
    public class ValidationBatteryTests
    {
        [Test]
        public void ValidationSeeds_AreFixedHeldOutAndFollowTheScheme()
        {
            var plan = new EvaluationPlan(new EvolutionSettings { ExperimentSeed = 7, ValidationSeed = 3 });
            var root = SplitMix64.Output(3, 0x76616C69UL);

            Assert.That(plan.ValidationSeedCount, Is.EqualTo(5));
            Assert.That(plan.ValidationSeeds(), Is.EqualTo(Enumerable.Range(1, 5).Select(i => SplitMix64.Output(root, (ulong)i))));
            Assert.That(plan.ValidationSeeds().Distinct().Count(), Is.EqualTo(5));

            var training = new HashSet<ulong>();
            for (var g = 0; g < 500; g++)
                training.UnionWith(plan.TrainingSeeds(g));
            Assert.That(plan.ValidationSeeds().Any(training.Contains), Is.False, "never a training seed");

            var otherExperiment = new EvaluationPlan(new EvolutionSettings { ExperimentSeed = 8, ValidationSeed = 3 });
            Assert.That(otherExperiment.ValidationSeeds(), Is.EqualTo(plan.ValidationSeeds()), "shared by the runs of a battery");
            Assert.That(otherExperiment.TrainingSeeds(0), Is.Not.EqualTo(plan.TrainingSeeds(0)));

            var copy = plan.ValidationSeeds();
            copy[0] = 0;
            Assert.That(plan.ValidationSeeds()[0], Is.Not.Zero, "callers get a copy");
            Assert.Throws<ArgumentException>(() => new EvaluationPlan(new EvolutionSettings { ValidationSeedCount = 0 }));
        }

        [Test]
        public void Training_NeverReceivesValidationResults()
        {
            var plain = new GenerationalEvolution(Small(61));
            var validated = new GenerationalEvolution(Small(61));
            var otherValidationSeeds = Small(61);
            otherValidationSeeds.ValidationSeed = 999;
            var validatedElsewhere = new GenerationalEvolution(otherValidationSeeds);

            for (var g = 0; g < 4; g++)
            {
                var a = plain.Advance();
                var b = validated.Advance();
                var c = validatedElsewhere.Advance();
                var check = validated.Validate(b);
                validatedElsewhere.Validate(c);

                Assert.That(check.Evaluation.Mode, Is.EqualTo(EvaluationMode.Validation));
                Assert.That(check.Evaluation.Seeds, Is.EqualTo(validated.Plan.ValidationSeeds()));
                Assert.That(check.Individuals, Is.EqualTo(b.Individuals));
                Assert.Throws<ArgumentException>(() => validated.Validate(check), "validation is never an input");
                AssertSameTraining(a, b);
                AssertSameTraining(a, c);
            }

            Assert.That(validated.Lineage.NextId, Is.EqualTo(plain.Lineage.NextId));
            for (var i = 0; i < plain.Population.Count; i++)
                Assert.That(validated.Population[i].HasSameContent(plain.Population[i]), Is.True);
        }

        [Test]
        public void Battery_RunsIndependentSeedsAgainstBaselinesOnTheSameScenarios()
        {
            var battery = new BatterySettings { Runs = 2, Generations = 3, ValidationInterval = 2 };
            var runner = new EvolutionBattery(Small(70), battery);
            var reports = new List<(int Run, GenerationReport Report)>();

            var result = runner.Run((run, report) => reports.Add((run, report)));

            Assert.That(result.IsPreliminary, Is.True);
            Assert.That(result.Runs.Select(r => r.ExperimentSeed), Is.EqualTo(new ulong[] { 70, 71 }));
            Assert.That(result.Runs.Select(r => r.Points.Select(p => p.Generation)), Is.All.EqualTo(new[] { 0, 2 }));
            Assert.That(reports.Count(r => r.Report.Evaluation.Mode == EvaluationMode.Training), Is.EqualTo(6));
            Assert.That(reports.Count(r => r.Report.Evaluation.Mode == EvaluationMode.Validation), Is.EqualTo(4));
            foreach (var (_, report) in reports.Where(r => r.Report.Evaluation.Mode == EvaluationMode.Validation))
                Assert.That(report.Evaluation.Seeds, Is.EqualTo(runner.Plan.ValidationSeeds()));
            Assert.That(result.RandomBaseline.Episodes.Count, Is.EqualTo(runner.Plan.ValidationSeedCount),
                "baselines get the same number of episodes as every genome");
            Assert.That(result.RandomBaseline.Episodes.Select(e => e.EpisodeSeed), Is.EqualTo(runner.Plan.ValidationSeeds()));

            var standalone = new GenerationalEvolution(Small(71));
            var training = standalone.Advance();
            var validation = standalone.Validate(training);
            var initial = result.Runs[1].Initial;
            Assert.That(initial.Validation.Median, Is.EqualTo(validation.Summary.Median), "a run is an ordinary evolution");
            Assert.That(initial.ChampionId, Is.EqualTo(training.Summary.BestIndividualId));
            var championIndex = training.Individuals.ToList().FindIndex(i => i.Id == initial.ChampionId);
            Assert.That(initial.ChampionFitness, Is.EqualTo(validation.Evaluation.Candidates[championIndex].MeanFitness));

            var again = new EvolutionBattery(Small(70), battery).Run();
            Assert.That(ValidationRows(again), Is.EqualTo(ValidationRows(result)), "same settings, same battery");
            Assert.That(BatteryReport.FormatBaselineRow("heuristic", again.HeuristicBaseline),
                Is.EqualTo(BatteryReport.FormatBaselineRow("heuristic", result.HeuristicBaseline)));
        }

        [Test]
        public void Report_StatesMissingGainInsteadOfHidingIt()
        {
            var settings = new BatterySettings { Runs = 2, Generations = 30, ValidationInterval = 29 };
            var plan = new EvaluationPlan(new EvolutionSettings());
            var runs = new[]
            {
                Run(0, initialMedian: 0.5f, finalMedian: 0.4f, champion: 0.6f),
                Run(1, initialMedian: 0.5f, finalMedian: 0.5f, champion: 0.7f)
            };
            var result = new BatteryResult(settings, plan, runs, Candidate(0.2f), Candidate(0.8f));

            Assert.That(result.IsPreliminary, Is.True, "2 runs is below the acceptance battery");
            Assert.That(result.MedianImproved, Is.Zero);
            Assert.That(result.MedianBeatsRandom, Is.EqualTo(2));
            Assert.That(result.MedianBeatsHeuristic, Is.Zero);
            Assert.That(result.ChampionBeatsHeuristic, Is.Zero);
            Assert.That(result.FinalValidationMedian.Mean, Is.EqualTo(0.45f).Within(1e-6f));
            Assert.That(result.FinalValidationMedian.StdDev, Is.EqualTo((float)Math.Sqrt(0.005)).Within(1e-6f));
            Assert.That(result.FinalChampion.Min, Is.EqualTo(0.6f));
            Assert.That(result.FinalChampion.Max, Is.EqualTo(0.7f));

            var text = string.Join("\n", BatteryReport.Describe(result));
            Assert.That(text, Does.Contain("PRELIMINAR"));
            Assert.That(text, Does.Contain("sem ganho na validação"));
            Assert.That(text, Does.Contain("mediana > heurístico:       0/2"));
            Assert.That(text, Does.Contain("heurístico só em 0"));

            var full = new BatteryResult(new BatterySettings(), plan,
                Enumerable.Range(0, 5).Select(i => Run(i, 0.1f, 1f, 1.2f)).ToArray(), Candidate(0.2f), Candidate(0.8f));
            Assert.That(full.IsPreliminary, Is.False);
            var fullText = string.Join("\n", BatteryReport.Describe(full));
            Assert.That(fullText, Does.Not.Contain("PRELIMINAR"));
            Assert.That(fullText, Does.Contain("ganho na validação em todas as execuções"));
        }

        [Test]
        public void BatteryFiles_AreInvariantCsvAndAReadableReport()
        {
            var previous = Thread.CurrentThread.CurrentCulture;
            var directory = Path.Combine(Path.GetTempPath(), "eden-battery-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("pt-BR");
                var result = new EvolutionBattery(Small(80), new BatterySettings { Runs = 1, Generations = 2, ValidationInterval = 1 })
                    .Run();

                BatteryReport.Write(directory, result);

                var validation = File.ReadAllLines(Path.Combine(directory, "validation.csv"));
                Assert.That(validation[0], Is.EqualTo(BatteryReport.ValidationHeader));
                Assert.That(validation, Has.Length.EqualTo(3));
                Assert.That(validation[1], Does.StartWith("0,80,0,"));
                Assert.That(validation[2], Does.StartWith("0,80,1,"));
                var baselines = File.ReadAllLines(Path.Combine(directory, "baselines.csv"));
                Assert.That(baselines.Select(l => l.Split(',')[0]), Is.EqualTo(new[] { "brain", "random", "heuristic" }));
                foreach (var line in validation.Concat(baselines))
                {
                    Assert.That(line, Does.Not.Contain("NaN").And.Not.Contain("∞"));
                    Assert.That(line.Split(',').Length,
                        Is.EqualTo((validation.Contains(line) ? BatteryReport.ValidationHeader : BatteryReport.BaselinesHeader)
                            .Split(',').Length));
                }

                var report = File.ReadAllText(Path.Combine(directory, "report.txt"));
                Assert.That(report, Does.Contain("Referências").And.Contain("Conclusão").And.Contain("Custo"));
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = previous;
                if (Directory.Exists(directory))
                    Directory.Delete(directory, true);
            }
        }

        [Test]
        public void SettingsCopy_IsDeep()
        {
            var original = new EvolutionSettings { ExperimentSeed = 1 };
            var copy = original.WithExperimentSeed(9);
            copy.World.InitialFood = 5;
            copy.PopulationSize = 3;

            Assert.That(copy.ExperimentSeed, Is.EqualTo(9UL));
            Assert.That(original.ExperimentSeed, Is.EqualTo(1UL));
            Assert.That(original.World.InitialFood, Is.EqualTo(32));
            Assert.That(original.PopulationSize, Is.EqualTo(64));
            Assert.Throws<ArgumentException>(() => new BatterySettings { Runs = 0 }.Validate());
        }

        private static string[] ValidationRows(BatteryResult result) =>
            result.Runs.SelectMany(run => run.Points.Select(point => BatteryReport.FormatValidationRow(run, point))).ToArray();

        private static void AssertSameTraining(GenerationReport expected, GenerationReport actual)
        {
            Assert.That(EvolutionCsvLog.FormatGeneration(actual.Summary), Is.EqualTo(EvolutionCsvLog.FormatGeneration(expected.Summary)));
            for (var c = 0; c < expected.Individuals.Count; c++)
                Assert.That(EvolutionCsvLog.FormatIndividual(actual, c), Is.EqualTo(EvolutionCsvLog.FormatIndividual(expected, c)));
        }

        private static EvolutionSettings Small(ulong seed)
        {
            var settings = new EvolutionSettings
            {
                ExperimentSeed = seed, EpisodeSeconds = 10f, TrainingSeedsPerGeneration = 2, ValidationSeedCount = 3,
                PopulationSize = 6, EliteCount = 2
            };
            settings.World.InitialFood = 64;
            return settings;
        }

        private static ExperimentRun Run(int index, float initialMedian, float finalMedian, float champion) =>
            new ExperimentRun(index, (ulong)index + 1, new[]
            {
                Point(0, initialMedian, initialMedian),
                Point(29, finalMedian, champion)
            }, 1.0);

        private static ValidationPoint Point(int generation, float median, float champion)
        {
            var individuals = new Lineage().Founders(InitialGenomes.Population(1, 3));
            var fitness = new[] { median - 0.1f, median, champion };
            var training = new GenerationSummary(Evaluation(generation, EvaluationMode.Training, fitness), individuals);
            var validation = new GenerationSummary(Evaluation(generation, EvaluationMode.Validation, fitness), individuals);
            return new ValidationPoint(training, validation, individuals[2].Id, Candidate(champion));
        }

        private static CandidateEvaluation Candidate(float fitness) =>
            new CandidateEvaluation(0, new[] { Episode(EvaluationMode.Validation, 0, 0, fitness) }, 20);

        private static GenerationEvaluation Evaluation(int generation, EvaluationMode mode, float[] fitness)
        {
            var episodes = fitness.Select((f, c) => Episode(mode, generation, c, f)).ToArray();
            var candidates = episodes.Select((e, c) => new CandidateEvaluation(c, new[] { e }, 20)).ToArray();
            return new GenerationEvaluation(generation, mode, new ulong[] { 1 }, episodes, candidates);
        }

        private static EpisodeResult Episode(EvaluationMode mode, int generation, int candidate, float fitness) =>
            new EpisodeResult(mode, generation, candidate, 0, 1, 2400, 120f, fitness * 120f, 2400, true, 50f);
    }
}
