using System;
using System.IO;
using System.Linq;
using Eden.Application;
using Eden.Domain;
using Eden.Evolution;
using Eden.Persistence;
using NUnit.Framework;

namespace Eden.Tests.EditMode
{
    /// <summary>NEAT in the generational laboratory: population, species, two-parent lineage, exports and refusals.</summary>
    public class NeatEvolutionTests
    {
        private static readonly DateTime CreatedAt = new DateTime(2026, 10, 27, 12, 0, 0, DateTimeKind.Utc);

        [Test]
        public void NeatGenerations_KeepTheirSize_FormSpecies_AndRecordBothParents()
        {
            var evolution = new GenerationalEvolution(Small(31));
            Assert.That(evolution.Population.All(g => g.Network == NeatNetwork.Kind), Is.True);
            Assert.That(evolution.Population.All(g => g.Neat.Connections.Count == 39), Is.True, "minimal networks");

            GenerationReport report = null;
            for (var g = 0; g < 4; g++)
            {
                report = evolution.Advance();
                Assert.That(evolution.Population.Count, Is.EqualTo(12));
                Assert.That(report.Neat, Is.Not.Null);
                Assert.That(report.Neat.Species.Count, Is.EqualTo(12));
                Assert.That(report.Neat.SpeciesCount, Is.GreaterThanOrEqualTo(1));
                Assert.That(float.IsNaN(report.Summary.WeightStdDev), Is.True, "weight diversity does not apply to NEAT");
            }

            Assert.That(evolution.Generation, Is.EqualTo(4));
            Assert.That(evolution.Individuals.Any(i => i.ParentIds.Count == 2), Is.True, "crossover children name both");
            foreach (var individual in evolution.Individuals.Where(i => i.ParentIds.Count == 2))
                Assert.That(individual.ParentIds[0], Is.Not.EqualTo(individual.ParentIds[1]));
            Assert.That(evolution.Innovations.Generation, Is.EqualTo(4));
            Assert.That(evolution.Speciation.Species, Is.Not.Empty);
        }

        [Test]
        public void NeatEvolution_IsReproducible()
        {
            var a = new GenerationalEvolution(Small(32));
            var b = new GenerationalEvolution(Small(32));
            for (var g = 0; g < 3; g++)
            {
                var ra = a.Advance();
                var rb = b.Advance();
                Assert.That(rb.Neat.Species, Is.EqualTo(ra.Neat.Species));
                Assert.That(rb.Summary.Median, Is.EqualTo(ra.Summary.Median));
            }

            for (var i = 0; i < a.Population.Count; i++)
            {
                Assert.That(b.Population[i].HasSameContent(a.Population[i]), Is.True, $"genome {i}");
                Assert.That(b.Individuals[i].ParentIds, Is.EqualTo(a.Individuals[i].ParentIds));
            }

            Assert.That(b.Innovations.NextInnovation, Is.EqualTo(a.Innovations.NextInnovation));
        }

        [Test]
        public void ExpressedBodyGenes_StartRandomAndMutateWithNeat()
        {
            var settings = Small(33);
            settings.ExpressedBodyGenes = BodyGeneSet.Size;
            settings.MutationProbability = 1f;
            var evolution = new GenerationalEvolution(settings);
            Assert.That(evolution.Population.Select(g => g.Body[BodyGene.Size]).Distinct().Count(), Is.GreaterThan(1));
            Assert.That(evolution.Population.All(g => g.Body[BodyGene.Speed] == 0f), Is.True, "not expressed: neutral");

            evolution.Advance();
            var bred = evolution.Population.Where((g, i) => evolution.Origins[i].IsElite == false).ToArray();
            Assert.That(bred, Is.Not.Empty);
            Assert.That(bred.All(g => g.Body[BodyGene.Speed] == 0f), Is.True);
        }

        [Test]
        public void Neat_RefusesPlasticityPoolsRestoreAndSaving()
        {
            var plastic = Small(34);
            plastic.Plasticity = true;
            Assert.That(() => new GenerationalEvolution(plastic), Throws.ArgumentException.With.Message.Contains("plasticity"));

            var pool = new[] { InitialGenomes.Population(1, 1)[0] };
            Assert.That(() => GenerationalEvolution.FromSeedPool(Small(35), pool, new[] { 1f }),
                Throws.ArgumentException.With.Message.Contains("NEAT"));

            var session = new EvolutionSession(Small(36), 1);
            var checkpoint = new Checkpoint("evolution", "minimal", 36, "neural", session.Capture(), null);
            Assert.That(() => CheckpointCodec.Encode(checkpoint, "0.1.0", "test", CreatedAt),
                Throws.TypeOf<CheckpointException>().With.Message.Contains("NEAT"));
        }

        [Test]
        public void Header_NamesTheAlgorithmAndHashesTheNeatSettings()
        {
            var neat = new ExperimentHeader(Small(37), 1, "0.1.0", CreatedAt);
            var mlpSettings = Small(37);
            mlpSettings.Algorithm = EvolutionAlgorithm.Mlp;
            var mlp = new ExperimentHeader(mlpSettings, 1, "0.1.0", CreatedAt);

            Assert.That(neat.Describe(), Does.Contain("evolution_algorithm=neat-v1").And.Contain("network=neat")
                .And.Not.Contain("elites=4").And.Some.StartsWith("neat=initial_weight_limit 1;"));
            Assert.That(mlp.Describe(), Does.Contain("evolution_algorithm=fixed-topology-ga-v1").And.Contain("crossover=off"));
            Assert.That(neat.ConfigHash, Is.Not.EqualTo(mlp.ConfigHash));

            var otherThreshold = Small(37);
            otherThreshold.Neat.CompatibilityThreshold = 2f;
            Assert.That(new ExperimentHeader(otherThreshold, 1, "0.1.0", CreatedAt).ConfigHash, Is.Not.EqualTo(neat.ConfigHash));
        }

        [Test]
        public void CsvLog_WritesTheNeatFilesOnlyForNeat()
        {
            var root = Path.Combine(Path.GetTempPath(), "eden-neat-" + Guid.NewGuid().ToString("N"));
            try
            {
                var neatDirectory = Path.Combine(root, "neat");
                var evolution = new GenerationalEvolution(Small(38));
                using (var log = new EvolutionCsvLog(neatDirectory, new ExperimentHeader(Small(38), 0, "0.1.0", CreatedAt)))
                {
                    log.Append(evolution.Advance());
                    log.Append(evolution.Advance());
                }

                var generations = File.ReadAllLines(Path.Combine(neatDirectory, "neat-generations.csv"));
                Assert.That(generations, Has.Length.EqualTo(3));
                Assert.That(generations[0], Is.EqualTo(EvolutionCsvLog.NeatGenerationsHeader));
                Assert.That(generations[1], Does.StartWith("0,"));
                Assert.That(File.ReadAllLines(Path.Combine(neatDirectory, "neat-individuals.csv")), Has.Length.EqualTo(25));
                var individuals = File.ReadAllLines(Path.Combine(neatDirectory, "individuals.csv"));
                Assert.That(individuals.Skip(13).Any(line => line.Split(',')[4].Contains(";")), Is.True,
                    "generation 1 lists both parents of crossover children");
                Assert.That(File.ReadAllText(Path.Combine(neatDirectory, "experiment.json")), Does.Contain("neat-individuals.csv"));

                var mlpDirectory = Path.Combine(root, "mlp");
                var mlpSettings = Small(38);
                mlpSettings.Algorithm = EvolutionAlgorithm.Mlp;
                using (new EvolutionCsvLog(mlpDirectory, new ExperimentHeader(mlpSettings, 0, "0.1.0", CreatedAt)))
                {
                }

                Assert.That(File.Exists(Path.Combine(mlpDirectory, "neat-generations.csv")), Is.False);
                Assert.That(File.ReadAllText(Path.Combine(mlpDirectory, "experiment.json")), Does.Not.Contain("neat-"));
            }
            finally
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, true);
            }
        }

        private static EvolutionSettings Small(ulong seed) =>
            new EvolutionSettings
            {
                ExperimentSeed = seed, Algorithm = EvolutionAlgorithm.Neat, PopulationSize = 12, EpisodeSeconds = 5f,
                TrainingSeedsPerGeneration = 1, ValidationSeedCount = 1
            };
    }
}
