using System;
using Eden.Application;
using Eden.Domain;
using Eden.Simulation;
using NUnit.Framework;

namespace Eden.Tests.EditMode
{
    /// <summary>
    /// Free placement (P2.4): founders, children and food appear only where they fit; a scenario that does not fit says
    /// so; a blocked birth costs nothing; a blocked respawn waits in the agenda.
    /// </summary>
    public class PlacementTests
    {
        private static readonly BodyGenes Big = new BodyGenes(1f, 0f, 0f, 0f);

        [Test]
        public void Founders_EachOfItsOwnSize_StartApart_InsideTheArena_AndOutOfTheObstacles()
        {
            var settings = Solid(s =>
            {
                s.ArenaWidth = 30f;
                s.ArenaHeight = 30f;
                s.InitialPopulation = 200;
                s.InitialFood = 300;
            });
            settings.Obstacles = ObstacleLayouts.Create(ObstacleLayout.Pillars, settings);
            var config = new SimulationConfig(settings);
            var world = WorldFactory.CreateInitial(config, i => new BodyGenes((i % 3) - 1f, 0f, 0f, 0f));
            var creatures = world.Creatures;
            Assert.That(creatures.Count, Is.EqualTo(200));
            for (var i = 0; i < creatures.Count; i++)
            {
                var a = creatures[i];
                var r = a.Body.Radius;
                Assert.That(r, Is.EqualTo(0.5f * (1f + ((i % 3) - 1f) * 0.5f)), "each founder has its own size");
                Assert.That(a.Position.X >= r && a.Position.X <= 30f - r && a.Position.Y >= r && a.Position.Y <= 30f - r,
                    Is.True);
                foreach (var obstacle in config.Obstacles)
                    Assert.That(obstacle.DistanceTo(a.Position), Is.GreaterThanOrEqualTo(r));
                for (var j = i + 1; j < creatures.Count; j++)
                    Assert.That(Distance(a.Position, creatures[j].Position), Is.GreaterThanOrEqualTo(r + creatures[j].Body.Radius));
            }

            foreach (var food in world.Food)
            {
                foreach (var obstacle in config.Obstacles)
                    Assert.That(obstacle.Contains(food.Position), Is.False, $"{food.Id} inside an obstacle");
                foreach (var creature in creatures)
                    Assert.That(Distance(food.Position, creature.Position), Is.GreaterThanOrEqualTo(creature.Body.Radius));
            }

            var again = WorldFactory.CreateInitial(config, i => new BodyGenes((i % 3) - 1f, 0f, 0f, 0f));
            Assert.That(WorldFingerprint.Compute(again), Is.EqualTo(WorldFingerprint.Compute(world)), "placement is seeded");
        }

        [Test]
        public void APopulationThatDoesNotFit_IsAnInfeasibleScenario_WithTheCapacityFound()
        {
            // 400 discs of area 0.785 cannot fit in 100 units of arena.
            var config = new SimulationConfig(Solid(s =>
            {
                s.ArenaWidth = 10f;
                s.ArenaHeight = 10f;
                s.InitialPopulation = 400;
            }));
            var error = Assert.Throws<ScenarioInfeasibleException>(() => WorldFactory.CreateInitial(config));
            Assert.That(error.Requested, Is.EqualTo(400));
            Assert.That(error.Placed, Is.InRange(1, 399));
            Assert.That(error.Message, Does.Contain($"only {error.Placed} of 400 creatures"));
            Assert.That(Assert.Throws<ScenarioInfeasibleException>(() => WorldFactory.CreateInitial(config)).Placed,
                Is.EqualTo(error.Placed), "the capacity found is reproducible");
        }

        [Test]
        public void FoodGoesOnlyWhereABodyCanEatIt()
        {
            // A slot 0.3 wide between two blocks: no body fits in it, so its depths are out of reach.
            var config = new SimulationConfig(Solid(s => s.Obstacles = new[]
            {
                new Obstacle(10f, 0f, 20f, 20f), new Obstacle(20.3f, 0f, 30f, 20f)
            }));
            var nobody = Array.Empty<CreatureState>();
            Assert.That(Placement.FoodFits(config, nobody, new Float2(20.15f, 5f)), Is.False, "deep in the slot");
            Assert.That(Placement.FoodFits(config, nobody, new Float2(20.15f, 19.9f)), Is.True, "at the slot's mouth");
            Assert.That(Placement.FoodFits(config, nobody, new Float2(15f, 10f)), Is.False, "inside a block");
            Assert.That(Placement.FoodFits(config, nobody, new Float2(40f, 40f)), Is.True, "in the open");

            var world = new WorldState(config);
            world.AddCreature(new Float2(40f, 40f), 0f, 50f);
            Assert.That(Placement.FoodFits(config, world.Creatures, new Float2(40.2f, 40f)), Is.False, "under a body");
        }

        [Test]
        public void FreeRegions_AreCounted_AndAnArenaWithNoRoomIsRefused()
        {
            Assert.That(SimulationConfig.CreateDefault(1).FreeSpace, Is.Null, "the open arena needs no map");
            var corridor = Solid(s => { });
            corridor.Obstacles = ObstacleLayouts.Create(ObstacleLayout.Corridor, corridor);
            Assert.That(new SimulationConfig(corridor).FreeSpace.Regions, Is.EqualTo(1));
            Assert.That(new SimulationConfig(Solid(s => s.Obstacles = new[] { new Obstacle(24f, 0f, 26f, 50f) }))
                .FreeSpace.Regions, Is.EqualTo(2), "a wall from side to side splits the arena");
            Assert.That(() => new SimulationConfig(Solid(s => s.Obstacles = new[] { new Obstacle(0f, 0f, 50f, 49.5f) })),
                Throws.ArgumentException.With.Message.Contains("no room for the reference body"));
        }

        [Test]
        public void ABirthWithNoRoom_IsRefusedForFree_AndTheConceivedGenomeIsForgotten()
        {
            // The parent sits in a box of its own size: no child fits next to it.
            var settings = Breeding(s => s.Obstacles = new[]
            {
                new Obstacle(23.5f, 23.5f, 24.5f, 26.5f), new Obstacle(25.5f, 23.5f, 26.5f, 26.5f),
                new Obstacle(24.5f, 23.5f, 25.5f, 24.5f), new Obstacle(24.5f, 25.5f, 25.5f, 26.5f)
            });
            var config = new SimulationConfig(settings);
            var boxed = Parent(config, out var genomes, out var boxedWorld);
            var simulation = new WorldSimulation(boxedWorld) { Heredity = genomes };

            var free = Parent(new SimulationConfig(Breeding(s => s.Reproduction = false)), out _, out var freeWorld);
            var freeSimulation = new WorldSimulation(freeWorld);

            simulation.Step(new ConstantController(CreatureAction.Idle));
            freeSimulation.Step(new ConstantController(CreatureAction.Idle));
            Assert.That(simulation.LastBirthsBlocked, Is.EqualTo(1));
            Assert.That(simulation.LastBirths, Is.Empty);
            Assert.That(simulation.LastBirthsRefused, Is.EqualTo(0), "not the population cap");
            Assert.That(boxed.Energy, Is.EqualTo(free.Energy), "nothing paid beyond living");
            Assert.That(boxed.ReproductionCooldownTicks, Is.EqualTo(0));
            Assert.That(boxedWorld.NextCreatureId, Is.EqualTo(2));
            Assert.That(genomes.BornCount, Is.EqualTo(0), "the child that was not born is forgotten");
        }

        [Test]
        public void ABirthWithRoom_PlacesTheChildJustClearOfItsParent_WhateverTheDirectionDrawn()
        {
            for (ulong seed = 1; seed <= 30; seed++)
            {
                var config = new SimulationConfig(Breeding(s => s.Seed = seed));
                var parent = Parent(config, out var genomes, out var world);
                var simulation = new WorldSimulation(world) { Heredity = genomes };
                simulation.Step(new ConstantController(CreatureAction.Idle));
                Assert.That(simulation.LastBirths, Has.Count.EqualTo(1), $"seed {seed}");
                Assert.That(simulation.LastBirthsBlocked, Is.EqualTo(0), $"seed {seed}");
                var child = world.Creatures[1];
                var gap = Distance(child.Position, parent.Position) - parent.Body.Radius - child.Body.Radius;
                Assert.That(gap, Is.InRange(0f, 2e-3f), $"seed {seed}");
                Assert.That(genomes.BornCount, Is.EqualTo(1));
            }
        }

        [Test]
        public void ARespawnWithNoRoom_WaitsInTheAgenda_SurvivesACheckpoint_AndHappensWhenThereIsRoom()
        {
            // A 1.6 × 1.6 room in the corner, the rest solid. With an eat radius of 0.5 every place food could be eaten
            // from is under the big creature that fills the room, until it starves.
            var settings = Solid(s =>
            {
                s.ArenaWidth = 10f;
                s.ArenaHeight = 10f;
                s.EatRadius = 0.5f;
                s.FoodRespawnSeconds = 0.05f;
                s.Obstacles = new[] { new Obstacle(0f, 0f, 10f, 8.4f), new Obstacle(0f, 8.4f, 8.4f, 10f) };
            });
            var config = new SimulationConfig(settings);
            var world = new WorldState(config);
            var tenant = world.AddCreature(new Float2(9.25f, 9.25f), 0f, 1f, Big);
            world.AddFood(new Float2(9.25f, 9.25f), 10f);
            var simulation = new WorldSimulation(world);
            simulation.Step(new ConstantController(CreatureAction.Idle));
            Assert.That(simulation.LastFoodEaten, Is.EqualTo(1));

            var delayed = 0;
            SimulationSnapshot saved = null;
            WorldSimulation resumed = null;
            for (var tick = 0; tick < 400 && world.Food.Count == 0; tick++)
            {
                simulation.Step(new ConstantController(CreatureAction.Idle));
                resumed?.Step(new ConstantController(CreatureAction.Idle));
                delayed += simulation.LastFoodRespawnsDelayed;
                Assert.That(world.Food.Count + simulation.SnapshotAgenda().Length, Is.EqualTo(1), "never lost nor doubled");
                if (tenant.IsAlive)
                    Assert.That(world.Food.Count, Is.EqualTo(0), $"tick {tick}: no room while the tenant lives");
                if (tick == 20)
                {
                    saved = simulation.Capture();
                    resumed = WorldSimulation.Restore(config, saved);
                }
            }

            Assert.That(delayed, Is.GreaterThan(20));
            Assert.That(tenant.IsAlive, Is.False);
            Assert.That(world.Food.Count, Is.EqualTo(1), "the food comes back once there is room");
            Assert.That(saved.Agenda, Has.Length.EqualTo(1), "the pending respawn is saved");
            Assert.That(resumed.ComputeFingerprint(), Is.EqualTo(simulation.ComputeFingerprint()));
        }

        // A mature, full parent at the arena's centre with its genome in a fresh SharedGenomes.
        private static CreatureState Parent(SimulationConfig config, out SharedGenomes genomes, out WorldState world)
        {
            world = new WorldState(config);
            var parent = world.AddCreature(new Float2(25f, 25f), 0f, config.MaxEnergy);
            parent.AgeTicks = config.MaturityTicks;
            var founder = InitialGenomes.RandomFor(config, parent.Id);
            genomes = new SharedGenomes(config, id => founder, BodyGeneSet.None);
            return parent;
        }

        private static SimulationSettings Breeding(Action<SimulationSettings> configure)
        {
            var settings = Solid(s =>
            {
                s.Reproduction = true;
                s.InitialPopulation = 1;
            });
            configure(settings);
            return settings;
        }

        private static SimulationSettings Solid(Action<SimulationSettings> configure)
        {
            var settings = new SimulationSettings { Seed = 13, InitialPopulation = 0, InitialFood = 0, SolidBodies = true };
            configure(settings);
            return settings;
        }

        private static float Distance(Float2 a, Float2 b) =>
            MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
    }
}
