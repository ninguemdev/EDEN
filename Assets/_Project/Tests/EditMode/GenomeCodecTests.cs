using System;
using System.Text.RegularExpressions;
using Eden.Domain;
using Eden.Evolution;
using Eden.Persistence;
using NUnit.Framework;

namespace Eden.Tests.EditMode
{
    /// <summary>Genome files, independent of <see cref="CheckpointCodec"/>: exact round trip and refusal of bad files.</summary>
    public class GenomeCodecTests
    {
        private static readonly DateTime SavedAt = new DateTime(2026, 10, 27, 12, 0, 0, DateTimeKind.Utc);

        [Test]
        public void RoundTrip_KeepsWeightsBodyAndPlasticity()
        {
            var cases = new[]
            {
                new Genome(NetworkTopology.Mlp12x16x3, Weights(1)),
                new Genome(NetworkTopology.Mlp12x16x3, Weights(2), new BodyGenes(0.5f, -0.25f, 0f, 1f)),
                new Genome(NetworkTopology.Mlp12x16x3, Weights(3), BodyGenes.Neutral, new PlasticityGenes(0.4f, 0.1f)),
                new Genome(NetworkTopology.Mlp12x16x3, Weights(4), new BodyGenes(-1f, 1f, 0.3f, -0.3f),
                    new PlasticityGenes(1f, 0.2f))
            };

            foreach (var genome in cases)
            {
                var text = GenomeCodec.Encode(new GenomeRecord(genome), "0.1.0", SavedAt);
                var decoded = GenomeCodec.Decode(text).Genome;
                Assert.That(decoded.HasSameContent(genome), Is.True);
                Assert.That(GenomeCodec.Decode(GenomeCodec.Encode(new GenomeRecord(decoded), "0.1.0", SavedAt)).Genome
                    .HasSameContent(genome), Is.True, "re-encoding the decoded genome gives the same genome again");
            }
        }

        [Test]
        public void RoundTrip_KeepsFitnessAndSourceWhenGiven()
        {
            var genome = new Genome(NetworkTopology.Mlp12x16x3, Weights(5));

            var withoutExtras = GenomeCodec.Decode(GenomeCodec.Encode(new GenomeRecord(genome), "0.1.0", SavedAt));
            Assert.That(withoutExtras.Fitness, Is.Null);
            Assert.That(withoutExtras.Source, Is.Null);

            var record = new GenomeRecord(genome, 2.5f, new GenomeSource(7, 12, 913));
            var withExtras = GenomeCodec.Decode(GenomeCodec.Encode(record, "0.1.0", SavedAt));
            Assert.That(withExtras.Fitness, Is.EqualTo(2.5f));
            Assert.That(withExtras.FitnessProfile, Is.EqualTo(Fitness.Profile));
            Assert.That(withExtras.FitnessFor(Fitness.Profile), Is.EqualTo(2.5f));
            Assert.That(withExtras.Source.Value.ExperimentSeed, Is.EqualTo(7UL));
            Assert.That(withExtras.Source.Value.Generation, Is.EqualTo(12));
            Assert.That(withExtras.Source.Value.IndividualId, Is.EqualTo(913L));
        }

        [Test]
        public void Decode_RejectsWhatIsNotAGenomeFileOrTheWrongNetwork()
        {
            Assert.That(() => GenomeCodec.Decode("{\"format\": \"eden-checkpoint\"}"), Throws.TypeOf<CheckpointException>());
            Assert.That(() => GenomeCodec.Decode("not json"), Throws.TypeOf<CheckpointException>());

            var genome = new Genome(NetworkTopology.Mlp12x16x3, Weights(6));
            var text = GenomeCodec.Encode(new GenomeRecord(genome), "0.1.0", SavedAt);
            Assert.That(text, Does.Contain("\"topology\": [12,16,3]"));
            Assert.That(() => GenomeCodec.Decode(text.Replace("\"topology\": [12,16,3]", "\"topology\": [12,16,4]")),
                Throws.TypeOf<CheckpointException>().With.Message.Contains("network"));
            Assert.That(() => GenomeCodec.Decode(text.Replace("\"schemaVersion\": 2", "\"schemaVersion\": 99")),
                Throws.TypeOf<CheckpointException>().With.Message.Contains("newer"));
        }

        [Test]
        public void Encode_NamesTheNetworkContractAndFitnessProfile()
        {
            var genome = new Genome(NetworkTopology.Mlp12x16x3, Weights(7));
            var text = GenomeCodec.Encode(new GenomeRecord(genome, 1.25f), "0.1.0", SavedAt);

            Assert.That(text, Does.Contain("\"schemaVersion\": 2"));
            Assert.That(text, Does.Contain("\"network\": \"mlp\""));
            Assert.That(text, Does.Contain("\"contract\": \"legacy-12-3-v1\""));
            Assert.That(text, Does.Contain("\"fitnessProfile\": \"food-energy-per-second-v1\""));
            Assert.That(GenomeCodec.Encode(new GenomeRecord(genome), "0.1.0", SavedAt), Does.Not.Contain("fitnessProfile"),
                "no profile without a fitness");
        }

        [Test]
        public void Decode_ReadsSchema1FilesAsLegacyMlpGenomes()
        {
            var genome = new Genome(NetworkTopology.Mlp12x16x3, Weights(8), new BodyGenes(0.5f, 0f, 0f, 0f));
            var text = GenomeCodec.Encode(new GenomeRecord(genome, 3f, new GenomeSource(1, 2, 3)), "0.1.0", SavedAt);
            var schema1 = SchemaOne(text);
            Assert.That(schema1, Does.Not.Contain("network").And.Not.Contain("contract").And.Not.Contain("fitnessProfile"));

            var decoded = GenomeCodec.Decode(schema1);
            Assert.That(decoded.Genome.HasSameContent(genome), Is.True);
            Assert.That(decoded.Genome.Contract, Is.SameAs(NeuralContract.Legacy));
            Assert.That(decoded.Fitness, Is.EqualTo(3f));
            Assert.That(decoded.FitnessProfile, Is.EqualTo(Fitness.Profile), "the only profile schema 1 files had");
        }

        [Test]
        public void Decode_RefusesAnUnknownNetworkOrContractAndAProfileWithoutFitness()
        {
            var genome = new Genome(NetworkTopology.Mlp12x16x3, Weights(9));
            var text = GenomeCodec.Encode(new GenomeRecord(genome, 2f), "0.1.0", SavedAt);

            Assert.That(() => GenomeCodec.Decode(text.Replace("\"network\": \"mlp\"", "\"network\": \"lstm\"")),
                Throws.TypeOf<CheckpointException>().With.Message.Contains("lstm"));
            Assert.That(() => GenomeCodec.Decode(text.Replace("\"network\": \"mlp\"", "\"network\": \"neat\"")),
                Throws.TypeOf<CheckpointException>().With.Message.Contains("connections"),
                "MLP content labelled as NEAT has no connections");
            Assert.That(() => GenomeCodec.Decode(text.Replace("legacy-12-3-v1", "eyes-v9")),
                Throws.TypeOf<CheckpointException>().With.Message.Contains("eyes-v9"));
            Assert.That(() => GenomeCodec.Decode(WithoutMember(text, "network")),
                Throws.TypeOf<CheckpointException>().With.Message.Contains("network"), "schema 2 must name its network");
            Assert.That(() => GenomeCodec.Decode(WithoutMember(text, "fitness")),
                Throws.TypeOf<CheckpointException>().With.Message.Contains("without a fitness"));
            var withoutProfile = Regex.Replace(text, @",\s*""fitnessProfile"": [^\n]*\n", "\n");
            Assert.That(() => GenomeCodec.Decode(withoutProfile),
                Throws.TypeOf<CheckpointException>().With.Message.Contains("profile"));
        }

        [Test]
        public void FitnessFor_IgnoresScoresOfAnotherProfile()
        {
            var genome = new Genome(NetworkTopology.Mlp12x16x3, Weights(10));
            var other = new GenomeRecord(genome, 9f, null, "water-and-food-v1");
            Assert.That(other.FitnessFor(Fitness.Profile), Is.Null);
            Assert.That(other.FitnessFor("water-and-food-v1"), Is.EqualTo(9f));
            Assert.That(new GenomeRecord(genome).FitnessFor(Fitness.Profile), Is.Null);
            Assert.That(new GenomeRecord(genome).FitnessProfile, Is.Null);
            Assert.That(() => new GenomeRecord(genome, 1f, null, ""), Throws.ArgumentException);

            var decoded = GenomeCodec.Decode(GenomeCodec.Encode(other, "0.1.0", SavedAt));
            Assert.That(decoded.FitnessProfile, Is.EqualTo("water-and-food-v1"), "kept as history");
            Assert.That(decoded.FitnessFor(Fitness.Profile), Is.Null);
        }

        [Test]
        public void NeatGenome_RoundTripsWithHiddenNodesDisabledGenesAndBody()
        {
            var contract = NeuralContract.Legacy;
            var output = NeatNetwork.OutputNodeId(contract, 1);
            var hidden = NeatNetwork.FirstHiddenNodeId(contract);
            var minimal = NeatNetwork.CreateMinimal(contract, new Pcg32(3, 3), 1f).Connections;
            var genes = new NeatConnection[minimal.Count + 2];
            for (var i = 0; i < minimal.Count; i++)
                genes[i] = minimal[i];
            genes[4] = genes[4].WithEnabled(false); // input 0 → output 1, split by the hidden node
            genes[minimal.Count] = new NeatConnection(39, genes[4].From, hidden, 1f, true);
            genes[minimal.Count + 1] = new NeatConnection(40, hidden, output, genes[4].Weight, true);
            var genome = new Genome(new NeatNetwork(contract, genes), new BodyGenes(0f, -0.5f, 0.25f, 0f));

            var text = GenomeCodec.Encode(new GenomeRecord(genome, 1.5f, new GenomeSource(4, 5, 6)), "0.1.0", SavedAt);
            Assert.That(text, Does.Contain("\"network\": \"neat\"").And.Contain("\"connections\"")
                .And.Not.Contain("\"topology\"").And.Not.Contain("\"weights\""));
            Assert.That(text, Does.Contain("[4,1,14,").And.Contain(",false]"), "[innovation, from, to, weight, enabled]");

            var decoded = GenomeCodec.Decode(text);
            Assert.That(decoded.Genome.HasSameContent(genome), Is.True);
            Assert.That(decoded.Genome.Neat.HiddenNodes, Is.EqualTo(new[] { hidden }));
            Assert.That(decoded.Fitness, Is.EqualTo(1.5f));
            Assert.That(GenomeCodec.Encode(decoded, "0.1.0", SavedAt), Is.EqualTo(text), "re-encodes to the same text");
        }

        [Test]
        public void NeatGenome_InvalidFilesAreRefusedAsGenomeErrors()
        {
            var contract = NeuralContract.Legacy;
            var genome = new Genome(NeatNetwork.CreateMinimal(contract, new Pcg32(4, 3), 1f));
            var text = GenomeCodec.Encode(new GenomeRecord(genome), "0.1.0", SavedAt);
            var first = $"[0,0,{NeatNetwork.OutputNodeId(contract, 0)},";

            Assert.That(text, Does.Contain(first));
            Assert.That(() => GenomeCodec.Decode(text.Replace(first, $"[0,{NeatNetwork.OutputNodeId(contract, 0)},1,")),
                Throws.TypeOf<CheckpointException>().With.Message.Contains("Not a valid genome"), "output into input");
            Assert.That(() => GenomeCodec.Decode(text.Replace(first, "[45,0,13,")),
                Throws.TypeOf<CheckpointException>().With.Message.Contains("innovation order"));
            Assert.That(() => GenomeCodec.Decode(text.Replace("\"contract\"", "\"plasticity\": [0.5,0.1],\n  \"contract\"")),
                Throws.TypeOf<CheckpointException>().With.Message.Contains("plasticity"));

            var mlp = GenomeCodec.Encode(new GenomeRecord(new Genome(NetworkTopology.Mlp12x16x3, Weights(11))), "0.1.0", SavedAt);
            var weightsAt = mlp.IndexOf("\"weights\": [", StringComparison.Ordinal) + "\"weights\": [".Length;
            var outOfRange = mlp.Substring(0, weightsAt) + "7," + mlp.Substring(mlp.IndexOf(',', weightsAt) + 1);
            Assert.That(() => GenomeCodec.Decode(outOfRange),
                Throws.TypeOf<CheckpointException>().With.Message.Contains("Not a valid genome"),
                "a bad MLP weight is a genome error too, not an ArgumentException");
        }

        // The same file as schema 1 wrote it: no network, contract or fitness profile.
        private static string SchemaOne(string schema2) =>
            WithoutMember(WithoutMember(WithoutMember(schema2, "network"), "contract"), "fitnessProfile")
                .Replace("\"schemaVersion\": 2", "\"schemaVersion\": 1");

        // Drops a one-line, non-final member of the top-level object (the writer puts one member per line).
        private static string WithoutMember(string text, string name) =>
            Regex.Replace(text, @"\n\s*""" + name + @""": [^\n]*,\n", "\n");

        private static float[] Weights(int seed)
        {
            var weights = new float[NetworkTopology.Mlp12x16x3.ParameterCount];
            for (var i = 0; i < weights.Length; i++)
                weights[i] = (float)Math.Sin(seed * 0.37 + i * 0.1) * 2f;
            return weights;
        }
    }
}
