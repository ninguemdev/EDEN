using System;
using System.Linq;
using Eden.Application;
using Eden.Brains;
using Eden.Domain;
using Eden.Evolution;
using Eden.Simulation;
using NUnit.Framework;
using static Eden.Tests.EditMode.ResumeChecks;

namespace Eden.Tests.EditMode
{
    /// <summary>Runs captured between ticks and restored continue exactly like the uninterrupted ones.</summary>
    public class ResumeTests
    {
        [Test]
        public void SharedWorld_SavedBetweenTicks_ContinuesLikeTheUninterruptedRun()
        {
            var config = SharedConfig();
            var original = new WorldSimulation(WorldFactory.CreateInitial(config));
            var brains = BrainController.Shared(config, new RandomBrain());
            Run(original, brains, 137);
            var atSave = original.ComputeFingerprint();
            var saved = original.Capture();
            var savedBrains = brains.Capture();
            var savedPosition = saved.Creatures[0].Position;

            Run(original, brains, 300);
            Assert.That(original.World.Creatures.Count, Is.LessThan(config.InitialPopulation), "deaths after the save");
            Assert.That(saved.Creatures[0].Position, Is.EqualTo(savedPosition), "the snapshot is detached");

            var restored = WorldSimulation.Restore(config, saved);
            var restoredBrains = BrainController.Shared(config, new RandomBrain());
            restoredBrains.Restore(savedBrains);
            Assert.That(restored.ComputeFingerprint(), Is.EqualTo(atSave));
            Run(restored, restoredBrains, 300);
            Assert.That(restored.ComputeFingerprint(), Is.EqualTo(original.ComputeFingerprint()));
            Assert.That(restored.CorrectedInputValues, Is.EqualTo(original.CorrectedInputValues));
        }

        [Test]
        public void WorldState_Restore_RefusesImpossibleWorlds()
        {
            var config = SharedConfig();
            var world = new WorldSimulation(WorldFactory.CreateInitial(config));
            Run(world, BrainController.Shared(config, new HeuristicBrain()), 30);

            Assert.That(() => RestoreWith(world, c => c.DeathCause = DeathCause.Starvation), Throws.ArgumentException);
            Assert.That(() => RestoreWith(world, c => c.Energy = config.MaxEnergy + 1f), Throws.ArgumentException);
            Assert.That(() => RestoreWith(world, c => c.Energy = 0f), Throws.ArgumentException);
            Assert.That(() => RestoreWith(world, c => c.Position = new Float2(-0.5f, 1f)), Throws.ArgumentException);
            Assert.That(() => RestoreWith(world, c => c.Velocity = new Float2(float.NaN, 0f)), Throws.ArgumentException);
            Assert.That(() => RestoreWith(world, c => c.AgeTicks = config.MaxAgeTicks), Throws.ArgumentException);
            Assert.That(() => RestoreWith(world, c => c.Heading = 7f), Throws.ArgumentException);

            var saved = world.Capture();
            var swapped = saved.Creatures.Reverse().ToArray();
            Assert.That(() => WorldState.Restore(config, saved.Tick, saved.NextCreatureId, saved.NextFoodId,
                saved.WorldRandom, saved.SpawnRandom, swapped, saved.Food), Throws.ArgumentException, "id order");
            Assert.That(() => WorldState.Restore(config, saved.Tick, 2, saved.NextFoodId, saved.WorldRandom,
                saved.SpawnRandom, saved.Creatures, saved.Food), Throws.ArgumentException, "ids below the counter");
            saved.Food[0].MarkEaten();
            Assert.That(() => WorldSimulation.Restore(config, saved), Throws.ArgumentException, "eaten food");
        }

        [Test]
        public void TickScheduler_Restore_ContinuesTheAgendaAndRefusesBrokenOnes()
        {
            var original = new TickScheduler<int>();
            original.Schedule(5, 50);
            original.Schedule(3, 30);
            original.Schedule(5, 51);
            original.Schedule(9, 90);
            var drained = new System.Collections.Generic.List<ScheduledEvent<int>>();
            original.DrainDue(3, drained);

            var restored = TickScheduler<int>.Restore(original.SnapshotPending(), original.NextSequence,
                original.LastDrainedTick);
            Assert.That(restored.Schedule(5, 52), Is.EqualTo(original.Schedule(5, 52)));
            var fromOriginal = new System.Collections.Generic.List<ScheduledEvent<int>>();
            var fromRestored = new System.Collections.Generic.List<ScheduledEvent<int>>();
            original.DrainDue(9, fromOriginal);
            restored.DrainDue(9, fromRestored);
            Assert.That(fromRestored.Select(e => (e.DueTick, e.Sequence, e.Payload)),
                Is.EqualTo(fromOriginal.Select(e => (e.DueTick, e.Sequence, e.Payload))));

            var a = new ScheduledEvent<int>(5, 0, 0);
            var b = new ScheduledEvent<int>(6, 1, 0);
            Assert.That(() => TickScheduler<int>.Restore(new[] { b, a }, 2, 4), Throws.ArgumentException, "run order");
            Assert.That(() => TickScheduler<int>.Restore(new[] { a, b }, 2, 5), Throws.ArgumentException, "already processed");
            Assert.That(() => TickScheduler<int>.Restore(new[] { a, b }, 1, 4), Throws.ArgumentException, "sequence counter");
            Assert.That(() => TickScheduler<int>.Restore(new[] { a, new ScheduledEvent<int>(6, 0, 0) }, 2, 4),
                Throws.ArgumentException, "repeated sequence");
        }

        [Test]
        public void BrainController_Restore_NeedsTheSameBrainsAndAFreshController()
        {
            var config = SharedConfig();
            var world = new WorldSimulation(WorldFactory.CreateInitial(config));
            var brains = BrainController.Shared(config, new RandomBrain());
            Run(world, brains, 3);
            var saved = brains.Capture();

            Assert.That(saved.Select(s => s.Creature.Value), Is.Ordered.And.Unique);
            Assert.That(() => brains.Restore(saved), Throws.InvalidOperationException, "already deciding");
            Assert.That(() => BrainController.Shared(config, new HeuristicBrain()).Restore(saved), Throws.ArgumentException);
            var late = saved.Select(s => new BrainSlotState(s.Creature, s.BrainName, s.Random, s.Action,
                config.DecisionIntervalTicks)).ToArray();
            Assert.That(() => BrainController.Shared(config, new RandomBrain()).Restore(late), Throws.ArgumentException);
        }

        [TestCase(EvolutionPhase.Training, 0)]
        [TestCase(EvolutionPhase.Baselines, 1)]
        [TestCase(EvolutionPhase.Validation, 1)]
        [TestCase(EvolutionPhase.Training, 2)]
        public void Session_SavedMidEpisode_ResumesExactly(EvolutionPhase phase, int generation)
        {
            var original = new EvolutionSession(Small(21), 2);
            AdvanceUntil(original, () => original.Phase == phase && original.Evolution.Generation >= generation);
            original.Advance(57);
            Assert.That(original.Job.Current.Tick, Is.GreaterThan(0), "saved in the middle of an episode");

            var restored = EvolutionSession.Restore(original.Capture());
            AssertSameSession(restored, original);
            Assert.That(restored.Job.Current.Simulation.ComputeFingerprint(),
                Is.EqualTo(original.Job.Current.Simulation.ComputeFingerprint()));

            AdvanceUntil(original, () => original.Training.Count >= 5, 113);
            AdvanceUntil(restored, () => restored.Training.Count >= 5, 29);
            CatchUp(restored, original);
            AssertSameSession(restored, original);
        }

        [Test]
        public void Session_Restore_RefusesSnapshotsThatDoNotFitTogether()
        {
            var session = new EvolutionSession(Small(4), 2);
            AdvanceUntil(session, () => session.Evolution.Generation == 2);
            var saved = session.Capture();

            Assert.That(() => EvolutionSession.Restore(With(saved, phase: EvolutionPhase.Baselines)),
                Throws.ArgumentException, "baselines already ran");
            Assert.That(() => EvolutionSession.Restore(With(saved, lineage: saved.Lineage.Take(3).ToArray())),
                Throws.ArgumentException, "individuals outside the lineage");
            Assert.That(() => EvolutionSession.Restore(With(saved, population: saved.Population.Skip(1).ToArray())),
                Throws.ArgumentException, "population size");
            Assert.That(() => EvolutionSession.Restore(With(saved, training: saved.Training.Take(1).ToArray())),
                Throws.ArgumentException, "history");
        }

        private static EvolutionSnapshot With(EvolutionSnapshot s, EvolutionPhase? phase = null,
            LineageEntry[] lineage = null, Individual[] population = null, GenerationSummary[] training = null) =>
            new EvolutionSnapshot(s.Settings, s.ValidationInterval, s.Generation, population ?? s.Population.ToArray(),
                lineage ?? s.Lineage.ToArray(), s.LastTrainingIndividuals, s.LastTrainingEpisodes, s.BaselineEpisodes,
                training ?? s.Training.ToArray(), s.Validation, s.TotalTicks, phase ?? s.Phase, s.Job);

        private static void RestoreWith(WorldSimulation world, Action<CreatureState> change)
        {
            var saved = world.Capture();
            change(saved.Creatures[0]);
            WorldSimulation.Restore(world.World.Config, saved);
        }
    }

    /// <summary>Shared by the resume and checkpoint tests.</summary>
    internal static class ResumeChecks
    {
        public static void AssertSameSession(EvolutionSession actual, EvolutionSession expected)
        {
            Assert.That(actual.TotalTicks, Is.EqualTo(expected.TotalTicks));
            Assert.That(actual.Phase, Is.EqualTo(expected.Phase));
            Assert.That(actual.Job.Generation, Is.EqualTo(expected.Job.Generation));
            Assert.That(actual.Job.CompletedEpisodes, Is.EqualTo(expected.Job.CompletedEpisodes));
            Assert.That(actual.Job.Current.Tick, Is.EqualTo(expected.Job.Current.Tick));
            Assert.That(actual.Evolution.Generation, Is.EqualTo(expected.Evolution.Generation));
            Assert.That(actual.Evolution.Lineage.NextId, Is.EqualTo(expected.Evolution.Lineage.NextId));
            Assert.That(actual.Evolution.Individuals.Select(i => i.Id), Is.EqualTo(expected.Evolution.Individuals.Select(i => i.Id)));
            for (var i = 0; i < expected.Evolution.Population.Count; i++)
                Assert.That(actual.Evolution.Population[i].HasSameContent(expected.Evolution.Population[i]), Is.True);
            Assert.That(actual.Evolution.Origins.Select(o => (o.ParentIndex, o.IsElite, o.MutatedWeights)),
                Is.EqualTo(expected.Evolution.Origins.Select(o => (o.ParentIndex, o.IsElite, o.MutatedWeights))));
            Assert.That(actual.Training.Count, Is.EqualTo(expected.Training.Count));
            for (var g = 0; g < expected.Training.Count; g++)
                AssertSameSummary(actual.Training[g], expected.Training[g]);
            Assert.That(actual.Validation.Count, Is.EqualTo(expected.Validation.Count));
            for (var v = 0; v < expected.Validation.Count; v++)
            {
                AssertSameSummary(actual.Validation[v].Validation, expected.Validation[v].Validation);
                Assert.That(actual.Validation[v].ChampionId, Is.EqualTo(expected.Validation[v].ChampionId));
                Assert.That(actual.Validation[v].ChampionFitness, Is.EqualTo(expected.Validation[v].ChampionFitness));
            }

            Assert.That(actual.HeuristicBaseline?.MeanFitness, Is.EqualTo(expected.HeuristicBaseline?.MeanFitness));
            Assert.That(actual.RandomBaseline?.MeanFitness, Is.EqualTo(expected.RandomBaseline?.MeanFitness));
            Assert.That(actual.LastTraining?.Summary.BestIndividualId, Is.EqualTo(expected.LastTraining?.Summary.BestIndividualId));
        }

        public static void AssertSameSummary(GenerationSummary actual, GenerationSummary expected)
        {
            Assert.That(actual.Generation, Is.EqualTo(expected.Generation));
            Assert.That(actual.Mode, Is.EqualTo(expected.Mode));
            Assert.That(Bits(actual.Best), Is.EqualTo(Bits(expected.Best)));
            Assert.That(Bits(actual.Median), Is.EqualTo(Bits(expected.Median)));
            Assert.That(Bits(actual.LowerQuartile), Is.EqualTo(Bits(expected.LowerQuartile)));
            Assert.That(Bits(actual.Worst), Is.EqualTo(Bits(expected.Worst)));
            Assert.That(Bits(actual.MeanFitness), Is.EqualTo(Bits(expected.MeanFitness)));
            Assert.That(actual.BestIndividualId, Is.EqualTo(expected.BestIndividualId));
            Assert.That(Bits(actual.WeightStdDev), Is.EqualTo(Bits(expected.WeightStdDev)));
            Assert.That(actual.NewIndividuals, Is.EqualTo(expected.NewIndividuals));
            Assert.That(actual.DistinctParents, Is.EqualTo(expected.DistinctParents));
        }

        public static void Run(WorldSimulation world, BrainController brains, int ticks)
        {
            for (var i = 0; i < ticks; i++)
            {
                world.Step(brains);
                brains.Forget(world.LastDeaths);
            }
        }

        public static void AdvanceUntil(EvolutionSession session, Func<bool> condition, long chunk = 50)
        {
            for (var i = 0; i < 1000000 && !condition(); i++)
                session.Advance(chunk);
            Assert.That(condition(), Is.True);
        }

        // Advances whichever session is behind to the other's tick count; Advance runs exactly the ticks asked for.
        public static void CatchUp(EvolutionSession a, EvolutionSession b)
        {
            if (a.TotalTicks < b.TotalTicks)
                a.Advance(b.TotalTicks - a.TotalTicks);
            else
                b.Advance(a.TotalTicks - b.TotalTicks);
            Assert.That(a.TotalTicks, Is.EqualTo(b.TotalTicks));
        }

        public static int Bits(float value) => BitConverter.SingleToInt32Bits(value);

        // Short lives and quick respawns, so deaths and pending respawns happen within a few hundred ticks.
        public static SimulationSettings SharedSettings() =>
            new SimulationSettings
            {
                Seed = 5, InitialPopulation = 24, InitialFood = 40, MaxAgeSeconds = 20f, FoodRespawnSeconds = 2f
            };

        public static SimulationConfig SharedConfig() => new SimulationConfig(SharedSettings());

        public static EvolutionSettings Small(ulong seed)
        {
            var settings = new EvolutionSettings
            {
                ExperimentSeed = seed, EpisodeSeconds = 10f, TrainingSeedsPerGeneration = 2, ValidationSeedCount = 3,
                PopulationSize = 6, EliteCount = 2
            };
            settings.World.InitialFood = 64;
            return settings;
        }
    }
}
