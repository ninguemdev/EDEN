using System;
using Eden.Domain;
using Eden.Evolution;
using NUnit.Framework;

namespace Eden.Tests.EditMode
{
    /// <summary>Historical markings: shared within a generation, new across generations, never reused.</summary>
    public class InnovationHistoryTests
    {
        private static readonly NeuralContract Contract = NeuralContract.Legacy;
        private static readonly int FirstOutput = NeatNetwork.OutputNodeId(Contract, 0);
        private static readonly int FirstHidden = NeatNetwork.FirstHiddenNodeId(Contract);

        [Test]
        public void NewHistory_StartsAfterTheMinimalNetwork()
        {
            var history = new InnovationHistory(Contract);
            Assert.That(history.Generation, Is.EqualTo(0));
            Assert.That(history.NextInnovation, Is.EqualTo(39));
            Assert.That(history.NextNodeId, Is.EqualTo(FirstHidden));
        }

        [Test]
        public void SameStructureInOneGeneration_GetsTheSameNumbers()
        {
            var history = new InnovationHistory(Contract);
            var split = history.Split(4);
            Assert.That(split.NodeId, Is.EqualTo(FirstHidden));
            Assert.That(split.InInnovation, Is.EqualTo(39));
            Assert.That(split.OutInnovation, Is.EqualTo(40));

            var again = history.Split(4);
            Assert.That(again.NodeId, Is.EqualTo(split.NodeId), "another genome splitting the same connection");
            Assert.That(again.InInnovation, Is.EqualTo(split.InInnovation));
            Assert.That(again.OutInnovation, Is.EqualTo(split.OutInnovation));

            var connection = history.ConnectionInnovation(2, split.NodeId);
            Assert.That(connection, Is.EqualTo(41));
            Assert.That(history.ConnectionInnovation(2, split.NodeId), Is.EqualTo(41));
            Assert.That(history.ConnectionInnovation(3, split.NodeId), Is.EqualTo(42), "a different pair is new");
            Assert.That(history.Split(5).NodeId, Is.EqualTo(FirstHidden + 1), "a different connection is new");
        }

        [Test]
        public void MinimalConnections_KeepTheirFixedNumbers()
        {
            var history = new InnovationHistory(Contract);
            Assert.That(history.ConnectionInnovation(0, FirstOutput), Is.EqualTo(0));
            Assert.That(history.ConnectionInnovation(12, FirstOutput + 2), Is.EqualTo(38));
            Assert.That(history.NextInnovation, Is.EqualTo(39), "nothing was taken");
        }

        [Test]
        public void ANewGeneration_GivesRediscoveredStructuresNewNumbers()
        {
            var history = new InnovationHistory(Contract);
            var first = history.Split(7);
            var connection = history.ConnectionInnovation(1, first.NodeId);

            history.BeginGeneration(1);
            var later = history.Split(7);
            Assert.That(later.NodeId, Is.Not.EqualTo(first.NodeId));
            Assert.That(later.InInnovation, Is.GreaterThan(connection));
            Assert.That(history.ConnectionInnovation(1, first.NodeId), Is.Not.EqualTo(connection));
            Assert.That(() => history.BeginGeneration(1), Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void CaptureAndRestore_ContinueTheSameNumbering()
        {
            var history = new InnovationHistory(Contract);
            history.Split(3);
            history.BeginGeneration(2);
            history.ConnectionInnovation(1, FirstHidden);

            var restored = InnovationHistory.Restore(history.Capture());
            Assert.That(restored.Generation, Is.EqualTo(2));
            Assert.That(restored.NextInnovation, Is.EqualTo(history.NextInnovation));
            Assert.That(restored.NextNodeId, Is.EqualTo(history.NextNodeId));
            restored.BeginGeneration(3);
            history.BeginGeneration(3);
            Assert.That(restored.Split(9).NodeId, Is.EqualTo(history.Split(9).NodeId));
            Assert.That(restored.ConnectionInnovation(2, FirstHidden), Is.EqualTo(history.ConnectionInnovation(2, FirstHidden)));

            Assert.That(() => InnovationHistory.Restore(new InnovationHistoryState("unknown", 0, 39, FirstHidden)),
                Throws.ArgumentException);
            Assert.That(() => InnovationHistory.Restore(new InnovationHistoryState(Contract.Id, 0, 38, FirstHidden)),
                Throws.ArgumentException, "inside the minimal network");
            Assert.That(() => InnovationHistory.Restore(new InnovationHistoryState(Contract.Id, 0, 39, FirstHidden - 1)),
                Throws.ArgumentException, "a contract node id");
            Assert.That(() => InnovationHistory.Restore(new InnovationHistoryState(Contract.Id, -1, 39, FirstHidden)),
                Throws.ArgumentException);
        }
    }
}
