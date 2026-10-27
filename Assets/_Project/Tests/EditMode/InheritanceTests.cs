using System;
using System.Linq;
using System.Text.RegularExpressions;
using Eden.Application;
using Eden.Brains;
using Eden.Domain;
using Eden.Evolution;
using Eden.Persistence;
using Eden.Simulation;
using NUnit.Framework;
using static Eden.Tests.EditMode.ResumeChecks;

namespace Eden.Tests.EditMode
{
    /// <summary>
    /// What is inherited (M7/T71): base weights and plasticity coefficients, mutated; never what a parent learned during
    /// its life.
    /// </summary>
    public class InheritanceTests
    {
        [Test]
        public void Coefficients_MutateOnlyWithPlasticity_WithinTheirLimits()
        {
            var parent = InitialGenomes.Population(6, 1, BodyGeneSet.All, true)[0];
            var copied = Reproduction.Mutate(parent, 1f, 0.5f, BodyGeneSet.All, false, new GaussianSampler(new Pcg32(2, 3)), out _);
            var mutated = Reproduction.Mutate(parent, 1f, 0.5f, BodyGeneSet.All, true, new GaussianSampler(new Pcg32(2, 3)), out _);

            Assert.That(copied.Plasticity, Is.EqualTo(parent.Plasticity), "without plasticity nothing is drawn for them");
            Assert.That(mutated.CopyWeights(), Is.EqualTo(copied.CopyWeights()), "coefficients are drawn last");
            Assert.That(mutated.Body, Is.EqualTo(copied.Body));
            Assert.That(mutated.Plasticity, Is.Not.EqualTo(parent.Plasticity));

            var random = new GaussianSampler(new Pcg32(8, 1));
            var current = parent;
            for (var i = 0; i < 500; i++)
            {
                current = Reproduction.Mutate(current, 1f, 1f, BodyGeneSet.None, true, random, out _);
                Assert.That(current.Plasticity.LearningRate, Is.InRange(0f, PlasticityGenes.MaxLearningRate));
                Assert.That(current.Plasticity.Decay, Is.InRange(0f, PlasticityGenes.MaxDecay));
            }

            var rules = new ReproductionRules(new EvolutionSettings { PopulationSize = 6, EliteCount = 1, Plasticity = true });
            Assert.That(rules.MutablePlasticity, Is.True);
            Assert.That(new ReproductionRules(new EvolutionSettings()).MutablePlasticity, Is.False);
        }

        [Test]
        public void Breeding_UsesGenomesAndFitnessOnly_NeverWhatParentsLearned()
        {
            var settings = Small(14);
            settings.Plasticity = true;
            settings.MutationProbability = 0.3f;
            var evolution = new GenerationalEvolution(settings);
            var parents = evolution.Population.ToArray();
            var report = evolution.Advance();
            var fitness = report.Evaluation.Candidates.Select(c => c.MeanFitness).ToArray();

            // Breeding again from the genomes and scores alone gives exactly the generation the evolution made, although
            // every parent learned during its episodes: nothing learned can have reached a child.
            var expected = Reproduction.Breed(evolution.Rules, parents, fitness, 1);
            for (var i = 0; i < expected.Length; i++)
                Assert.That(evolution.Population[i].HasSameContent(expected[i].Genome), Is.True, $"child {i}");
            Assert.That(evolution.Population.Where((g, i) => !evolution.Origins[i].IsElite)
                .Any(g => !g.Plasticity.Equals(parents[0].Plasticity)), Is.True, "coefficients are inherited with variation");
            foreach (var origin in evolution.Origins.Where(o => o.IsElite))
                Assert.That(evolution.Population.Contains(parents[origin.ParentIndex]), Is.True, "elites keep the very genome");
        }

        [Test]
        public void SharedWorldChild_GetsItsParentsMutatedGenome_AndStartsWithoutTheParentsLearning()
        {
            var world = new WorldSimulation(WorldFactory.CreateInitial(new SimulationConfig(new SimulationSettings
            {
                Seed = 3, InitialPopulation = 6, InitialFood = 60, Reproduction = true, MaturitySeconds = 2f,
                ReproductionEnergyFraction = 0.3f, BirthMutationProbability = 0.5f
            })));
            var config = world.World.Config;
            var genomes = new SharedGenomes(config, id => InitialGenomes.RandomFor(config, id)
                .WithPlasticity(new PlasticityGenes(1f, 0.05f)), BodyGeneSet.None, true);
            world.Heredity = genomes;
            var brains = new BrainController(config, id => new MlpBrain(genomes.GenomeOf(id)));
            for (var t = 0; t < 400 && world.LastBirths.Count == 0; t++)
            {
                world.Step(brains);
                brains.Forget(world.LastDeaths);
            }

            var birth = world.LastBirths.First();
            Assert.That(brains.TryGetState(birth.Parent, out var parent) && parent.Plastic.MaxAbsDelta > 0f, Is.True,
                "the parent learned");
            var expected = Reproduction.Mutate(genomes.GenomeOf(birth.Parent), config.BirthMutationProbability,
                config.BirthMutationSigma, BodyGeneSet.None, true,
                new GaussianSampler(SeedDerivation.CreateCreatureStream(config.Seed, RandomStream.Birth, birth.Child)), out _);
            Assert.That(genomes.GenomeOf(birth.Child).HasSameContent(expected), Is.True,
                "the child's genome comes from the parent's genome alone");

            world.Step(brains);
            Assert.That(brains.TryGetState(birth.Child, out var child), Is.True);
            Assert.That(child.Plastic.MaxAbsDelta, Is.Zero, "the child learns from zero");
        }

        [Test]
        public void Checkpoints_KeepWhatALifeLearnedWithItsBrain_NeverWithAGenome()
        {
            var settings = Small(21);
            settings.Plasticity = true;
            var session = new EvolutionSession(settings, 2);
            AdvanceUntil(session, () => session.Phase == EvolutionPhase.Training && session.Training.Count >= 1 &&
                                        session.Job.Current.Tick > 50);
            var text = CheckpointCodec.Encode(new Checkpoint("evolution", "minimal", 21, "neural", session.Capture(), null),
                "0.1.0", "test", new DateTime(2026, 10, 27, 0, 0, 0, DateTimeKind.Utc));
            var root = JsonText.Parse(text);
            var individuals = root["state"]["evolution"]["individuals"];
            for (var i = 0; i < individuals.Count; i++)
                Assert.That(individuals[i].ToCompactString(), Does.Not.Contain("deltaWeights"));
            Assert.That(Regex.Matches(text, "\"deltaWeights\"").Count,
                Is.EqualTo(session.Job.Current.Controller.Capture().Count(s => s.Plastic != null)),
                "only the brain of the life in progress");
        }
    }
}
