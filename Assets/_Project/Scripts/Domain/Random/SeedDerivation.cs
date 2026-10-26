namespace Eden.Domain
{
    /// <summary>
    /// Independent random streams derived from one run seed. Each consumer owns its stream, so adding
    /// draws in one area (e.g. visual effects) never shifts results in another (e.g. world rules).
    /// Values are persisted in checkpoints: never renumber existing members.
    /// </summary>
    public enum RandomStream : ulong
    {
        World = 1,
        Spawn = 2,
        Mutation = 3,
        Presentation = 4,

        /// <summary>Per-creature streams for stochastic brains; see <see cref="SeedDerivation.CreateCreatureStream"/>.</summary>
        Brain = 5,

        /// <summary>Per-creature streams for initial random genomes.</summary>
        Genome = 6,

        /// <summary>Per-creature streams for the mutation of an offspring born in a shared world (by child id).</summary>
        Birth = 7
    }

    /// <summary>
    /// Stable, documented seed derivation (scheme version 1):
    /// initState = SplitMix64 output number <c>stream</c> for seed <c>runSeed</c>; PCG32 sequence = <c>stream</c>.
    /// Changing this scheme changes every run and requires a new <see cref="SchemeVersion"/>.
    /// </summary>
    public static class SeedDerivation
    {
        public const int SchemeVersion = 1;

        public static Pcg32 CreateStream(ulong runSeed, RandomStream stream)
        {
            var index = (ulong)stream;
            return new Pcg32(SplitMix64.Output(runSeed, index), index);
        }

        /// <summary>
        /// Independent stream per creature: initState = SplitMix64 output number <c>creature id</c> for seed
        /// (SplitMix64 output number <c>stream</c> for <c>runSeed</c>); sequence = <c>stream</c>. Births and deaths
        /// of other creatures never shift a creature's draws.
        /// </summary>
        public static Pcg32 CreateCreatureStream(ulong runSeed, RandomStream stream, CreatureId creature) =>
            CreateIndexedStream(runSeed, stream, (ulong)creature.Value);

        /// <summary>
        /// Independent sub-stream number <paramref name="index"/> of a stream (e.g. one per generation), with the same
        /// derivation as <see cref="CreateCreatureStream"/>: initState = SplitMix64 output number <c>index</c> for seed
        /// (SplitMix64 output number <c>stream</c> for <c>runSeed</c>); sequence = <c>stream</c>.
        /// </summary>
        public static Pcg32 CreateIndexedStream(ulong runSeed, RandomStream stream, ulong index)
        {
            var streamIndex = (ulong)stream;
            var streamSeed = SplitMix64.Output(runSeed, streamIndex);
            return new Pcg32(SplitMix64.Output(streamSeed, index), streamIndex);
        }
    }

    /// <summary>SplitMix64 (S. Vigna), used only to turn one seed into well-mixed stream seeds.</summary>
    public static class SplitMix64
    {
        private const ulong Gamma = 0x9E3779B97F4A7C15UL;

        /// <summary>The n-th output (n starting at 1) of a SplitMix64 generator seeded with <paramref name="seed"/>.</summary>
        public static ulong Output(ulong seed, ulong n)
        {
            var z = unchecked(seed + n * Gamma);
            z = unchecked((z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL);
            z = unchecked((z ^ (z >> 27)) * 0x94D049BB133111EBUL);
            return z ^ (z >> 31);
        }
    }
}
