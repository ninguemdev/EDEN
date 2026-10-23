using System;
using System.Linq;
using System.Text;
using Eden.Domain;
using NUnit.Framework;

namespace Eden.Tests.EditMode
{
    public class Pcg32Tests
    {
        // Round 1 of pcg-c test-high/expected/check-pcg32.out, seeded with pcg32_srandom_r(42, 54).
        private static readonly uint[] ReferenceOutputs =
            { 0xa15c02b7, 0x7b47f409, 0xba1d3330, 0x83d2f293, 0xbfa4784b, 0xcbed606e };
        private const string ReferenceCoins = "HHTTTHTHHHTHTTTHHHHHTTTHHHTHTHTHTTHTTTHHHHHHTTTTHHTTTTTHTTTTTTTHT";
        private const string ReferenceRolls = "3 4 1 1 2 2 3 2 4 3 2 4 3 3 5 2 3 1 3 1 5 1 4 1 5 6 4 6 6 2 6 3 3";

        [Test]
        public void Seed42Sequence54_MatchesPcgReferenceOutputs()
        {
            var random = new Pcg32(42, 54);
            var outputs = Enumerable.Range(0, ReferenceOutputs.Length).Select(_ => random.NextUInt()).ToArray();
            Assert.That(outputs, Is.EqualTo(ReferenceOutputs));
        }

        [Test]
        public void BoundedDraws_MatchPcgReferenceCoinsAndRolls()
        {
            var random = new Pcg32(42, 54);
            for (var i = 0; i < ReferenceOutputs.Length; i++)
                random.NextUInt();

            var coins = new StringBuilder();
            for (var i = 0; i < ReferenceCoins.Length; i++)
                coins.Append(random.NextUInt(2) == 1 ? 'H' : 'T');
            var rolls = string.Join(" ", Enumerable.Range(0, 33).Select(_ => random.NextUInt(6) + 1));

            Assert.That(coins.ToString(), Is.EqualTo(ReferenceCoins));
            Assert.That(rolls, Is.EqualTo(ReferenceRolls));
        }

        [Test]
        public void RestoredState_ContinuesTheSameSequence()
        {
            var original = new Pcg32(2026, 7);
            for (var i = 0; i < 100; i++)
                original.NextUInt();

            var restored = Pcg32.FromState(original.State);

            for (var i = 0; i < 1000; i++)
                Assert.That(restored.NextUInt(), Is.EqualTo(original.NextUInt()), $"draw {i}");
            Assert.That(restored.State, Is.EqualTo(original.State));
        }

        [Test]
        public void DifferentSequences_ProduceDifferentStreams()
        {
            var first = new Pcg32(42, 1);
            var second = new Pcg32(42, 2);
            var firstValues = Enumerable.Range(0, 8).Select(_ => first.NextUInt()).ToArray();
            var secondValues = Enumerable.Range(0, 8).Select(_ => second.NextUInt()).ToArray();
            Assert.That(firstValues, Is.Not.EqualTo(secondValues));
        }

        [Test]
        public void NextFloat_StaysInsideUnitInterval()
        {
            var random = new Pcg32(1, 1);
            for (var i = 0; i < 100000; i++)
            {
                var value = random.NextFloat();
                Assert.That(value, Is.GreaterThanOrEqualTo(0f).And.LessThan(1f));
            }
        }

        [Test]
        public void InvalidArguments_AreRejected()
        {
            var random = new Pcg32(1, 1);
            Assert.Throws<ArgumentOutOfRangeException>(() => random.NextUInt(0));
            Assert.Throws<ArgumentException>(() => random.NextFloat(2f, 1f));
            Assert.Throws<ArgumentException>(() => random.NextFloat(float.NaN, 1f));
            Assert.Throws<ArgumentException>(() => new Pcg32State(1, 2));
        }
    }
}
