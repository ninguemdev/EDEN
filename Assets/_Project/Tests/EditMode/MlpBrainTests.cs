using System;
using System.Diagnostics;
using Eden.Application;
using Eden.Brains;
using Eden.Domain;
using Eden.Simulation;
using NUnit.Framework;

namespace Eden.Tests.EditMode
{
    public class MlpBrainTests
    {
        private const float Tolerance = 1e-5f;

        // Same observation and weight pattern as the float64 reference computed outside the project.
        private static readonly Observation ReferenceObservation =
            new Observation(0.8f, 0.25f, 0.5f, -0.2f, 0.3f, 0.95f, 0.4f, 1f, 1f, 0.6f, 0.2f, 0.9f);

        private static readonly float[] ReferenceHidden =
        {
            0.801636565f, 0.871269724f, -0.764665981f, -0.340320203f, 0.835488183f, -0.617818695f,
            -0.924914629f, 0.479030114f, 0.144599208f, -0.942176647f, -0.160547356f, 0.836044807f,
            -0.612408912f, -0.542148817f, 0.931449997f, 0.582430114f
        };

        private static readonly float[] ReferenceOutputs = { 0.930223858f, 0.910316203f, 0.832794674f };

        [Test]
        public void Layout_Has259ParametersInTheDocumentedOrder()
        {
            Assert.That(NeuralLayout.ParameterCount, Is.EqualTo(259));
            Assert.That(NeuralLayout.HiddenWeight(0, 0), Is.EqualTo(0));
            Assert.That(NeuralLayout.HiddenWeight(15, 11), Is.EqualTo(191));
            Assert.That(NeuralLayout.HiddenBias(0), Is.EqualTo(192));
            Assert.That(NeuralLayout.OutputWeight(0, 0), Is.EqualTo(208));
            Assert.That(NeuralLayout.OutputWeight(2, 15), Is.EqualTo(255));
            Assert.That(NeuralLayout.OutputBias(2), Is.EqualTo(258));
            Assert.That(NeuralLayout.Inputs, Is.EqualTo(Observation.Size));
        }

        [Test]
        public void KnownWeights_MatchTheIndependentReference()
        {
            var brain = new MlpBrain(ReferenceParameters());
            var hidden = new float[NeuralLayout.Hidden];
            var outputs = new float[NeuralLayout.Outputs];

            brain.ComputeActivations(ReferenceObservation, hidden, outputs);
            var action = brain.Evaluate(ReferenceObservation, null);

            Assert.That(hidden, Is.EqualTo(ReferenceHidden).Within(Tolerance));
            Assert.That(outputs, Is.EqualTo(ReferenceOutputs).Within(Tolerance));
            Assert.That(action.Thrust, Is.EqualTo(0.965111929f).Within(Tolerance));
            Assert.That(action.Turn, Is.EqualTo(0.910316203f).Within(Tolerance));
            Assert.That(action.Strafe, Is.EqualTo(0.832794674f).Within(Tolerance));
        }

        [Test]
        public void HandCheckablePaths_FollowTheLayoutAndActionMapping()
        {
            var parameters = new float[NeuralLayout.ParameterCount];
            parameters[NeuralLayout.OutputBias(1)] = 0.5f;
            parameters[NeuralLayout.OutputBias(2)] = -0.5f;
            var biasesOnly = new MlpBrain(parameters).Evaluate(ReferenceObservation, null);
            Assert.That(biasesOnly.Thrust, Is.EqualTo(0.5f).Within(Tolerance), "tanh(0) maps to half thrust");
            Assert.That(biasesOnly.Turn, Is.EqualTo(MathF.Tanh(0.5f)).Within(Tolerance));
            Assert.That(biasesOnly.Strafe, Is.EqualTo(-MathF.Tanh(0.5f)).Within(Tolerance));

            parameters = new float[NeuralLayout.ParameterCount];
            parameters[NeuralLayout.HiddenWeight(3, 0)] = 1f;
            parameters[NeuralLayout.OutputWeight(1, 3)] = 2f;
            var singlePath = new MlpBrain(parameters).Evaluate(ReferenceObservation, null);
            Assert.That(singlePath.Turn, Is.EqualTo(MathF.Tanh(2f * MathF.Tanh(0.8f))).Within(Tolerance),
                "energy input → hidden 3 → turn");
            Assert.That(singlePath.Strafe, Is.EqualTo(0f).Within(Tolerance));
        }

        [Test]
        public void Parameters_AreCopiedOnConstruction()
        {
            var source = ReferenceParameters();
            var brain = new MlpBrain(source);
            var before = brain.Evaluate(ReferenceObservation, null);

            Array.Clear(source, 0, source.Length);
            var after = brain.Evaluate(ReferenceObservation, null);
            var copy = new float[NeuralLayout.ParameterCount];
            brain.CopyParametersTo(copy);

            Assert.That(after.Turn, Is.EqualTo(before.Turn));
            Assert.That(copy, Is.EqualTo(ReferenceParameters()));
            Assert.That(brain.GetParameter(NeuralLayout.OutputBias(2)), Is.EqualTo(copy[258]));
        }

        [Test]
        public void InvalidParameters_AreRejected()
        {
            Assert.Throws<ArgumentNullException>(() => new MlpBrain(null));
            Assert.Throws<ArgumentException>(() => new MlpBrain(new float[258]));
            var nan = ReferenceParameters();
            nan[100] = float.NaN;
            Assert.Throws<ArgumentException>(() => new MlpBrain(nan));
            var infinite = ReferenceParameters();
            infinite[258] = float.NegativeInfinity;
            Assert.Throws<ArgumentException>(() => new MlpBrain(infinite));
        }

        [Test]
        public void MlpBrain_RunsTheWorldThroughTheExistingController()
        {
            Assert.That(RunWorld(), Is.EqualTo(RunWorld()), "same seed, weights and ticks give the same world");
        }

        [Test]
        public void Inference_ThroughputIsRecorded()
        {
            var brain = new MlpBrain(ReferenceParameters());
            const int evaluations = 200000;
            brain.Evaluate(ReferenceObservation, null);
            var timer = Stopwatch.StartNew();
            var checksum = 0f;
            for (var i = 0; i < evaluations; i++)
                checksum += brain.Evaluate(ReferenceObservation, null).Turn;
            timer.Stop();

            TestContext.WriteLine($"MLP 12-16-3: {evaluations} evaluations in {timer.Elapsed.TotalMilliseconds:0.0} ms " +
                                  $"({timer.Elapsed.TotalMilliseconds * 1000.0 / evaluations:0.000} µs each, Editor, checksum {checksum:0.0})");
            Assert.That(float.IsNaN(checksum), Is.False);
        }

        internal static float[] ReferenceParameters()
        {
            var parameters = new float[NeuralLayout.ParameterCount];
            for (var k = 0; k < parameters.Length; k++)
                parameters[k] = (float)(0.5 * Math.Sin(0.37 * k + 0.1));
            return parameters;
        }

        internal static Observation Reference => ReferenceObservation;

        private static ulong RunWorld()
        {
            var config = SimulationConfig.CreateDefault(21);
            var simulation = new WorldSimulation(WorldFactory.CreateInitial(config));
            var controller = BrainController.Shared(config, new MlpBrain(ReferenceParameters()));
            for (var t = 0; t < 600; t++)
            {
                simulation.Step(controller);
                controller.Forget(simulation.LastDeaths);
                foreach (var creature in simulation.World.Creatures)
                {
                    if (float.IsNaN(creature.Energy) || float.IsNaN(creature.Position.X) || float.IsNaN(creature.Heading))
                        Assert.Fail($"Non-finite state for {creature.Id} at tick {simulation.World.Tick}");
                }
            }

            return simulation.ComputeFingerprint();
        }
    }
}
