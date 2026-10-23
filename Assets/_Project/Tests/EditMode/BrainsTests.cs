using System.Linq;
using Eden.Brains;
using Eden.Domain;
using NUnit.Framework;

namespace Eden.Tests.EditMode
{
    public class BrainsTests
    {
        [Test]
        public void RandomBrain_ProducesValidVaryingActionsFromItsOwnStream()
        {
            var brain = new RandomBrain();
            var first = new BrainState(new Pcg32(3, 5));
            var again = new BrainState(new Pcg32(3, 5));

            var actions = Enumerable.Range(0, 200).Select(_ => brain.Evaluate(default, first)).ToArray();
            var repeated = Enumerable.Range(0, 200).Select(_ => brain.Evaluate(default, again)).ToArray();

            Assert.That(actions.All(a => a.Thrust >= 0f && a.Thrust <= 1f && a.Turn >= -1f && a.Turn <= 1f &&
                                         a.Strafe >= -1f && a.Strafe <= 1f), Is.True);
            Assert.That(actions.Select(a => a.Turn).Distinct().Count(), Is.GreaterThan(150));
            Assert.That(actions.Min(a => a.Turn), Is.LessThan(-0.8f));
            Assert.That(actions.Max(a => a.Turn), Is.GreaterThan(0.8f));
            Assert.That(repeated.Select(a => a.Thrust), Is.EqualTo(actions.Select(a => a.Thrust)));
        }

        [Test]
        public void HeuristicBrain_SteersTowardVisibleFood()
        {
            var brain = new HeuristicBrain();
            var state = new BrainState(new Pcg32(1, 1));
            var stateBefore = state.Random.State;

            var aheadLeft = brain.Evaluate(Food(sin: 0.2f, cos: 0.98f), state);
            Assert.That(aheadLeft.Thrust, Is.EqualTo(1f));
            Assert.That(aheadLeft.Turn, Is.EqualTo(0.6f).Within(1e-5f));

            var behindRight = brain.Evaluate(Food(sin: -0.1f, cos: -0.99f), state);
            Assert.That(behindRight.Turn, Is.EqualTo(-1f));
            Assert.That(behindRight.Thrust, Is.LessThan(0.5f));

            Assert.That(state.Random.State, Is.EqualTo(stateBefore), "the heuristic is deterministic");
        }

        [Test]
        public void HeuristicBrain_CruisesAndAvoidsWallsWithoutFood()
        {
            var brain = new HeuristicBrain();
            var state = new BrainState(new Pcg32(1, 1));

            var open = brain.Evaluate(Walls(front: 1f, right: 1f, left: 1f), state);
            Assert.That(open.Turn, Is.EqualTo(0f));
            Assert.That(open.Thrust, Is.GreaterThan(0f));

            Assert.That(brain.Evaluate(Walls(front: 0.1f, right: 0.2f, left: 0.9f), state).Turn, Is.EqualTo(1f));
            Assert.That(brain.Evaluate(Walls(front: 0.1f, right: 0.9f, left: 0.2f), state).Turn, Is.EqualTo(-1f));
        }

        private static Observation Food(float sin, float cos) =>
            new Observation(0.5f, 0f, 0f, 0f, sin, cos, 0.5f, 1f, 1f, 1f, 1f, 1f);

        private static Observation Walls(float front, float right, float left) =>
            new Observation(0.5f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, front, right, 1f, left);
    }
}
