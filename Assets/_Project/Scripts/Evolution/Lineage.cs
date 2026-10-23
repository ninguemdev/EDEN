using System;
using System.Collections.Generic;
using Eden.Domain;

namespace Eden.Evolution
{
    /// <summary>
    /// A genome with its identity: a unique id, its parents (one parent while reproduction is asexual, none for the
    /// first generation), the generation it was born in and how many of its weights mutated. An elite is the same
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
        private readonly Dictionary<long, Entry> entries = new Dictionary<long, Entry>();

        /// <summary>Id the next new individual will get.</summary>
        public long NextId { get; private set; } = 1;

        public int Count => entries.Count;

        public bool Contains(long id) => entries.ContainsKey(id);

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
        /// same individuals, every other child is new and records its parent's id.
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
                next[i] = child.IsElite
                    ? parent
                    : Register(new[] { parent.Id }, generation, child.Genome, child.MutatedWeights);
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
            while (entry.ParentIds.Length > 0)
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
            entries.Add(NextId, new Entry(parentIds, generation));
            NextId++;
            return individual;
        }

        private Entry Get(long id)
        {
            if (!entries.TryGetValue(id, out var entry))
                throw new KeyNotFoundException($"Individual {id} is not in this lineage.");
            return entry;
        }

        private readonly struct Entry
        {
            public Entry(long[] parentIds, int birthGeneration)
            {
                ParentIds = parentIds;
                BirthGeneration = birthGeneration;
            }

            public long[] ParentIds { get; }
            public int BirthGeneration { get; }
        }
    }
}
