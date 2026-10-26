using System;
using System.Collections.Generic;
using Eden.Domain;
using Eden.Evolution;
using Eden.Simulation;

namespace Eden.Application
{
    /// <summary>
    /// Brain genomes of a shared world. The creatures the run started with (ids 1 … initial population) take theirs
    /// from the founder source; every child gets a mutated copy of its parent's genome, drawn from its own stream
    /// (<see cref="RandomStream.Birth"/> + child id), so a birth never shifts any other draw. Mutation uses the config's
    /// birth probability and sigma and touches only the <see cref="ExpressedBodyGenes"/>, which are also the only body
    /// genes the child expresses. Genomes of dead children are dropped with <see cref="Forget"/>.
    /// </summary>
    public sealed class SharedGenomes : IHeredity
    {
        private readonly SimulationConfig config;
        private readonly Func<CreatureId, Genome> founders;
        private readonly SortedDictionary<int, Genome> born = new SortedDictionary<int, Genome>();

        public SharedGenomes(SimulationConfig config, Func<CreatureId, Genome> founders, BodyGeneSet expressedBodyGenes)
        {
            this.config = config ?? throw new ArgumentNullException(nameof(config));
            this.founders = founders ?? throw new ArgumentNullException(nameof(founders));
            BodyGenes.RequireSet(expressedBodyGenes);
            ExpressedBodyGenes = expressedBodyGenes;
        }

        public BodyGeneSet ExpressedBodyGenes { get; }

        /// <summary>Children whose genome is kept (alive, or not forgotten yet).</summary>
        public int BornCount => born.Count;

        public Genome GenomeOf(CreatureId creature)
        {
            if (born.TryGetValue(creature.Value, out var genome))
                return genome;
            if (creature.Value <= config.InitialPopulation)
                return founders(creature) ?? throw new InvalidOperationException($"No founder genome for {creature}.");
            throw new InvalidOperationException($"{creature} was born in the world but its genome is not kept.");
        }

        public BodyGenes Conceive(CreatureState parent, CreatureId child)
        {
            if (parent == null)
                throw new ArgumentNullException(nameof(parent));
            if (child.Value <= config.InitialPopulation || born.ContainsKey(child.Value))
                throw new InvalidOperationException($"{child} is not a new child.");
            var random = new GaussianSampler(SeedDerivation.CreateCreatureStream(config.Seed, RandomStream.Birth, child));
            var genome = Reproduction.Mutate(GenomeOf(parent.Id), config.BirthMutationProbability, config.BirthMutationSigma,
                ExpressedBodyGenes, random, out _);
            born.Add(child.Value, genome);
            return genome.Body.Masked(ExpressedBodyGenes);
        }

        /// <summary>Drops the genomes of children that died, e.g. with <see cref="WorldSimulation.LastDeaths"/>.</summary>
        public void Forget(IReadOnlyList<CreatureDeath> deaths)
        {
            if (deaths == null)
                throw new ArgumentNullException(nameof(deaths));
            for (var i = 0; i < deaths.Count; i++)
                born.Remove(deaths[i].Creature.Value);
        }

        /// <summary>Genomes of the children kept, in id order.</summary>
        public BornGenome[] Capture()
        {
            var saved = new BornGenome[born.Count];
            var i = 0;
            foreach (var pair in born)
                saved[i++] = new BornGenome(new CreatureId(pair.Key), pair.Value);
            return saved;
        }

        /// <summary>
        /// Gives a new instance the saved children of <paramref name="world"/>: exactly one genome for every living
        /// creature born in the run, expressing the body genes that creature has. Everything is checked first.
        /// </summary>
        public void Restore(IReadOnlyList<BornGenome> saved, WorldState world)
        {
            if (saved == null)
                throw new ArgumentNullException(nameof(saved));
            if (world == null)
                throw new ArgumentNullException(nameof(world));
            if (born.Count > 0)
                throw new InvalidOperationException("Only a new instance can be restored.");

            var genomes = new Dictionary<int, Genome>(saved.Count);
            foreach (var entry in saved)
            {
                if (entry.Genome == null || genomes.ContainsKey(entry.Creature.Value))
                    throw new ArgumentException($"{entry.Creature} is saved twice or without a genome.", nameof(saved));
                genomes.Add(entry.Creature.Value, entry.Genome);
            }

            var children = 0;
            foreach (var creature in world.Creatures)
            {
                if (!creature.Parent.HasValue)
                    continue;
                children++;
                if (!genomes.TryGetValue(creature.Id.Value, out var genome))
                    throw new ArgumentException($"{creature.Id} was born in the world but has no saved genome.", nameof(saved));
                if (!genome.Body.Masked(ExpressedBodyGenes).Equals(creature.Genes))
                    throw new ArgumentException($"{creature.Id}'s saved genome does not express its body genes.", nameof(saved));
            }

            if (children != genomes.Count)
                throw new ArgumentException("Saved genomes must belong to living children of the world.", nameof(saved));
            foreach (var pair in genomes)
                born.Add(pair.Key, pair.Value);
        }
    }

    /// <summary>The genome of a creature born in a shared world.</summary>
    public readonly struct BornGenome
    {
        public BornGenome(CreatureId creature, Genome genome)
        {
            Creature = creature;
            Genome = genome ?? throw new ArgumentNullException(nameof(genome));
        }

        public CreatureId Creature { get; }
        public Genome Genome { get; }
    }
}
