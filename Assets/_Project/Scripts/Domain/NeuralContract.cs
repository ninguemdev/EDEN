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
        BarrierProximity = 8,

        /// <summary>1 when part of a water source lies inside the cone and range with a free line to it; only in
        /// <see cref="NeuralContract.EyesWater"/>.</summary>
        WaterVisible = 9,
        WaterSin = 10,
        WaterCos = 11,
        WaterProximity = 12
    }

    /// <summary>Number of <see cref="EyeChannel"/> values per eye.</summary>
    public static class EyeChannels
    {
        /// <summary>Channels per eye in <see cref="NeuralContract.Eyes"/>: present, food and barrier.</summary>
        public const int Count = 9;

        /// <summary>Channels per eye in <see cref="NeuralContract.EyesWater"/>: those of <see cref="Count"/>, then water.</summary>
        public const int WithWater = 13;
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
        public static readonly NeuralContract Eyes = new NeuralContract(EyesId, EyesInputs(false),
            new[] { new NeuralChannel(0, "Thrust"), new NeuralChannel(1, "Turn"), new NeuralChannel(2, "Strafe") },
            EyeChannels.Count);

        /// <summary>Id of <see cref="EyesWater"/>.</summary>
        public const string EyesWaterId = "eyes-water-v1";

        /// <summary>Id of the hydration channel (hydration / max hydration, [0, 1]); no other contract used 12.</summary>
        public const int HydrationChannelId = 12;

        /// <summary>Position of the hydration channel in <see cref="EyesWater"/>, right after the body channels.</summary>
        public const int HydrationInput = EyesBodyInputs;

        /// <summary>
        /// Eyes in a world with water: the body channels of <see cref="Eyes"/>, then hydration
        /// (<see cref="HydrationChannelId"/>), then, for each eye slot, the channels of <see cref="Eyes"/> with the same
        /// ids followed by the water channels (<see cref="EyeChannel.WaterVisible"/> to
        /// <see cref="EyeChannel.WaterProximity"/>, ids within the slot's stride). Positions differ from
        /// <see cref="Eyes"/>, so its genomes do not read this contract. Same outputs.
        /// </summary>
        public static readonly NeuralContract EyesWater = new NeuralContract(EyesWaterId, EyesInputs(true),
            new[] { new NeuralChannel(0, "Thrust"), new NeuralChannel(1, "Turn"), new NeuralChannel(2, "Strafe") },
            EyeChannels.WithWater);

        private static readonly NeuralContract[] Known = { Legacy, Eyes, EyesWater };

        private readonly NeuralChannel[] inputs;
        private readonly NeuralChannel[] outputs;

        private NeuralContract(string id, NeuralChannel[] inputs, NeuralChannel[] outputs, int channelsPerEye = 0)
        {
            Id = id;
            this.inputs = inputs;
            this.outputs = outputs;
            ChannelsPerEye = channelsPerEye;
            RequireUniqueIds(inputs, nameof(inputs));
            RequireUniqueIds(outputs, nameof(outputs));
        }

        public string Id { get; }
        public IReadOnlyList<NeuralChannel> Inputs => inputs;
        public IReadOnlyList<NeuralChannel> Outputs => outputs;
        public int InputCount => inputs.Length;
        public int OutputCount => outputs.Length;

        /// <summary>Whether this contract senses through eyes (its genomes carry an <see cref="EyeSet"/>).</summary>
        public bool HasEyes => ChannelsPerEye > 0;

        /// <summary>Whether its brains feel their hydration and their eyes see water.</summary>
        public bool SensesWater => ChannelsPerEye == EyeChannels.WithWater;

        /// <summary>Inputs per eye slot; 0 without eyes.</summary>
        public int ChannelsPerEye { get; }

        /// <summary>Inputs about the body itself, before the eye slots; meaningful only with eyes.</summary>
        public int BodyInputs => SensesWater ? EyesBodyInputs + 1 : EyesBodyInputs;

        /// <summary>The eye contract of a world: <see cref="EyesWater"/> when it has water, else <see cref="Eyes"/>.</summary>
        public static NeuralContract ForEyes(bool water) => water ? EyesWater : Eyes;

        /// <summary>The eye contract whose input vector has this length, or null.</summary>
        public static NeuralContract OfEyeInputs(int inputCount) =>
            inputCount == Eyes.InputCount ? Eyes : inputCount == EyesWater.InputCount ? EyesWater : null;

        /// <summary>Position of one channel of an eye slot in this contract's input vector.</summary>
        public int EyeInput(int slot, EyeChannel channel)
        {
            if ((int)channel < 0 || (int)channel >= ChannelsPerEye)
                throw new ArgumentOutOfRangeException(nameof(channel), $"{Id} has no eye channel {channel}.");
            return BodyInputs + CheckSlot(slot) * ChannelsPerEye + (int)channel;
        }

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
        public static int EyeInputIndex(int slot, EyeChannel channel) => Eyes.EyeInput(slot, channel);

        private static int CheckSlot(int slot)
        {
            if (slot < 0 || slot >= EyeSet.MaxEyes)
                throw new ArgumentOutOfRangeException(nameof(slot), $"Eye slots are 0 to {EyeSet.MaxEyes - 1}.");
            return slot;
        }

        private static NeuralChannel[] EyesInputs(bool water)
        {
            var body = water ? EyesBodyInputs + 1 : EyesBodyInputs;
            var perEye = water ? EyeChannels.WithWater : EyeChannels.Count;
            var channels = new NeuralChannel[body + EyeSet.MaxEyes * perEye];
            for (var i = 0; i < EyesBodyInputs; i++)
                channels[i] = new NeuralChannel(i, ObservationLimits.Name(i));
            if (water)
                channels[EyesBodyInputs] = new NeuralChannel(HydrationChannelId, "Hydration");
            for (var slot = 0; slot < EyeSet.MaxEyes; slot++)
            {
                for (var c = 0; c < perEye; c++)
                {
                    var channel = (EyeChannel)c;
                    channels[body + slot * perEye + c] =
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
