using System;
using System.Collections.Generic;
using System.Linq;
using Eden.Application;
using Eden.Domain;
using Eden.Evolution;
using Eden.Persistence;
using Eden.Simulation;
using NUnit.Framework;

namespace Eden.Tests.EditMode
{
    /// <summary>
    /// What the lab shows of the physical arena (P2.5): touching bodies by the solver's own rule, what obstacles hide,
    /// why a birth was refused, and the scenario named in the run records.
    /// </summary>
    public class PhysicalInspectionTests
    {
        private static readonly DateTime CreatedAt = new DateTime(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);

        [Test]
        public void TouchingPairs_AreThoseTheSolverCounted()
        {
            var config = Solid(s =>
            {
                s.ArenaWidth = 20f;
                s.ArenaHeight = 20f;
                s.InitialPopulation = 64;
                s.InitialFood = 40;
            });
            var simulation = new WorldSimulation(WorldFactory.CreateInitial(config));
            var inspection = new PhysicalInspection();
            var pairs = new List<TouchingPair>();
            var touched = 0;
            for (var tick = 0; tick < 200; tick++)
            {
                simulation.Step(new SeekFoodController());
                if (simulation.LastDeaths.Count > 0)
                    continue;
                Assert.That(inspection.FindPairs(simulation.World, pairs), Is.EqualTo(simulation.LastContacts),
                    $"tick {tick}");
                Assert.That(pairs.Distinct().Count(), Is.EqualTo(pairs.Count), "each pair once");
                touched += pairs.Count;
            }

            Assert.That(touched, Is.GreaterThan(0), "the crowd should touch");
            Assert.That(new PhysicalInspection().FindPairs(TestWorlds.Empty(), pairs), Is.Zero, "no contacts for points");
        }

        [Test]
        public void ContactsOf_NamesTheBodies_TheWallAndTheObstacleTouched()
        {
            var world = new WorldState(Solid(s => s.Obstacles = new[] { new Obstacle(10f, 0f, 12f, 50f) }));
            var middle = world.AddCreature(new Float2(5f, 25f), 0f, 50f);
            var left = world.AddCreature(new Float2(4f, 25f), 0f, 50f);
            var right = world.AddCreature(new Float2(6f, 25f), 0f, 50f);
            var walled = world.AddCreature(new Float2(0.5f, 10f), 0f, 50f);
            var blocked = world.AddCreature(new Float2(9.5f, 10f), 0f, 50f);
            world.AddCreature(new Float2(30f, 30f), 0f, 50f);

            var touching = new List<CreatureId>();
            var contacts = PhysicalInspection.ContactsOf(world, middle, touching);
            Assert.That(contacts.Creatures, Is.EqualTo(2));
            Assert.That(touching, Is.EqualTo(new[] { left.Id, right.Id }));
            Assert.That(contacts.Wall || contacts.Obstacle, Is.False);
            Assert.That(PhysicalInspection.ContactsOf(world, walled, null).Wall, Is.True);
            Assert.That(PhysicalInspection.ContactsOf(world, blocked, null).Obstacle, Is.True);
            Assert.That(PhysicalInspection.ContactsOf(world, world.Creatures[5], null).Any, Is.False);
        }

        [Test]
        public void Occlusion_CountsFoodHiddenFromEyesAndMouth_AndTheLegacySenseLookingThroughAWall()
        {
            var config = Solid(s =>
            {
                s.EatRadius = 3.5f;
                s.Obstacles = new[] { new Obstacle(24f, 20f, 26f, 30f) };
            });
            var world = new WorldState(config);
            var looking = world.AddCreature(new Float2(22f, 25f), 0f, 50f);
            looking.Eyes = new EyeSet(Eye.Frontal);
            var legacy = world.AddCreature(new Float2(22f, 15f), 0f, 50f);
            world.AddFood(new Float2(28f, 25f), 10f); // behind the wall from the first creature, within its mouth's reach
            world.AddFood(new Float2(23f, 18f), 10f); // in plain sight of both

            var seen = PhysicalInspection.OcclusionOf(world, looking);
            Assert.That(seen.HiddenFromEyes, Is.EqualTo(1));
            Assert.That(seen.BlockedFromMouth, Is.EqualTo(0), "the food behind the wall is 6 away, past the reach");
            Assert.That(seen.LegacyTargetHidden, Is.False, "only for creatures without eyes");

            world.AddFood(new Float2(25.5f, 19.5f), 10f);
            Assert.That(PhysicalInspection.OcclusionOf(world, legacy).LegacyTargetHidden, Is.False,
                "its nearest food is in plain sight");

            var behind = new WorldState(config);
            var close = behind.AddCreature(new Float2(23.5f, 25f), 0f, 50f);
            behind.AddFood(new Float2(26.5f, 25f), 10f);
            var occlusion = PhysicalInspection.OcclusionOf(behind, close);
            Assert.That(occlusion.LegacyTargetHidden, Is.True, "the legacy sense points through the wall");
            Assert.That(occlusion.BlockedFromMouth, Is.EqualTo(1), "within reach, but not through the wall");
            Assert.That(PhysicalInspection.OcclusionOf(TestWorlds.Empty(), close).BlockedFromMouth, Is.Zero,
                "nothing is hidden without obstacles");
        }

        [Test]
        public void RefusedBirths_NameTheParentAndTheReason()
        {
            var boxed = Breeding(s =>
            {
                s.SolidBodies = true;
                s.Obstacles = new[]
                {
                    new Obstacle(23.5f, 23.5f, 24.5f, 26.5f), new Obstacle(25.5f, 23.5f, 26.5f, 26.5f),
                    new Obstacle(24.5f, 23.5f, 25.5f, 24.5f), new Obstacle(24.5f, 25.5f, 25.5f, 26.5f)
                };
            });
            var simulation = Breed(boxed, out var parent);
            Assert.That(simulation.LastRefusedBirths, Has.Count.EqualTo(1));
            Assert.That(simulation.LastRefusedBirths[0].Parent, Is.EqualTo(parent));
            Assert.That(simulation.LastRefusedBirths[0].Reason, Is.EqualTo(BirthRefusalReason.NoRoom));
            Assert.That(simulation.LastRefusedBirths[0].Tick, Is.EqualTo(0));

            var capped = Breed(Breeding(s => s.PopulationCap = 1), out parent);
            Assert.That(capped.LastRefusedBirths.Single().Reason, Is.EqualTo(BirthRefusalReason.PopulationCap));
            Assert.That(capped.LastBirthsRefused, Is.EqualTo(1));

            var born = Breed(Breeding(s => { }), out _);
            Assert.That(born.LastBirths, Has.Count.EqualTo(1));
            Assert.That(born.LastRefusedBirths, Is.Empty);
        }

        [Test]
        public void Layouts_AreRecognized_AndHandPlacedObstaclesAreCustom()
        {
            foreach (ObstacleLayout layout in Enum.GetValues(typeof(ObstacleLayout)))
            {
                var settings = new SimulationSettings { SolidBodies = true, ArenaWidth = 60f, ArenaHeight = 40f };
                settings.Obstacles = ObstacleLayouts.Create(layout, settings);
                Assert.That(ObstacleLayouts.Identify(settings), Is.EqualTo(layout));
                Assert.That(ObstacleLayouts.Identify(new SimulationConfig(settings)), Is.EqualTo(layout));
            }

            var custom = new SimulationSettings { SolidBodies = true, Obstacles = new[] { new Obstacle(1f, 1f, 3f, 3f) } };
            Assert.That(ObstacleLayouts.Identify(custom), Is.Null);
            Assert.That(ObstacleLayouts.Name(ObstacleLayout.Corridor), Is.EqualTo("corridor"));
        }

        [Test]
        public void RunRecords_NameTheScenario_AndRunsWithoutItAreDescribedAsBefore()
        {
            var open = new SimulationConfig(new SimulationSettings());
            var openInfo = RunInfo.Describe(open, "heuristic-v1", "0.1.0", CreatedAt);
            Assert.That(openInfo.Any(line => line.StartsWith("bodies=") || line.StartsWith("obstacle")), Is.False);

            var settings = new SimulationSettings { SolidBodies = true };
            settings.Obstacles = ObstacleLayouts.Create(ObstacleLayout.Pillars, settings);
            var info = RunInfo.Describe(new SimulationConfig(settings), "heuristic-v1", "0.1.0", CreatedAt);
            Assert.That(info, Does.Contain("bodies=solid;radius 0.5;substeps 2"));
            Assert.That(info, Does.Contain("obstacle_layout=pillars"));
            Assert.That(info, Does.Contain("obstacles=9"));
            Assert.That(info, Does.Contain("free_regions=1"));

            var evolution = new EvolutionSettings { PopulationSize = 6, TrainingSeedsPerGeneration = 1 };
            evolution.World.SolidBodies = true;
            evolution.World.Obstacles = ObstacleLayouts.Create(ObstacleLayout.Corridor, evolution.World);
            var header = new ExperimentHeader(evolution, 1, "0.1.0", CreatedAt);
            Assert.That(header.Describe(), Does.Contain("obstacle_layout=corridor"));
            var arena = JsonText.Parse(ExperimentJson.Experiment(header))["arena"];
            Assert.That(arena["bodies"].String(), Is.EqualTo("solid"));
            Assert.That(arena["obstacleLayout"].String(), Is.EqualTo("corridor"));
            Assert.That(arena["obstacles"].Int(), Is.EqualTo(2));
            Assert.That(arena["freeRegions"].Int(), Is.EqualTo(1));

            var points = JsonText.Parse(ExperimentJson.Experiment(new ExperimentHeader(
                new EvolutionSettings { PopulationSize = 6, TrainingSeedsPerGeneration = 1 }, 1, "0.1.0", CreatedAt)))["arena"];
            Assert.That(points["bodies"].String(), Is.EqualTo("points"));
            Assert.That(points["obstacleLayout"].String(), Is.EqualTo("none"));
            Assert.That(points["freeRegions"].IsNull, Is.True);
        }

        // One mature, full parent alone at the arena's centre, after one tick.
        private static WorldSimulation Breed(SimulationSettings settings, out CreatureId parent)
        {
            var config = new SimulationConfig(settings);
            var world = new WorldState(config);
            var creature = world.AddCreature(new Float2(25f, 25f), 0f, config.MaxEnergy);
            creature.AgeTicks = config.MaturityTicks;
            parent = creature.Id;
            var simulation = new WorldSimulation(world);
            simulation.Step(new ConstantController(CreatureAction.Idle));
            return simulation;
        }

        private static SimulationSettings Breeding(Action<SimulationSettings> configure)
        {
            var settings = new SimulationSettings
            {
                Seed = 13, InitialPopulation = 1, InitialFood = 0, SolidBodies = true, Reproduction = true
            };
            configure(settings);
            return settings;
        }

        private static SimulationConfig Solid(Action<SimulationSettings> configure)
        {
            var settings = new SimulationSettings { Seed = 3, InitialPopulation = 0, InitialFood = 0, SolidBodies = true };
            configure(settings);
            return new SimulationConfig(settings);
        }
    }
}
