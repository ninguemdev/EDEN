using System;
using System.Linq;
using Eden.Domain;
using Eden.Simulation;
using NUnit.Framework;

namespace Eden.Tests.EditMode
{
    /// <summary>Eyes: cone and range borders, absent eyes, ties, walls as barriers seen only inside the sector.</summary>
    public class VisionTests
    {
        private const float Tolerance = 1e-4f;

        // Default rules: arena 50 × 50, sensor range 10, body (eating) radius 0.75. Facing +x from (25, 25), the
        // founder's frontal eye sits at (25.75, 25) and sees 60° to each side, 10 units far.
        private static readonly Float2 Eye0 = new Float2(25.75f, 25f);

        [Test]
        public void Food_IsSeenOnlyInsideTheConeAndRange()
        {
            Assert.That(SeeFood(new Float2(30f, 25f))[(int)EyeChannel.FoodVisible], Is.EqualTo(1f), "ahead");
            var ahead = SeeFood(new Float2(30f, 25f));
            Assert.That(ahead[(int)EyeChannel.FoodCos], Is.EqualTo(1f).Within(Tolerance));
            Assert.That(ahead[(int)EyeChannel.FoodSin], Is.EqualTo(0f).Within(Tolerance));
            Assert.That(ahead[(int)EyeChannel.FoodProximity], Is.EqualTo(1f - 4.25f / 10f).Within(Tolerance));

            Assert.That(SeeFood(new Float2(21f, 25f))[(int)EyeChannel.FoodVisible], Is.Zero,
                "behind, though the circular legacy sensor would see it");
            Assert.That(SeeFood(new Float2(35.74f, 25f))[(int)EyeChannel.FoodVisible], Is.EqualTo(1f), "just in range");
            Assert.That(SeeFood(new Float2(35.76f, 25f))[(int)EyeChannel.FoodVisible], Is.Zero, "just out of range");

            Assert.That(SeeFood(At(59.5f, 5f))[(int)EyeChannel.FoodVisible], Is.EqualTo(1f), "inside the 60° border");
            Assert.That(SeeFood(At(60f, 5f))[(int)EyeChannel.FoodVisible], Is.EqualTo(1f), "on the border counts");
            Assert.That(SeeFood(At(60.5f, 5f))[(int)EyeChannel.FoodVisible], Is.Zero, "outside the border");
            Assert.That(SeeFood(At(-59.5f, 5f))[(int)EyeChannel.FoodVisible], Is.EqualTo(1f), "the other side");
            var left = SeeFood(At(45f, 5f));
            Assert.That(left[(int)EyeChannel.FoodSin], Is.GreaterThan(0f), "on the left");
        }

        [Test]
        public void EyeRangeAndDirection_FollowTheGenes()
        {
            var shortEye = new EyeSet(new Eye(0f, Eye.FounderAperture, 0.5f));
            Assert.That(See(shortEye, 0, w => w.AddFood(new Float2(31f, 25f), 10f))[(int)EyeChannel.FoodVisible], Is.Zero,
                "5.25 away is beyond 0.5 × 10");
            Assert.That(See(shortEye, 0, w => w.AddFood(new Float2(29f, 25f), 10f))[(int)EyeChannel.FoodVisible], Is.EqualTo(1f));

            // A narrow eye pointing left (+y when facing +x) sits at (25, 25.75).
            var leftEye = new EyeSet(new Eye(MathF.PI / 2f, Eye.MinAperture, 1f));
            var seen = See(leftEye, 0, w => w.AddFood(new Float2(25f, 30f), 10f));
            Assert.That(seen[(int)EyeChannel.FoodVisible], Is.EqualTo(1f));
            Assert.That(seen[(int)EyeChannel.FoodSin], Is.EqualTo(1f).Within(Tolerance), "body frame: to the left");
            Assert.That(seen[(int)EyeChannel.FoodProximity], Is.EqualTo(1f - 4.25f / 10f).Within(Tolerance));
            Assert.That(See(leftEye, 0, w => w.AddFood(new Float2(30f, 25f), 10f))[(int)EyeChannel.FoodVisible], Is.Zero,
                "ahead is outside a left eye");
        }

        [Test]
        public void AbsentEye_IsAllZero_WhilePresentEyeWithoutTargetSaysPresent()
        {
            var eyes = new EyeSet(Eye.Frontal, null);
            var present = See(eyes, 0, _ => { });
            Assert.That(present[(int)EyeChannel.Present], Is.EqualTo(1f));
            Assert.That(present.Skip(1), Is.All.EqualTo(0f), "nothing in reach: no food, walls 25 away");

            var absent = See(eyes, 1, w => w.AddFood(new Float2(30f, 25f), 10f));
            Assert.That(absent, Is.All.EqualTo(0f), "an empty slot reads nothing, food or not");
        }

        [Test]
        public void EqualDistances_GoToTheLowerFoodId_AndTheNearestWins()
        {
            var tie = See(EyeSet.Founder, 0, w =>
            {
                w.AddFood(new Float2(30f, 27f), 10f);
                w.AddFood(new Float2(30f, 23f), 10f);
            });
            Assert.That(tie[(int)EyeChannel.FoodSin], Is.GreaterThan(0f), "tie: the lower id, on the left");

            var nearest = See(EyeSet.Founder, 0, w =>
            {
                w.AddFood(new Float2(33f, 25f), 10f);
                w.AddFood(new Float2(29f, 24f), 10f);
            });
            Assert.That(nearest[(int)EyeChannel.FoodSin], Is.LessThan(0f), "the nearer food, on the right");
        }

        [Test]
        public void Walls_AreSeenAsBarriersOnlyInsideTheSector()
        {
            // Facing the right wall from (45, 25): the eye at (45.75, 25) sees it 4.25 ahead.
            var facing = See(EyeSet.Founder, 0, _ => { }, new Float2(45f, 25f), 0f);
            Assert.That(facing[(int)EyeChannel.BarrierVisible], Is.EqualTo(1f));
            Assert.That(facing[(int)EyeChannel.BarrierCos], Is.EqualTo(1f).Within(Tolerance));
            Assert.That(facing[(int)EyeChannel.BarrierProximity], Is.EqualTo(1f - 4.25f / 10f).Within(Tolerance));

            var away = See(EyeSet.Founder, 0, _ => { }, new Float2(45f, 25f), MathF.PI);
            Assert.That(away[(int)EyeChannel.BarrierVisible], Is.Zero, "the wall behind is not a radar reading");

            // Facing +y from (45, 25): the wall's foot is 90° off the axis, so the nearest visible point is where the
            // right edge of the cone (30° from +x) meets x = 50, 5 / cos 30° away.
            var edge = See(EyeSet.Founder, 0, _ => { }, new Float2(45f, 25f), MathF.PI / 2f);
            var distance = 5f / MathF.Cos(MathF.PI / 6f);
            Assert.That(edge[(int)EyeChannel.BarrierVisible], Is.EqualTo(1f));
            Assert.That(edge[(int)EyeChannel.BarrierProximity], Is.EqualTo(1f - distance / 10f).Within(Tolerance));
            Assert.That(edge[(int)EyeChannel.BarrierSin], Is.EqualTo(-MathF.Sin(MathF.PI / 3f)).Within(Tolerance), "60° right");
            Assert.That(edge[(int)EyeChannel.BarrierCos], Is.EqualTo(0.5f).Within(Tolerance));

            var far = See(EyeSet.Founder, 0, _ => { }, new Float2(35f, 25f), 0f);
            Assert.That(far[(int)EyeChannel.BarrierVisible], Is.Zero, "14.25 away is out of range");
        }

        [Test]
        public void Eyes_KeepTheirLimitsAndSlots()
        {
            Assert.That(() => new Eye(0f, Eye.MinAperture - 0.01f, 1f), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => new Eye(0f, Eye.MaxAperture + 0.01f, 1f), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => new Eye(0f, Eye.FounderAperture, 1.6f), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => new Eye(4f, Eye.FounderAperture, 1f), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => new EyeSet(Eye.Frontal, Eye.Frontal, Eye.Frontal, Eye.Frontal, Eye.Frontal), Throws.ArgumentException);

            var gap = new EyeSet(null, Eye.Frontal);
            Assert.That(gap.Has(0), Is.False);
            Assert.That(gap.Has(1), Is.True);
            Assert.That(gap.Count, Is.EqualTo(1));
            Assert.That(() => gap[0], Throws.InvalidOperationException);
            Assert.That(gap.Equals(new EyeSet(null, Eye.Frontal)), Is.True);
            Assert.That(gap.Equals(EyeSet.Founder), Is.False, "the same eye in another slot is another eye");
            Assert.That(EyeSet.Founder[0].Equals(Eye.Frontal), Is.True);
            Assert.That(EyeSet.None.Count, Is.Zero);
        }

        [Test]
        public void EyesContract_ReservesStableChannelsPerSlot()
        {
            var contract = NeuralContract.Eyes;
            Assert.That(contract.Id, Is.EqualTo("eyes-v1"));
            Assert.That(contract.InputCount, Is.EqualTo(4 + 4 * 9));
            Assert.That(contract.OutputCount, Is.EqualTo(3));
            for (var i = 0; i < 4; i++)
                Assert.That(contract.Inputs[i].Id, Is.EqualTo(NeuralContract.Legacy.Inputs[i].Id), "body channels keep their ids");
            Assert.That(NeuralContract.EyeInputIndex(1, EyeChannel.FoodSin), Is.EqualTo(15));
            Assert.That(contract.Inputs[15].Id, Is.EqualTo(NeuralContract.EyeChannelId(1, EyeChannel.FoodSin)));
            Assert.That(NeuralContract.EyeChannelId(1, EyeChannel.FoodSin), Is.EqualTo(118));
            Assert.That(contract.Inputs[15].Name, Is.EqualTo("Eye1FoodSin"));
            Assert.That(contract.InputIndexOf(NeuralContract.EyeChannelId(3, EyeChannel.BarrierProximity)), Is.EqualTo(39));
            Assert.That(contract.InputIndexOf(8), Is.EqualTo(-1), "no wall radar channel");
            Assert.That(NeuralContract.TryFind("eyes-v1", out var found) && found == contract, Is.True);
        }

        private static float[] SeeFood(Float2 food) => See(EyeSet.Founder, 0, w => w.AddFood(food, 10f));

        // A point at the given angle (degrees, from the eye's axis +x) and distance from the frontal eye.
        private static Float2 At(float degrees, float distance)
        {
            var radians = degrees * MathF.PI / 180f;
            return new Float2(Eye0.X + distance * MathF.Cos(radians), Eye0.Y + distance * MathF.Sin(radians));
        }

        private static float[] See(EyeSet eyes, int slot, Action<WorldState> place, Float2? at = null, float heading = 0f)
        {
            var world = TestWorlds.Empty();
            var creature = world.AddCreature(at ?? new Float2(25f, 25f), heading, 50f);
            place(world);
            var buffer = new float[EyeChannels.Count + 2];
            buffer[0] = 7f; // anything before the offset stays untouched
            Vision.See(world, creature, eyes, slot, buffer, 1);
            Assert.That(buffer[0], Is.EqualTo(7f));
            return buffer.Skip(1).Take(EyeChannels.Count).ToArray();
        }
    }
}
