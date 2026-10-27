using System;
using System.Collections.Generic;

namespace Eden.Domain
{
    /// <summary>
    /// What one eye reports, in this order, in <see cref="NeuralContract.Eyes"/>. Directions are sine and cosine of the
    /// angle in the body's frame (positive sine = to the left, cosine 1 = straight ahead); proximity is
    /// 1 − distance / the eye's range. A channel whose target is not seen reads 0. Persisted as channel ids: never
    /// renumber.
    /// </summary>
    public enum EyeChannel
    {
        /// <summary>1 when the slot holds an eye, 0 for an absent eye.</summary>
        Present = 0,
        FoodVisible = 1,
        FoodSin = 2,
        FoodCos = 3,
        FoodProximity = 4,

        /// <summary>1 when part of the arena's border lies inside the cone and range.</summary>
        BarrierVisible = 5,
        BarrierSin = 6,
        BarrierCos = 7,
        BarrierProximity = 8
    }

    /// <summary>Number of <see cref="EyeChannel"/> values per eye.</summary>
    public static class EyeChannels
    {
        public const int Count = 9;
    }

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

        /// <summary>Id of <see cref="Eyes"/>.</summary>
        public const string EyesId = "eyes-v1";

        /// <summary>Inputs of <see cref="Eyes"/> about the body itself, before the eye channels.</summary>
        public const int EyesBodyInputs = 4;

        /// <summary>Channel ids of eye slot s start at <see cref="EyeChannelBase"/> + s × <see cref="EyeChannelStride"/>.</summary>
        public const int EyeChannelBase = 100;

        public const int EyeChannelStride = 16;

        /// <summary>
        /// Senses through eyes: energy, age and the two velocities (ids 0–3, the same meaning and ids as in
        /// <see cref="Legacy"/>), then, for each of the <see cref="EyeSet.MaxEyes"/> eye slots in slot order, the
        /// <see cref="EyeChannel"/> channels, with ids <see cref="EyeChannelBase"/> + slot × <see cref="EyeChannelStride"/>
        /// + channel. Every slot is always reserved, so an absent eye reads 0 everywhere (present 0) and is told apart
        /// from a present eye that sees nothing (present 1). There is no wall radar and no circular food sense: only what
        /// an eye's cone and range reach. Same outputs as <see cref="Legacy"/>.
        /// </summary>
        public static readonly NeuralContract Eyes = new NeuralContract(EyesId, EyesInputs(),
            new[] { new NeuralChannel(0, "Thrust"), new NeuralChannel(1, "Turn"), new NeuralChannel(2, "Strafe") });

        private static readonly NeuralContract[] Known = { Legacy, Eyes };

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

        /// <summary>Stable id of one channel of an eye slot.</summary>
        public static int EyeChannelId(int slot, EyeChannel channel) =>
            EyeChannelBase + CheckSlot(slot) * EyeChannelStride + (int)channel;

        /// <summary>Position of one channel of an eye slot in the input vector of <see cref="Eyes"/>.</summary>
        public static int EyeInputIndex(int slot, EyeChannel channel) =>
            EyesBodyInputs + CheckSlot(slot) * EyeChannels.Count + (int)channel;

        private static int CheckSlot(int slot)
        {
            if (slot < 0 || slot >= EyeSet.MaxEyes)
                throw new ArgumentOutOfRangeException(nameof(slot), $"Eye slots are 0 to {EyeSet.MaxEyes - 1}.");
            return slot;
        }

        private static NeuralChannel[] EyesInputs()
        {
            var channels = new NeuralChannel[EyesBodyInputs + EyeSet.MaxEyes * EyeChannels.Count];
            for (var i = 0; i < EyesBodyInputs; i++)
                channels[i] = new NeuralChannel(i, ObservationLimits.Name(i));
            for (var slot = 0; slot < EyeSet.MaxEyes; slot++)
            {
                for (var c = 0; c < EyeChannels.Count; c++)
                {
                    var channel = (EyeChannel)c;
                    channels[EyesBodyInputs + slot * EyeChannels.Count + c] =
                        new NeuralChannel(EyeChannelBase + slot * EyeChannelStride + c, $"Eye{slot}{channel}");
                }
            }

            return channels;
        }

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
