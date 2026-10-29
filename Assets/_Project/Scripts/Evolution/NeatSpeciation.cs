using System;
using System.Collections.Generic;
using Eden.Domain;

namespace Eden.Evolution
{
    /// <summary>One NEAT species as it continues between generations; its members are only those of the current one.</summary>
    public sealed class NeatSpecies
    {
        private readonly List<int> members = new List<int>();

        internal NeatSpecies(int id, Genome representative, int createdGeneration, float bestFitness,
            int lastImprovedGeneration)
        {
            Id = id;
            Representative = representative;
            CreatedGeneration = createdGeneration;
            BestFitness = bestFitness;
            LastImprovedGeneration = lastImprovedGeneration;
        }

        public int Id { get; }

        /// <summary>Genome new members are compared with: a member of the generation last assigned.</summary>
        public Genome Representative { get; internal set; }

        public int CreatedGeneration { get; }

        /// <summary>Best valid fitness any member ever had (−∞ until one is valid).</summary>
        public float BestFitness { get; internal set; }

        /// <summary>Generation in which <see cref="BestFitness"/> last improved (or the species appeared).</summary>
        public int LastImprovedGeneration { get; internal set; }

        /// <summary>Indices, in the generation last assigned, of the members, ascending.</summary>
        public IReadOnlyList<int> Members => members;

        internal List<int> MemberList => members;

        public NeatSpeciesState Capture() =>
            new NeatSpeciesState(Id, Representative, CreatedGeneration, BestFitness, LastImprovedGeneration);
    }

    /// <summary>What a <see cref="NeatSpecies"/> keeps between generations, for checkpoints.</summary>
    public readonly struct NeatSpeciesState
    {
        public NeatSpeciesState(int id, Genome representative, int createdGeneration, float bestFitness,
            int lastImprovedGeneration)
        {
            Id = id;
            Representative = representative;
            CreatedGeneration = createdGeneration;
            BestFitness = bestFitness;
            LastImprovedGeneration = lastImprovedGeneration;
        }

        public int Id { get; }
        public Genome Representative { get; }
        public int CreatedGeneration { get; }
        public float BestFitness { get; }
        public int LastImprovedGeneration { get; }
    }

    /// <summary>A NEAT child, where it came from and what changed.</summary>
    public readonly struct NeatOffspring
    {
        public NeatOffspring(Genome genome, int firstParent, int secondParent, bool isChampion, int species,
            NeatMutationCount mutation)
        {
            Genome = genome ?? throw new ArgumentNullException(nameof(genome));
            FirstParent = firstParent;
            SecondParent = secondParent;
            IsChampion = isChampion;
            Species = species;
            Mutation = mutation;
        }

        public Genome Genome { get; }

        /// <summary>Index of the fitter (or only) parent in the previous generation.</summary>
        public int FirstParent { get; }

        /// <summary>Index of the other crossover parent, or -1 for a child of one parent.</summary>
        public int SecondParent { get; }

        /// <summary>A species champion copied unchanged; it carries no score and is evaluated again.</summary>
        public bool IsChampion { get; }

        /// <summary>Species of the first parent.</summary>
        public int Species { get; }

        public NeatMutationCount Mutation { get; }
    }

    /// <summary>
    /// Species of a NEAT experiment and the selection that uses them (Stanley and Miikkulainen, 2002, section 3.3):
    /// <list type="number">
    /// <item><see cref="Assign"/>: every genome, in index order, joins the first species (in id order) whose
    /// representative is closer than <see cref="NeatRules.CompatibilityThreshold"/>, or founds a new one; empty species
    /// disappear, and each species takes as its next representative the member closest to the old one (ties: lower
    /// index).</item>
    /// <item><see cref="Breed"/>: fitness is shared within each species: invalid scores count as the worst valid
    /// one, scores are shifted so the worst valid is 0, and a species' share is the mean shifted score of its members
    /// (the sum of the adjusted fitness f/|s|). A species whose best never improved for
    /// <see cref="NeatRules.StagnationGenerations"/> generations gets nothing and is dropped, except the
    /// <see cref="NeatRules.ProtectedSpecies"/> with the best fitness so far. Children are divided by shares with the
    /// largest-remainder method (ties: lower species id), so the population size is exact; with no positive share,
    /// by species size.</item>
    /// <item>Within a species (in id order): the champion is copied unchanged when the species has at least
    /// <see cref="NeatRules.ChampionMinimumSize"/> members; every other child picks a parent among the best
    /// <see cref="NeatRules.SurvivalFraction"/> of its species, crosses it with a second parent (same species, or another
    /// with <see cref="NeatRules.InterspeciesProbability"/>) with <see cref="NeatRules.CrossoverProbability"/>, the
    /// better ranked one as the fitter, and then mutates.</item>
    /// </list>
    /// Ranking is by fitness, NaN and −∞ last, ties by lower index. Every draw comes from the one stream given to
    /// <see cref="Breed"/>, in the order above, so a generation is rebuilt exactly from its parents.
    /// </summary>
    public sealed class NeatSpeciation
    {
        private readonly NeatRules rules;
        private readonly List<NeatSpecies> species = new List<NeatSpecies>();
        private int assignedCount = -1;

        public NeatSpeciation(NeatRules rules)
        {
            this.rules = rules ?? throw new ArgumentNullException(nameof(rules));
            NextSpeciesId = 1;
        }

        /// <summary>Living species in id order.</summary>
        public IReadOnlyList<NeatSpecies> Species => species;

        public int NextSpeciesId { get; private set; }

        /// <summary>
        /// Places the genomes of generation <paramref name="generation"/> into species and returns each one's species
        /// id, by index.
        /// </summary>
        public int[] Assign(IReadOnlyList<Genome> population, int generation)
        {
            if (population == null)
                throw new ArgumentNullException(nameof(population));
            foreach (var current in species)
                current.MemberList.Clear();

            var speciesOf = new int[population.Count];
            for (var i = 0; i < population.Count; i++)
            {
                var genome = population[i] ?? throw new ArgumentException($"Genome {i} is null.", nameof(population));
                NeatSpecies home = null;
                foreach (var candidate in species)
                {
                    if (NeatCrossover.Distance(genome, candidate.Representative, rules) < rules.CompatibilityThreshold)
                    {
                        home = candidate;
                        break;
                    }
                }

                if (home == null)
                {
                    home = new NeatSpecies(NextSpeciesId++, genome, generation, Fitness.Worst, generation);
                    species.Add(home);
                }

                home.MemberList.Add(i);
                speciesOf[i] = home.Id;
            }

            species.RemoveAll(s => s.MemberList.Count == 0);
            foreach (var current in species)
                current.Representative = ClosestMember(current, population);
            assignedCount = population.Count;
            return speciesOf;
        }

        /// <summary>
        /// Next generation (<paramref name="populationSize"/> children) from the generation last passed to
        /// <see cref="Assign"/> and its fitness. Updates every species' best fitness and drops stagnant species.
        /// </summary>
        public NeatOffspring[] Breed(IReadOnlyList<Genome> population, IReadOnlyList<float> fitness, int generation,
            int populationSize, InnovationHistory history, GaussianSampler random, BodyMutation body = default)
        {
            if (population == null)
                throw new ArgumentNullException(nameof(population));
            if (fitness == null)
                throw new ArgumentNullException(nameof(fitness));
            if (history == null)
                throw new ArgumentNullException(nameof(history));
            if (random == null)
                throw new ArgumentNullException(nameof(random));
            if (population.Count != assignedCount || fitness.Count != population.Count)
                throw new ArgumentException("Assign this population first; every genome needs one fitness value.",
                    nameof(population));
            if (populationSize < 1 || populationSize > ReproductionRules.MaxPopulationSize)
                throw new ArgumentOutOfRangeException(nameof(populationSize));

            history.BeginGeneration(generation + 1);
            var scores = SelectionScores(fitness);
            var breeding = UpdateStagnation(fitness, generation);
            var counts = Allocate(breeding, scores, populationSize);
            var parentsOf = new int[breeding.Count][];
            for (var s = 0; s < breeding.Count; s++)
                parentsOf[s] = Survivors(breeding[s], fitness);

            var children = new List<NeatOffspring>(populationSize);
            var source = random.Source;
            for (var s = 0; s < breeding.Count; s++)
            {
                var current = breeding[s];
                var left = counts[s];
                if (left > 0 && current.Members.Count >= rules.ChampionMinimumSize)
                {
                    var champion = parentsOf[s][0];
                    children.Add(new NeatOffspring(population[champion], champion, -1, true, current.Id,
                        default(NeatMutationCount)));
                    left--;
                }

                var pool = parentsOf[s];
                for (; left > 0; left--)
                {
                    var first = pool[(int)source.NextUInt((uint)pool.Length)];
                    var second = -1;
                    var genome = population[first];
                    if (source.NextFloat() < rules.CrossoverProbability)
                    {
                        var partners = pool;
                        if (breeding.Count > 1 && source.NextFloat() < rules.InterspeciesProbability)
                        {
                            var other = (int)source.NextUInt((uint)(breeding.Count - 1));
                            partners = parentsOf[other >= s ? other + 1 : other];
                        }

                        second = partners[(int)source.NextUInt((uint)partners.Length)];
                        if (IsBetter(fitness, second, first))
                        {
                            var swap = first;
                            first = second;
                            second = swap;
                        }

                        genome = NeatCrossover.Cross(population[first], population[second], rules, source);
                    }

                    genome = NeatMutation.Mutate(genome, rules, history, random, out var mutation, body);
                    children.Add(new NeatOffspring(genome, first, second, false, current.Id, mutation));
                }
            }

            species.RemoveAll(s => !breeding.Contains(s));
            return children.ToArray();
        }

        public NeatSpeciesState[] Capture()
        {
            var saved = new NeatSpeciesState[species.Count];
            for (var i = 0; i < saved.Length; i++)
                saved[i] = species[i].Capture();
            return saved;
        }

        /// <summary>Species saved between generations (before the next <see cref="Assign"/>), in increasing id order.</summary>
        public static NeatSpeciation Restore(NeatRules rules, IReadOnlyList<NeatSpeciesState> saved, int nextSpeciesId)
        {
            if (saved == null)
                throw new ArgumentNullException(nameof(saved));
            var speciation = new NeatSpeciation(rules);
            var previous = 0;
            foreach (var state in saved)
            {
                if (state.Id <= previous || state.Id >= nextSpeciesId)
                    throw new ArgumentException($"Species {state.Id} is out of order or not below {nextSpeciesId}.",
                        nameof(saved));
                if (state.Representative?.Neat == null)
                    throw new ArgumentException($"Species {state.Id} needs a NEAT representative.", nameof(saved));
                if (state.CreatedGeneration < 0 || state.LastImprovedGeneration < state.CreatedGeneration ||
                    float.IsNaN(state.BestFitness) || float.IsPositiveInfinity(state.BestFitness))
                    throw new ArgumentException($"Species {state.Id} has impossible values.", nameof(saved));
                speciation.species.Add(new NeatSpecies(state.Id, state.Representative, state.CreatedGeneration,
                    state.BestFitness, state.LastImprovedGeneration));
                previous = state.Id;
            }

            speciation.NextSpeciesId = nextSpeciesId;
            return speciation;
        }

        // Shifted scores for sharing: the worst valid fitness is 0 and invalid ones count as it.
        private static double[] SelectionScores(IReadOnlyList<float> fitness)
        {
            var worst = double.PositiveInfinity;
            foreach (var value in fitness)
            {
                if (Fitness.IsValid(value) && value < worst)
                    worst = value;
            }

            var scores = new double[fitness.Count];
            for (var i = 0; i < scores.Length; i++)
                scores[i] = Fitness.IsValid(fitness[i]) ? fitness[i] - worst : 0.0;
            return scores;
        }

        // Updates the best fitness of every species and returns those that may breed, in id order.
        private List<NeatSpecies> UpdateStagnation(IReadOnlyList<float> fitness, int generation)
        {
            foreach (var current in species)
            {
                foreach (var member in current.Members)
                {
                    var value = fitness[member];
                    if (Fitness.IsValid(value) && value > current.BestFitness)
                    {
                        current.BestFitness = value;
                        current.LastImprovedGeneration = generation;
                    }
                }
            }

            var byBest = new List<NeatSpecies>(species);
            byBest.Sort((a, b) => a.BestFitness != b.BestFitness ? b.BestFitness.CompareTo(a.BestFitness) : a.Id.CompareTo(b.Id));
            var breeding = new List<NeatSpecies>();
            foreach (var current in species)
            {
                var stagnant = generation - current.LastImprovedGeneration >= rules.StagnationGenerations;
                if (!stagnant || byBest.IndexOf(current) < rules.ProtectedSpecies)
                    breeding.Add(current);
            }

            return breeding;
        }

        // Largest-remainder division of the population by shares (mean shifted score), or by size when all are 0.
        private static int[] Allocate(List<NeatSpecies> breeding, double[] scores, int populationSize)
        {
            var weights = new double[breeding.Count];
            var total = 0.0;
            for (var s = 0; s < weights.Length; s++)
            {
                var sum = 0.0;
                foreach (var member in breeding[s].Members)
                    sum += scores[member];
                weights[s] = sum / breeding[s].Members.Count;
                total += weights[s];
            }

            if (!(total > 0.0))
            {
                total = 0.0;
                for (var s = 0; s < weights.Length; s++)
                {
                    weights[s] = breeding[s].Members.Count;
                    total += weights[s];
                }
            }

            var counts = new int[weights.Length];
            var remainders = new double[weights.Length];
            var given = 0;
            for (var s = 0; s < weights.Length; s++)
            {
                var quota = populationSize * weights[s] / total;
                counts[s] = (int)Math.Floor(quota);
                remainders[s] = quota - counts[s];
                given += counts[s];
            }

            var order = new int[weights.Length];
            for (var s = 0; s < order.Length; s++)
                order[s] = s;
            Array.Sort(order, (a, b) => remainders[a] != remainders[b] ? remainders[b].CompareTo(remainders[a]) : a.CompareTo(b));
            for (var k = 0; given < populationSize; k = (k + 1) % order.Length, given++)
                counts[order[k]]++;
            return counts;
        }

        // The best SurvivalFraction of a species (at least one), best first.
        private int[] Survivors(NeatSpecies current, IReadOnlyList<float> fitness)
        {
            var ranked = new List<int>(current.Members);
            ranked.Sort((a, b) => IsBetter(fitness, a, b) ? -1 : IsBetter(fitness, b, a) ? 1 : 0);
            var keep = Math.Max(1, (int)Math.Ceiling(rules.SurvivalFraction * ranked.Count));
            return ranked.GetRange(0, Math.Min(keep, ranked.Count)).ToArray();
        }

        private Genome ClosestMember(NeatSpecies current, IReadOnlyList<Genome> population)
        {
            var best = current.Members[0];
            var bestDistance = NeatCrossover.Distance(population[best], current.Representative, rules);
            for (var m = 1; m < current.Members.Count; m++)
            {
                var member = current.Members[m];
                var distance = NeatCrossover.Distance(population[member], current.Representative, rules);
                if (distance < bestDistance)
                {
                    best = member;
                    bestDistance = distance;
                }
            }

            return population[best];
        }

        private static bool IsBetter(IReadOnlyList<float> fitness, int a, int b)
        {
            var fa = Score(fitness[a]);
            var fb = Score(fitness[b]);
            return fa > fb || (fa == fb && a < b);
        }

        private static float Score(float value) => float.IsNaN(value) ? Fitness.Worst : value;
    }
}
