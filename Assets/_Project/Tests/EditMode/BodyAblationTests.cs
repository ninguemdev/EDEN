using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Eden.Application;
using Eden.Domain;
using Eden.Evolution;
using Eden.Persistence;
using NUnit.Framework;
using static Eden.Tests.EditMode.ResumeChecks;

namespace Eden.Tests.EditMode
{
    /// <summary>Body gene ablation: same battery and budget per variant, phenotype summaries and the report.</summary>
    public class BodyAblationTests
    {
        private static readonly DateTime CreatedAt = new DateTime(2026, 10, 26, 18, 0, 0, DateTimeKind.Utc);

        private string root;

        [SetUp]
        public void CreateDirectory()
        {
            root = Path.Combine(Path.GetTempPath(), "eden-ablation-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
        }

        [TearDown]
        public void DeleteDirectory()
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }

        [Test]
        public void Variants_AreNamedAndParsed()
        {
            Assert.That(BodyVariant.Ablation.Select(v => v.Name),
                Is.EqualTo(new[] { "fixed", "all", "no-size", "no-speed", "no-sensor", "no-efficiency" }));
            Assert.That(BodyVariant.Extended.Count, Is.EqualTo(10));
            Assert.That(BodyVariant.Parse("no-sensor").Genes,
                Is.EqualTo(BodyGeneSet.Size | BodyGeneSet.Speed | BodyGeneSet.Efficiency));
            Assert.That(BodyVariant.Parse("only-speed").Genes, Is.EqualTo(BodyGeneSet.Speed));
            Assert.That(BodyVariant.Parse("fixed").Genes, Is.EqualTo(BodyGeneSet.None));
            Assert.Throws<ArgumentException>(() => BodyVariant.Parse("no-wings"));
        }

        [Test]
        public void PhenotypeSummary_GivesEachGenesMeanAndSpread()
        {
            var lineage = new Lineage();
            var genomes = InitialGenomes.Population(1, 2)
                .Select((g, i) => g.WithBody(new BodyGenes(i == 0 ? -1f : 1f, 0.5f, 0f, i == 0 ? 0.2f : 0.6f)))
                .ToArray();
            var summary = new PhenotypeSummary(4, lineage.Founders(genomes));

            Assert.That(summary.Generation, Is.EqualTo(4));
            Assert.That(summary.PopulationSize, Is.EqualTo(2));
            Assert.That(summary.Mean(BodyGene.Size), Is.EqualTo(0f));
            Assert.That(summary.StdDev(BodyGene.Size), Is.EqualTo(1f), "population standard deviation");
            Assert.That(summary.StdDev(BodyGene.Speed), Is.EqualTo(0f));
            Assert.That(summary.Mean(BodyGene.Efficiency), Is.EqualTo(0.4f).Within(1e-6f));
            Assert.That(summary.StdDev(BodyGene.Efficiency), Is.EqualTo(0.2f).Within(1e-6f));
            Assert.That(summary.Diversity(BodyGeneSet.Size | BodyGeneSet.Speed), Is.EqualTo(0.5f));
            Assert.That(summary.Diversity(BodyGeneSet.All), Is.EqualTo((1f + 0f + 0f + 0.2f) / 4f).Within(1e-6f));
            Assert.That(summary.Diversity(BodyGeneSet.None), Is.Zero);
        }

        [Test]
        public void Variants_RunTheSameBattery_WithTheSameBudget_AndTheReportComparesThem()
        {
            var template = Small(3);
            template.EpisodeSeconds = 5f;
            var battery = new BatterySettings { Runs = 2, Generations = 3, ValidationInterval = 1 };
            var variants = new[] { BodyVariant.Fixed, BodyVariant.AllGenes, BodyVariant.Without(BodyGene.Sensor) };
            Assert.That(() => new BodyAblation(template, battery, new[] { variants[0], BodyVariant.Parse("fixed") }),
                Throws.ArgumentException, "distinct variants");

            var ablation = new BodyAblation(template, battery, variants);
            var result = ablation.Run();

            Assert.That(result.Control.Variant.Name, Is.EqualTo("fixed"));
            Assert.That(result.AllGenes.Variant.Name, Is.EqualTo("all"));
            foreach (var outcome in result.Outcomes)
            {
                Assert.That(outcome.TrainingEpisodes, Is.EqualTo(2L * 3 * 6 * 2), "same budget for every variant");
                Assert.That(outcome.ValidationEpisodes, Is.EqualTo(2L * 3 * 6 * 3));
                Assert.That(outcome.Phenotypes.Count, Is.EqualTo(2 * 3), "every training generation of every run");
            }

            var control = result.Control;
            Assert.That(control.Phenotypes.All(p => p.Summary.Diversity(BodyGeneSet.All) == 0f), Is.True,
                "the fixed body never varies");
            Assert.That(control.FinalDiversity.Mean, Is.Zero);
            Assert.That(result.AllGenes.FinalDiversity.Mean, Is.GreaterThan(0f));
            var noSensor = result.Outcomes[2];
            Assert.That(noSensor.Phenotypes.All(p => p.Summary.StdDev(BodyGene.Sensor) == 0f &&
                                                     p.Summary.Mean(BodyGene.Sensor) == 0f), Is.True, "sensor switched off");

            var plain = new EvolutionBattery(template, battery).Run();
            for (var r = 0; r < battery.Runs; r++)
                Assert.That(Bits(control.Result.Runs[r].Final.Validation.Median),
                    Is.EqualTo(Bits(plain.Runs[r].Final.Validation.Median)), "the control is the plain battery");

            var fixedSettings = BodyAblationReport.Header(ablation, variants[0], "0.1.0", CreatedAt).SettingsJson().ToCompactString();
            var allSettings = BodyAblationReport.Header(ablation, variants[1], "0.1.0", CreatedAt).SettingsJson().ToCompactString();
            Assert.That(allSettings, Is.EqualTo(fixedSettings.Substring(0, fixedSettings.Length - 1) +
                                                ",\"expressedBodyGenes\":[\"size\",\"speed\",\"sensor\",\"efficiency\"]}"),
                "variants differ only in the expressed genes");

            var culture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("pt-BR");
                BodyAblationReport.Write(Path.Combine(root, "ablation"), ablation, result, "0.1.0", CreatedAt);
            }
            finally
            {
                CultureInfo.CurrentCulture = culture;
            }

            var directory = Path.Combine(root, "ablation");
            var rows = File.ReadAllLines(Path.Combine(directory, "variants.csv"));
            Assert.That(rows, Has.Length.EqualTo(4));
            Assert.That(rows[0], Is.EqualTo(BodyAblationReport.VariantsHeader));
            Assert.That(rows.Select(r => r.Split(',').Length), Is.All.EqualTo(rows[0].Split(',').Length), "invariant numbers");
            Assert.That(rows[1], Does.StartWith("fixed,,2,3,72,108,"));
            Assert.That(rows[3], Does.StartWith("no-sensor,size;speed;efficiency,2,3,72,108,"));
            var phenotypes = File.ReadAllLines(Path.Combine(directory, "phenotypes.csv"));
            Assert.That(phenotypes, Has.Length.EqualTo(1 + 3 * 2 * 3));
            Assert.That(phenotypes[0], Is.EqualTo(BodyAblationReport.PhenotypesHeader));

            var json = JsonText.Parse(File.ReadAllText(Path.Combine(directory, "ablation.json")));
            Assert.That(json["format"].String(), Is.EqualTo(BodyAblationReport.Format));
            Assert.That(json["preliminary"].Boolean(), Is.True);
            Assert.That(json["variants"].Count, Is.EqualTo(3));
            Assert.That(json["variants"][2]["expressedBodyGenes"].Count, Is.EqualTo(3));

            var text = File.ReadAllText(Path.Combine(directory, "ablation.txt"));
            StringAssert.Contains("PRELIMINAR", text);
            StringAssert.Contains("mesmo orçamento", text);
            StringAssert.Contains("diferença para o corpo fixo", text);
            StringAssert.Contains("diferença para todos os genes", text);
            StringAssert.Contains("Conclusão: ", text);
            Assert.That(File.Exists(Path.Combine(directory, "no-sensor", "report.txt")), Is.True);
        }

        [Test]
        public void Separation_NeedsIntervalsApartAndMoreThanOneRun()
        {
            var battery = new BatterySettings { Runs = 2, Generations = 1, ValidationInterval = 1 };
            var low = Outcome(BodyVariant.Fixed, battery, 1f, 1.2f);
            var high = Outcome(BodyVariant.AllGenes, battery, 2f, 2.2f);
            var close = Outcome(BodyVariant.Without(BodyGene.Size), battery, 1.1f, 2.1f);
            var result = new BodyAblationResult(battery, new[] { low, high, close });

            Assert.That(result.IsSeparated(high, low), Is.True);
            Assert.That(result.IsSeparated(close, low), Is.False, "overlapping intervals");
            Assert.That(result.IsSeparated(low, low), Is.False);
            Assert.That(BodyAblationResult.Difference(high, low), Is.EqualTo(1f).Within(1e-6f));
            StringAssert.Contains("separadas do corpo fixo: all (+1)", string.Join("\n", BodyAblationReport.Describe(result)));

            var single = new BatterySettings { Runs = 1, Generations = 1, ValidationInterval = 1 };
            var alone = new BodyAblationResult(single, new[] { Outcome(BodyVariant.Fixed, single, 1f), Outcome(BodyVariant.AllGenes, single, 5f) });
            Assert.That(alone.IsSeparated(alone.Outcomes[1], alone.Outcomes[0]), Is.False, "one run has no spread");
        }

        // An outcome whose runs end with the given validation medians (other values are placeholders).
        private static BodyVariantOutcome Outcome(BodyVariant variant, BatterySettings battery, params float[] medians)
        {
            var plan = new EvaluationPlan(Small(1));
            var individuals = new Lineage().Founders(InitialGenomes.Population(1, 2, variant.Genes));
            var runs = medians.Select((median, r) => new ExperimentRun(r, (ulong)r + 1, new[]
            {
                new ValidationPoint(Summary(median, EvaluationMode.Training), Summary(median, EvaluationMode.Validation), 1,
                    median, 1f)
            }, 0)).ToArray();
            var baseline = new IsolatedEvaluator(plan).Evaluate(new Func<Eden.Brains.IBrain>[] { () => new Eden.Brains.RandomBrain() },
                new[] { 1UL }, 0, EvaluationMode.Validation).Candidates[0];
            var phenotypes = runs.Select(r => new RunPhenotype(r.Index, new PhenotypeSummary(0, individuals))).ToArray();
            return new BodyVariantOutcome(variant, new BatteryResult(battery, plan, runs, baseline, baseline), 1, 1,
                phenotypes, 0);
        }

        private static GenerationSummary Summary(float median, EvaluationMode mode) =>
            GenerationSummary.Restore(0, mode, 2, 2, median, median, median, median, median, median, 1,
                1f, 0f, 0f, 0f, 0f, 2, 0, 0, 0f);
    }
}
