using System;
using System.Collections.Generic;
using Eden.Domain;

namespace Eden.Evolution
{
    /// <summary>
    /// A genome with its identity: a unique id, its parents (none for the first generation, one for a mutated copy,
    /// two for a NEAT crossover child, the fitter first), the generation it was born in and how many of its weights mutated. An elite is the same
    /// individual in a later generation: it keeps its id and birth generation.
    /// </summary>
    public sealed class Individual
    {
        private readonly long[] parentIds;

        public Individual(long id, IReadOnlyList<long> parentIds, int birthGeneration, Genome genome, int mutatedWeights)
        {
            if (id <= 0)
                throw new ArgumentOutOfRangeException(nameof(id), "Ids start at 1.");
            if (parentIds == null)
                throw new ArgumentNullException(nameof(parentIds));
            if (birthGeneration < 0)
                throw new ArgumentOutOfRangeException(nameof(birthGeneration));

            Id = id;
            this.parentIds = new long[parentIds.Count];
            for (var i = 0; i < this.parentIds.Length; i++)
                this.parentIds[i] = parentIds[i];
            BirthGeneration = birthGeneration;
            Genome = genome ?? throw new ArgumentNullException(nameof(genome));
            MutatedWeights = mutatedWeights;
        }

        public long Id { get; }
        public IReadOnlyList<long> ParentIds => parentIds;
        public int BirthGeneration { get; }
        public Genome Genome { get; }
        public int MutatedWeights { get; }
    }

    /// <summary>
    /// Hands out ids in creation order and remembers every individual's parents and birth generation (not its
    /// weights), so the ancestry of any id can be traced back to the first generation.
    /// </summary>
    public sealed class Lineage
    {
        private readonly Dictionary<long, LineageEntry> entries = new Dictionary<long, LineageEntry>();

        /// <summary>Id the next new individual will get.</summary>
        public long NextId { get; private set; } = 1;

        public int Count => entries.Count;

        public bool Contains(long id) => entries.ContainsKey(id);

        /// <summary>
        /// Lineage of a saved experiment. Ids are handed out without gaps, so <paramref name="saved"/>[i] must describe
        /// id i + 1; parents must come earlier, founders (no parents) belong to generation 0 and children are born
        /// after their parents. Everything is checked before the lineage is built.
        /// </summary>
        public static Lineage Restore(IReadOnlyList<LineageEntry> saved)
        {
            if (saved == null)
                throw new ArgumentNullException(nameof(saved));

            for (var i = 0; i < saved.Count; i++)
            {
                var entry = saved[i];
                if (entry.Id != i + 1)
                    throw new ArgumentException($"Entry {i} has id {entry.Id}; ids must run from 1 without gaps.", nameof(saved));
                if (entry.ParentIds == null || (entry.ParentIds.Count == 0) != (entry.BirthGeneration == 0))
                    throw new ArgumentException($"Individual {entry.Id}: only generation 0 has no parents.", nameof(saved));
                foreach (var parent in entry.ParentIds)
                {
                    if (parent < 1 || parent >= entry.Id || saved[(int)parent - 1].BirthGeneration >= entry.BirthGeneration)
                        throw new ArgumentException($"Individual {entry.Id}: parent {parent} must be an earlier individual " +
                                                    "born in an earlier generation.", nameof(saved));
                }
            }

            var lineage = new Lineage();
            foreach (var entry in saved)
                lineage.entries.Add(entry.Id, entry);
            lineage.NextId = saved.Count + 1;
            return lineage;
        }

        /// <summary>Every individual created so far, in id order (what <see cref="Restore"/> takes back).</summary>
        public LineageEntry[] Entries()
        {
            var all = new LineageEntry[entries.Count];
            for (var i = 0; i < all.Length; i++)
                all[i] = entries[i + 1];
            return all;
        }

        /// <summary>Generation 0: one new individual per genome, without parents.</summary>
        public Individual[] Founders(IReadOnlyList<Genome> genomes)
        {
            if (genomes == null)
                throw new ArgumentNullException(nameof(genomes));
            var founders = new Individual[genomes.Count];
            for (var i = 0; i < founders.Length; i++)
                founders[i] = Register(Array.Empty<long>(), 0, genomes[i], 0);
            return founders;
        }

        /// <summary>
        /// Individuals of <paramref name="generation"/> from the offspring of <paramref name="parents"/>: elites stay the
        /// same individuals, every other child is new and records its parent's id (both, fitter first, after a crossover
        /// of two different parents).
        /// </summary>
        public Individual[] Descend(IReadOnlyList<Individual> parents, IReadOnlyList<Offspring> offspring, int generation)
        {
            if (parents == null)
                throw new ArgumentNullException(nameof(parents));
            if (offspring == null)
                throw new ArgumentNullException(nameof(offspring));
            if (generation < 1)
                throw new ArgumentOutOfRangeException(nameof(generation));

            var next = new Individual[offspring.Count];
            for (var i = 0; i < next.Length; i++)
            {
                var child = offspring[i];
                var parent = parents[child.ParentIndex];
                if (!entries.ContainsKey(parent.Id))
                    throw new ArgumentException($"Parent {parent.Id} does not belong to this lineage.", nameof(parents));
                if (child.IsElite)
                {
                    next[i] = parent;
                    continue;
                }

                var second = child.SecondParentIndex >= 0 ? parents[child.SecondParentIndex] : null;
                var parentIds = second == null || second.Id == parent.Id ? new[] { parent.Id } : new[] { parent.Id, second.Id };
                next[i] = Register(parentIds, generation, child.Genome, child.MutatedWeights);
            }

            return next;
        }

        public int BirthGeneration(long id) => Get(id).BirthGeneration;

        public IReadOnlyList<long> ParentIds(long id) => Get(id).ParentIds;

        /// <summary>The id followed by its parent, grandparent and so on, ending with a first-generation founder.</summary>
        public IReadOnlyList<long> Ancestry(long id)
        {
            var chain = new List<long> { id };
            var entry = Get(id);
            while (entry.ParentIds.Count > 0)
            {
                var parent = entry.ParentIds[0];
                chain.Add(parent);
                entry = Get(parent);
            }

            return chain;
        }

        private Individual Register(long[] parentIds, int generation, Genome genome, int mutatedWeights)
        {
            var individual = new Individual(NextId, parentIds, generation, genome, mutatedWeights);
            entries.Add(NextId, new LineageEntry(NextId, parentIds, generation));
            NextId++;
            return individual;
        }

        private LineageEntry Get(long id)
        {
            if (!entries.TryGetValue(id, out var entry))
                throw new KeyNotFoundException($"Individual {id} is not in this lineage.");
            return entry;
        }

    }

    /// <summary>One individual of a <see cref="Lineage"/>: id, parents and birth generation (never its weights).</summary>
    public readonly struct LineageEntry
    {
        private readonly long[] parentIds;

        public LineageEntry(long id, IReadOnlyList<long> parentIds, int birthGeneration)
        {
            if (parentIds == null)
                throw new ArgumentNullException(nameof(parentIds));
            Id = id;
            this.parentIds = new long[parentIds.Count];
            for (var i = 0; i < this.parentIds.Length; i++)
                this.parentIds[i] = parentIds[i];
            BirthGeneration = birthGeneration;
        }

        public long Id { get; }
        public IReadOnlyList<long> ParentIds => parentIds;
        public int BirthGeneration { get; }
    }
}
