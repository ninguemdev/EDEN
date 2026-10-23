using System.Linq;
using Eden.Domain;
using NUnit.Framework;

namespace Eden.Tests.EditMode
{
    public class SeedDerivationTests
    {
        // Outputs of the reference next() in https://prng.di.unimi.it/splitmix.c for the given seeds.
        [TestCase(0UL, 0xE220A8397B1DCDAFUL, 0x6E789E6AA1B965F4UL, 0x06C45D188009454FUL)]
        [TestCase(1234567UL, 0x599ED017FB08FC85UL, 0x2C73F08458540FA5UL, 0x883EBCE5A3F27C77UL)]
        public void SplitMix64_MatchesReferenceGenerator(ulong seed, ulong first, ulong second, ulong third)
        {
            Assert.That(SplitMix64.Output(seed, 1), Is.EqualTo(first));
            Assert.That(SplitMix64.Output(seed, 2), Is.EqualTo(second));
            Assert.That(SplitMix64.Output(seed, 3), Is.EqualTo(third));
        }

        [Test]
        public void CreateStream_FollowsDocumentedScheme()
        {
            const ulong seed = 98765;
            var expected = new Pcg32(SplitMix64.Output(seed, (ulong)RandomStream.Spawn), (ulong)RandomStream.Spawn);
            Assert.That(SeedDerivation.CreateStream(seed, RandomStream.Spawn).State, Is.EqualTo(expected.State));
        }

        [Test]
        public void SameSeedAndStream_AreReproducible()
        {
            var first = SeedDerivation.CreateStream(123, RandomStream.World);
            var second = SeedDerivation.CreateStream(123, RandomStream.World);
            for (var i = 0; i < 100; i++)
                Assert.That(second.NextUInt(), Is.EqualTo(first.NextUInt()));
        }

        [Test]
        public void StreamsAndSeeds_AreDistinct()
        {
            var streams = new[] { RandomStream.World, RandomStream.Spawn, RandomStream.Mutation, RandomStream.Presentation };
            var firstDraws = streams.Select(stream => FirstDraws(123, stream)).ToList();
            for (var i = 0; i < firstDraws.Count; i++)
            for (var j = i + 1; j < firstDraws.Count; j++)
                Assert.That(firstDraws[i], Is.Not.EqualTo(firstDraws[j]), $"{streams[i]} vs {streams[j]}");

            Assert.That(FirstDraws(124, RandomStream.World), Is.Not.EqualTo(FirstDraws(123, RandomStream.World)));
        }

        private static uint[] FirstDraws(ulong seed, RandomStream stream)
        {
            var random = SeedDerivation.CreateStream(seed, stream);
            return Enumerable.Range(0, 8).Select(_ => random.NextUInt()).ToArray();
        }
    }
}
