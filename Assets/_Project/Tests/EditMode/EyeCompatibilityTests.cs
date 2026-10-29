using System;
using Eden.Domain;
using Eden.Evolution;
using NUnit.Framework;

namespace Eden.Tests.EditMode
{
    /// <summary>The optional eye term of the compatibility distance: off by default, by slot, bounded.</summary>
    public class EyeCompatibilityTests
    {
        private static readonly NeuralContract Contract = NeuralContract.Eyes;

        [Test]
        public void EyeDifference_ComparesSlotsByIdentity()
        {
            Assert.That(NeatCrossover.EyeDifference(EyeSet.Founder, EyeSet.Founder), Is.Zero);
            Assert.That(NeatCrossover.EyeDifference(EyeSet.Founder, new EyeSet(Eye.Frontal, Eye.Frontal)), Is.EqualTo(1f),
                "a slot only one set fills");
            Assert.That(NeatCrossover.EyeDifference(EyeSet.Founder, new EyeSet(null, Eye.Frontal)), Is.EqualTo(2f),
                "the same eye in another slot is another eye");

            var turned = new EyeSet(new Eye(MathF.PI, Eye.FounderAperture, 1f));
            Assert.That(NeatCrossover.EyeDifference(EyeSet.Founder, turned), Is.EqualTo(1f / 3f).Within(1e-6f),
                "half a turn is the largest orientation difference");
            var nearlyAround = new EyeSet(new Eye(-MathF.PI + 0.1f, Eye.FounderAperture, 1f));
            var otherSide = new EyeSet(new Eye(MathF.PI - 0.1f, Eye.FounderAperture, 1f));
            Assert.That(NeatCrossover.EyeDifference(nearlyAround, otherSide), Is.EqualTo(0.2f / MathF.PI / 3f).Within(1e-5f),
                "the shorter way around");
            var extreme = new EyeSet(new Eye(MathF.PI, Eye.MaxAperture, Eye.MaxRange));
            var opposite = new EyeSet(new Eye(0f, Eye.MinAperture, Eye.MinRange));
            Assert.That(NeatCrossover.EyeDifference(extreme, opposite), Is.EqualTo(1f).Within(1e-6f), "bounded by 1 per slot");
        }

        [Test]
        public void Distance_AddsTheEyeTermOnlyWhenItsCoefficientIsSet()
        {
            var network = NeatNetwork.CreateMinimal(Contract, new Pcg32(1, 1), 1f);
            var one = new Genome(network, BodyGenes.Neutral, EyeSet.Founder);
            var two = new Genome(network, BodyGenes.Neutral, new EyeSet(Eye.Frontal, Eye.Frontal));

            var neural = new NeatRules(new NeatSettings());
            Assert.That(NeatCrossover.Distance(one, two, neural), Is.Zero, "c4 = 0: purely neural, as in NEAT");

            var withEyes = new NeatRules(new NeatSettings { EyeCompatibilityCoefficient = 2f });
            Assert.That(NeatCrossover.Distance(one, two, withEyes), Is.EqualTo(2f));
            Assert.That(() => new NeatRules(new NeatSettings { EyeCompatibilityCoefficient = -1f }), Throws.ArgumentException);

            var speciation = new NeatSpeciation(new NeatRules(new NeatSettings
            {
                EyeCompatibilityCoefficient = 4f, CompatibilityThreshold = 3f
            }));
            Assert.That(speciation.Assign(new[] { one, two, one }, 0), Is.EqualTo(new[] { 1, 2, 1 }),
                "an extra eye can found a species of its own");
            Assert.That(new NeatSpeciation(neural).Assign(new[] { one, two, one }, 0), Is.EqualTo(new[] { 1, 1, 1 }));
        }
    }
}
