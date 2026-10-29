using System;
using System.Globalization;
using Eden.Domain;

namespace Eden.Persistence
{
    /// <summary>Where an exported genome came from; recorded for people, never checked on import.</summary>
    public readonly struct GenomeSource
    {
        public GenomeSource(ulong experimentSeed, int generation, long individualId)
        {
            ExperimentSeed = experimentSeed;
            Generation = generation;
            IndividualId = individualId;
        }

        public ulong ExperimentSeed { get; }
        public int Generation { get; }
        public long IndividualId { get; }
    }

    /// <summary>
    /// A genome exported on its own, outside any checkpoint: the genome itself, an optional fitness (absent when
    /// exporting a creature that was never scored on its own, e.g. mid-episode) and optional provenance.
    /// </summary>
    public sealed class GenomeRecord
    {
        public GenomeRecord(Genome genome, float? fitness = null, GenomeSource? source = null)
        {
            Genome = genome ?? throw new ArgumentNullException(nameof(genome));
            Fitness = fitness;
            Source = source;
        }

        public Genome Genome { get; }
        public float? Fitness { get; }
        public GenomeSource? Source { get; }
    }

    /// <summary>
    /// JSON form of one exported genome (schema version 1), independent of <see cref="CheckpointCodec"/>: a genome file
    /// is read standalone, with no surrounding checkpoint to imply the network's shape, so unlike a checkpoint's
    /// internal genome encoding it always writes and checks the topology explicitly.
    /// </summary>
    public static class GenomeCodec
    {
        public const string Format = "eden-genome";
        public const int SchemaVersion = 1;

        public static string Encode(GenomeRecord record, string appVersion, DateTime savedUtc)
        {
            if (record == null)
                throw new ArgumentNullException(nameof(record));
            var genome = record.Genome;
            var weights = JsonValue.NewArray();
            for (var w = 0; w < genome.WeightCount; w++)
                weights.Add(JsonValue.Of(genome.GetWeight(w)));

            var root = JsonValue.NewObject()
                .Add("format", JsonValue.Of(Format))
                .Add("schemaVersion", JsonValue.Of(SchemaVersion))
                .Add("app", JsonValue.NewObject().Add("version", JsonValue.Of(appVersion ?? string.Empty)))
                .Add("savedUtc", JsonValue.Of(savedUtc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)))
                .Add("genomeSchema", JsonValue.Of(Genome.CurrentSchemaVersion))
                .Add("topology", JsonValue.NewArray()
                    .Add(JsonValue.Of(genome.Topology.Inputs))
                    .Add(JsonValue.Of(genome.Topology.Hidden))
                    .Add(JsonValue.Of(genome.Topology.Outputs)))
                .Add("weights", weights);
            if (!genome.Body.IsNeutral)
                root.Add("body", EncodeBodyGenes(genome.Body));
            if (!genome.Plasticity.IsFixed)
            {
                root.Add("plasticity", JsonValue.NewArray()
                    .Add(JsonValue.Of(genome.Plasticity.LearningRate))
                    .Add(JsonValue.Of(genome.Plasticity.Decay)));
            }

            if (record.Fitness.HasValue)
                root.Add("fitness", JsonValue.OfAnyFloat(record.Fitness.Value));
            if (record.Source.HasValue)
            {
                var source = record.Source.Value;
                root.Add("source", JsonValue.NewObject()
                    .Add("experimentSeed", JsonValue.Of(source.ExperimentSeed))
                    .Add("generation", JsonValue.Of(source.Generation))
                    .Add("individualId", JsonValue.Of(source.IndividualId)));
            }

            return JsonText.Write(root);
        }

        /// <summary>Reads and checks a genome file; anything wrong raises a <see cref="CheckpointException"/>.</summary>
        public static GenomeRecord Decode(string text)
        {
            if (text == null)
                throw new ArgumentNullException(nameof(text));
            var root = JsonText.Parse(text);
            if (root.Kind != JsonKind.Object || TryString(root, "format") != Format)
                throw new CheckpointException("Not an EDEN genome file.");
            var schema = root["schemaVersion"].Long(1);
            if (schema > SchemaVersion)
                throw new CheckpointException($"Genome file schema version {schema} is newer than version {SchemaVersion}, " +
                                              "the newest this build reads; open it with a newer EDEN.");

            var expected = NetworkTopology.Mlp12x16x3;
            var topology = root["topology"].ArrayOf(3);
            if (topology.Count != 3 || topology[0].Int() != expected.Inputs || topology[1].Int() != expected.Hidden ||
                topology[2].Int() != expected.Outputs)
                throw new CheckpointException($"{topology.Path}: only the {expected} network is supported.");

            var values = root["weights"].ArrayOf(expected.ParameterCount);
            if (values.Count != expected.ParameterCount)
                throw new CheckpointException($"{values.Path}: {expected} needs {expected.ParameterCount} weights.");
            var weights = new float[values.Count];
            for (var w = 0; w < weights.Length; w++)
                weights[w] = values[w].Float();

            var genome = new Genome(expected, weights, DecodeBodyGenes(root.TryGet("body")),
                DecodePlasticity(root.TryGet("plasticity")));
            float? fitness = root.TryGet("fitness")?.AnyFloat();
            GenomeSource? source = null;
            var sourceValue = root.TryGet("source");
            if (sourceValue != null)
            {
                source = new GenomeSource(sourceValue["experimentSeed"].ULong(), sourceValue["generation"].Int(0),
                    sourceValue["individualId"].Long(1));
            }

            return new GenomeRecord(genome, fitness, source);
        }

        private static JsonValue EncodeBodyGenes(BodyGenes genes)
        {
            var values = JsonValue.NewArray();
            for (var g = 0; g < BodyGenes.Count; g++)
                values.Add(JsonValue.Of(genes[(BodyGene)g]));
            return values;
        }

        // Missing means neutral genes; an explicit neutral list does not read back the same and is refused.
        private static BodyGenes DecodeBodyGenes(JsonValue saved)
        {
            if (saved == null)
                return BodyGenes.Neutral;
            var values = saved.ArrayOf(BodyGenes.Count);
            if (values.Count != BodyGenes.Count)
                throw new CheckpointException($"{values.Path}: expected [size, speed, sensor, efficiency].");
            return new BodyGenes(values[0].Float(), values[1].Float(), values[2].Float(), values[3].Float());
        }

        // Missing means a fixed network; an explicit [0, 0] does not read back the same and is refused.
        private static PlasticityGenes DecodePlasticity(JsonValue saved)
        {
            if (saved == null)
                return PlasticityGenes.Fixed;
            var values = saved.ArrayOf(2);
            if (values.Count != 2)
                throw new CheckpointException($"{values.Path}: expected [learning rate, decay].");
            return new PlasticityGenes(values[0].Float(), values[1].Float());
        }

        private static string TryString(JsonValue root, string name)
        {
            foreach (var member in root.Members)
            {
                if (member.Key == name)
                    return member.Value.Kind == JsonKind.String ? member.Value.Text : null;
            }

            return null;
        }
    }
}
