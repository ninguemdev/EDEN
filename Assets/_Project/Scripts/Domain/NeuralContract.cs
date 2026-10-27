using System;
using System.Collections.Generic;

namespace Eden.Domain
{
    /// <summary>One brain input or output: an id that keeps a single meaning forever, and a name for people.</summary>
    public readonly struct NeuralChannel
    {
        public NeuralChannel(int id, string name)
        {
            if (id < 0)
                throw new ArgumentOutOfRangeException(nameof(id), "Channel ids are not negative.");
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("A channel needs a name.", nameof(name));
            Id = id;
            Name = name;
        }

        public int Id { get; }
        public string Name { get; }

        public override string ToString() => $"{Name}#{Id}";
    }

    /// <summary>
    /// What a brain reads and writes, identified by a versioned id that files record. Every channel has an id that
    /// keeps one meaning forever: a new sense (e.g. an eye) adds channels with new ids in a new contract, it never
    /// renumbers or reuses an existing id, so a genome that names a channel (e.g. a NEAT input node) keeps reading the
    /// same sense. A channel's index is its position in the input or output vector of this contract; ids and indices
    /// only coincide in <see cref="Legacy"/>.
    /// </summary>
    public sealed class NeuralContract
    {
        /// <summary>Id of <see cref="Legacy"/>.</summary>
        public const string LegacyId = "legacy-12-3-v1";

        /// <summary>
        /// The contract of the MVP: the 12 inputs of <see cref="Observation"/> (ids 0–11, in its order, named as in
        /// <see cref="ObservationLimits"/>) and the 3 outputs of <see cref="CreatureAction"/> (ids 0–2: thrust, turn,
        /// strafe). Food is seen in a circle around the creature and the four wall readings act as a radar; eye
        /// contracts replace both.
        /// </summary>
        public static readonly NeuralContract Legacy = new NeuralContract(LegacyId, LegacyInputs(),
            new[] { new NeuralChannel(0, "Thrust"), new NeuralChannel(1, "Turn"), new NeuralChannel(2, "Strafe") });

        private static readonly NeuralContract[] Known = { Legacy };

        private readonly NeuralChannel[] inputs;
        private readonly NeuralChannel[] outputs;

        private NeuralContract(string id, NeuralChannel[] inputs, NeuralChannel[] outputs)
        {
            Id = id;
            this.inputs = inputs;
            this.outputs = outputs;
            RequireUniqueIds(inputs, nameof(inputs));
            RequireUniqueIds(outputs, nameof(outputs));
        }

        public string Id { get; }
        public IReadOnlyList<NeuralChannel> Inputs => inputs;
        public IReadOnlyList<NeuralChannel> Outputs => outputs;
        public int InputCount => inputs.Length;
        public int OutputCount => outputs.Length;

        /// <summary>Index of the input channel with this id, or -1 when this contract has no such channel.</summary>
        public int InputIndexOf(int channelId) => IndexOf(inputs, channelId);

        /// <summary>Index of the output channel with this id, or -1 when this contract has no such channel.</summary>
        public int OutputIndexOf(int channelId) => IndexOf(outputs, channelId);

        /// <summary>The contract with this id, if this build knows it.</summary>
        public static bool TryFind(string id, out NeuralContract contract)
        {
            foreach (var known in Known)
            {
                if (known.Id == id)
                {
                    contract = known;
                    return true;
                }
            }

            contract = null;
            return false;
        }

        public override string ToString() => Id;

        private static NeuralChannel[] LegacyInputs()
        {
            var channels = new NeuralChannel[Observation.Size];
            for (var i = 0; i < channels.Length; i++)
                channels[i] = new NeuralChannel(i, ObservationLimits.Name(i));
            return channels;
        }

        private static int IndexOf(NeuralChannel[] channels, int channelId)
        {
            for (var i = 0; i < channels.Length; i++)
            {
                if (channels[i].Id == channelId)
                    return i;
            }

            return -1;
        }

        private static void RequireUniqueIds(NeuralChannel[] channels, string name)
        {
            for (var i = 0; i < channels.Length; i++)
            {
                if (IndexOf(channels, channels[i].Id) != i)
                    throw new ArgumentException($"Channel id {channels[i].Id} appears twice.", name);
            }
        }
    }
}
