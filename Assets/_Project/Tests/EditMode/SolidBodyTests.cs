using System;
using System.Collections.Generic;
using Eden.Application;
using Eden.Domain;
using Eden.Persistence;
using Eden.Simulation;
using NUnit.Framework;

namespace Eden.Tests.EditMode
{
    /// <summary>Solid bodies (P2.1): discs that do not overlap, stay inside the arena, push each other and resume exactly.</summary>
    public class SolidBodyTests
    {
        // Overlap a scene without a jam may keep after a tick: float rounding only.
        private const float Tolerance = 1e-3f;

        // Overlap, as a share of the two radii, that a crowd pushing against a wall may keep (16 passes; measured 1.7%).
        private const float JamTolerance = 0.025f;

        private static readonly CreatureAction FullThrust = new CreatureAction(1f, 0f, 0f);
        private static readonly BodyGenes Small = new BodyGenes(-1f, 0f, 0f, 0f);
        private static readonly BodyGenes SmallAndFast = new BodyGenes(-1f, 1f, 0f, 0f);
        private static readonly BodyGenes Big = new BodyGenes(1f, 0f, 0f, 0f);

        [Test]
        public void Settings_AreOffByDefault_RadiusScalesWithSize_AndSubstepsCoverTheFastestStep()
        {
            Assert.That(new SimulationSettings().SolidBodies, Is.False);
            Assert.That(new SimulationSettings().BodyRadius, Is.EqualTo(0.5f));
            Assert.That(SimulationConfig.CreateDefault(1).SolidSubsteps, Is.EqualTo(1));

            var config = Solid();
            // Fastest step 5 × 1.5 / 20 = 0.375 over the smallest radius 0.5 × 0.5 = 0.25.
            Assert.That(config.SolidSubsteps, Is.EqualTo(2));
            Assert.That(CreatureBody.Reference(config).Radius, Is.EqualTo(0.5f));
            Assert.That(CreatureBody.Express(config, Big).Radius, Is.EqualTo(0.75f));
            Assert.That(CreatureBody.Express(config, Small).Radius, Is.EqualTo(0.25f));
            Assert.That(CreatureBody.Express(config, Small).EatRadius, Is.EqualTo(0.375f), "the eat radius is its own trait");

            Assert.That(() => new SimulationConfig(new SimulationSettings { BodyRadius = 0f }), Throws.ArgumentException);
            Assert.That(() => new SimulationConfig(new SimulationSettings { SolidBodies = true, BodyRadius = 20f }),
                Throws.ArgumentException.With.Message.Contains("does not fit"));
            Assert.That(() => new SimulationConfig(new SimulationSettings { SolidBodies = true, MaxSpeed = 1000f }),
                Throws.ArgumentException.With.Message.Contains("substeps"));
            Assert.That(new SimulationConfig(new SimulationSettings { MaxSpeed = 1000f }).SolidSubsteps, Is.EqualTo(1),
                "point bodies never need substeps");
        }

        [Test]
        public void WithoutSolidBodies_CreaturesStillPassThroughEachOther()
        {
            var world = TestWorlds.Empty();
            var first = world.AddCreature(new Float2(20f, 25f), 0f, 50f);
            var second = world.AddCreature(new Float2(22f, 25f), MathF.PI, 50f);
            var simulation = new WorldSimulation(world);
            for (var tick = 0; tick < 20; tick++)
            {
                simulation.Step(new ConstantController(FullThrust));
                Assert.That(simulation.LastContacts, Is.EqualTo(0));
            }

            Assert.That(first.Position.X, Is.GreaterThan(second.Position.X), "point bodies cross");
        }

        [Test]
        public void HeadOnAtFullSpeed_TheSmallestFastestBodiesNeverOverlapOrPassThrough()
        {
            var world = new WorldState(Solid());
            var first = world.AddCreature(new Float2(20f, 25f), 0f, 40f, SmallAndFast);
            var second = world.AddCreature(new Float2(30f, 25f), MathF.PI, 40f, SmallAndFast);
            var simulation = new WorldSimulation(world);
            var touched = false;
            for (var tick = 0; tick < 200; tick++)
            {
                simulation.Step(new ConstantController(FullThrust));
                touched |= simulation.LastContacts == 1;
                Assert.That(Distance(first, second), Is.GreaterThanOrEqualTo(0.5f - Tolerance), $"tick {tick}");
                Assert.That(first.Position.X, Is.LessThan(second.Position.X), $"tick {tick}: nobody passes through");
            }

            Assert.That(touched, Is.True);
        }

        [Test]
        public void OverlappingBodies_AreSeparatedInOneTick_EachByTheOthersShareOfTheMass()
        {
            var world = new WorldState(Solid());
            var big = world.AddCreature(new Float2(25f, 25f), 0f, 40f, Big);
            var small = world.AddCreature(new Float2(25.5f, 25f), 0f, 40f, Small);
            var simulation = new WorldSimulation(world);
            simulation.Step(new ConstantController(CreatureAction.Idle));

            // Overlap 0.5; masses 0.75² and 0.25², so the big body moves 1/10 of it and the small one 9/10.
            Assert.That(big.Position.X, Is.EqualTo(24.95f).Within(1e-4f));
            Assert.That(small.Position.X, Is.EqualTo(25.95f).Within(1e-4f));
            Assert.That(Distance(big, small), Is.GreaterThanOrEqualTo(1f - Tolerance));
            Assert.That(simulation.LastContacts, Is.EqualTo(1));
        }

        [Test]
        public void Pushing_MovesTheOtherBody_AndCostsExactlyTheThrust()
        {
            var world = new WorldState(Solid());
            var pusher = world.AddCreature(new Float2(20f, 25f), 0f, 50f);
            var pushed = world.AddCreature(new Float2(21f, 25f), 0f, 50f);
            var simulation = new WorldSimulation(world);

            var alone = new WorldState(Solid());
            var lone = alone.AddCreature(new Float2(20f, 25f), 0f, 50f);
            var loneSimulation = new WorldSimulation(alone);

            var controller = new ByIdController(pusher.Id, FullThrust, CreatureAction.Idle);
            for (var tick = 0; tick < 40; tick++)
            {
                simulation.Step(controller);
                loneSimulation.Step(controller);
                Assert.That(Distance(pusher, pushed), Is.GreaterThanOrEqualTo(1f - Tolerance), $"tick {tick}");
            }

            Assert.That(pushed.Position.X, Is.GreaterThan(22f), "the idle body was pushed along");
            Assert.That(pusher.Position.X, Is.LessThan(pushed.Position.X));
            Assert.That(lone.Position.X - 20f, Is.GreaterThan(pusher.Position.X - 20f), "pushing slows the pusher down");
            Assert.That(pusher.Energy, Is.EqualTo(lone.Energy), "effort is paid for the thrust, contact or not");
        }

        [Test]
        public void Bodies_StayWholeInsideTheArena()
        {
            var world = new WorldState(Solid());
            var creature = world.AddCreature(new Float2(2f, 49f), 0.75f * MathF.PI, 50f);
            var simulation = new WorldSimulation(world);
            for (var tick = 0; tick < 60; tick++)
                simulation.Step(new ConstantController(FullThrust));

            Assert.That(creature.Position.X, Is.EqualTo(0.5f));
            Assert.That(creature.Position.Y, Is.EqualTo(49.5f));
            Assert.That(creature.Velocity.X, Is.GreaterThanOrEqualTo(0f));
            Assert.That(creature.Velocity.Y, Is.LessThanOrEqualTo(0f));
        }

        [Test]
        public void SwappingIds_SwapsTheOutcome_SoNoIdHasPriority()
        {
            var places = new[] { new Float2(20f, 25.3f), new Float2(24f, 25f), new Float2(22f, 24.2f) };
            var headings = new[] { 0f, MathF.PI, 0.5f * MathF.PI };
            var actions = new[] { FullThrust, FullThrust, new CreatureAction(0.4f, 0.2f, 0f) };

            var straight = RunScene(places, headings, actions, new[] { 0, 1, 2 });
            var swapped = RunScene(places, headings, actions, new[] { 2, 0, 1 });
            for (var body = 0; body < places.Length; body++)
            {
                Assert.That(swapped[body].X, Is.EqualTo(straight[body].X).Within(1e-4f), $"body {body}");
                Assert.That(swapped[body].Y, Is.EqualTo(straight[body].Y).Within(1e-4f), $"body {body}");
            }
        }

        [Test]
        public void Crowd_StaysInside_WithoutNaN_AndAJamKeepsOnlyASmallOverlap()
        {
            var config = Solid(s =>
            {
                s.ArenaWidth = 20f;
                s.ArenaHeight = 20f;
                s.InitialPopulation = 64;
                s.InitialFood = 40;
            });
            var simulation = new WorldSimulation(WorldFactory.CreateInitial(config));
            var contacts = 0;
            var worst = 0f;
            for (var tick = 0; tick < 400; tick++)
            {
                simulation.Step(new SeekFoodController());
                contacts += simulation.LastContacts;
                var creatures = simulation.World.Creatures;
                for (var i = 0; i < creatures.Count; i++)
                {
                    var creature = creatures[i];
                    var r = creature.Body.Radius;
                    Assert.That(float.IsNaN(creature.Position.X) || float.IsNaN(creature.Position.Y), Is.False);
                    Assert.That(creature.Position.X >= r && creature.Position.X <= 20f - r &&
                                creature.Position.Y >= r && creature.Position.Y <= 20f - r, Is.True, $"tick {tick}");
                    // Founders start anywhere, overlapping; from the second tick on they must be apart.
                    for (var j = i + 1; tick > 0 && j < creatures.Count; j++)
                    {
                        var sum = r + creatures[j].Body.Radius;
                        worst = Math.Max(worst, (sum - Distance(creature, creatures[j])) / sum);
                    }
                }
            }

            TestContext.WriteLine($"contacts over the run {contacts}, worst overlap {worst} of the radii");
            Assert.That(contacts, Is.GreaterThan(0), "the crowd should touch");
            Assert.That(worst, Is.LessThanOrEqualTo(JamTolerance), "everyone seeks the same food, so the crowd jams");

            for (var tick = 0; tick < 40; tick++)
                simulation.Step(new ConstantController(CreatureAction.Idle));
            var resting = simulation.World.Creatures;
            for (var i = 0; i < resting.Count; i++)
            {
                for (var j = i + 1; j < resting.Count; j++)
                {
                    Assert.That(Distance(resting[i], resting[j]),
                        Is.GreaterThanOrEqualTo(resting[i].Body.Radius + resting[j].Body.Radius - Tolerance),
                        "once nobody pushes, the jam comes apart");
                }
            }
        }

        [Test]
        public void ResumingDuringContacts_MatchesTheUninterruptedRun_AndOnlySolidRunsWriteTheSetting()
        {
            var settings = new SimulationSettings
            {
                Seed = 6, ArenaWidth = 15f, ArenaHeight = 15f, InitialPopulation = 40, InitialFood = 30, SolidBodies = true
            };
            var config = new SimulationConfig(settings);
            var original = new WorldSimulation(WorldFactory.CreateInitial(config));
            var brains = BrainController.Shared(config, new Eden.Brains.HeuristicBrain());
            Run(original, brains, 60);
            Assert.That(original.LastContacts, Is.GreaterThan(0), "the checkpoint is taken while bodies touch");
            var saved = original.Capture();
            var savedBrains = brains.Capture();
            Run(original, brains, 100);

            var text = CheckpointCodec.Encode(new Checkpoint("shared", "minimal", 6, "heuristic", null,
                new SharedWorldSnapshot(settings, saved, savedBrains)), "0.1.0", "test", new DateTime(2026, 10, 27, 0, 0, 0,
                DateTimeKind.Utc));
            Assert.That(text, Does.Contain("\"solidBodies\": true"));
            Assert.That(text, Does.Not.Contain("bodyRadius"), "the default radius is not written");
            var loaded = CheckpointCodec.Decode(text, out _).SharedWorld;
            Assert.That(loaded.Settings.SolidBodies, Is.True);
            var restored = WorldSimulation.Restore(new SimulationConfig(loaded.Settings), loaded.World);
            var restoredBrains = BrainController.Shared(config, new Eden.Brains.HeuristicBrain());
            restoredBrains.Restore(loaded.Brains);
            Run(restored, restoredBrains, 100);
            Assert.That(restored.ComputeFingerprint(), Is.EqualTo(original.ComputeFingerprint()));

            var plain = new SimulationSettings { Seed = 6, InitialPopulation = 4, InitialFood = 4 };
            var plainText = CheckpointCodec.Encode(new Checkpoint("shared", "minimal", 6, "heuristic", null,
                new SharedWorldSnapshot(plain, new WorldSimulation(WorldFactory.CreateInitial(new SimulationConfig(plain)))
                    .Capture(), Array.Empty<BrainSlotState>())), "0.1.0", "test", DateTime.UtcNow);
            Assert.That(plainText, Does.Not.Contain("solidBodies"));
            Assert.That(plainText, Does.Not.Contain("bodyRadius"));

            settings.BodyRadius = 0.6f;
            var radiusText = CheckpointCodec.Encode(new Checkpoint("shared", "minimal", 6, "heuristic", null,
                new SharedWorldSnapshot(settings, saved, savedBrains)), "0.1.0", "test", DateTime.UtcNow);
            Assert.That(CheckpointCodec.Decode(radiusText, out _).SharedWorld.Settings.BodyRadius, Is.EqualTo(0.6f));
        }

        [Test]
        public void Eyes_SitOnTheSolidDisc_AndOnTheEatRadiusWithoutIt()
        {
            var solid = new WorldState(Solid());
            var onDisc = solid.AddCreature(new Float2(25f, 25f), 0f, 50f);
            Assert.That(Vision.EyePosition(solid.Config, onDisc, Eye.Frontal).X, Is.EqualTo(25.5f));

            var points = TestWorlds.Empty();
            var onEatRadius = points.AddCreature(new Float2(25f, 25f), 0f, 50f);
            Assert.That(Vision.EyePosition(points.Config, onEatRadius, Eye.Frontal).X, Is.EqualTo(25.75f));
        }

        private static SimulationConfig Solid(Action<SimulationSettings> configure = null)
        {
            var settings = new SimulationSettings { Seed = 3, InitialPopulation = 0, InitialFood = 0, SolidBodies = true };
            configure?.Invoke(settings);
            return new SimulationConfig(settings);
        }

        // Places body b of the scene as creature id rank[b] + 1 and returns where each body ended, by body.
        private static Float2[] RunScene(Float2[] places, float[] headings, CreatureAction[] actions, int[] rank)
        {
            var world = new WorldState(Solid());
            var byId = new Dictionary<CreatureId, CreatureAction>();
            var bodyOf = new int[places.Length];
            for (var id = 0; id < places.Length; id++)
                bodyOf[id] = Array.IndexOf(rank, id);
            var created = new CreatureState[places.Length];
            for (var id = 0; id < places.Length; id++)
            {
                var body = bodyOf[id];
                created[body] = world.AddCreature(places[body], headings[body], 50f);
                byId[created[body].Id] = actions[body];
            }

            var simulation = new WorldSimulation(world);
            var controller = new MapController(byId);
            for (var tick = 0; tick < 60; tick++)
                simulation.Step(controller);
            return Array.ConvertAll(created, c => c.Position);
        }

        private static void Run(WorldSimulation simulation, ICreatureController controller, int ticks)
        {
            for (var i = 0; i < ticks; i++)
                simulation.Step(controller);
        }

        private static float Distance(CreatureState a, CreatureState b)
        {
            var dx = a.Position.X - b.Position.X;
            var dy = a.Position.Y - b.Position.Y;
            return MathF.Sqrt(dx * dx + dy * dy);
        }

        private sealed class ByIdController : ICreatureController
        {
            private readonly CreatureId special;
            private readonly CreatureAction specialAction;
            private readonly CreatureAction otherAction;

            public ByIdController(CreatureId special, CreatureAction specialAction, CreatureAction otherAction)
            {
                this.special = special;
                this.specialAction = specialAction;
                this.otherAction = otherAction;
            }

            public CreatureAction Decide(CreatureId creature, in Observation observation) =>
                creature == special ? specialAction : otherAction;
        }

        private sealed class MapController : ICreatureController
        {
            private readonly Dictionary<CreatureId, CreatureAction> actions;

            public MapController(Dictionary<CreatureId, CreatureAction> actions)
            {
                this.actions = actions;
            }

            public CreatureAction Decide(CreatureId creature, in Observation observation) => actions[creature];
        }
    }
}
