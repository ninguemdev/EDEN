using Eden.Brains;
using Eden.Domain;
using NUnit.Framework;

namespace Eden.Tests.EditMode
{
    /// <summary>The legacy contract describes exactly what brains read and write today, by stable channel ids.</summary>
    public class NeuralContractTests
    {
        [Test]
        public void Legacy_ListsTheObservationAndActionInBrainOrder()
        {
            var contract = NeuralContract.Legacy;

            Assert.That(contract.Id, Is.EqualTo("legacy-12-3-v1"));
            Assert.That(contract.InputCount, Is.EqualTo(Observation.Size));
            Assert.That(contract.InputCount, Is.EqualTo(NeuralLayout.Inputs));
            Assert.That(contract.OutputCount, Is.EqualTo(NeuralLayout.Outputs));
            for (var i = 0; i < contract.InputCount; i++)
            {
                Assert.That(contract.Inputs[i].Id, Is.EqualTo(i), "legacy ids are the observation indices");
                Assert.That(contract.Inputs[i].Name, Is.EqualTo(ObservationLimits.Name(i)));
                Assert.That(contract.InputIndexOf(i), Is.EqualTo(i));
            }

            Assert.That(contract.Outputs[0].Name, Is.EqualTo("Thrust"));
            Assert.That(contract.Outputs[1].Name, Is.EqualTo("Turn"));
            Assert.That(contract.Outputs[2].Name, Is.EqualTo("Strafe"));
            Assert.That(contract.InputIndexOf(12), Is.EqualTo(-1));
            Assert.That(contract.OutputIndexOf(3), Is.EqualTo(-1));
        }

        [Test]
        public void Legacy_ChannelsFollowTheObservationValues()
        {
            // Each value of the observation lands on the channel of the same name, and each output on the action field
            // of the same name, so a brain that reads channels by id reads what the sensors wrote.
            var observation = new Observation(0.01f, 0.02f, 0.03f, 0.04f, 0.05f, 0.06f, 0.07f, 1f, 0.09f, 0.10f, 0.11f, 0.12f);
            var buffer = new float[Observation.Size];
            observation.CopyTo(buffer, 0);
            var contract = NeuralContract.Legacy;
            Assert.That(buffer[contract.InputIndexOf(0)], Is.EqualTo(observation.Energy));
            Assert.That(buffer[contract.InputIndexOf(6)], Is.EqualTo(observation.FoodProximity));
            Assert.That(buffer[contract.InputIndexOf(7)], Is.EqualTo(observation.FoodVisible));
            Assert.That(buffer[contract.InputIndexOf(11)], Is.EqualTo(observation.WallLeft));

            var action = MlpBrain.ToAction(new[] { 0.2f, -0.4f, 0.6f });
            Assert.That(action.Thrust, Is.EqualTo(0.6f).Within(1e-6f), "output 0: thrust");
            Assert.That(action.Turn, Is.EqualTo(-0.4f), "output 1: turn");
            Assert.That(action.Strafe, Is.EqualTo(0.6f), "output 2: strafe");
        }

        [Test]
        public void TryFind_KnowsOnlyTheContractsOfThisBuild()
        {
            Assert.That(NeuralContract.TryFind(NeuralContract.LegacyId, out var found), Is.True);
            Assert.That(found, Is.SameAs(NeuralContract.Legacy));
            Assert.That(NeuralContract.TryFind("eyes-v1", out found), Is.False);
            Assert.That(found, Is.Null);
            Assert.That(NeuralContract.TryFind(null, out _), Is.False);
        }

        [Test]
        public void MlpGenomes_NameTheirNetworkAndContract()
        {
            var genome = new Genome(NetworkTopology.Mlp12x16x3, new float[NetworkTopology.Mlp12x16x3.ParameterCount]);
            Assert.That(genome.Network, Is.EqualTo(Genome.MlpNetwork));
            Assert.That(genome.Contract, Is.SameAs(NeuralContract.Legacy));
            Assert.That(genome.Topology.Inputs, Is.EqualTo(genome.Contract.InputCount));
            Assert.That(genome.Topology.Outputs, Is.EqualTo(genome.Contract.OutputCount));
        }
    }
}
