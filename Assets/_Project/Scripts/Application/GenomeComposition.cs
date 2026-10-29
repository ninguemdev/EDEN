using System;
using System.Collections.Generic;
using System.Globalization;
using Eden.Domain;
using Eden.Evolution;
using Eden.Simulation;

namespace Eden.Application
{
    /// <summary>One genome of a <see cref="GenomeComposition"/> and how many creatures start with it.</summary>
    public sealed class CompositionEntry
    {
        public CompositionEntry(Genome genome, int count, string source)
        {
            Genome = genome ?? throw new ArgumentNullException(nameof(genome));
            if (count < 1)
                throw new ArgumentOutOfRangeException(nameof(count), "Every genome of a composition has at least one copy.");
            Count = count;
            Source = string.IsNullOrEmpty(source) ? "genoma" : source;
        }

        public Genome Genome { get; }
        public int Count { get; }

        /// <summary>Where the genome came from, for people (e.g. its file name).</summary>
        public string Source { get; }
    }

    /// <summary>
    /// The founders of a shared world by composition (planning, section 9): N copies of genome A, M of genome B, … in
    /// that order, so founder ids 1 … N are A's, the next M are B's. Copies share their heredity (the same genome, body
    /// and eyes) and nothing else: each is a creature with its own position, energy, brain state and life. Two explicit
    /// modes: <see cref="Frozen"/> genomes face each other unchanged (no reproduction, so no genome ever changes), or
    /// founders have descendants that mutate as in any shared world, recorded in the genealogy. Children mutate the body
    /// genes some founder expresses (a gene that is 0 in every founder stays 0) and, when a founder has them, plasticity
    /// coefficients; eyes are inherited unchanged. Every genome is checked against the world before anything starts.
    /// </summary>
    public sealed class GenomeComposition
    {
        private readonly CompositionEntry[] entries;
        private readonly int[] firstFounder;

        public GenomeComposition(IReadOnlyList<CompositionEntry> entries, bool frozen)
        {
            if (entries == null || entries.Count == 0)
                throw new ArgumentException("A composition needs at least one genome.", nameof(entries));
            this.entries = new CompositionEntry[entries.Count];
            firstFounder = new int[entries.Count];
            long total = 0;
            for (var i = 0; i < entries.Count; i++)
            {
                this.entries[i] = entries[i] ?? throw new ArgumentException($"Entry {i} is null.", nameof(entries));
                firstFounder[i] = (int)Math.Min(total + 1, int.MaxValue);
                total += entries[i].Count;
            }

            if (total > SimulationConfig.MaxPopulation)
                throw new ArgumentException($"{total} creatures exceed the limit of {SimulationConfig.MaxPopulation}.",
                    nameof(entries));
            Total = (int)total;
            Frozen = frozen;
            var expressed = BodyGeneSet.None;
            foreach (var entry in this.entries)
            {
                var body = entry.Genome.Body;
                if (body.Size != 0f) expressed |= BodyGeneSet.Size;
                if (body.Speed != 0f) expressed |= BodyGeneSet.Speed;
                if (body.Sensor != 0f) expressed |= BodyGeneSet.Sensor;
                if (body.Efficiency != 0f) expressed |= BodyGeneSet.Efficiency;
                if (entry.Genome.Neat == null && !entry.Genome.Plasticity.IsFixed)
                    MutatesPlasticity = true;
            }

            ExpressedBodyGenes = expressed;
        }

        public IReadOnlyList<CompositionEntry> Entries => entries;

        /// <summary>Creatures the world starts with: the sum of every entry's copies.</summary>
        public int Total { get; }

        /// <summary>Genomes face each other unchanged (no reproduction); otherwise founders have mutating descendants.</summary>
        public bool Frozen { get; }

        /// <summary>Body genes some founder expresses: the ones children may mutate.</summary>
        public BodyGeneSet ExpressedBodyGenes { get; }

        /// <summary>Whether some founder is a plastic MLP, so children's plasticity coefficients mutate.</summary>
        public bool MutatesPlasticity { get; }

        /// <summary>Index of the entry founder <paramref name="founder"/> (1 … <see cref="Total"/>) is a copy of.</summary>
        public int EntryOf(CreatureId founder)
        {
            if (founder.Value < 1 || founder.Value > Total)
                throw new ArgumentOutOfRangeException(nameof(founder), $"{founder} is not a founder of this composition.");
            var index = Array.BinarySearch(firstFounder, founder.Value);
            return index >= 0 ? index : ~index - 1;
        }

        /// <summary>Which copy of its entry a founder is, from 1.</summary>
        public int CopyOf(CreatureId founder) => founder.Value - firstFounder[EntryOf(founder)] + 1;

        public Genome GenomeOf(CreatureId founder) => entries[EntryOf(founder)].Genome;

        /// <summary>
        /// Writes the population and reproduction into a shared world's settings: <see cref="Total"/> founders,
        /// reproduction off when frozen and on otherwise (with room for at least the founders).
        /// </summary>
        public void Apply(SimulationSettings world)
        {
            if (world == null)
                throw new ArgumentNullException(nameof(world));
            world.InitialPopulation = Total;
            world.Reproduction = !Frozen;
            world.PopulationCap = Math.Max(world.PopulationCap, Total);
        }

        /// <summary>
        /// Why each genome cannot live in this world, one line per refused genome (empty when all fit): a brain must read
        /// what the world senses (eyes-water-v1 only with water, eyes-v1 only without; the legacy contract anywhere).
        /// </summary>
        public IReadOnlyList<string> Refusals(SimulationConfig config)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));
            var refusals = new List<string>();
            var eyes = Sensors.EyeContract(config);
            foreach (var entry in entries)
            {
                var contract = entry.Genome.Contract;
                if (contract.HasEyes && contract != eyes)
                    refusals.Add($"{entry.Source}: sees with {contract.Id}, but this world needs {eyes.Id} " +
                                 (config.HasWater ? "(it has water)" : "(it has no water)"));
                else if (!contract.HasEyes && contract != NeuralContract.Legacy)
                    refusals.Add($"{entry.Source}: contract {contract.Id} cannot live in a shared world");
            }

            return refusals;
        }

        /// <summary>Throws with every refusal when some genome does not fit the world.</summary>
        public void RequireFits(SimulationConfig config)
        {
            var refusals = Refusals(config);
            if (refusals.Count > 0)
                throw new ArgumentException("Composition refused: " + string.Join("; ", refusals));
            if (config.InitialPopulation != Total)
                throw new ArgumentException($"The world starts with {config.InitialPopulation} creatures, the composition " +
                                            $"with {Total}.");
            if (config.Reproduction == Frozen)
                throw new ArgumentException(Frozen
                    ? "Frozen genomes face each other without reproduction."
                    : "Descendants need reproduction.");
        }

        /// <summary>The world's first creatures, as the composition says: each founder with its genome's body and eyes.</summary>
        public WorldState CreateWorld(SimulationConfig config)
        {
            RequireFits(config);
            var world = WorldFactory.CreateInitial(config, i => entries[EntryOf(new CreatureId(i + 1))].Genome.Body);
            foreach (var creature in world.Creatures)
                creature.Eyes = GenomeOf(creature.Id).Eyes;
            return world;
        }

        /// <summary>Brain genomes of the world: founders from the composition, children mutated from their parent.</summary>
        public SharedGenomes CreateGenomes(SimulationConfig config) =>
            new SharedGenomes(config, GenomeOf, ExpressedBodyGenes, MutatesPlasticity);

        /// <summary>"A ×20 + B ×5 = 25", for people.</summary>
        public string Describe()
        {
            var parts = new string[entries.Length];
            for (var i = 0; i < parts.Length; i++)
                parts[i] = entries[i].Source + " ×" + entries[i].Count.ToString(CultureInfo.InvariantCulture);
            return string.Join(" + ", parts) + " = " + Total.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Reads a composition such as <c>genome-a.json*20; #2*5</c> against a pool of named genomes: each part names a
        /// genome by its file name (with or without <c>.json</c>) or by its rank in the pool (<c>#1</c> is the highest
        /// fitness, ties in name order), then <c>*N</c> (or <c>×N</c>) copies, 1 when omitted. Parts are separated by
        /// <c>;</c>, <c>,</c> or line breaks. An empty text takes one copy of every genome of the pool, in name order.
        /// </summary>
        public static GenomeComposition Parse(string text, IReadOnlyList<string> names, IReadOnlyList<Genome> genomes,
            IReadOnlyList<float> fitness, bool frozen)
        {
            if (names == null || genomes == null || fitness == null || names.Count != genomes.Count ||
                names.Count != fitness.Count || names.Count == 0)
                throw new ArgumentException("A composition needs a pool of named genomes, each with a fitness.");
            var entries = new List<CompositionEntry>();
            if (string.IsNullOrWhiteSpace(text))
            {
                for (var i = 0; i < genomes.Count; i++)
                    entries.Add(new CompositionEntry(genomes[i], 1, names[i]));
                return new GenomeComposition(entries, frozen);
            }

            var ranking = Reproduction.Rank(fitness);
            foreach (var raw in text.Split(new[] { ';', ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var part = raw.Trim();
                if (part.Length == 0)
                    continue;
                var star = part.LastIndexOfAny(new[] { '*', '×' });
                var name = (star >= 0 ? part.Substring(0, star) : part).Trim();
                var count = 1;
                if (star >= 0 && (!int.TryParse(part.Substring(star + 1).Trim(), NumberStyles.None,
                                      CultureInfo.InvariantCulture, out count) || count < 1))
                    throw new ArgumentException($"\"{part}\": the number of copies must be a whole number from 1.");
                int index;
                if (name.StartsWith("#", StringComparison.Ordinal))
                {
                    if (!int.TryParse(name.Substring(1), NumberStyles.None, CultureInfo.InvariantCulture, out var rank) ||
                        rank < 1 || rank > genomes.Count)
                        throw new ArgumentException($"\"{part}\": the pool ranks go from #1 to #{genomes.Count}.");
                    index = ranking[rank - 1];
                }
                else
                {
                    index = IndexOfName(names, name);
                    if (index < 0)
                        throw new ArgumentException($"\"{part}\": the pool has no genome named {name}.");
                }

                entries.Add(new CompositionEntry(genomes[index], count, names[index]));
            }

            if (entries.Count == 0)
                throw new ArgumentException("The composition names no genome.");
            return new GenomeComposition(entries, frozen);
        }

        private static int IndexOfName(IReadOnlyList<string> names, string name)
        {
            for (var i = 0; i < names.Count; i++)
            {
                if (string.Equals(names[i], name, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(names[i], name + ".json", StringComparison.OrdinalIgnoreCase))
                    return i;
            }

            return -1;
        }
    }
}
