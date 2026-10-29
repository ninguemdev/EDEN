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
    /// <summary>
    /// Sensing water (P2.6b): the eyes-water-v1 contract, eyes that see water (cone, range, not through obstacles, hiding
    /// nothing), hydration as an input, experiments and files that use the new contract only with water and eyes, and
    /// refusals to mix contracts. The older contracts keep their channels.
    /// </summary>
    public class WaterSensesTests
    {
        private const float Tolerance = 1e-4f;
        private static readonly DateTime SavedAt = new DateTime(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);

        // Default rules with solid bodies (radius 0.5): facing +x from (10, 25), the frontal eye sits at (10.5, 25) and
        // sees 60° to each side, 10 units far.
        private static readonly Float2 Creature = new Float2(10f, 25f);
        private static readonly Obstacle PondAhead = new Obstacle(14f, 23f, 16f, 27f);

        [Test]
        public void OlderContracts_KeepTheirChannels_AndTheNewOneAddsWaterWithNewIds()
        {
            Assert.That(NeuralContract.Legacy.InputCount, Is.EqualTo(12));
            var eyes = NeuralContract.Eyes;
            Assert.That(eyes.InputCount, Is.EqualTo(4 + 4 * 9));
            Assert.That(eyes.HasEyes && !eyes.SensesWater && eyes.ChannelsPerEye == 9, Is.True);
            for (var i = 0; i < eyes.InputCount; i++)
            {
                var expected = i < 4 ? i : 100 + (i - 4) / 9 * 16 + (i - 4) % 9;
                Assert.That(eyes.Inputs[i].Id, Is.EqualTo(expected), $"eyes-v1 input {i}");
            }

            var water = NeuralContract.EyesWater;
            Assert.That(water.Id, Is.EqualTo("eyes-water-v1"));
            Assert.That(water.InputCount, Is.EqualTo(5 + 4 * 13));
            Assert.That(water.OutputCount, Is.EqualTo(3));
            Assert.That(water.HasEyes && water.SensesWater, Is.True);
            Assert.That(NeuralContract.Legacy.HasEyes, Is.False);
            for (var i = 0; i < 4; i++)
                Assert.That(water.Inputs[i].Id, Is.EqualTo(i), "body channels keep their ids");
            Assert.That(water.Inputs[NeuralContract.HydrationInput].Id, Is.EqualTo(12));
            Assert.That(water.Inputs[NeuralContract.HydrationInput].Name, Is.EqualTo("Hydration"));
            Assert.That(NeuralContract.Legacy.InputIndexOf(12) + eyes.InputIndexOf(12), Is.EqualTo(-2), "id 12 is new");

            Assert.That(water.EyeInput(1, EyeChannel.FoodSin), Is.EqualTo(5 + 13 + 2));
            Assert.That(water.Inputs[water.EyeInput(1, EyeChannel.FoodSin)].Id,
                Is.EqualTo(NeuralContract.EyeChannelId(1, EyeChannel.FoodSin)), "an eye channel keeps its id");
            Assert.That(water.Inputs[water.EyeInput(3, EyeChannel.WaterProximity)].Id, Is.EqualTo(100 + 3 * 16 + 12));
            Assert.That(water.Inputs[water.EyeInput(0, EyeChannel.WaterVisible)].Name, Is.EqualTo("Eye0WaterVisible"));
            Assert.That(eyes.InputIndexOf(NeuralContract.EyeChannelId(0, EyeChannel.WaterVisible)), Is.EqualTo(-1));
            Assert.That(() => eyes.EyeInput(0, EyeChannel.WaterVisible), Throws.TypeOf<ArgumentOutOfRangeException>());

            Assert.That(NeuralContract.TryFind("eyes-water-v1", out var found) && found == water, Is.True);
            Assert.That(NeuralContract.ForEyes(true), Is.SameAs(water));
            Assert.That(NeuralContract.ForEyes(false), Is.SameAs(eyes));
            Assert.That(NeuralContract.OfEyeInputs(57), Is.SameAs(water));
            Assert.That(NeuralContract.OfEyeInputs(40), Is.SameAs(eyes));
            Assert.That(NeuralContract.OfEyeInputs(12), Is.Null);
        }

        [Test]
        public void Water_IsSeenOnlyInsideTheConeAndRange()
        {
            var ahead = See(PondAhead);
            Assert.That(ahead[(int)EyeChannel.WaterVisible], Is.EqualTo(1f));
            Assert.That(ahead[(int)EyeChannel.WaterCos], Is.EqualTo(1f).Within(Tolerance));
            Assert.That(ahead[(int)EyeChannel.WaterSin], Is.EqualTo(0f).Within(Tolerance));
            Assert.That(ahead[(int)EyeChannel.WaterProximity], Is.EqualTo(1f - 3.5f / 10f).Within(Tolerance),
                "the nearest point of the pond, 3.5 away");

            Assert.That(See(PondAhead, MathF.PI)[(int)EyeChannel.WaterVisible], Is.Zero, "behind");
            Assert.That(See(new Obstacle(21f, 23f, 23f, 27f))[(int)EyeChannel.WaterVisible], Is.Zero,
                "10.5 away is out of range");
            Assert.That(See(new Obstacle(9f, 29f, 12f, 31f))[(int)EyeChannel.WaterVisible], Is.Zero,
                "to the side, outside the cone");

            // Wider to the right, the pond reaches the cone's left edge (60°) at y = 29, 4 / sin 60° away.
            var edge = See(new Obstacle(9f, 29f, 14f, 31f));
            var distance = 4f / MathF.Sin(MathF.PI / 3f);
            Assert.That(edge[(int)EyeChannel.WaterVisible], Is.EqualTo(1f));
            Assert.That(edge[(int)EyeChannel.WaterSin], Is.EqualTo(MathF.Sin(MathF.PI / 3f)).Within(Tolerance), "60° left");
            Assert.That(edge[(int)EyeChannel.WaterCos], Is.EqualTo(0.5f).Within(Tolerance));
            Assert.That(edge[(int)EyeChannel.WaterProximity], Is.EqualTo(1f - distance / 10f).Within(Tolerance));
        }

        [Test]
        public void Obstacles_HideWater_WhileWaterHidesNothing_AndIsNoBarrier()
        {
            var behindObstacle = See(PondAhead, 0f, new Obstacle(12f, 24f, 13f, 26f));
            Assert.That(behindObstacle[(int)EyeChannel.WaterVisible], Is.Zero, "the obstacle hides the pond");
            Assert.That(behindObstacle[(int)EyeChannel.BarrierVisible], Is.EqualTo(1f), "and is seen itself");
            Assert.That(behindObstacle.Skip((int)EyeChannel.WaterSin).Take(3), Is.All.EqualTo(0f));

            var past = See(PondAhead, 0f, null, new Float2(18f, 25f));
            Assert.That(past[(int)EyeChannel.FoodVisible], Is.EqualTo(1f), "food across the pond is seen");
            Assert.That(past[(int)EyeChannel.FoodProximity], Is.EqualTo(1f - 7.5f / 10f).Within(Tolerance));
            Assert.That(past[(int)EyeChannel.WaterVisible], Is.EqualTo(1f));
            Assert.That(past[(int)EyeChannel.BarrierVisible], Is.Zero, "water is not a barrier; walls are far");
        }

        [Test]
        public void WithoutTheWaterFlag_AnEyeWritesOnlyTheChannelsOfEyesV1()
        {
            var world = World(PondAhead);
            var creature = world.AddCreature(Creature, 0f, 50f);
            var buffer = Enumerable.Repeat(7f, EyeChannels.WithWater).ToArray();
            Vision.See(world, creature, EyeSet.Founder, 0, buffer, 0);
            Assert.That(buffer.Skip(EyeChannels.Count), Is.All.EqualTo(7f), "nothing past the nine channels");
            Assert.That(buffer[(int)EyeChannel.Present], Is.EqualTo(1f));
        }

        [Test]
        public void TheTick_GivesHydrationAndWaterOnlyToEyesInAWorldWithWater()
        {
            var world = World(PondAhead);
            var eyed = world.AddCreature(Creature, 0f, 50f);
            eyed.Eyes = EyeSet.Founder;
            eyed.Hydration = 40f;
            var legacy = world.AddCreature(new Float2(40f, 40f), 0f, 50f);
            var recorder = new Recorder();
            var simulation = new WorldSimulation(world);
            simulation.Step(recorder);

            var contract = NeuralContract.EyesWater;
            var seen = recorder.Inputs[eyed.Id.Value];
            Assert.That(seen.Length, Is.EqualTo(contract.InputCount));
            Assert.That(seen[NeuralContract.HydrationInput], Is.EqualTo(0.4f).Within(Tolerance), "hydration / max");
            Assert.That(seen[contract.EyeInput(0, EyeChannel.Present)], Is.EqualTo(1f));
            Assert.That(seen[contract.EyeInput(0, EyeChannel.WaterVisible)], Is.EqualTo(1f));
            Assert.That(seen[contract.EyeInput(1, EyeChannel.Present)], Is.Zero);
            Assert.That(recorder.Inputs[legacy.Id.Value], Is.Null, "the legacy senses neither see water nor feel thirst");
            Assert.That(simulation.LastCorrectedInputValues, Is.Zero);

            var dry = TestWorlds.Empty(s => s.SolidBodies = true);
            dry.AddCreature(Creature, 0f, 50f).Eyes = EyeSet.Founder;
            new WorldSimulation(dry).Step(recorder);
            Assert.That(recorder.Inputs[1].Length, Is.EqualTo(NeuralContract.Eyes.InputCount), "eyes-v1 without water");
        }

        [Test]
        public void WaterInputs_FollowThePolicy()
        {
            var contract = NeuralContract.EyesWater;
            var inputs = new float[contract.InputCount];
            inputs[NeuralContract.HydrationInput] = 1.5f;
            inputs[contract.EyeInput(0, EyeChannel.Present)] = 1f;
            inputs[contract.EyeInput(0, EyeChannel.WaterVisible)] = 0.7f;
            inputs[contract.EyeInput(0, EyeChannel.WaterSin)] = float.NaN;
            inputs[contract.EyeInput(0, EyeChannel.WaterCos)] = 1f;
            inputs[contract.EyeInput(0, EyeChannel.WaterProximity)] = 1.2f;
            inputs[contract.EyeInput(2, EyeChannel.WaterCos)] = 0.3f; // absent eye: masked
            var observation = new Observation(0.5f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 1f, 1f, 1f, 1f, inputs);
            Assert.That(ObservationLimits.IsValid(observation), Is.False);

            var fixedUp = ObservationLimits.Sanitize(observation, out var corrections);
            Assert.That(corrections, Is.EqualTo(5));
            Assert.That(ObservationLimits.IsValid(fixedUp), Is.True);
            Assert.That(inputs[NeuralContract.HydrationInput], Is.EqualTo(1f));
            Assert.That(inputs[contract.EyeInput(0, EyeChannel.WaterVisible)], Is.EqualTo(1f));
            Assert.That(inputs[contract.EyeInput(0, EyeChannel.WaterSin)], Is.Zero);
            Assert.That(inputs[contract.EyeInput(0, EyeChannel.WaterProximity)], Is.EqualTo(1f));
            Assert.That(inputs[contract.EyeInput(2, EyeChannel.WaterCos)], Is.Zero);
        }

        [Test]
        public void NeatBrains_ReadTheirOwnEyeContract_AndRefuseTheOther()
        {
            // Seeing water with eye 0 turns the creature left; thirst pushes it forward.
            var contract = NeuralContract.EyesWater;
            var waterVisible = contract.EyeInput(0, EyeChannel.WaterVisible);
            var network = new NeatNetwork(contract, new[]
            {
                new NeatConnection(500, NeatNetwork.InputNodeId(waterVisible), NeatNetwork.OutputNodeId(contract, 1), 1f, true)
            });
            var brain = new NeatBrain(new Genome(network, BodyGenes.Neutral, EyeSet.Founder));
            var inputs = new float[contract.InputCount];
            inputs[contract.EyeInput(0, EyeChannel.Present)] = 1f;
            Assert.That(brain.Evaluate(With(inputs), null).Turn, Is.EqualTo(0f));
            inputs[waterVisible] = 1f;
            inputs[waterVisible + 2] = 1f;
            Assert.That(brain.Evaluate(With(inputs), null).Turn, Is.EqualTo(MathF.Tanh(1f)).Within(1e-6f));

            var eyesV1 = new NeatBrain(new Genome(NeatNetwork.CreateMinimal(NeuralContract.Eyes, new Pcg32(2, 1), 1f),
                BodyGenes.Neutral, EyeSet.Founder));
            Assert.That(() => eyesV1.Evaluate(With(inputs), null),
                Throws.InvalidOperationException.With.Message.Contains("without water"));
            Assert.That(() => brain.Evaluate(With(new float[NeuralContract.Eyes.InputCount]), null),
                Throws.InvalidOperationException.With.Message.Contains("with water"));
        }

        [Test]
        public void EyeExperimentsWithWater_UseTheNewContract_AndOthersDoNot()
        {
            var settings = EyesWithWater(61);
            var evolution = new GenerationalEvolution(settings);
            Assert.That(evolution.Plan.Contract, Is.SameAs(NeuralContract.EyesWater));
            Assert.That(evolution.Population.All(g => g.Contract == NeuralContract.EyesWater && g.Eyes.Equals(EyeSet.Founder)),
                Is.True);
            Assert.That(evolution.Population[0].Neat.Connections.Count, Is.EqualTo(58 * 3), "bias and 57 inputs to 3 outputs");
            var job = evolution.BeginTraining();
            evolution.Advance(job.RunToEnd());
            Assert.That(evolution.Innovations.Contract, Is.SameAs(NeuralContract.EyesWater));
            var header = new ExperimentHeader(settings, 1, "0.1.0", SavedAt).Describe();
            Assert.That(header, Does.Contain("neural_contract=eyes-water-v1"));
            Assert.That(header, Has.Some.StartsWith("water_senses=eyes see water"));

            var dry = EyesWithWater(62);
            dry.World.Water = Array.Empty<WaterSource>();
            Assert.That(new EvaluationPlan(dry).Contract, Is.SameAs(NeuralContract.Eyes));
            Assert.That(new ExperimentHeader(dry, 1, "0.1.0", SavedAt).Describe(),
                Does.Contain("neural_contract=eyes-v1").And.None.StartsWith("water_senses="));

            var legacy = EyesWithWater(63);
            legacy.Senses = Senses.Legacy;
            Assert.That(new EvaluationPlan(legacy).Contract, Is.SameAs(NeuralContract.Legacy));
            Assert.That(new ExperimentHeader(legacy, 1, "0.1.0", SavedAt).Describe(),
                Has.Some.StartsWith("water_senses=none"));
        }

        [Test]
        public void EyesV1Genomes_AreRefusedInAWorldWithWater_AndFilesKeepTheNewContract()
        {
            var dry = EyesWithWater(64);
            dry.World.Water = Array.Empty<WaterSource>();
            var pool = new GenerationalEvolution(dry).Population.Take(2).ToArray();
            Assert.That(() => GenerationalEvolution.FromSeedPool(EyesWithWater(64), pool, new[] { 1f, 2f }),
                Throws.ArgumentException.With.Message.Contains("uses eyes-v1, not eyes-water-v1"));

            var eyes = new EyeSet(null, new Eye(-0.5f, Eye.MinAperture, 1.25f));
            var genome = new Genome(NeatNetwork.CreateMinimal(NeuralContract.EyesWater, new Pcg32(3, 1), 1f),
                BodyGenes.Neutral, eyes);
            var text = GenomeCodec.Encode(new GenomeRecord(genome), "0.1.0", SavedAt);
            Assert.That(text, Does.Contain("\"contract\": \"eyes-water-v1\"").And.Contain("\"eyes\": ["));
            var decoded = GenomeCodec.Decode(text).Genome;
            Assert.That(decoded.HasSameContent(genome), Is.True);
            Assert.That(decoded.Contract, Is.SameAs(NeuralContract.EyesWater));
            Assert.That(decoded.Eyes, Is.EqualTo(eyes));
            Assert.That(() => new Genome(NeatNetwork.CreateMinimal(NeuralContract.EyesWater, new Pcg32(5, 1), 1f),
                BodyGenes.Neutral), Throws.ArgumentException.With.Message.Contains("eyes-water-v1 needs its eyes"));
        }

        [Test]
        public void EyeSessionWithWater_SavedMidEpisode_ResumesExactly_AndRefusesAnotherContract()
        {
            var original = new EvolutionSession(EyesWithWater(65), 2);
            AdvanceUntil(original, () => original.Evolution.Generation >= 1 && original.Phase == EvolutionPhase.Training);
            original.Advance(41);
            var text = CheckpointCodec.Encode(new Checkpoint("evolution", "minimal", 65, "neural", original.Capture(), null),
                "0.1.0", "test", SavedAt);
            Assert.That(text, Does.Contain("\"contract\": \"eyes-water-v1\"").And.Contain("\"water\""));
            var restored = EvolutionSession.Restore(CheckpointCodec.Decode(text, out _).Evolution);
            Assert.That(restored.Evolution.Plan.Contract, Is.SameAs(NeuralContract.EyesWater));
            AssertSameSession(restored, original);
            AdvanceUntil(original, () => original.Training.Count >= 4, 97);
            AdvanceUntil(restored, () => restored.Training.Count >= 4, 31);
            CatchUp(restored, original);
            AssertSameSession(restored, original);

            // The same save in a world without water would breed eyes-v1 genomes: refused, not reinterpreted.
            var dry = CheckpointCodec.Decode(text, out _).Evolution;
            dry.Settings.World.Water = Array.Empty<WaterSource>();
            Assert.That(() => EvolutionSession.Restore(dry),
                Throws.ArgumentException.With.Message.Contains("of eyes-water-v1").And.Message.Contains("of eyes-v1"));
        }

        private static Observation With(float[] inputs) =>
            new Observation(1f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 1f, 1f, 1f, 1f, inputs);

        private static EvolutionSettings EyesWithWater(ulong seed)
        {
            var settings = Small(seed);
            settings.Algorithm = EvolutionAlgorithm.Neat;
            settings.Senses = Senses.Eyes;
            settings.World.SolidBodies = true;
            settings.World.Water = WaterLayouts.Create(WaterLayout.Pond, settings.World);
            return settings;
        }

        private static WorldState World(Obstacle pond, Obstacle? obstacle = null) =>
            TestWorlds.Empty(s =>
            {
                s.SolidBodies = true;
                s.Water = new[] { new WaterSource(pond, 200f, 2f) };
                if (obstacle.HasValue)
                    s.Obstacles = new[] { obstacle.Value };
            });

        // What the frontal eye of a creature at (10, 25) reports with water, in the channels of one eyes-water-v1 slot.
        private static float[] See(Obstacle pond, float heading = 0f, Obstacle? obstacle = null, Float2? food = null)
        {
            var world = World(pond, obstacle);
            var creature = world.AddCreature(Creature, heading, 50f);
            if (food.HasValue)
                world.AddFood(food.Value, 10f);
            var buffer = new float[EyeChannels.WithWater + 2];
            buffer[0] = 7f;
            buffer[buffer.Length - 1] = 7f;
            Vision.See(world, creature, EyeSet.Founder, 0, buffer, 1, true);
            Assert.That(buffer[0] == 7f && buffer[buffer.Length - 1] == 7f, Is.True, "only its own channels");
            return buffer.Skip(1).Take(EyeChannels.WithWater).ToArray();
        }

        private sealed class Recorder : ICreatureController
        {
            public readonly float[][] Inputs = new float[8][];

            public CreatureAction Decide(CreatureId creature, in Observation observation)
            {
                Inputs[creature.Value] = observation.EyeInputs == null ? null : (float[])observation.EyeInputs.Clone();
                return CreatureAction.Idle;
            }
        }
    }
}
