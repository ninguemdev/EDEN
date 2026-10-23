using System;
using Eden.Domain;
using NUnit.Framework;

namespace Eden.Tests.EditMode
{
    public class GaussianSamplerTests
    {
        [Test]
        public void Samples_HaveStandardNormalMomentsAndStayFinite()
        {
            var sampler = new GaussianSampler(new Pcg32(2026, 3));
            const int count = 200000;
            double sum = 0, sumSquares = 0, maxAbs = 0;
            var withinOneSigma = 0;
            for (var i = 0; i < count; i++)
            {
                var z = sampler.NextStandard();
                Assert.That(double.IsNaN(z) || double.IsInfinity(z), Is.False);
                sum += z;
                sumSquares += z * z;
                maxAbs = Math.Max(maxAbs, Math.Abs(z));
                if (Math.Abs(z) <= 1.0)
                    withinOneSigma++;
            }

            var mean = sum / count;
            var variance = sumSquares / count - mean * mean;
            Assert.That(mean, Is.EqualTo(0.0).Within(0.01));
            Assert.That(Math.Sqrt(variance), Is.EqualTo(1.0).Within(0.01));
            Assert.That((double)withinOneSigma / count, Is.EqualTo(0.6827).Within(0.005));
            Assert.That(maxAbs, Is.LessThanOrEqualTo(6.67), "u1 >= 2^-32 bounds the radius");
        }

        [Test]
        public void SecondSample_ComesFromTheCacheWithoutDrawing()
        {
            var sampler = new GaussianSampler(new Pcg32(5, 3));
            sampler.NextStandard();
            var afterFirst = sampler.Source.State;
            Assert.That(sampler.State.HasSpare, Is.True);

            sampler.NextStandard();

            Assert.That(sampler.Source.State, Is.EqualTo(afterFirst));
            Assert.That(sampler.State.HasSpare, Is.False);
        }

        [Test]
        public void State_RoundTripsIncludingThePendingSpare()
        {
            var original = new GaussianSampler(new Pcg32(77, 3));
            original.NextStandard();
            original.Source.NextFloat();
            original.NextStandard();
            original.NextStandard();
            Assert.That(original.State.HasSpare, Is.True, "an odd number of samples leaves a spare");

            var restored = GaussianSampler.FromState(original.State);

            Assert.That(restored.State, Is.EqualTo(original.State));
            for (var i = 0; i < 9; i++)
            {
                Assert.That(restored.NextStandard(), Is.EqualTo(original.NextStandard()));
                Assert.That(restored.Source.NextUInt(), Is.EqualTo(original.Source.NextUInt()));
            }
        }

        [Test]
        public void SameSeed_GivesSameSequence()
        {
            var a = new GaussianSampler(SeedDerivation.CreateIndexedStream(9, RandomStream.Mutation, 4));
            var b = new GaussianSampler(SeedDerivation.CreateIndexedStream(9, RandomStream.Mutation, 4));
            var c = new GaussianSampler(SeedDerivation.CreateIndexedStream(9, RandomStream.Mutation, 5));
            var differs = false;
            for (var i = 0; i < 16; i++)
            {
                var value = a.NextStandard();
                Assert.That(b.NextStandard(), Is.EqualTo(value));
                differs |= c.NextStandard() != value;
            }

            Assert.That(differs, Is.True);
        }

        [Test]
        public void IndexedStream_UsesTheCreatureStreamDerivation()
        {
            Assert.That(SeedDerivation.CreateIndexedStream(31, RandomStream.Brain, 7).State,
                Is.EqualTo(SeedDerivation.CreateCreatureStream(31, RandomStream.Brain, new CreatureId(7)).State));
            var streamSeed = SplitMix64.Output(31, (ulong)RandomStream.Mutation);
            Assert.That(SeedDerivation.CreateIndexedStream(31, RandomStream.Mutation, 2).State,
                Is.EqualTo(new Pcg32(SplitMix64.Output(streamSeed, 2), (ulong)RandomStream.Mutation).State));
        }
    }
}
