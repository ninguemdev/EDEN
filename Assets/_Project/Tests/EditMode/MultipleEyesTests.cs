using System;
using System.Linq;
using Eden.Application;
using Eden.Domain;
using Eden.Evolution;
using Eden.Simulation;
using NUnit.Framework;
using static Eden.Tests.EditMode.ResumeChecks;

namespace Eden.Tests.EditMode
{
    /// <summary>Several eyes: added by duplication, removed but never the last, crossed by slot, channels kept apart.</summary>
    public class MultipleEyesTests
    {
        private static readonly Eye Left = new Eye(MathF.PI / 2f, Eye.MinAperture, 1f);
        private static readonly Eye Right = new Eye(-MathF.PI / 2f, Eye.MaxAperture, Eye.MaxRange);

        [Test]
        public void AddingAnEye_DuplicatesOneIntoTheLowestFreeSlot_CloseToIt()
        {
            var add = new EyeMutation(0f, 0.05f, 0.05f, 0.05f, 1f, 0f);
            var two = Reproduction.MutateEyes(new EyeSet(null, Left), add, Random(1));
            Assert.That(two.Count, Is.EqualTo(2));
            Assert.That(two[1], Is.EqualTo(Left), "the original keeps its slot and traits");
            Assert.That(two.Has(0), Is.True, "the copy takes the lowest free slot");
            Assert.That(Math.Abs(two[0].Orientation - Left.Orientation), Is.LessThan(0.3f), "and starts close to it");
            Assert.That(Math.Abs(two[0].Range - Left.Range), Is.LessThan(0.3f));

            var full = new EyeSet(Eye.Frontal, Left, Right, Eye.Frontal);
            Assert.That(Reproduction.MutateEyes(full, add, Random(2)), Is.EqualTo(full), "no free slot");
        }

        [Test]
        public void RemovingAnEye_KeepsTheOthersInTheirSlots_AndNeverTheLast()
        {
            var remove = new EyeMutation(0f, 0.1f, 0.1f, 0.1f, 0f, 1f);
            var three = new EyeSet(Eye.Frontal, null, Left, Right);
            for (ulong seed = 1; seed <= 20; seed++)
            {
                var two = Reproduction.MutateEyes(three, remove, Random(seed));
                Assert.That(two.Count, Is.EqualTo(2));
                for (var slot = 0; slot < EyeSet.MaxEyes; slot++)
                {
                    if (two.Has(slot))
                        Assert.That(two[slot], Is.EqualTo(three[slot]), $"slot {slot} is the same eye");
                }
            }

            Assert.That(Reproduction.MutateEyes(EyeSet.Founder, remove, Random(3)), Is.EqualTo(EyeSet.Founder),
                "a genome never loses its last eye");
        }

        [Test]
        public void AnEyesChannels_DoNotDependOnTheOtherSlots()
        {
            // The same left eye in slot 2, with and without a frontal eye in slot 0: its channels read the same.
            float[] Inputs(EyeSet eyes)
            {
                var world = TestWorlds.Empty();
                var creature = world.AddCreature(new Float2(25f, 25f), 0f, 50f);
                creature.Eyes = eyes;
                world.AddFood(new Float2(25f, 29f), 10f);
                world.AddFood(new Float2(29f, 25f), 10f);
                var buffer = new float[NeuralContract.Eyes.InputCount];
                Sensors.WithEyes(Sensors.Observe(world, creature), world, creature, buffer);
                return buffer;
            }

            var with = Inputs(new EyeSet(Eye.Frontal, null, Left));
            var without = Inputs(new EyeSet(null, null, Left));
            var slot2 = NeuralContract.EyeInputIndex(2, EyeChannel.Present);
            Assert.That(with.Skip(slot2).Take(EyeChannels.Count), Is.EqualTo(without.Skip(slot2).Take(EyeChannels.Count)));
            Assert.That(with[NeuralContract.EyeInputIndex(2, EyeChannel.FoodVisible)], Is.EqualTo(1f), "the left eye sees left");
            Assert.That(with[NeuralContract.EyeInputIndex(0, EyeChannel.FoodVisible)], Is.EqualTo(1f));
            Assert.That(without[NeuralContract.EyeInputIndex(0, EyeChannel.Present)], Is.Zero);
        }

        [Test]
        public void Crossover_AlignsEyesBySlot_NeverByAngle()
        {
            var fitter = new EyeSet(Left, null, Right);
            var other = new EyeSet(Right, Eye.Frontal, null, Left);
            var fromOther = 0;
            for (ulong seed = 1; seed <= 40; seed++)
            {
                var child = Reproduction.CrossEyes(fitter, other, new Pcg32(seed, 5));
                Assert.That(child.Has(1) || child.Has(3), Is.False, "eyes only the other parent has are not inherited");
                Assert.That(child[2], Is.EqualTo(Right), "slot 2 only the fitter has");
                Assert.That(child[0].Equals(Left) || child[0].Equals(Right), Is.True, "slot 0 from either parent's slot 0");
                if (child[0].Equals(Right))
                    fromOther++;
            }

            Assert.That(fromOther, Is.InRange(5, 35));
            Assert.That(Reproduction.CrossEyes(null, null, new Pcg32(1, 1)), Is.Null, "no eyes, no draws");
        }

        [Test]
        public void EvolvingEyes_GrowAndLoseEyesWithinLimits_AndPayForEach()
        {
            var settings = Small(71);
            settings.Algorithm = EvolutionAlgorithm.Neat;
            settings.Senses = Senses.Eyes;
            settings.EvolvingEyes = true;
            settings.Neat.EyeAddProbability = 0.6f;
            settings.Neat.EyeRemoveProbability = 0.2f;
            var evolution = new GenerationalEvolution(settings);
            GenerationReport report = null;
            for (var g = 0; g < 4; g++)
                report = evolution.Advance();

            Assert.That(evolution.Population.All(g => g.Eyes.Count >= 1 && g.Eyes.Count <= EyeSet.MaxEyes), Is.True);
            Assert.That(evolution.Population.Any(g => g.Eyes.Count > 1), Is.True, "eyes were added");
            Assert.That(report.Neat.MeanEyes, Is.GreaterThan(1f));

            var body = TestWorlds.Empty().ReferenceBody;
            var many = evolution.Population.OrderByDescending(g => g.Eyes.Count).First();
            var each = Enumerable.Range(0, EyeSet.MaxEyes).Where(many.Eyes.Has).Sum(s => EyeCost.PerSecond(body, many.Eyes[s]));
            Assert.That(EyeCost.PerSecond(body, many.Eyes), Is.EqualTo(each).Within(1e-5f), "every eye pays");
        }

        private static GaussianSampler Random(ulong seed) => new GaussianSampler(new Pcg32(seed, 3));
    }
}
