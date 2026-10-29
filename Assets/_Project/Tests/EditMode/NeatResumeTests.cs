using System;
using System.Linq;
using Eden.Application;
using Eden.Domain;
using Eden.Evolution;
using Eden.Persistence;
using NUnit.Framework;
using static Eden.Tests.EditMode.ResumeChecks;

namespace Eden.Tests.EditMode
{
    /// <summary>NEAT checkpoints resume exactly; imported NEAT pools are renumbered and seed only NEAT evolutions.</summary>
    public class NeatResumeTests
    {
        private static readonly DateTime SavedAt = new DateTime(2026, 10, 27, 12, 0, 0, DateTimeKind.Utc);

        [TestCase(EvolutionPhase.Training, 0)]
        [TestCase(EvolutionPhase.Baselines, 1)]
        [TestCase(EvolutionPhase.Validation, 1)]
        [TestCase(EvolutionPhase.Training, 2)]
        public void NeatSession_SavedToAFileMidEpisode_ResumesExactly(EvolutionPhase phase, int generation)
        {
            var original = new EvolutionSession(Neat(41), 2);
            AdvanceUntil(original, () => original.Phase == phase && original.Evolution.Generation >= generation);
            original.Advance(57);
            Assert.That(original.Job.Current.Tick, Is.GreaterThan(0), "saved in the middle of an episode");

            var text = CheckpointCodec.Encode(new Checkpoint("evolution", "minimal", 41, "neural", original.Capture(), null),
                "0.1.0", "test", SavedAt);
            Assert.That(text, Does.Contain("\"schemaVersion\": 6,").And.Contain("\"algorithm\": \"neat\""));
            var restored = EvolutionSession.Restore(CheckpointCodec.Decode(text, out var info).Evolution);
            Assert.That(info.SchemaVersion, Is.EqualTo(6));
            AssertSameSession(restored, original);
            AssertSameNeat(restored, original);

            AdvanceUntil(original, () => original.Training.Count >= 5, 113);
            AdvanceUntil(restored, () => restored.Training.Count >= 5, 29);
            CatchUp(restored, original);
            AssertSameSession(restored, original);
            AssertSameNeat(restored, original);
        }

        [Test]
        public void MlpCheckpoints_StaySchema5_WithoutNeatFields()
        {
            var session = new EvolutionSession(Small(42), 2);
            AdvanceUntil(session, () => session.Evolution.Generation == 1);
            var text = CheckpointCodec.Encode(new Checkpoint("evolution", "minimal", 42, "neural", session.Capture(), null),
                "0.1.0", "test", SavedAt);
            Assert.That(text, Does.Contain("\"schemaVersion\": 5,"));
            Assert.That(text, Does.Not.Contain("\"neat\"").And.Not.Contain("\"algorithm\"").And.Not.Contain("\"network\""));
        }

        [Test]
        public void Restore_RefusesNeatStateThatDoesNotFit()
        {
            var session = new EvolutionSession(Neat(43), 2);
            AdvanceUntil(session, () => session.Evolution.Generation == 2);
            var saved = session.Capture();
            var neat = saved.Neat;

            Assert.That(() => EvolutionSession.Restore(With(saved, null)), Throws.ArgumentException, "NEAT state missing");
            var behind = new NeatEvolutionState(new InnovationHistoryState(neat.Innovations.Contract,
                neat.Innovations.Generation, NeatNetwork.MinimalConnectionCount(NeuralContract.Legacy),
                neat.Innovations.NextNodeId), neat.Species, neat.NextSpeciesId, neat.LastTrainingSpecies);
            if (saved.Population.Any(i => i.Genome.Neat.Connections.Count > 39))
                Assert.That(() => EvolutionSession.Restore(With(saved, behind)), Throws.ArgumentException,
                    "numbers the history would hand out again");
            var otherGeneration = new NeatEvolutionState(new InnovationHistoryState(neat.Innovations.Contract, 7,
                neat.Innovations.NextInnovation, neat.Innovations.NextNodeId), neat.Species, neat.NextSpeciesId,
                neat.LastTrainingSpecies);
            Assert.That(() => EvolutionSession.Restore(With(saved, otherGeneration)), Throws.ArgumentException);
            var noSpecies = new NeatEvolutionState(neat.Innovations, neat.Species, neat.NextSpeciesId, null);
            Assert.That(() => EvolutionSession.Restore(With(saved, noSpecies)), Throws.ArgumentException);

            var mlp = new EvolutionSession(Small(44), 2);
            AdvanceUntil(mlp, () => mlp.Evolution.Generation == 1);
            Assert.That(() => EvolutionSession.Restore(With(mlp.Capture(), neat)), Throws.ArgumentException,
                "an MLP evolution has no NEAT state");
        }

        [Test]
        public void Renumber_KeepsTheMinimalNetworkAndGivesEverythingElseNewNumbers()
        {
            var contract = NeuralContract.Legacy;
            var hidden = NeatNetwork.FirstHiddenNodeId(contract);
            var output = NeatNetwork.OutputNodeId(contract, 0);
            // Two genomes from "another experiment" whose hidden node and innovations carry the same numbers.
            var a = new Genome(new NeatNetwork(contract, new[]
            {
                new NeatConnection(3, 1, output, 0.5f, false),
                new NeatConnection(50, 1, hidden, 1f, true),
                new NeatConnection(51, hidden, output, 0.5f, true)
            }));
            var b = new Genome(new NeatNetwork(contract, new[]
            {
                new NeatConnection(6, 2, output, 0.25f, false),
                new NeatConnection(50, 2, hidden, 1f, true),
                new NeatConnection(51, hidden, output, 0.25f, true)
            }), new BodyGenes(0.5f, 0f, 0f, 0f));

            var history = new InnovationHistory(contract);
            var renumbered = NeatSeeding.Renumber(new[] { a, b }, history);

            var ra = renumbered[0].Neat;
            var rb = renumbered[1].Neat;
            Assert.That(ra.Connections[0].Innovation, Is.EqualTo(3), "minimal connection keeps its fixed number");
            Assert.That(rb.Connections[0].Innovation, Is.EqualTo(6));
            Assert.That(ra.HiddenNodes.Single(), Is.Not.EqualTo(rb.HiddenNodes.Single()), "no homology by equal numbers");
            Assert.That(ra.Connections.Skip(1).Select(c => c.Innovation)
                .Intersect(rb.Connections.Skip(1).Select(c => c.Innovation)), Is.Empty);
            Assert.That(renumbered[1].Body, Is.EqualTo(b.Body));
            Assert.That(history.NextNodeId, Is.EqualTo(hidden + 2));
            Assert.That(history.NextInnovation, Is.EqualTo(39 + 4));
        }

        [Test]
        public void NeatPool_SeedsOnlyANeatEvolution()
        {
            var source = new GenerationalEvolution(Neat(45));
            for (var g = 0; g < 3; g++)
                source.Advance();
            var pool = source.Population.Take(4).ToArray();
            var fitness = new[] { 1f, 4f, 2f, 3f };

            var seeded = GenerationalEvolution.FromSeedPool(Neat(46), pool, fitness);
            Assert.That(seeded.Population.Count, Is.EqualTo(6));
            Assert.That(seeded.Population.All(g => g.Network == NeatNetwork.Kind), Is.True);
            Assert.That(seeded.Population[0].Neat.Connections.Count, Is.EqualTo(pool[1].Neat.Connections.Count),
                "the best of the pool first");
            seeded.Advance();
            Assert.That(seeded.Generation, Is.EqualTo(1));

            var again = GenerationalEvolution.FromSeedPool(Neat(46), pool, fitness);
            for (var i = 0; i < 6; i++)
                Assert.That(again.Population[i].HasSameContent(GenerationalEvolution.FromSeedPool(Neat(46), pool, fitness)
                    .Population[i]), Is.True, "reproducible");

            Assert.That(() => GenerationalEvolution.FromSeedPool(Small(47), pool, fitness),
                Throws.ArgumentException.With.Message.Contains("neat genome"), "an MLP evolution refuses NEAT genomes");
        }

        private static void AssertSameNeat(EvolutionSession actual, EvolutionSession expected)
        {
            var a = actual.Evolution;
            var e = expected.Evolution;
            Assert.That(a.Innovations.Generation, Is.EqualTo(e.Innovations.Generation));
            Assert.That(a.Innovations.NextInnovation, Is.EqualTo(e.Innovations.NextInnovation));
            Assert.That(a.Innovations.NextNodeId, Is.EqualTo(e.Innovations.NextNodeId));
            Assert.That(a.Speciation.Species.Select(s => (s.Id, s.BestFitness, s.LastImprovedGeneration)),
                Is.EqualTo(e.Speciation.Species.Select(s => (s.Id, s.BestFitness, s.LastImprovedGeneration))));
            Assert.That(a.Speciation.NextSpeciesId, Is.EqualTo(e.Speciation.NextSpeciesId));
            Assert.That(a.Origins.Select(o => o.SecondParentIndex), Is.EqualTo(e.Origins.Select(o => o.SecondParentIndex)));
            Assert.That(actual.LastTraining?.Neat?.Species, Is.EqualTo(expected.LastTraining?.Neat?.Species));
        }

        private static EvolutionSnapshot With(EvolutionSnapshot s, NeatEvolutionState neat) =>
            new EvolutionSnapshot(s.Settings, s.ValidationInterval, s.Generation, s.Population, s.Lineage,
                s.LastTrainingIndividuals, s.LastTrainingEpisodes, s.BaselineEpisodes, s.Training, s.Validation,
                s.TotalTicks, s.Phase, s.Job, neat);

        private static EvolutionSettings Neat(ulong seed)
        {
            var settings = Small(seed);
            settings.Algorithm = EvolutionAlgorithm.Neat;
            settings.Neat.AddNodeProbability = 0.3f;
            settings.Neat.AddConnectionProbability = 0.3f;
            return settings;
        }
    }
}
