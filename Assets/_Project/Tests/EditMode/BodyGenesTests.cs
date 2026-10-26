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
            Assert.That(Genome.CurrentSchemaVersion, Is.EqualTo(3));
        }

        [Test]
        public void Mutation_ChangesOnlyTheExpressedBodyGenes_WithinTheirLimits()
        {
            var parent = InitialGenomes.Population(21, 1, BodyGeneSet.All)[0];
            var weightsOnly = Reproduction.Mutate(parent, 1f, 10f, new GaussianSampler(new Pcg32(4, 9)), out var mutated);
            Assert.That(mutated, Is.EqualTo(parent.WeightCount));
            Assert.That(weightsOnly.Body, Is.EqualTo(parent.Body), "a fixed-body run never touches body genes");
            Assert.That(Enumerable.Range(0, weightsOnly.WeightCount).All(w => Math.Abs(weightsOnly.GetWeight(w)) <= Genome.MaxAbsWeight));

            var all = Reproduction.Mutate(parent, 1f, 10f, BodyGeneSet.All, new GaussianSampler(new Pcg32(4, 9)), out _);
            Assert.That(all.CopyWeights(), Is.EqualTo(weightsOnly.CopyWeights()), "body genes are drawn after the weights");
            for (var g = 0; g < BodyGenes.Count; g++)
            {
                Assert.That(all.Body[(BodyGene)g], Is.Not.EqualTo(parent.Body[(BodyGene)g]));
                Assert.That(Math.Abs(all.Body[(BodyGene)g]), Is.LessThanOrEqualTo(BodyGenes.MaxAbsValue));
            }

            var sensorParent = InitialGenomes.Population(21, 1, BodyGeneSet.Sensor)[0];
            var sensorChild = Reproduction.Mutate(sensorParent, 1f, 0.5f, BodyGeneSet.Sensor,
                new GaussianSampler(new Pcg32(4, 9)), out _);
            Assert.That(sensorChild.Body.Sensor, Is.Not.EqualTo(sensorParent.Body.Sensor));
            Assert.That(sensorChild.Body.Masked(BodyGeneSet.Sensor), Is.EqualTo(sensorChild.Body), "the others stay neutral");

            var rules = new ReproductionRules(new EvolutionSettings
            {
                ExperimentSeed = 3, PopulationSize = 12, EliteCount = 2, MutationProbability = 0.5f,
                ExpressedBodyGenes = BodyGeneSet.All
            });
            var parents = InitialGenomes.Population(8, 12, BodyGeneSet.All);
            var fitness = Enumerable.Range(0, 12).Select(i => (float)(i % 5)).ToArray();
            var children = Reproduction.Breed(rules, parents, fitness, 1);
            Assert.That(children.Where(c => c.IsElite).All(c => c.Genome.Body.Equals(parents[c.ParentIndex].Body)));
            Assert.That(children.Count(c => !c.IsElite && !c.Genome.Body.Equals(parents[c.ParentIndex].Body)),
                Is.GreaterThan(0), "children inherit mutated body genes");
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
        public void Expression_ScalesEachTrait_AndNeutralGenesGiveTheReferenceBody()
        {
            var config = SimulationConfig.CreateDefault(1);
            var reference = CreatureBody.Reference(config);
            Assert.That(CreatureBody.Express(config, BodyGenes.Neutral), Is.EqualTo(reference));
            var odd = new SimulationConfig(new SimulationSettings
            {
                SizeCostExponent = 3.3f, SpeedCostExponent = 0f, SensorCostExponent = 1.7f, EfficiencyPowerExponent = 4f
            });
            Assert.That(CreatureBody.Express(odd, BodyGenes.Neutral), Is.EqualTo(CreatureBody.Reference(odd)),
                "whatever the exponents");

            var largest = CreatureBody.Express(config, new BodyGenes(1f, 1f, 1f, -1f));
            Assert.That(largest.EatRadius, Is.EqualTo(config.EatRadius * 1.5f));
            Assert.That(largest.MaxEnergy, Is.EqualTo(config.MaxEnergy * 1.5f));
            Assert.That(largest.MaxSpeed, Is.EqualTo(config.MaxSpeed * 1.5f));
            Assert.That(largest.SensorRange, Is.EqualTo(config.SensorRange * 1.5f));
            var smallest = CreatureBody.Express(config, new BodyGenes(-1f, -1f, -1f, 1f));
            Assert.That(smallest.EatRadius, Is.EqualTo(config.EatRadius * 0.5f));
            Assert.That(smallest.MaxEnergy, Is.EqualTo(CreatureBody.MaxPossibleEnergy(config) / 3f));
            Assert.That(CreatureBody.Express(config, new BodyGenes(0.75f, 0f, 0f, 0f)).EatRadius,
                Is.EqualTo(config.EatRadius * 1.375f));
            Assert.That(Extreme.Masked(BodyGeneSet.Sensor), Is.EqualTo(new BodyGenes(0f, 0f, 0.75f, 0f)));
        }

        [Test]
        public void World_BuildsBodiesFromGenes_AndRefusesOthersOnRestore()
        {
            var world = TestWorlds.Empty();
            var genes = new BodyGenes(-1f, 0.5f, 0f, 0f);
            var creature = world.AddCreature(new Float2(5f, 5f), 0f, 40f, genes);
            Assert.That(creature.Genes, Is.EqualTo(genes));
            Assert.That(creature.Body, Is.EqualTo(CreatureBody.Express(world.Config, genes)));
            Assert.That(world.AddCreature(new Float2(6f, 5f), 0f, 40f).Body, Is.EqualTo(world.ReferenceBody));

            var shared = new WorldSimulation(WorldFactory.CreateInitial(SharedConfig(), i => i == 0 ? genes : BodyGenes.Neutral));
            var config = shared.World.Config;
            Assert.That(shared.World.Creatures[0].Energy, Is.EqualTo(Math.Min(config.InitialEnergy, config.MaxEnergy * 0.5f)),
                "a small body starts at most full");
            var saved = shared.Capture();
            Assert.That(WorldSimulation.Restore(config, saved).World.Creatures[0].Body, Is.EqualTo(saved.Creatures[0].Body));

            AssertRefused(saved, c => new CreatureState(c.Id, c.Position, c.Heading, c.Energy, c.Genes, shared.World.ReferenceBody),
                "body");
            AssertRefused(saved, c => new CreatureState(c.Id, c.Position, c.Heading, config.MaxEnergy * 0.6f, c.Genes, c.Body),
                "energy");
            var big = new BodyGenes(1f, 0f, 0f, 0f);
            Assert.DoesNotThrow(() => Replace(saved, c => new CreatureState(c.Id, c.Position, c.Heading,
                config.MaxEnergy * 1.4f, big, CreatureBody.Express(config, big)), config), "a large body holds more");
        }

        [Test]
        public void Simulation_UsesEachCreaturesOwnBody()
        {
            var world = TestWorlds.Empty();
            var config = world.Config;
            var reference = world.ReferenceBody;
            var small = world.AddCreature(new Float2(10f, 10f), 0f, 50f);
            var big = world.AddCreature(new Float2(10f, 30f), 0f, 50f, new BodyGenes(1f, 1f, 1f, 1f));
            var large = big.Body;

            // Food 12 units ahead: only the longer sensor sees it, and readings are relative to each creature's body.
            world.AddFood(new Float2(22f, 10f), config.FoodEnergy);
            world.AddFood(new Float2(22f, 30f), config.FoodEnergy);
            Assert.That(Sensors.Observe(world, small).FoodVisible, Is.EqualTo(0f), "12 units are beyond range 10");
            Assert.That(Sensors.Observe(world, big).FoodVisible, Is.EqualTo(1f), "but within range 15");
            Assert.That(Sensors.Observe(world, big).FoodProximity, Is.EqualTo(1f - 12f / large.SensorRange).Within(1e-6f));
            Assert.That(Sensors.Observe(world, small).WallBack, Is.EqualTo(1f), "10 units to the wall behind");
            Assert.That(Sensors.Observe(world, big).WallBack, Is.EqualTo(10f / large.SensorRange).Within(1e-6f));
            Assert.That(Sensors.Observe(world, big).Energy, Is.EqualTo(50f / large.MaxEnergy).Within(1e-6f), "own capacity");

            // Food 1 unit away: only the larger body reaches it; each pays its own costs.
            world.AddFood(new Float2(11f, 10f), config.FoodEnergy);
            world.AddFood(new Float2(11f, 30f), config.FoodEnergy);
            new WorldSimulation(world).Step(new ConstantController(CreatureAction.Idle));
            Assert.That(small.FoodEnergyEaten, Is.EqualTo(0f), "1 unit is beyond the reference eat radius 0.75");
            Assert.That(big.FoodEnergyEaten, Is.EqualTo(config.FoodEnergy), "within the larger radius 1.125");
            Assert.That(small.Energy, Is.EqualTo(50f - config.TickDuration * (reference.MetabolismPerSecond +
                reference.SensorCostPerSecond)));
            Assert.That(big.Energy, Is.EqualTo(50f + config.FoodEnergy - config.TickDuration *
                (large.MetabolismPerSecond + large.SensorCostPerSecond)));

            // Each creature's speed is capped by its own body (a tall arena, so nobody reaches a wall).
            var track = TestWorlds.Empty(s => s.ArenaHeight = 1000f);
            var slow = track.AddCreature(new Float2(10f, 10f), MathF.PI / 2f, 100f);
            var fast = track.AddCreature(new Float2(30f, 10f), MathF.PI / 2f, 100f, new BodyGenes(0f, 1f, 0f, 0f));
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
            Assert.That(episode.Creature.Genes, Is.EqualTo(shaped[0].Body));
            Assert.That(episode.Creature.Body, Is.EqualTo(CreatureBody.Express(bodyPlan.EpisodeConfig(0), shaped[0].Body)));
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
            Assert.That(text, Does.Contain("\"expressedBodyGenes\": [").And.Contain("\"body\": [")
                .And.Contain("\"bodyGenes\": ["));
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
            var old = text.Replace("\"schemaVersion\": 5,", "\"schemaVersion\": 1,")
                .Replace("\"configSchema\": 5,", "\"configSchema\": 3,")
                .Replace("\"genomeSchema\": 3", "\"genomeSchema\": 1");
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

        private static void AssertRefused(SimulationSnapshot saved, Func<CreatureState, CreatureState> change, string reason)
        {
            var config = new SimulationConfig(SharedSettings());
            Assert.That(() => Replace(saved, change, config), Throws.ArgumentException.With.Message.Contains(reason));
        }

        // Restores the saved world with its first creature replaced.
        private static void Replace(SimulationSnapshot saved, Func<CreatureState, CreatureState> change, SimulationConfig config)
        {
            var creatures = saved.Creatures.ToArray();
            creatures[0] = change(creatures[0]);
            WorldState.Restore(config, saved.Tick, saved.NextCreatureId, saved.NextFoodId, saved.WorldRandom,
                saved.SpawnRandom, creatures, saved.Food);
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
