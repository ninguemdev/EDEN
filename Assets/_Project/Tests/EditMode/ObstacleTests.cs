using System;
using System.Collections.Generic;
using Eden.Application;
using Eden.Domain;
using Eden.Persistence;
using Eden.Simulation;
using NUnit.Framework;

namespace Eden.Tests.EditMode
{
    /// <summary>Static obstacles (P2.2): validated rectangles solid bodies never enter, named layouts and their resume.</summary>
    public class ObstacleTests
    {
        // Overlap with an obstacle a body may keep after a tick: float rounding only.
        private const float Tolerance = 1e-3f;

        private static readonly CreatureAction FullThrust = new CreatureAction(1f, 0f, 0f);
        private static readonly BodyGenes SmallAndFast = new BodyGenes(-1f, 1f, 0f, 0f);

        [Test]
        public void Obstacles_AreValidated_AndCopiedIntoTheConfig()
        {
            var wall = new Obstacle(10f, 10f, 12f, 20f);
            Assert.That(() => Config(s => { s.SolidBodies = false; s.Obstacles = new[] { wall }; }),
                Throws.ArgumentException.With.Message.Contains("need solid bodies"));
            Assert.That(() => Config(s => s.Obstacles = new[] { new Obstacle(45f, 10f, 51f, 20f) }),
                Throws.ArgumentException.With.Message.Contains("not inside the arena"));
            Assert.That(() => Config(s => s.Obstacles = new[] { new Obstacle(10f, 10f, 10.4f, 20f) }),
                Throws.ArgumentException.With.Message.Contains("thinner than the smallest body"));
            Assert.That(() => Config(s => s.Obstacles = new[] { new Obstacle(float.NaN, 10f, 12f, 20f) }),
                Throws.ArgumentException);
            Assert.That(() => Config(s => s.Obstacles = new Obstacle[SimulationConfig.MaxObstacles + 1]),
                Throws.ArgumentException.With.Message.Contains("exceed the limit"));
            Assert.That(Config(s => s.Obstacles = null).Obstacles, Is.Empty, "null is the open arena");
            Assert.That(SimulationConfig.CreateDefault(1).Obstacles, Is.Empty);

            var settings = new SimulationSettings { SolidBodies = true, Obstacles = new[] { wall } };
            var config = new SimulationConfig(settings);
            var copy = settings.Clone();
            settings.Obstacles[0] = new Obstacle(0f, 0f, 1f, 1f);
            Assert.That(config.Obstacles[0], Is.EqualTo(wall), "the config keeps its own copy");
            Assert.That(copy.Obstacles[0], Is.EqualTo(wall), "a cloned template keeps its own copy");
        }

        [TestCase(50f)]
        [TestCase(100f)]
        [TestCase(20f)]
        public void Layouts_AreValid_AndLeaveTheFreeSpaceConnectedForTheLargestBody(float size)
        {
            var template = new SimulationSettings { ArenaWidth = size, ArenaHeight = size, SolidBodies = true };
            Assert.That(ObstacleLayouts.Create(ObstacleLayout.None, template), Is.Empty);
            foreach (var layout in new[] { ObstacleLayout.Pillars, ObstacleLayout.Corridor })
            {
                var obstacles = ObstacleLayouts.Create(layout, template);
                Assert.That(obstacles, Is.Not.Empty);
                template.Obstacles = obstacles;
                var config = new SimulationConfig(template);
                var largest = CreatureBody.Express(config, new BodyGenes(1f, 0f, 0f, 0f)).Radius;
                Assert.That(FreeRegions(config, largest), Is.EqualTo(1), $"{layout} in {size} × {size}");
            }

            template.Obstacles = ObstacleLayouts.Create(ObstacleLayout.Corridor, template);
            var corridor = new SimulationConfig(template);
            Assert.That(FreeRegions(corridor, corridor.ArenaHeight), Is.EqualTo(0), "the probe itself is checked");
        }

        [Test]
        public void TheSmallestFastestBody_NeverEntersAnObstacle_FromAnyDirection_CornersIncluded()
        {
            var pillar = new Obstacle(23f, 23f, 27f, 27f);
            var config = Config(s => s.Obstacles = new[] { pillar });
            for (var k = 0; k < 16; k++)
            {
                // Aimed at the pillar's centre from a circle around it, so half the runs meet a corner first.
                var angle = k * MathF.PI / 8f + 0.1f;
                var world = new WorldState(config);
                var start = new Float2(25f + 9f * MathF.Cos(angle), 25f + 9f * MathF.Sin(angle));
                var heading = angle + MathF.PI;
                heading = heading >= 2f * MathF.PI ? heading - 2f * MathF.PI : heading;
                var creature = world.AddCreature(start, heading, 40f, SmallAndFast);
                var simulation = new WorldSimulation(world);
                for (var tick = 0; tick < 120; tick++)
                {
                    simulation.Step(new ConstantController(FullThrust));
                    Assert.That(DistanceTo(pillar, creature.Position), Is.GreaterThanOrEqualTo(0.25f - Tolerance),
                        $"direction {k}, tick {tick}");
                }
            }
        }

        [Test]
        public void APassageWiderThanTheBody_LetsItThrough_ANarrowerOneStopsIt()
        {
            Assert.That(CrossesPassage(1.2f), Is.True, "gap 1.2 for a body 1 across");
            Assert.That(CrossesPassage(0.8f), Is.False, "gap 0.8 for a body 1 across");
        }

        [Test]
        public void ABodyPushedAgainstAnObstacle_StaysOut_AndThePusherIsStopped()
        {
            var wall = new Obstacle(25f, 10f, 27f, 40f);
            var world = new WorldState(Config(s => s.Obstacles = new[] { wall }));
            var pusher = world.AddCreature(new Float2(22f, 25f), 0f, 50f);
            var pushed = world.AddCreature(new Float2(23f, 25f), 0f, 50f);
            var simulation = new WorldSimulation(world);
            var controller = new OnlyController(pusher.Id, FullThrust);
            for (var tick = 0; tick < 80; tick++)
            {
                simulation.Step(controller);
                Assert.That(DistanceTo(wall, pushed.Position), Is.GreaterThanOrEqualTo(0.5f - Tolerance), $"tick {tick}");
                Assert.That(Distance(pusher.Position, pushed.Position), Is.GreaterThanOrEqualTo(1f - Tolerance),
                    $"tick {tick}");
            }

            Assert.That(pushed.Position.X, Is.EqualTo(24.5f).Within(Tolerance), "pressed flat against the obstacle");
            Assert.That(pusher.Position.X, Is.EqualTo(23.5f).Within(Tolerance));
        }

        [Test]
        public void ACrowdAmongPillars_ResumesExactly_AndOnlyObstaclesAreWritten()
        {
            var settings = new SimulationSettings
            {
                Seed = 8, ArenaWidth = 20f, ArenaHeight = 20f, InitialPopulation = 30, InitialFood = 30, SolidBodies = true
            };
            settings.Obstacles = ObstacleLayouts.Create(ObstacleLayout.Pillars, settings);
            var config = new SimulationConfig(settings);
            var original = new WorldSimulation(WorldFactory.CreateInitial(config));
            var brains = BrainController.Shared(config, new Eden.Brains.HeuristicBrain());
            Run(original, brains, 60);
            var saved = original.Capture();
            var savedBrains = brains.Capture();
            Run(original, brains, 140);
            // Founders start outside the pillars and nobody ever enters one.
            foreach (var creature in original.World.Creatures)
            {
                foreach (var pillar in config.Obstacles)
                {
                    Assert.That(DistanceTo(pillar, creature.Position),
                        Is.GreaterThanOrEqualTo(creature.Body.Radius - Tolerance), $"{creature.Id}");
                }
            }

            var text = CheckpointCodec.Encode(new Checkpoint("shared", "minimal", 8, "heuristic", null,
                new SharedWorldSnapshot(settings, saved, savedBrains)), "0.1.0", "test", new DateTime(2026, 10, 28, 0, 0, 0,
                DateTimeKind.Utc));
            Assert.That(text, Does.Contain("\"obstacles\""));
            var loaded = CheckpointCodec.Decode(text, out _).SharedWorld;
            Assert.That(loaded.Settings.Obstacles, Is.EqualTo(settings.Obstacles));
            var restored = WorldSimulation.Restore(new SimulationConfig(loaded.Settings), loaded.World);
            var restoredBrains = BrainController.Shared(config, new Eden.Brains.HeuristicBrain());
            restoredBrains.Restore(loaded.Brains);
            Run(restored, restoredBrains, 140);
            Assert.That(restored.ComputeFingerprint(), Is.EqualTo(original.ComputeFingerprint()));

            settings.Obstacles = Array.Empty<Obstacle>();
            var open = CheckpointCodec.Encode(new Checkpoint("shared", "minimal", 8, "heuristic", null,
                new SharedWorldSnapshot(settings, saved, savedBrains)), "0.1.0", "test", DateTime.UtcNow);
            Assert.That(open, Does.Not.Contain("obstacles"), "the open arena is written as before");
        }

        // Two blocks leave a horizontal passage of the given height at y = 25; a reference body drives through it.
        private static bool CrossesPassage(float gap)
        {
            var below = new Obstacle(24f, 0f, 26f, 25f - gap * 0.5f);
            var above = new Obstacle(24f, 25f + gap * 0.5f, 26f, 50f);
            var world = new WorldState(Config(s => s.Obstacles = new[] { below, above }));
            var creature = world.AddCreature(new Float2(20f, 25f), 0f, 50f);
            var simulation = new WorldSimulation(world);
            for (var tick = 0; tick < 100; tick++)
            {
                simulation.Step(new ConstantController(FullThrust));
                Assert.That(DistanceTo(below, creature.Position), Is.GreaterThanOrEqualTo(0.5f - Tolerance));
                Assert.That(DistanceTo(above, creature.Position), Is.GreaterThanOrEqualTo(0.5f - Tolerance));
            }

            return creature.Position.X > 26.5f;
        }

        // Regions of the arena where a disc of the radius fits, found on a grid of quarter units (4-neighbour flood fill).
        private static int FreeRegions(SimulationConfig config, float r)
        {
            const float cell = 0.25f;
            var columns = (int)(config.ArenaWidth / cell);
            var rows = (int)(config.ArenaHeight / cell);
            var free = new bool[columns, rows];
            for (var c = 0; c < columns; c++)
            {
                for (var row = 0; row < rows; row++)
                {
                    var point = new Float2((c + 0.5f) * cell, (row + 0.5f) * cell);
                    var fits = point.X >= r && point.X <= config.ArenaWidth - r && point.Y >= r &&
                               point.Y <= config.ArenaHeight - r;
                    foreach (var obstacle in config.Obstacles)
                        fits &= DistanceTo(obstacle, point) >= r;
                    free[c, row] = fits;
                }
            }

            var regions = 0;
            var pending = new Stack<(int, int)>();
            for (var c = 0; c < columns; c++)
            {
                for (var row = 0; row < rows; row++)
                {
                    if (!free[c, row])
                        continue;
                    regions++;
                    free[c, row] = false;
                    pending.Push((c, row));
                    while (pending.Count > 0)
                    {
                        var (pc, pr) = pending.Pop();
                        foreach (var (nc, nr) in new[] { (pc + 1, pr), (pc - 1, pr), (pc, pr + 1), (pc, pr - 1) })
                        {
                            if (nc < 0 || nr < 0 || nc >= columns || nr >= rows || !free[nc, nr])
                                continue;
                            free[nc, nr] = false;
                            pending.Push((nc, nr));
                        }
                    }
                }
            }

            return regions;
        }

        private static SimulationConfig Config(Action<SimulationSettings> configure)
        {
            var settings = new SimulationSettings { Seed = 5, InitialPopulation = 0, InitialFood = 0, SolidBodies = true };
            configure(settings);
            return new SimulationConfig(settings);
        }

        private static void Run(WorldSimulation simulation, ICreatureController controller, int ticks)
        {
            for (var i = 0; i < ticks; i++)
                simulation.Step(controller);
        }

        // Distance from a point to the nearest point of a rectangle; 0 inside it.
        private static float DistanceTo(Obstacle obstacle, Float2 point)
        {
            var dx = Math.Max(Math.Max(obstacle.MinX - point.X, 0f), point.X - obstacle.MaxX);
            var dy = Math.Max(Math.Max(obstacle.MinY - point.Y, 0f), point.Y - obstacle.MaxY);
            return MathF.Sqrt(dx * dx + dy * dy);
        }

        private static float Distance(Float2 a, Float2 b) =>
            MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

        private sealed class OnlyController : ICreatureController
        {
            private readonly CreatureId mover;
            private readonly CreatureAction action;

            public OnlyController(CreatureId mover, CreatureAction action)
            {
                this.mover = mover;
                this.action = action;
            }

            public CreatureAction Decide(CreatureId creature, in Observation observation) =>
                creature == mover ? action : CreatureAction.Idle;
        }
    }
}
