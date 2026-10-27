using System;
using System.Linq;
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
    /// <summary>Lifetime plasticity (M7): bounded genes, the Hebbian rule, resets per life and episode, no explosion, resume.</summary>
    public class PlasticityTests
    {
        private static readonly Observation Sample = new Observation(0.4f, 0.1f, 0.3f, -0.2f, 0.5f, 0.8f, 0.6f, 1f,
            0.9f, 0.7f, 1f, 0.4f);

        [Test]
        public void Genes_AreBounded_AndAFixedNetworkIsTheDefault()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new PlasticityGenes(-0.1f, 0f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PlasticityGenes(float.NaN, 0f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PlasticityGenes(1.01f, 0f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PlasticityGenes(0.5f, 0.21f));
            Assert.That(new PlasticityGenes(0f, 0.1f).IsPlastic, Is.False, "no learning rate, no learning");

            var genome = InitialGenomes.Population(3, 1)[0];
            Assert.That(genome.Plasticity, Is.EqualTo(PlasticityGenes.Fixed));
            Assert.That(Genome.CurrentSchemaVersion, Is.EqualTo(3));
            var plastic = genome.WithPlasticity(new PlasticityGenes(0.5f, 0.1f));
            Assert.That(plastic.HasSameContent(genome), Is.False);
            Assert.That(plastic.CopyWeights(), Is.EqualTo(genome.CopyWeights()));
            var child = Reproduction.Mutate(plastic, 1f, 0.5f, BodyGeneSet.All, new GaussianSampler(new Pcg32(1, 2)), out _);
            Assert.That(child.Plasticity, Is.EqualTo(plastic.Plasticity), "coefficients are copied");

            var fixedPopulation = InitialGenomes.Population(9, 6, BodyGeneSet.All);
            var plasticPopulation = InitialGenomes.Population(9, 6, BodyGeneSet.All, true);
            for (var i = 0; i < 6; i++)
            {
                Assert.That(plasticPopulation[i].CopyWeights(), Is.EqualTo(fixedPopulation[i].CopyWeights()));
                Assert.That(plasticPopulation[i].Body, Is.EqualTo(fixedPopulation[i].Body), "drawn after the body genes");
                Assert.That(plasticPopulation[i].Plasticity.IsPlastic, Is.True);
                Assert.That(plasticPopulation[i].Plasticity.Decay, Is.InRange(0f, PlasticityGenes.MaxDecay));
            }
        }

        [Test]
        public void FixedNetwork_KeepsNoMemory_AndActsAsBefore()
        {
            var brain = new MlpBrain(InitialGenomes.Population(4, 1)[0]);
            var state = new BrainState(new Pcg32(1, 1));
            var withState = brain.Evaluate(Sample, state);
            var without = brain.Evaluate(Sample, null);
            Assert.That(withState, Is.EqualTo(without));
            Assert.That(state.Plastic, Is.Null);
        }

        [Test]
        public void HebbianRule_PairsTheLastDecisionWithTheEnergyChange_DecaysAndClamps()
        {
            var genome = InitialGenomes.Population(5, 1)[0].WithPlasticity(new PlasticityGenes(0.5f, 0.1f));
            var brain = new MlpBrain(genome);
            var state = new BrainState(new Pcg32(1, 1));

            brain.Evaluate(Sample, state);
            var memory = state.Plastic;
            Assert.That(memory.MaxAbsDelta, Is.Zero, "the first decision has nothing to learn from");
            var hidden = (float[])memory.LastHidden.Clone();
            var output = (float[])memory.LastOutput.Clone();

            var richer = WithEnergy(Sample, 0.6f);
            var plasticAction = brain.Evaluate(richer, state);
            for (var o = 0; o < NeuralLayout.Outputs; o++)
            {
                for (var h = 0; h < NeuralLayout.Hidden; h++)
                    Assert.That(memory.DeltaWeights[o * NeuralLayout.Hidden + h],
                        Is.EqualTo(0.5f * (0.6f - 0.4f) * output[o] * hidden[h]).Within(1e-6f));
            }

            Assert.That(plasticAction, Is.Not.EqualTo(new MlpBrain(InitialGenomes.Population(5, 1)[0]).Evaluate(richer, null)),
                "W_base + ΔW is what acts");
            var before = (float[])memory.DeltaWeights.Clone();
            brain.Evaluate(richer, state);
            for (var i = 0; i < PlasticMemory.WeightCount; i++)
                Assert.That(memory.DeltaWeights[i], Is.EqualTo(before[i] * 0.9f).Within(1e-6f), "no energy change: decay only");

            var greedy = new MlpBrain(genome.WithPlasticity(new PlasticityGenes(1f, 0f)));
            var full = new BrainState(new Pcg32(1, 1));
            var saturated = full.EnsurePlastic();
            for (var i = 0; i < PlasticMemory.WeightCount; i++)
                saturated.DeltaWeights[i] = 0.99f;
            Array.Fill(saturated.LastHidden, 1f);
            Array.Fill(saturated.LastOutput, 1f);
            saturated.LastEnergy = 0f;
            saturated.HasPrevious = true;
            greedy.Evaluate(WithEnergy(Sample, 1f), full);
            Assert.That(saturated.DeltaWeights, Is.All.EqualTo(PlasticMemory.MaxDelta), "changes stay within ±1");
        }

        [Test]
        public void LongPlasticRun_StaysFinite_AndBounded()
        {
            var config = new SimulationConfig(new SimulationSettings { Seed = 31, InitialPopulation = 32, InitialFood = 96 });
            var world = new WorldSimulation(WorldFactory.CreateInitial(config));
            var brains = new BrainController(config, id => new MlpBrain(InitialGenomes.RandomFor(config, id)
                .WithPlasticity(new PlasticityGenes(1f, id.Value % 2 == 0 ? 0f : 0.01f))));
            var learned = 0f;
            for (var t = 0; t < 4000 && !world.IsExtinct; t++)
            {
                world.Step(brains);
                foreach (var creature in world.World.Creatures)
                {
                    Assert.That(float.IsNaN(creature.Position.X) || float.IsNaN(creature.Velocity.Y), Is.False);
                    if (!brains.TryGetState(creature.Id, out var state) || state.Plastic == null)
                        continue;
                    Assert.That(state.Plastic.IsValid(), Is.True, $"tick {t}, {creature.Id}");
                    learned = Math.Max(learned, state.Plastic.MaxAbsDelta);
                }

                brains.Forget(world.LastDeaths);
            }

            Assert.That(world.CorrectedInputValues, Is.Zero);
            Assert.That(learned, Is.GreaterThan(0.01f), "the networks did change during their lives");
        }

        [Test]
        public void Memory_StartsEmpty_InEveryEpisodeAndAtBirth()
        {
            var settings = Small(17);
            settings.Plasticity = true;
            var plan = new EvaluationPlan(settings);
            var genome = InitialGenomes.Population(17, 1, BodyGeneSet.None, true)[0];
            var twice = new IsolatedEvaluator(plan).EvaluateTraining(new[] { genome, genome }, 0);
            var first = twice.Candidates[0].Episodes;
            var second = twice.Candidates[1].Episodes;
            for (var s = 0; s < first.Count; s++)
            {
                Assert.That(Bits(second[s].FoodEnergy), Is.EqualTo(Bits(first[s].FoodEnergy)), "nothing carried over");
                Assert.That(Bits(second[s].FinalEnergy), Is.EqualTo(Bits(first[s].FinalEnergy)));
            }

            var job = new IsolatedEvaluator(plan).TrainingJob(new[] { genome }, 0);
            job.Advance(plan.EpisodeTicks / 2);
            Assert.That(job.Current.Controller.TryGetState(new CreatureId(1), out var learning), Is.True);
            Assert.That(learning.Plastic.MaxAbsDelta, Is.GreaterThan(0f), "it learns within the episode");
            while (job.Current.SeedIndex == 0)
                job.Advance(1);
            Assert.That(job.Current.Controller.TryGetState(new CreatureId(1), out var fresh) && fresh.Plastic != null &&
                        fresh.Plastic.MaxAbsDelta > 0f, Is.False, "a new episode starts without what the last one learned");

            var world = new WorldSimulation(WorldFactory.CreateInitial(new SimulationConfig(new SimulationSettings
            {
                Seed = 3, InitialPopulation = 6, InitialFood = 60, Reproduction = true, MaturitySeconds = 2f,
                ReproductionEnergyFraction = 0.3f
            })));
            var config = world.World.Config;
            var genomes = new SharedGenomes(config, id => InitialGenomes.RandomFor(config, id)
                .WithPlasticity(new PlasticityGenes(1f, 0f)), BodyGeneSet.None);
            world.Heredity = genomes;
            var brains = new BrainController(config, id => new MlpBrain(genomes.GenomeOf(id)));
            for (var t = 0; t < 400 && world.LastBirths.Count == 0; t++)
            {
                world.Step(brains);
                brains.Forget(world.LastDeaths);
            }

            var birth = world.LastBirths.First();
            Assert.That(genomes.GenomeOf(birth.Child).Plasticity, Is.EqualTo(genomes.GenomeOf(birth.Parent).Plasticity));
            Assert.That(brains.TryGetState(birth.Parent, out var parent) && parent.Plastic.MaxAbsDelta > 0f, Is.True);
            world.Step(brains);
            Assert.That(brains.TryGetState(birth.Child, out var child), Is.True);
            Assert.That(child.Plastic.MaxAbsDelta, Is.Zero, "a child starts with nothing learned");
            Assert.That(child.Plastic.HasPrevious, Is.True);
        }

        [Test]
        public void ALife_SavedAndLoaded_ContinuesWithWhatItLearned()
        {
            var settings = new SimulationSettings { Seed = 41, InitialPopulation = 12, InitialFood = 60 };
            var config = new SimulationConfig(settings);
            BrainController Brains() => new BrainController(config, id => new MlpBrain(InitialGenomes.RandomFor(config, id)
                .WithPlasticity(new PlasticityGenes(0.8f, 0.02f))));
            var world = new WorldSimulation(WorldFactory.CreateInitial(config));
            var brains = Brains();
            Run(world, brains, 300);

            var checkpoint = new Checkpoint("shared", "minimal", 41, "neural", null,
                new SharedWorldSnapshot(settings, world.Capture(), brains.Capture()));
            var text = CheckpointCodec.Encode(checkpoint, "0.1.0", "test", new DateTime(2026, 10, 27, 0, 0, 0, DateTimeKind.Utc));
            Assert.That(text, Does.Contain("\"plastic\": {").And.Contain("\"deltaWeights\": ["));
            var saved = CheckpointCodec.Decode(text, out _).SharedWorld;
            var restored = WorldSimulation.Restore(config, saved.World);
            var restoredBrains = Brains();
            restoredBrains.Restore(saved.Brains);

            Run(world, brains, 300);
            Run(restored, restoredBrains, 300);
            Assert.That(restored.ComputeFingerprint(), Is.EqualTo(world.ComputeFingerprint()), "learning is part of the life");

            var forgetful = Brains();
            var emptied = saved.Brains.Select(s => new BrainSlotState(s.Creature, s.BrainName, s.Random, s.Action,
                s.TicksUntilDecision)).ToArray();
            forgetful.Restore(emptied);
            var withoutMemory = WorldSimulation.Restore(config, saved.World);
            Run(withoutMemory, forgetful, 300);
            Assert.That(withoutMemory.ComputeFingerprint(), Is.Not.EqualTo(world.ComputeFingerprint()),
                "dropping what was learned changes the life");

            var fixedBrains = new BrainController(config, id => new MlpBrain(InitialGenomes.RandomFor(config, id)));
            Assert.That(() => fixedBrains.Restore(saved.Brains), Throws.ArgumentException, "a fixed network cannot hold it");
        }

        [Test]
        public void ExperimentOption_IsSavedAndDescribed()
        {
            var settings = Small(2);
            var createdAt = new DateTime(2026, 10, 27, 0, 0, 0, DateTimeKind.Utc);
            Assert.That(new ExperimentHeader(settings, 1, "0.1.0", createdAt).Describe(), Does.Contain("plasticity=off"));
            var fixedHash = new ExperimentHeader(settings, 1, "0.1.0", createdAt).ConfigHash;
            settings.Plasticity = true;
            var header = new ExperimentHeader(settings, 1, "0.1.0", createdAt);
            Assert.That(header.Describe(), Does.Contain("plasticity=hebbian_output_layer"));
            Assert.That(header.ConfigHash, Is.Not.EqualTo(fixedHash));

            var session = new EvolutionSession(settings, 2);
            AdvanceUntil(session, () => session.Phase == EvolutionPhase.Training && session.Job.Current.Tick > 30);
            var text = CheckpointCodec.Encode(new Checkpoint("evolution", "minimal", 2, "neural", session.Capture(), null),
                "0.1.0", "test", createdAt);
            Assert.That(text, Does.Contain("\"plasticity\": true").And.Contain("\"plasticity\": ["));
            var restored = EvolutionSession.Restore(CheckpointCodec.Decode(text, out _).Evolution);
            session.Advance(500);
            restored.Advance(500);
            AssertSameSession(restored, session);
        }

        private static Observation WithEnergy(Observation o, float energy) =>
            new Observation(energy, o.Age, o.VelocityForward, o.VelocityLeft, o.FoodSin, o.FoodCos, o.FoodProximity,
                o.FoodVisible, o.WallFront, o.WallRight, o.WallBack, o.WallLeft);
    }
}
