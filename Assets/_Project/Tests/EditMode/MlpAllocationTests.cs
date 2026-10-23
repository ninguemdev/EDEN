using Eden.Application;
using Eden.Brains;
using Eden.Domain;
using NUnit.Framework;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace Eden.Tests.EditMode
{
    /// <summary>GC allocation checks with the Unity Test Framework recorder.</summary>
    public class MlpAllocationTests
    {
        [Test]
        public void AllocationConstraint_DetectsAllocations()
        {
            // Positive control: proves the recorder sees allocations, so the checks below are not vacuous.
            Assert.That(() => { var buffer = new float[64]; buffer[0] = 1f; }, Is.AllocatingGCMemory());
        }

        [Test]
        public void Evaluate_DoesNotAllocateAfterWarmup()
        {
            var brain = new MlpBrain(MlpBrainTests.ReferenceParameters());
            var state = new BrainState(new Pcg32(1, 1));
            var observation = MlpBrainTests.Reference;
            brain.Evaluate(observation, state);

            Assert.That(() =>
            {
                for (var i = 0; i < 1000; i++)
                    brain.Evaluate(observation, state);
            }, Is.Not.AllocatingGCMemory());
        }

        [Test]
        public void ComputeActivations_DoesNotAllocateWithCallerBuffers()
        {
            var brain = new MlpBrain(MlpBrainTests.ReferenceParameters());
            var observation = MlpBrainTests.Reference;
            var hidden = new float[NeuralLayout.Hidden];
            var outputs = new float[NeuralLayout.Outputs];
            brain.ComputeActivations(observation, hidden, outputs);

            Assert.That(() =>
            {
                for (var i = 0; i < 1000; i++)
                    brain.ComputeActivations(observation, hidden, outputs);
            }, Is.Not.AllocatingGCMemory());
        }

        [Test]
        public void Capture_DoesNotAllocateAfterWarmup()
        {
            var brain = new MlpBrain(MlpBrainTests.ReferenceParameters());
            var snapshot = new NeuralSnapshot();
            var observation = MlpBrainTests.Reference;
            var creature = new CreatureId(1);
            snapshot.Capture(creature, brain, observation);

            Assert.That(() =>
            {
                for (var i = 0; i < 1000; i++)
                    snapshot.Capture(creature, brain, observation);
            }, Is.Not.AllocatingGCMemory());
        }
    }
}
