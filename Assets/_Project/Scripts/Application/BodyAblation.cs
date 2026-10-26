using System;
using System.Collections.Generic;
using System.Diagnostics;
using Eden.Domain;
using Eden.Evolution;

namespace Eden.Application
{
    /// <summary>
    /// A body configuration of the ablation: which body genes are expressed. <c>fixed</c> is the control, <c>all</c>
    /// expresses every gene, <c>no-X</c> switches gene X off (ablation) and <c>only-X</c> switches only X on.
    /// </summary>
    public sealed class BodyVariant
    {
        public static readonly BodyVariant Fixed = new BodyVariant("fixed", BodyGeneSet.None);
        public static readonly BodyVariant AllGenes = new BodyVariant("all", BodyGeneSet.All);

        /// <summary>The proposed ablation: control, every gene, and every gene but one.</summary>
        public static readonly IReadOnlyList<BodyVariant> Ablation = new[]
        {
            Fixed, AllGenes, Without(BodyGene.Size), Without(BodyGene.Speed), Without(BodyGene.Sensor),
            Without(BodyGene.Efficiency)
        };

        /// <summary><see cref="Ablation"/> plus each gene alone.</summary>
        public static readonly IReadOnlyList<BodyVariant> Extended = new[]
        {
            Fixed, AllGenes, Without(BodyGene.Size), Without(BodyGene.Speed), Without(BodyGene.Sensor),
            Without(BodyGene.Efficiency), Only(BodyGene.Size), Only(BodyGene.Speed), Only(BodyGene.Sensor),
            Only(BodyGene.Efficiency)
        };

        private BodyVariant(string name, BodyGeneSet genes)
        {
            Name = name;
            Genes = genes;
        }

        public string Name { get; }
        public BodyGeneSet Genes { get; }

        public static BodyVariant Without(BodyGene gene) =>
            new BodyVariant("no-" + GeneName(gene), BodyGeneSet.All & ~SetOf(gene));

        public static BodyVariant Only(BodyGene gene) => new BodyVariant("only-" + GeneName(gene), SetOf(gene));

        /// <summary>A variant by its name (fixed, all, no-size, only-sensor, …).</summary>
        public static BodyVariant Parse(string name)
        {
            foreach (var variant in Extended)
            {
                if (variant.Name == name)
                    return variant;
            }

            throw new ArgumentException($"Unknown body variant \"{name}\" (fixed, all, no-<gene>, only-<gene>; " +
                                        "genes: size, speed, sensor, efficiency).", nameof(name));
        }

        public static string GeneName(BodyGene gene) => gene.ToString().ToLowerInvariant();

        private static BodyGeneSet SetOf(BodyGene gene) => (BodyGeneSet)(1 << (int)gene);
    }

    /// <summary>
    /// The same battery (experiment seeds, validation seeds, generations, population and episode world) run once per
    /// <see cref="BodyVariant"/>, so the variants differ only in which body genes are expressed and every variant
    /// evaluates exactly the same number of episodes. Besides the battery results, the body genes of every training
    /// generation are summarized (<see cref="PhenotypeSummary"/>).
    /// </summary>
    public sealed class BodyAblation
    {
        private readonly EvolutionSettings template;

        public BodyAblation(EvolutionSettings template, BatterySettings battery, IReadOnlyList<BodyVariant> variants)
        {
            this.template = template ?? throw new ArgumentNullException(nameof(template));
            Battery = battery ?? throw new ArgumentNullException(nameof(battery));
            if (variants == null || variants.Count == 0)
                throw new ArgumentException("At least one variant is required.", nameof(variants));
            var names = new HashSet<string>();
            foreach (var variant in variants)
            {
                if (variant == null || !names.Add(variant.Name))
                    throw new ArgumentException("Variants must be distinct.", nameof(variants));
            }

            battery.Validate();
            Variants = variants;
            foreach (var variant in variants)
                new EvaluationPlan(Settings(variant));
        }

        public IReadOnlyList<BodyVariant> Variants { get; }
        public BatterySettings Battery { get; }

        /// <summary>The template with the variant's expressed genes; everything else is shared by all variants.</summary>
        public EvolutionSettings Settings(BodyVariant variant)
        {
            if (variant == null)
                throw new ArgumentNullException(nameof(variant));
            var settings = template.WithExperimentSeed(template.ExperimentSeed);
            settings.ExpressedBodyGenes = variant.Genes;
            return settings;
        }

        /// <summary>
        /// Runs every variant in order. <paramref name="onReport"/> receives each training and validation report with its
        /// variant and run index, for exports and progress logs.
        /// </summary>
        public BodyAblationResult Run(Action<BodyVariant, int, GenerationReport> onReport = null)
        {
            var outcomes = new List<BodyVariantOutcome>(Variants.Count);
            foreach (var variant in Variants)
            {
                var training = 0L;
                var validation = 0L;
                var phenotypes = new List<RunPhenotype>();
                var watch = Stopwatch.StartNew();
                var result = new EvolutionBattery(Settings(variant), Battery).Run((run, report) =>
                {
                    if (report.Evaluation.Mode == EvaluationMode.Training)
                    {
                        training += report.Evaluation.Episodes.Count;
                        phenotypes.Add(new RunPhenotype(run,
                            new PhenotypeSummary(report.Evaluation.Generation, report.Individuals)));
                    }
                    else
                    {
                        validation += report.Evaluation.Episodes.Count;
                    }

                    onReport?.Invoke(variant, run, report);
                });
                outcomes.Add(new BodyVariantOutcome(variant, result, training, validation, phenotypes,
                    watch.Elapsed.TotalSeconds));
            }

            return new BodyAblationResult(Battery, outcomes);
        }
    }

    /// <summary>Body genes of one training generation of one run.</summary>
    public readonly struct RunPhenotype
    {
        public RunPhenotype(int run, PhenotypeSummary summary)
        {
            Run = run;
            Summary = summary ?? throw new ArgumentNullException(nameof(summary));
        }

        public int Run { get; }
        public PhenotypeSummary Summary { get; }
    }

    /// <summary>
    /// One variant's battery, its body genes per training generation and what it cost: episodes evaluated
    /// (deterministic) and wall-clock seconds (informative). Final values are those of each run's last training
    /// generation, spread across runs.
    /// </summary>
    public sealed class BodyVariantOutcome
    {
        private readonly Spread[] finalGeneMeans;

        public BodyVariantOutcome(BodyVariant variant, BatteryResult result, long trainingEpisodes, long validationEpisodes,
            IReadOnlyList<RunPhenotype> phenotypes, double seconds)
        {
            Variant = variant ?? throw new ArgumentNullException(nameof(variant));
            Result = result ?? throw new ArgumentNullException(nameof(result));
            Phenotypes = phenotypes ?? throw new ArgumentNullException(nameof(phenotypes));
            TrainingEpisodes = trainingEpisodes;
            ValidationEpisodes = validationEpisodes;
            Seconds = seconds;

            var finals = new PhenotypeSummary[result.Runs.Count];
            foreach (var phenotype in phenotypes)
            {
                var last = finals[phenotype.Run];
                if (last == null || phenotype.Summary.Generation > last.Generation)
                    finals[phenotype.Run] = phenotype.Summary;
            }

            var diversity = new float[finals.Length];
            var geneMeans = new float[BodyGenes.Count][];
            for (var g = 0; g < BodyGenes.Count; g++)
                geneMeans[g] = new float[finals.Length];
            for (var r = 0; r < finals.Length; r++)
            {
                if (finals[r] == null)
                    throw new ArgumentException($"Run {r} has no training generation.", nameof(phenotypes));
                diversity[r] = finals[r].Diversity(variant.Genes);
                for (var g = 0; g < BodyGenes.Count; g++)
                    geneMeans[g][r] = finals[r].Mean((BodyGene)g);
            }

            FinalDiversity = new Spread(diversity);
            finalGeneMeans = Array.ConvertAll(geneMeans, values => new Spread(values));
        }

        public BodyVariant Variant { get; }
        public BatteryResult Result { get; }
        public IReadOnlyList<RunPhenotype> Phenotypes { get; }

        /// <summary>Training episodes over all runs: runs × generations × population × training seeds.</summary>
        public long TrainingEpisodes { get; }

        /// <summary>Validation episodes over all runs, reference brains excluded.</summary>
        public long ValidationEpisodes { get; }

        public double Seconds { get; }

        /// <summary>Body diversity of each run's last training generation, across runs.</summary>
        public Spread FinalDiversity { get; }

        /// <summary>Population mean of a gene in each run's last training generation, across runs.</summary>
        public Spread FinalGeneMean(BodyGene gene) => finalGeneMeans[(int)gene];
    }

    /// <summary>
    /// Variants side by side on the same validation seeds and budget. Differences are compared with the spread across
    /// runs: two variants are <see cref="IsSeparated">separated</see> only when their mean ± standard deviation
    /// intervals of the final validation median do not overlap (with at least two runs); anything else is within the
    /// variation between runs and does not show an effect.
    /// </summary>
    public sealed class BodyAblationResult
    {
        public BodyAblationResult(BatterySettings battery, IReadOnlyList<BodyVariantOutcome> outcomes)
        {
            Battery = battery ?? throw new ArgumentNullException(nameof(battery));
            if (outcomes == null || outcomes.Count == 0)
                throw new ArgumentException("At least one variant outcome is required.", nameof(outcomes));
            Outcomes = outcomes;
            foreach (var outcome in outcomes)
            {
                if (outcome.Variant.Genes == BodyGeneSet.None)
                    Control = outcome;
                else if (outcome.Variant.Genes == BodyGeneSet.All)
                    AllGenes = outcome;
            }
        }

        public BatterySettings Battery { get; }
        public IReadOnlyList<BodyVariantOutcome> Outcomes { get; }

        /// <summary>The fixed-body variant, if it was run.</summary>
        public BodyVariantOutcome Control { get; }

        /// <summary>The variant with every gene expressed, if it was run.</summary>
        public BodyVariantOutcome AllGenes { get; }

        public bool IsPreliminary => Outcomes[0].Result.IsPreliminary;

        /// <summary>Mean final validation median of <paramref name="outcome"/> minus that of <paramref name="baseline"/>.</summary>
        public static float Difference(BodyVariantOutcome outcome, BodyVariantOutcome baseline) =>
            outcome.Result.FinalValidationMedian.Mean - baseline.Result.FinalValidationMedian.Mean;

        public bool IsSeparated(BodyVariantOutcome a, BodyVariantOutcome b)
        {
            if (a == null || b == null || a == b || Battery.Runs < 2)
                return false;
            var x = a.Result.FinalValidationMedian;
            var y = b.Result.FinalValidationMedian;
            return x.Mean + x.StdDev < y.Mean - y.StdDev || y.Mean + y.StdDev < x.Mean - x.StdDev;
        }
    }
}
