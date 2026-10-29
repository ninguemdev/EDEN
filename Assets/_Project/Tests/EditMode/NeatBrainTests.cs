using System;
using Eden.Application;
using Eden.Brains;
using Eden.Domain;
using Eden.Evolution;
using NUnit.Framework;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace Eden.Tests.EditMode
{
    /// <summary>NEAT inference: outputs computed by hand, disabled genes, node order and no allocation.</summary>
    public class NeatBrainTests
    {
        private static readonly NeuralContract Contract = NeuralContract.Legacy;
        private static readonly int Thrust = NeatNetwork.OutputNodeId(Contract, 0);
        private static readonly int Turn = NeatNetwork.OutputNodeId(Contract, 1);
        private static readonly int Strafe = NeatNetwork.OutputNodeId(Contract, 2);
        private static readonly int H0 = NeatNetwork.FirstHiddenNodeId(Contract);

        // Energy 0.8, age 0.25, everything else 0 (no food visible).
        private static readonly Observation Sample = new Observation(0.8f, 0.25f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f);

        [Test]
        public void DirectConnections_MatchHandComputation()
        {
            var brain = Brain(
                new NeatConnection(0, NeatNetwork.BiasNodeId, Thrust, 0.5f, true),
                new NeatConnection(4, NeatNetwork.InputNodeId(0), Turn, 1f, true),
                new NeatConnection(5, NeatNetwork.InputNodeId(0), Strafe, -2f, true),
                new NeatConnection(8, NeatNetwork.InputNodeId(1), Strafe, 0.4f, true));

            var action = brain.Evaluate(Sample, null);

            Assert.That(action.Thrust, Is.EqualTo((MathF.Tanh(0.5f) + 1f) * 0.5f).Within(1e-6f));
            Assert.That(action.Turn, Is.EqualTo(MathF.Tanh(0.8f)).Within(1e-6f));
            Assert.That(action.Strafe, Is.EqualTo(MathF.Tanh(-2f * 0.8f + 0.4f * 0.25f)).Within(1e-6f));
            Assert.That(brain.Name, Is.EqualTo("neat-v1"));
        }

        [Test]
        public void HiddenNode_AndDisabledConnection()
        {
            var brain = Brain(
                new NeatConnection(4, NeatNetwork.InputNodeId(0), Turn, 3f, false),
                new NeatConnection(39, NeatNetwork.InputNodeId(0), H0, 2f, true),
                new NeatConnection(40, H0, Turn, -1.5f, true),
                new NeatConnection(41, NeatNetwork.BiasNodeId, H0, 0.3f, true));

            var action = brain.Evaluate(Sample, null);

            var hidden = MathF.Tanh(2f * 0.8f + 0.3f * 1f);
            Assert.That(action.Turn, Is.EqualTo(MathF.Tanh(-1.5f * hidden)).Within(1e-6f), "the disabled input → turn carries nothing");
            Assert.That(action.Thrust, Is.EqualTo(0.5f), "unconnected output: tanh(0) = 0 → thrust 0.5");
            Assert.That(action.Strafe, Is.EqualTo(0f));

            var values = new float[brain.NodeIds.Count];
            brain.ComputeActivations(Sample, values);
            Assert.That(brain.NodeIds[brain.NodeIds.Count - 1], Is.EqualTo(H0));
            Assert.That(values[0], Is.EqualTo(1f), "bias");
            Assert.That(values[NeatNetwork.InputNodeId(0)], Is.EqualTo(0.8f));
            Assert.That(values[brain.NodeIds.Count - 1], Is.EqualTo(hidden).Within(1e-6f));
            Assert.That(values[Turn], Is.EqualTo(action.Turn));
        }

        [Test]
        public void NodesAreEvaluatedAfterTheirSources_NotInIdOrder()
        {
            // input 0 → H0+4 → H0+1 → H0 → strafe: each hidden id is lower than the node it reads from.
            var brain = Brain(
                new NeatConnection(50, NeatNetwork.InputNodeId(0), H0 + 4, 1.1f, true),
                new NeatConnection(51, H0 + 4, H0 + 1, -0.7f, true),
                new NeatConnection(52, H0 + 1, H0, 1.9f, true),
                new NeatConnection(53, H0, Strafe, 0.6f, true));

            var expected = MathF.Tanh(0.6f * MathF.Tanh(1.9f * MathF.Tanh(-0.7f * MathF.Tanh(1.1f * 0.8f))));
            Assert.That(brain.Evaluate(Sample, null).Strafe, Is.EqualTo(expected).Within(1e-6f));
        }

        [Test]
        public void EmptyNetwork_GivesTheNeutralAction()
        {
            var action = Brain().Evaluate(Sample, null);
            Assert.That(action.Thrust, Is.EqualTo(0.5f));
            Assert.That(action.Turn, Is.EqualTo(0f));
            Assert.That(action.Strafe, Is.EqualTo(0f));
        }

        [Test]
        public void InvalidInputs_FollowTheObservationPolicy()
        {
            var brain = Brain(new NeatConnection(4, NeatNetwork.InputNodeId(0), Turn, 1f, true));
            var nan = new Observation(float.NaN, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f);
            var high = new Observation(7f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f);
            Assert.That(brain.Evaluate(nan, null).Turn, Is.EqualTo(0f), "NaN reads as 0");
            Assert.That(brain.Evaluate(high, null).Turn, Is.EqualTo(MathF.Tanh(1f)).Within(1e-6f), "clamped to the range");
        }

        [Test]
        public void Evaluate_DoesNotAllocateAfterWarmup()
        {
            var brain = new NeatBrain(new Genome(NeatNetwork.CreateMinimal(Contract, new Pcg32(3, 1), 1f)));
            var state = new BrainState(new Pcg32(1, 1));
            brain.Evaluate(Sample, state);

            Assert.That(() =>
            {
                for (var i = 0; i < 1000; i++)
                    brain.Evaluate(Sample, state);
            }, Is.Not.AllocatingGCMemory());
        }

        [Test]
        public void GenomeBrains_BuildTheBrainOfEachNetworkKind()
        {
            var neat = new Genome(NeatNetwork.CreateMinimal(Contract, new Pcg32(4, 1), 1f));
            var mlp = InitialGenomes.Population(3, 1)[0];
            Assert.That(GenomeBrains.Create(neat), Is.TypeOf<NeatBrain>());
            Assert.That(GenomeBrains.Create(mlp), Is.TypeOf<MlpBrain>());
            Assert.That(() => new NeatBrain(mlp), Throws.ArgumentException);
        }

        [Test]
        public void IsolatedEpisodes_RunNeatGenomesReproducibly()
        {
            var plan = new EvaluationPlan(new EvolutionSettings { ExperimentSeed = 11, EpisodeSeconds = 10f });
            var genomes = new[]
            {
                new Genome(NeatNetwork.CreateMinimal(Contract, new Pcg32(5, 1), 1f)),
                new Genome(NeatNetwork.CreateMinimal(Contract, new Pcg32(6, 1), 1f))
            };

            var first = new IsolatedEvaluator(plan).EvaluateTraining(genomes, 0);
            var second = new IsolatedEvaluator(plan).EvaluateTraining(genomes, 0);

            Assert.That(first.Candidates.Count, Is.EqualTo(2));
            for (var c = 0; c < 2; c++)
            {
                Assert.That(float.IsNaN(first.Candidates[c].MeanFitness), Is.False);
                Assert.That(first.Candidates[c].MeanFitness, Is.GreaterThanOrEqualTo(0f));
                Assert.That(second.Candidates[c].MeanFitness, Is.EqualTo(first.Candidates[c].MeanFitness));
            }
        }

        private static NeatBrain Brain(params NeatConnection[] connections) =>
            new NeatBrain(new Genome(new NeatNetwork(Contract, connections)));
    }
}
