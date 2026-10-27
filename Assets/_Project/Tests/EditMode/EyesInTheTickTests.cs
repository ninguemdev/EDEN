using System;
using System.Linq;
using Eden.Application;
using Eden.Brains;
using Eden.Domain;
using Eden.Evolution;
using Eden.Persistence;
using Eden.Simulation;
using NUnit.Framework;
using UnityEngine.TestTools.Constraints;
using static Eden.Tests.EditMode.ResumeChecks;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace Eden.Tests.EditMode
{
    /// <summary>Eyes in the tick: what reaches a brain, NEAT brains of the eye senses, experiments, files and resume.</summary>
    public class EyesInTheTickTests
    {
        private static readonly DateTime SavedAt = new DateTime(2026, 10, 27, 12, 0, 0, DateTimeKind.Utc);
        private static readonly int FoodVisible = NeuralContract.EyeInputIndex(0, EyeChannel.FoodVisible);

        [Test]
        public void TheTick_GivesEyeCreaturesOnlyWhatTheirEyesSee()
        {
            var world = TestWorlds.Empty();
            var eyed = world.AddCreature(new Float2(25f, 25f), 0f, 50f);
            eyed.Eyes = EyeSet.Founder;
            var legacy = world.AddCreature(new Float2(10f, 10f), 0f, 50f);
            world.AddFood(new Float2(20f, 25f), 10f); // behind the eye, inside the legacy circle
            var recorder = new ObservationRecorder();
            var simulation = new WorldSimulation(world);

            simulation.Step(recorder);
            var seen = recorder.Inputs[eyed.Id.Value];
            Assert.That(seen, Is.Not.Null);
            Assert.That(seen.Length, Is.EqualTo(NeuralContract.Eyes.InputCount));
            Assert.That(seen[NeuralContract.EyeInputIndex(0, EyeChannel.Present)], Is.EqualTo(1f));
            Assert.That(seen[FoodVisible], Is.Zero, "food behind is invisible to the eye");
            Assert.That(recorder.Legacy[eyed.Id.Value].FoodVisible, Is.EqualTo(1f), "while the old circle would see it");
            Assert.That(seen[NeuralContract.EyeInputIndex(1, EyeChannel.Present)], Is.Zero, "slot 1 is empty");
            Assert.That(recorder.Inputs[legacy.Id.Value], Is.Null, "legacy senses carry no eye inputs");
            Assert.That(simulation.LastCorrectedInputValues, Is.Zero);

            eyed.Heading = MathF.PI;
            simulation.Step(recorder);
            Assert.That(recorder.Inputs[eyed.Id.Value][FoodVisible], Is.EqualTo(1f), "turned around, the eye sees it");
        }

        [Test]
        public void NeatBrains_OfTheEyeSenses_ReadTheEyeChannels()
        {
            // Eye 0 seeing food turns the creature left.
            var contract = NeuralContract.Eyes;
            var network = new NeatNetwork(contract, new[]
            {
                new NeatConnection(500, NeatNetwork.InputNodeId(FoodVisible), NeatNetwork.OutputNodeId(contract, 1), 1f, true)
            });
            var brain = new NeatBrain(new Genome(network, BodyGenes.Neutral, EyeSet.Founder));
            var inputs = new float[contract.InputCount];
            inputs[NeuralContract.EyeInputIndex(0, EyeChannel.Present)] = 1f;
            var blind = new Observation(1f, 0f, 0f, 0f, 0f, 0f, 0f, 1f, 1f, 1f, 1f, 1f, inputs);
            Assert.That(brain.Evaluate(blind, null).Turn, Is.EqualTo(0f), "legacy food reading is ignored");

            inputs[FoodVisible] = 1f;
            inputs[FoodVisible + 1] = 0f;
            inputs[FoodVisible + 2] = 1f;
            inputs[FoodVisible + 3] = 0.5f;
            var seeing = new Observation(1f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 1f, 1f, 1f, 1f, inputs);
            Assert.That(brain.Evaluate(seeing, null).Turn, Is.EqualTo(MathF.Tanh(1f)).Within(1e-6f));
            Assert.That(() => brain.Evaluate(new Observation(1f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 1f, 1f, 1f, 1f), null),
                Throws.InvalidOperationException, "an eye brain needs a creature with eyes");

            brain.Evaluate(seeing, null);
            Assert.That(() =>
            {
                for (var i = 0; i < 1000; i++)
                    brain.Evaluate(seeing, null);
            }, Is.Not.AllocatingGCMemory());
        }

        [Test]
        public void EyeInputs_FollowThePolicy()
        {
            var inputs = new float[NeuralContract.Eyes.InputCount];
            inputs[0] = 2f; // energy above 1
            inputs[NeuralContract.EyeInputIndex(0, EyeChannel.Present)] = 1f;
            inputs[NeuralContract.EyeInputIndex(0, EyeChannel.FoodSin)] = float.NaN; // food not visible: masked anyway
            inputs[NeuralContract.EyeInputIndex(0, EyeChannel.BarrierVisible)] = 0.7f; // a flag must be 0 or 1
            inputs[NeuralContract.EyeInputIndex(0, EyeChannel.BarrierProximity)] = 1.5f;
            inputs[NeuralContract.EyeInputIndex(2, EyeChannel.FoodCos)] = 0.3f; // absent eye: masked
            var observation = new Observation(0.5f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 1f, 1f, 1f, 1f, inputs);
            Assert.That(ObservationLimits.IsValid(observation), Is.False);

            var fixedUp = ObservationLimits.Sanitize(observation, out var corrections);
            Assert.That(corrections, Is.GreaterThanOrEqualTo(5));
            Assert.That(ObservationLimits.IsValid(fixedUp), Is.True);
            Assert.That(inputs[0], Is.EqualTo(1f));
            Assert.That(inputs[NeuralContract.EyeInputIndex(0, EyeChannel.FoodSin)], Is.Zero);
            Assert.That(inputs[NeuralContract.EyeInputIndex(0, EyeChannel.BarrierVisible)], Is.EqualTo(1f));
            Assert.That(inputs[NeuralContract.EyeInputIndex(0, EyeChannel.BarrierProximity)], Is.EqualTo(1f));
            Assert.That(inputs[NeuralContract.EyeInputIndex(2, EyeChannel.FoodCos)], Is.Zero);
        }

        [Test]
        public void EyeExperiments_BreedNeatGenomesWithEyes_AndEpisodesUseThem()
        {
            var settings = Eyes(51);
            var evolution = new GenerationalEvolution(settings);
            Assert.That(evolution.Population.All(g => g.Contract == NeuralContract.Eyes && g.Eyes.Equals(EyeSet.Founder)),
                Is.True);
            Assert.That(evolution.Population[0].Neat.Connections.Count, Is.EqualTo(41 * 3), "bias and 40 inputs to 3 outputs");

            var job = evolution.BeginTraining();
            Assert.That(job.Current.Creature.Eyes, Is.EqualTo(EyeSet.Founder), "the episode creature has the genome's eyes");
            evolution.Advance(job.RunToEnd());
            evolution.Advance();
            Assert.That(evolution.Population.All(g => g.Eyes != null), Is.True);
            Assert.That(evolution.Innovations.Contract, Is.SameAs(NeuralContract.Eyes));

            var mlp = Eyes(52);
            mlp.Algorithm = EvolutionAlgorithm.Mlp;
            Assert.That(() => new GenerationalEvolution(mlp), Throws.ArgumentException.With.Message.Contains("NEAT"));
            Assert.That(new ExperimentHeader(settings, 1, "0.1.0", SavedAt).Describe(),
                Does.Contain("neural_contract=eyes-v1").And.Some.StartsWith("senses=eyes"));
        }

        [Test]
        public void EyeSession_SavedToAFileMidEpisode_ResumesExactly()
        {
            var original = new EvolutionSession(Eyes(53), 2);
            AdvanceUntil(original, () => original.Evolution.Generation >= 1 && original.Phase == EvolutionPhase.Training);
            original.Advance(41);
            var text = CheckpointCodec.Encode(new Checkpoint("evolution", "minimal", 53, "neural", original.Capture(), null),
                "0.1.0", "test", SavedAt);
            Assert.That(text, Does.Contain("\"senses\": \"eyes\"").And.Contain("\"contract\": \"eyes-v1\"")
                .And.Contain("\"eyes\": [").And.Contain("[0,0,"));
            var restored = EvolutionSession.Restore(CheckpointCodec.Decode(text, out _).Evolution);
            Assert.That(restored.Job.Current.Creature.Eyes, Is.EqualTo(EyeSet.Founder));
            AssertSameSession(restored, original);

            AdvanceUntil(original, () => original.Training.Count >= 4, 97);
            AdvanceUntil(restored, () => restored.Training.Count >= 4, 31);
            CatchUp(restored, original);
            AssertSameSession(restored, original);
        }

        [Test]
        public void GenomeFiles_KeepTheEyes_AndRefuseEyesWhereTheyDoNotBelong()
        {
            var contract = NeuralContract.Eyes;
            var eyes = new EyeSet(null, new Eye(-0.5f, Eye.MinAperture, 1.25f));
            var genome = new Genome(NeatNetwork.CreateMinimal(contract, new Pcg32(3, 1), 1f), BodyGenes.Neutral, eyes);
            var text = GenomeCodec.Encode(new GenomeRecord(genome), "0.1.0", SavedAt);
            Assert.That(text, Does.Contain("\"contract\": \"eyes-v1\"").And.Contain("\"eyes\": [").And.Contain("[1,-0.5,"));
            var decoded = GenomeCodec.Decode(text).Genome;
            Assert.That(decoded.HasSameContent(genome), Is.True);
            Assert.That(decoded.Eyes, Is.EqualTo(eyes));

            Assert.That(() => GenomeCodec.Decode(text.Replace("[1,-0.5,", "[7,-0.5,")),
                Throws.TypeOf<CheckpointException>());
            var legacy = GenomeCodec.Encode(new GenomeRecord(new Genome(
                NeatNetwork.CreateMinimal(NeuralContract.Legacy, new Pcg32(4, 1), 1f))), "0.1.0", SavedAt);
            Assert.That(() => GenomeCodec.Decode(legacy.Replace("\"connections\"", "\"eyes\": [],\n  \"connections\"")),
                Throws.TypeOf<CheckpointException>().With.Message.Contains("only a genome of eyes-v1"));
            Assert.That(() => new Genome(NeatNetwork.CreateMinimal(contract, new Pcg32(5, 1), 1f), BodyGenes.Neutral),
                Throws.ArgumentException, "an eye network needs its eyes");
        }

        [Test]
        public void Fingerprints_IncludeEyes_OnlyWhenThereAreEyes()
        {
            ulong Fingerprint(EyeSet eyes)
            {
                var world = TestWorlds.Empty();
                world.AddCreature(new Float2(25f, 25f), 0f, 50f).Eyes = eyes;
                return WorldFingerprint.Compute(world);
            }

            Assert.That(Fingerprint(EyeSet.Founder), Is.Not.EqualTo(Fingerprint(null)));
            Assert.That(Fingerprint(EyeSet.Founder), Is.Not.EqualTo(Fingerprint(new EyeSet(null, Eye.Frontal))));
            Assert.That(Fingerprint(EyeSet.Founder), Is.EqualTo(Fingerprint(new EyeSet(Eye.Frontal))));
        }

        private static EvolutionSettings Eyes(ulong seed)
        {
            var settings = Small(seed);
            settings.Algorithm = EvolutionAlgorithm.Neat;
            settings.Senses = Senses.Eyes;
            return settings;
        }

        private sealed class ObservationRecorder : ICreatureController
        {
            public readonly float[][] Inputs = new float[8][];
            public readonly Observation[] Legacy = new Observation[8];

            public CreatureAction Decide(CreatureId creature, in Observation observation)
            {
                Inputs[creature.Value] = observation.EyeInputs == null ? null : (float[])observation.EyeInputs.Clone();
                Legacy[creature.Value] = observation;
                return CreatureAction.Idle;
            }
        }
    }
}
