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
    /// <summary>Body genes: limits, inheritance, expression in the world and the fixed-body control.</summary>
    public class BodyGenesTests
    {
        private static readonly BodyGenes Extreme = new BodyGenes(1f, -1f, 0.75f, -0.5f);

        [Test]
        public void Genes_OutsideTheirLimits_AreRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new BodyGenes(float.NaN, 0f, 0f, 0f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new BodyGenes(0f, float.PositiveInfinity, 0f, 0f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new BodyGenes(0f, 0f, 1.01f, 0f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new BodyGenes(0f, 0f, 0f, -1.01f));
            Assert.DoesNotThrow(() => new BodyGenes(BodyGenes.MaxAbsValue, -BodyGenes.MaxAbsValue, 0f, 1f));
            Assert.That(BodyGenes.Neutral.IsNeutral, Is.True);
            Assert.That(Extreme.IsNeutral, Is.False);
            Assert.That(Extreme[BodyGene.Sensor], Is.EqualTo(0.75f));
            Assert.Throws<ArgumentOutOfRangeException>(() => Extreme.Masked((BodyGeneSet)16));
        }

        [Test]
        public void Genome_CarriesBodyGenesBesideItsWeights()
        {
            var weights = MlpBrainTests.ReferenceParameters();
            var plain = new Genome(NetworkTopology.Mlp12x16x3, weights);
            var shaped = plain.WithBody(Extreme);

            Assert.That(plain.Body, Is.EqualTo(BodyGenes.Neutral));
            Assert.That(shaped.Body, Is.EqualTo(Extreme));
            Assert.That(shaped.CopyWeights(), Is.EqualTo(plain.CopyWeights()));
            Assert.That(shaped.HasSameContent(plain), Is.False, "body genes are part of the content");
            Assert.That(shaped.HasSameContent(new Genome(NetworkTopology.Mlp12x16x3, weights, Extreme)), Is.True);
            Assert.That(Genome.CurrentSchemaVersion, Is.EqualTo(2));
        }

        [Test]
        public void Mutation_CopiesBodyGenesUnchanged()
        {
            var parent = InitialGenomes.Population(21, 1, BodyGeneSet.All)[0];
            var random = new GaussianSampler(new Pcg32(4, 9));
            var child = Reproduction.Mutate(parent, 1f, 10f, random, out var mutated);

            Assert.That(mutated, Is.EqualTo(parent.WeightCount));
            Assert.That(child.Body, Is.EqualTo(parent.Body));
            Assert.That(Enumerable.Range(0, child.WeightCount).All(w => Math.Abs(child.GetWeight(w)) <= Genome.MaxAbsWeight));

            var rules = new ReproductionRules(new EvolutionSettings { ExperimentSeed = 3, PopulationSize = 12, EliteCount = 2 });
            var parents = InitialGenomes.Population(8, 12, BodyGeneSet.All);
            var fitness = Enumerable.Range(0, 12).Select(i => (float)(i % 5)).ToArray();
            foreach (var offspring in Reproduction.Breed(rules, parents, fitness, 1))
                Assert.That(offspring.Genome.Body, Is.EqualTo(parents[offspring.ParentIndex].Body));
        }

        [Test]
        public void InitialPopulation_DrawsBodyGenesAfterTheWeights_AndOnlyExpressesTheChosenOnes()
        {
            var fixedBody = InitialGenomes.Population(17, 20);
            var all = InitialGenomes.Population(17, 20, BodyGeneSet.All);
            var sensorOnly = InitialGenomes.Population(17, 20, BodyGeneSet.Sensor);

            for (var i = 0; i < fixedBody.Length; i++)
            {
                Assert.That(fixedBody[i].Body.IsNeutral, Is.True);
                Assert.That(all[i].CopyWeights(), Is.EqualTo(fixedBody[i].CopyWeights()), "weights never depend on body genes");
                Assert.That(sensorOnly[i].CopyWeights(), Is.EqualTo(fixedBody[i].CopyWeights()));
                Assert.That(sensorOnly[i].Body, Is.EqualTo(new BodyGenes(0f, 0f, all[i].Body.Sensor, 0f)));
                for (var g = 0; g < BodyGenes.Count; g++)
                    Assert.That(BodyGenes.IsValidValue(all[i].Body[(BodyGene)g]), Is.True);
            }

            Assert.That(all.Select(g => g.Body.Size).Distinct().Count(), Is.EqualTo(all.Length), "genes vary");
        }

        [Test]
        public void Expression_KeepsTraitsWithinLimits_AndNeutralGenesGiveTheReferenceBody()
        {
            var config = SimulationConfig.CreateDefault(1);
            var reference = CreatureBody.Reference(config);
            Assert.That(CreatureBody.Express(config, BodyGenes.Neutral, BodyGeneSet.All), Is.EqualTo(reference));
            Assert.That(CreatureBody.Express(config, Extreme, BodyGeneSet.None), Is.EqualTo(reference), "fixed body");

            var largest = CreatureBody.Express(config, new BodyGenes(1f, 1f, 1f, -1f), BodyGeneSet.All);
            Assert.That(largest.EatRadius, Is.EqualTo(config.EatRadius * 1.5f));
            Assert.That(largest.MaxSpeed, Is.EqualTo(config.MaxSpeed * 1.5f));
            Assert.That(largest.SensorRange, Is.EqualTo(config.SensorRange * 1.5f));
            Assert.That(largest.MetabolismPerSecond, Is.EqualTo(config.MetabolismPerSecond * 1.5f), "least efficient");
            var smallest = CreatureBody.Express(config, new BodyGenes(-1f, -1f, -1f, 1f), BodyGeneSet.All);
            Assert.That(smallest.EatRadius, Is.EqualTo(config.EatRadius * 0.5f));
            Assert.That(smallest.MetabolismPerSecond, Is.EqualTo(config.MetabolismPerSecond * 0.5f), "most efficient");

            var sensor = CreatureBody.Express(config, Extreme, BodyGeneSet.Sensor);
            Assert.That(sensor, Is.EqualTo(new CreatureBody(config.EatRadius, config.MaxSpeed,
                config.SensorRange * 1.375f, config.MetabolismPerSecond)));

            var random = new Pcg32(7, 1);
            for (var i = 0; i < 2000; i++)
                Assert.That(CreatureBody.Express(config, BodyGenes.CreateRandom(random), BodyGeneSet.All)
                    .IsWithinLimits(config), Is.True);
            Assert.That(new CreatureBody(config.EatRadius * 1.6f, config.MaxSpeed, config.SensorRange,
                config.MetabolismPerSecond).IsWithinLimits(config), Is.False);
            Assert.That(new CreatureBody(config.EatRadius, float.NaN, config.SensorRange,
                config.MetabolismPerSecond).IsWithinLimits(config), Is.False);
        }

        [Test]
        public void World_RefusesBodiesOutsideTheLimits()
        {
            var world = TestWorlds.Empty();
            var config = world.Config;
            var tooFast = new CreatureBody(config.EatRadius, config.MaxSpeed * 2f, config.SensorRange,
                config.MetabolismPerSecond);
            Assert.Throws<ArgumentException>(() => world.AddCreature(new Float2(5f, 5f), 0f, 50f, tooFast));

            var shared = new WorldSimulation(WorldFactory.CreateInitial(SharedConfig()));
            var saved = shared.Capture();
            var creatures = saved.Creatures.ToArray();
            var first = creatures[0];
            creatures[0] = new CreatureState(first.Id, first.Position, first.Heading, first.Energy,
                new CreatureBody(0.1f, first.Body.MaxSpeed, first.Body.SensorRange, first.Body.MetabolismPerSecond));
            Assert.That(() => WorldState.Restore(shared.World.Config, saved.Tick, saved.NextCreatureId, saved.NextFoodId,
                saved.WorldRandom, saved.SpawnRandom, creatures, saved.Food),
                Throws.ArgumentException.With.Message.Contains("body"));
        }

        [Test]
        public void Simulation_UsesEachCreaturesOwnBody()
        {
            var world = TestWorlds.Empty();
            var config = world.Config;
            var reference = world.ReferenceBody;
            var large = CreatureBody.Express(config, new BodyGenes(1f, 1f, 1f, 1f), BodyGeneSet.All);
            var small = world.AddCreature(new Float2(10f, 10f), 0f, 50f);
            var big = world.AddCreature(new Float2(10f, 30f), 0f, 50f, large);

            // Food 12 units ahead: only the longer sensor sees it, and readings are relative to each creature's range.
            world.AddFood(new Float2(22f, 10f), config.FoodEnergy);
            world.AddFood(new Float2(22f, 30f), config.FoodEnergy);
            Assert.That(Sensors.Observe(world, small).FoodVisible, Is.EqualTo(0f), "12 units are beyond range 10");
            Assert.That(Sensors.Observe(world, big).FoodVisible, Is.EqualTo(1f), "but within range 15");
            Assert.That(Sensors.Observe(world, big).FoodProximity, Is.EqualTo(1f - 12f / large.SensorRange).Within(1e-6f));
            Assert.That(Sensors.Observe(world, small).WallBack, Is.EqualTo(1f), "10 units to the wall behind");
            Assert.That(Sensors.Observe(world, big).WallBack, Is.EqualTo(10f / large.SensorRange).Within(1e-6f));

            // Food 1 unit away: only the larger body reaches it; the efficient one spends half the metabolism.
            world.AddFood(new Float2(11f, 10f), config.FoodEnergy);
            world.AddFood(new Float2(11f, 30f), config.FoodEnergy);
            new WorldSimulation(world).Step(new ConstantController(CreatureAction.Idle));
            Assert.That(small.FoodEnergyEaten, Is.EqualTo(0f), "1 unit is beyond the reference eat radius 0.75");
            Assert.That(big.FoodEnergyEaten, Is.EqualTo(config.FoodEnergy), "within the larger radius 1.125");
            Assert.That(small.Energy, Is.EqualTo(50f - config.TickDuration * (reference.MetabolismPerSecond +
                config.SensorCostPerSecond)));
            Assert.That(big.Energy, Is.EqualTo(50f + config.FoodEnergy - config.TickDuration *
                (large.MetabolismPerSecond + config.SensorCostPerSecond)));

            // Each creature's speed is capped by its own body (a tall arena, so nobody reaches a wall).
            var track = TestWorlds.Empty(s => s.ArenaHeight = 1000f);
            var slow = track.AddCreature(new Float2(10f, 10f), MathF.PI / 2f, 100f);
            var fast = track.AddCreature(new Float2(30f, 10f), MathF.PI / 2f, 100f,
                CreatureBody.Express(track.Config, new BodyGenes(0f, 1f, 0f, 0f), BodyGeneSet.All));
            var simulation = new WorldSimulation(track);
            var thrust = new ConstantController(new CreatureAction(1f, 0f, 0f));
            for (var i = 0; i < 200; i++)
                simulation.Step(thrust);
            Assert.That(Speed(slow), Is.EqualTo(track.Config.MaxSpeed).Within(1e-3f));
            Assert.That(Speed(fast), Is.EqualTo(fast.Body.MaxSpeed).Within(1e-3f));
            Assert.That(Sensors.Observe(track, fast).VelocityForward, Is.EqualTo(1f).Within(1e-3f), "relative to its own cap");
        }

        [Test]
        public void FixedBody_IgnoresBodyGenes_AndExpressedGenesChangeTheEpisode()
        {
            var settings = Small(31);
            var fixedPlan = new EvaluationPlan(settings);
            settings.ExpressedBodyGenes = BodyGeneSet.All;
            var bodyPlan = new EvaluationPlan(settings);

            var neutral = InitialGenomes.Population(31, 4);
            var shaped = neutral.Select((g, i) => g.WithBody(new BodyGenes(1f, 1f, 1f, i % 2 == 0 ? 1f : -1f))).ToArray();

            var control = new IsolatedEvaluator(fixedPlan).EvaluateTraining(neutral, 0);
            var ignored = new IsolatedEvaluator(fixedPlan).EvaluateTraining(shaped, 0);
            AssertSameEpisodes(ignored, control);

            var expressed = new IsolatedEvaluator(bodyPlan).EvaluateTraining(shaped, 0);
            Assert.That(expressed.Episodes.Select(e => (e.FoodEnergy, e.SurvivedTicks, e.FinalEnergy)),
                Is.Not.EqualTo(control.Episodes.Select(e => (e.FoodEnergy, e.SurvivedTicks, e.FinalEnergy))));
            AssertSameEpisodes(new IsolatedEvaluator(bodyPlan).EvaluateTraining(neutral, 0), control);

            var episode = new IsolatedEpisode(bodyPlan, () => new MlpBrain(shaped[0]), bodyPlan.TrainingSeed(0, 0), 0, 0, 0,
                EvaluationMode.Training, shaped[0].Body);
            Assert.That(episode.Creature.Body,
                Is.EqualTo(CreatureBody.Express(bodyPlan.EpisodeConfig(0), shaped[0].Body, BodyGeneSet.All)));
        }

        [Test]
        public void Job_ResumedWithOtherBodyGenes_IsRefused()
        {
            var settings = Small(5);
            settings.ExpressedBodyGenes = BodyGeneSet.Size | BodyGeneSet.Speed;
            var plan = new EvaluationPlan(settings);
            var genomes = InitialGenomes.Population(5, 3, plan.ExpressedBodyGenes);
            var job = new IsolatedEvaluator(plan).TrainingJob(genomes, 0);
            job.Advance(plan.EpisodeTicks + 37);
            var progress = job.Capture();

            var resumed = new IsolatedEvaluator(plan).TrainingJob(genomes, 0);
            resumed.Resume(progress);
            Assert.That(resumed.RunToEnd().Episodes.Select(e => e.FoodEnergy),
                Is.EqualTo(job.RunToEnd().Episodes.Select(e => e.FoodEnergy)));

            var otherBodies = genomes.Select(g => g.WithBody(BodyGenes.Neutral)).ToArray();
            Assert.That(() => new IsolatedEvaluator(plan).TrainingJob(otherBodies, 0).Resume(progress),
                Throws.ArgumentException.With.Message.Contains("body"));
        }

        [Test]
        public void Plan_RejectsUnknownGenes()
        {
            var settings = Small(1);
            settings.ExpressedBodyGenes = (BodyGeneSet)16;
            Assert.That(() => new EvaluationPlan(settings),
                Throws.ArgumentException.With.Message.Contains("ExpressedBodyGenes"));
            Assert.That(new EvaluationPlan(Small(1)).ExpressedBodyGenes, Is.EqualTo(BodyGeneSet.None), "fixed body by default");
            Assert.That(settings.WithExperimentSeed(2).ExpressedBodyGenes, Is.EqualTo(settings.ExpressedBodyGenes));
        }

        [Test]
        public void Checkpoint_WithBodyGenes_ContinuesLikeTheUninterruptedRun()
        {
            var settings = Small(19);
            settings.ExpressedBodyGenes = BodyGeneSet.All;
            var session = new EvolutionSession(settings, 2);
            AdvanceUntil(session, () => session.Training.Count >= 1 && session.Phase == EvolutionPhase.Training);
            session.Advance(77);

            var text = CheckpointCodec.Encode(new Checkpoint("evolution", "minimal", 19, "neural", session.Capture(), null),
                "0.1.0", "test", new DateTime(2026, 10, 26, 0, 0, 0, DateTimeKind.Utc));
            Assert.That(text, Does.Contain("\"expressedBodyGenes\": [").And.Contain("\"body\": ["));
            var decoded = CheckpointCodec.Decode(text, out _);
            Assert.That(decoded.Evolution.Settings.ExpressedBodyGenes, Is.EqualTo(BodyGeneSet.All));

            var restored = EvolutionSession.Restore(decoded.Evolution);
            Assert.That(restored.Job.Current.Creature.Body, Is.EqualTo(session.Job.Current.Creature.Body));
            AdvanceUntil(session, () => session.Training.Count >= 3);
            CatchUp(restored, session);
            AssertSameSession(restored, session);
            for (var i = 0; i < session.Evolution.Population.Count; i++)
                Assert.That(restored.Evolution.Population[i].Body, Is.EqualTo(session.Evolution.Population[i].Body));
        }

        [Test]
        public void Checkpoint_FixedBody_WritesNoBodyFields_AndSchema1FilesStillLoad()
        {
            var session = new EvolutionSession(Small(23), 2);
            session.Advance(150);
            var config = SharedConfig();
            var world = new WorldSimulation(WorldFactory.CreateInitial(config));
            var brains = BrainController.Shared(config, new RandomBrain());
            Run(world, brains, 20);
            var checkpoint = new Checkpoint("shared", "minimal", 23, "random", session.Capture(),
                new SharedWorldSnapshot(SharedSettings(), world.Capture(), brains.Capture()));
            var savedAt = new DateTime(2026, 10, 26, 0, 0, 0, DateTimeKind.Utc);
            var text = CheckpointCodec.Encode(checkpoint, "0.1.0", "test", savedAt);

            Assert.That(text, Does.Not.Contain("body").IgnoreCase, "the fixed body is written as before body genes");
            var old = text.Replace("\"schemaVersion\": 2,", "\"schemaVersion\": 1,")
                .Replace("\"genomeSchema\": 2", "\"genomeSchema\": 1");
            Assert.That(old, Is.Not.EqualTo(text));
            var decoded = CheckpointCodec.Decode(old, out var info);
            Assert.That(info.SchemaVersion, Is.EqualTo(1));
            Assert.That(CheckpointCodec.Encode(decoded, "0.1.0", "test", savedAt), Is.EqualTo(text));

            // An explicit neutral body is not the canonical text and is refused like any other edit.
            var explicitBody = text.Replace("\"weights\": [", "\"body\": [0, 0, 0, 0],\n        \"weights\": [");
            Assert.That(() => CheckpointCodec.Decode(Rehash(explicitBody), out _),
                Throws.TypeOf<CheckpointException>());
        }

        [Test]
        public void ExperimentHeader_NamesTheExpressedGenes()
        {
            var settings = Small(2);
            var createdAt = new DateTime(2026, 10, 26, 0, 0, 0, DateTimeKind.Utc);
            Assert.That(new ExperimentHeader(settings, 1, "0.1.0", createdAt).Describe(), Does.Contain("body_genes=fixed"));
            var fixedHash = new ExperimentHeader(settings, 1, "0.1.0", createdAt).ConfigHash;

            settings.ExpressedBodyGenes = BodyGeneSet.Size | BodyGeneSet.Sensor;
            var header = new ExperimentHeader(settings, 1, "0.1.0", createdAt);
            Assert.That(header.Describe(), Does.Contain("body_genes=size;sensor"));
            Assert.That(header.ConfigHash, Is.Not.EqualTo(fixedHash));
        }

        private static void AssertSameEpisodes(GenerationEvaluation actual, GenerationEvaluation expected)
        {
            Assert.That(actual.Episodes.Count, Is.EqualTo(expected.Episodes.Count));
            for (var i = 0; i < expected.Episodes.Count; i++)
            {
                Assert.That(Bits(actual.Episodes[i].FoodEnergy), Is.EqualTo(Bits(expected.Episodes[i].FoodEnergy)));
                Assert.That(actual.Episodes[i].SurvivedTicks, Is.EqualTo(expected.Episodes[i].SurvivedTicks));
                Assert.That(Bits(actual.Episodes[i].FinalEnergy), Is.EqualTo(Bits(expected.Episodes[i].FinalEnergy)));
            }
        }

        private static float Speed(CreatureState creature) =>
            MathF.Sqrt(creature.Velocity.X * creature.Velocity.X + creature.Velocity.Y * creature.Velocity.Y);

        // What someone editing the file by hand and fixing up the hashes would produce.
        private static string Rehash(string text)
        {
            var root = JsonText.Parse(text);
            var config = CheckpointCodec.Hex(CheckpointCodec.Hash(root["config"].ToCompactString()));
            var state = CheckpointCodec.Hex(CheckpointCodec.Hash(root["state"].ToCompactString()));
            text = System.Text.RegularExpressions.Regex.Replace(text, "\"configHash\": \"[0-9a-f]{16}\"",
                $"\"configHash\": \"{config}\"");
            return System.Text.RegularExpressions.Regex.Replace(text, "\"stateHash\": \"[0-9a-f]{16}\"",
                $"\"stateHash\": \"{state}\"");
        }
    }
}
