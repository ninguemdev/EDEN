using System;
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
    /// <summary>Evolving eyes: limits, identity of slots, inheritance in both modes, files and metrics.</summary>
    public class EvolvingEyesTests
    {
        private static readonly DateTime CreatedAt = new DateTime(2026, 10, 27, 12, 0, 0, DateTimeKind.Utc);
        private static readonly EyeMutation Always = new EyeMutation(1f, 0.5f, 0.4f, 0.3f);

        [Test]
        public void MutateEyes_ChangesTraitsWithinLimits_AndKeepsSlots()
        {
            var eyes = new EyeSet(null, new Eye(3.1f, Eye.MaxAperture - 0.01f, Eye.MaxRange), null, Eye.Frontal);
            var mutated = Reproduction.MutateEyes(eyes, Always, new GaussianSampler(new Pcg32(1, 1)));

            Assert.That(mutated.Has(0) || mutated.Has(2), Is.False, "empty slots stay empty");
            Assert.That(mutated.Has(1) && mutated.Has(3), Is.True, "eyes keep their slots");
            Assert.That(mutated[3].Equals(Eye.Frontal), Is.False, "every trait may change");
            for (var trial = 0; trial < 200; trial++)
            {
                var again = Reproduction.MutateEyes(eyes, new EyeMutation(1f, 3f, 3f, 3f),
                    new GaussianSampler(new Pcg32((ulong)trial, 2)));
                var eye = again[1];
                Assert.That(eye.Orientation, Is.InRange(-MathF.PI, MathF.PI), "orientation wraps");
                Assert.That(eye.Aperture, Is.InRange(Eye.MinAperture, Eye.MaxAperture));
                Assert.That(eye.Range, Is.InRange(Eye.MinRange, Eye.MaxRange));
            }

            Assert.That(Reproduction.MutateEyes(eyes, default, new GaussianSampler(new Pcg32(1, 1))), Is.SameAs(eyes),
                "fixed eyes draw nothing");
            Assert.That(Reproduction.MutateEyes(eyes, Always, new GaussianSampler(new Pcg32(4, 1))),
                Is.EqualTo(Reproduction.MutateEyes(eyes, Always, new GaussianSampler(new Pcg32(4, 1)))), "reproducible");
        }

        [Test]
        public void EvolvingEyes_DriftFromTheFounders_FixedEyesDoNot()
        {
            var evolving = Settings(61, true);
            var evolution = new GenerationalEvolution(evolving);
            for (var g = 0; g < 3; g++)
                evolution.Advance();
            Assert.That(evolution.Population.Any(genome => !genome.Eyes.Equals(EyeSet.Founder)), Is.True);
            Assert.That(evolution.Population.All(genome => genome.Eyes.Has(0) && genome.Eyes.Count == 1), Is.True,
                "one eye, still in slot 0");

            var fixedEyes = new GenerationalEvolution(Settings(61, false));
            GenerationReport report = null;
            for (var g = 0; g < 3; g++)
                report = fixedEyes.Advance();
            Assert.That(fixedEyes.Population.All(genome => genome.Eyes.Equals(EyeSet.Founder)), Is.True);
            Assert.That(report.Neat.MeanEyes, Is.EqualTo(1f));
            Assert.That(report.Neat.MeanEyeApertureDegrees, Is.EqualTo(120f).Within(1e-3f));
            Assert.That(report.Neat.MeanEyeRange, Is.EqualTo(1f));
        }

        [Test]
        public void SharedWorldChildren_GetTheEyesOfTheirOwnGenome()
        {
            var config = new SimulationConfig(new SimulationSettings
            {
                Seed = 3, InitialPopulation = 1, InitialFood = 0, Reproduction = true, MaturitySeconds = 1f
            });
            var founder = new Genome(NeatNetwork.CreateMinimal(NeuralContract.Eyes, new Pcg32(5, 1), 1f), BodyGenes.Neutral,
                EyeSet.Founder);
            var genomes = new SharedGenomes(config, _ => founder, BodyGeneSet.None, false, Always);
            var world = new WorldState(config);
            var parent = world.AddCreature(new Float2(25f, 25f), 0f, 90f);
            parent.Eyes = founder.Eyes;
            parent.AgeTicks = config.MaturityTicks;
            var simulation = new WorldSimulation(world) { Heredity = genomes };

            simulation.Step(new ConstantController(CreatureAction.Idle));

            Assert.That(simulation.LastBirths.Count, Is.EqualTo(1));
            var child = world.Creatures.Single(c => c.Parent.HasValue);
            Assert.That(child.Eyes, Is.EqualTo(genomes.GenomeOf(child.Id).Eyes), "the child's creature has its genome's eyes");
            Assert.That(child.Eyes.Equals(parent.Eyes), Is.False, "they mutated");
        }

        [Test]
        public void Settings_FilesAndHeader_RecordEvolvingEyes()
        {
            var noEyes = Settings(62, true);
            noEyes.Senses = Senses.Legacy;
            Assert.That(() => new EvaluationPlan(noEyes), Throws.ArgumentException.With.Message.Contains("eye senses"));

            var settings = Settings(63, true);
            var header = new ExperimentHeader(settings, 1, "0.1.0", CreatedAt);
            Assert.That(header.Describe(), Has.Some.StartsWith("eyes=evolving;probability 0.5;"));
            Assert.That(new ExperimentHeader(Settings(63, false), 1, "0.1.0", CreatedAt).Describe(), Does.Contain("eyes=fixed"));
            Assert.That(header.ConfigHash, Is.Not.EqualTo(new ExperimentHeader(Settings(63, false), 1, "0.1.0", CreatedAt).ConfigHash));

            var session = new EvolutionSession(settings, 2);
            AdvanceUntil(session, () => session.Evolution.Generation >= 1 && session.Phase == EvolutionPhase.Training);
            var text = CheckpointCodec.Encode(new Checkpoint("evolution", "minimal", 63, "neural", session.Capture(), null),
                "0.1.0", "test", CreatedAt);
            Assert.That(text, Does.Contain("\"evolvingEyes\": true").And.Contain("\"eyeMutationProbability\""));
            var restored = EvolutionSession.Restore(CheckpointCodec.Decode(text, out _).Evolution);
            Assert.That(restored.Plan.EvolvingEyes, Is.True);
            AssertSameSession(restored, session);
        }

        private static EvolutionSettings Settings(ulong seed, bool evolvingEyes)
        {
            var settings = Small(seed);
            settings.Algorithm = EvolutionAlgorithm.Neat;
            settings.Senses = Senses.Eyes;
            settings.EvolvingEyes = evolvingEyes;
            settings.Neat.EyeMutationProbability = 0.5f;
            return settings;
        }
    }
}
