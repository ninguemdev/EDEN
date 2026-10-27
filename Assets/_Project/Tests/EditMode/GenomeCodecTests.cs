using System;
using Eden.Domain;
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
            Assert.That(() => GenomeCodec.Decode(text.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 99")),
                Throws.TypeOf<CheckpointException>().With.Message.Contains("newer"));
        }

        private static float[] Weights(int seed)
        {
            var weights = new float[NetworkTopology.Mlp12x16x3.ParameterCount];
            for (var i = 0; i < weights.Length; i++)
                weights[i] = (float)Math.Sin(seed * 0.37 + i * 0.1) * 2f;
            return weights;
        }
    }
}
