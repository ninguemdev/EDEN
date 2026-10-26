using System;
using System.Collections.Generic;
using Eden.Domain;
using Eden.Simulation;

namespace Eden.Application
{
    /// <summary>One creature of a shared world: where it came from and, once dead, when and how it died.</summary>
    public readonly struct GenealogyEntry
    {
        public GenealogyEntry(CreatureId creature, CreatureId? parent, int generation, long birthTick, long deathTick,
            DeathCause deathCause)
        {
            Creature = creature;
            Parent = parent;
            Generation = generation;
            BirthTick = birthTick;
            DeathTick = deathTick;
            DeathCause = deathCause;
        }

        public CreatureId Creature { get; }

        /// <summary>Null for a creature the run started with (a founder).</summary>
        public CreatureId? Parent { get; }

        public int Generation { get; }

        /// <summary>Tick during which the creature was born; founders are recorded at tick 0.</summary>
        public long BirthTick { get; }

        /// <summary>Tick during which the creature died; −1 while it lives.</summary>
        public long DeathTick { get; }

        public DeathCause DeathCause { get; }
        public bool IsAlive => DeathTick < 0;

        public GenealogyEntry Died(long tick, DeathCause cause) =>
            new GenealogyEntry(Creature, Parent, Generation, BirthTick, tick, cause);
    }

    /// <summary>
    /// Every creature a shared world has had, dead ones included, so the line of any creature can be followed back to
    /// its founder and forward to its descendants. Fed after every tick with <see cref="Record"/>; there is no global
    /// generation, only each creature's genealogical one.
    /// </summary>
    public sealed class Genealogy
    {
        private readonly SortedDictionary<int, GenealogyEntry> entries = new SortedDictionary<int, GenealogyEntry>();
        private readonly Dictionary<int, List<int>> children = new Dictionary<int, List<int>>();

        /// <summary>Starts with the living creatures of <paramref name="world"/> (its founders, for a new world).</summary>
        public Genealogy(WorldState world)
        {
            if (world == null)
                throw new ArgumentNullException(nameof(world));
            foreach (var creature in world.Creatures)
                Add(new GenealogyEntry(creature.Id, creature.Parent, creature.Generation,
                    creature.Parent.HasValue ? world.Tick : 0, -1, DeathCause.None));
        }

        private Genealogy()
        {
        }

        public int Count => entries.Count;

        /// <summary>Every entry in id order.</summary>
        public IEnumerable<GenealogyEntry> Entries => entries.Values;

        /// <summary>Adds the births and deaths of the tick <paramref name="simulation"/> just ran (deaths first).</summary>
        public void Record(WorldSimulation simulation)
        {
            if (simulation == null)
                throw new ArgumentNullException(nameof(simulation));
            var deaths = simulation.LastDeaths;
            for (var i = 0; i < deaths.Count; i++)
            {
                var death = deaths[i];
                if (entries.TryGetValue(death.Creature.Value, out var entry))
                    entries[death.Creature.Value] = entry.Died(death.Tick, death.Cause);
            }

            var births = simulation.LastBirths;
            for (var i = 0; i < births.Count; i++)
            {
                var birth = births[i];
                var parent = entries.TryGetValue(birth.Parent.Value, out var parentEntry) ? parentEntry.Generation : -1;
                Add(new GenealogyEntry(birth.Child, birth.Parent, parent + 1, birth.Tick, -1, DeathCause.None));
            }
        }

        public bool TryGet(CreatureId creature, out GenealogyEntry entry) =>
            entries.TryGetValue(creature.Value, out entry);

        /// <summary>The creature and its ancestors, nearest first, ending with its founder.</summary>
        public IReadOnlyList<GenealogyEntry> Ancestry(CreatureId creature)
        {
            var line = new List<GenealogyEntry>();
            var next = (CreatureId?)creature;
            while (next.HasValue && entries.TryGetValue(next.Value.Value, out var entry))
            {
                line.Add(entry);
                next = entry.Parent;
            }

            return line;
        }

        /// <summary>Number of descendants of a creature (children, grandchildren, …) and how many of them live.</summary>
        public int Descendants(CreatureId creature, out int living)
        {
            living = 0;
            var count = 0;
            var pending = new Stack<int>();
            pending.Push(creature.Value);
            while (pending.Count > 0)
            {
                if (!children.TryGetValue(pending.Pop(), out var list))
                    continue;
                foreach (var child in list)
                {
                    count++;
                    if (entries[child].IsAlive)
                        living++;
                    pending.Push(child);
                }
            }

            return count;
        }

        /// <summary>Copy of every entry, in id order.</summary>
        public GenealogyEntry[] Capture()
        {
            var copy = new GenealogyEntry[entries.Count];
            entries.Values.CopyTo(copy, 0);
            return copy;
        }

        /// <summary>
        /// Rebuilds a genealogy saved with <paramref name="world"/>: ids in ascending order below the world's next id,
        /// parents recorded before their children with generation + 1, and exactly the living creatures of the world
        /// alive, with the same parent and generation. Everything is checked before anything is built.
        /// </summary>
        public static Genealogy Restore(IReadOnlyList<GenealogyEntry> saved, WorldState world)
        {
            if (saved == null)
                throw new ArgumentNullException(nameof(saved));
            if (world == null)
                throw new ArgumentNullException(nameof(world));

            var genealogy = new Genealogy();
            var previous = 0;
            foreach (var entry in saved)
            {
                var id = entry.Creature.Value;
                if (id <= previous || id >= world.NextCreatureId)
                    throw new ArgumentException($"{entry.Creature} breaks the ascending id order.", nameof(saved));
                previous = id;
                if (entry.Parent.HasValue)
                {
                    if (!genealogy.entries.TryGetValue(entry.Parent.Value.Value, out var parent) ||
                        entry.Generation != parent.Generation + 1 || entry.BirthTick < parent.BirthTick)
                        throw new ArgumentException($"{entry.Creature} does not follow its parent.", nameof(saved));
                }
                else if (entry.Generation != 0)
                {
                    throw new ArgumentException($"Founder {entry.Creature} has generation {entry.Generation}.", nameof(saved));
                }

                if (entry.BirthTick < 0 || entry.BirthTick >= world.Tick && entry.Parent.HasValue ||
                    !entry.IsAlive && (entry.DeathTick < entry.BirthTick || entry.DeathTick >= world.Tick ||
                                       entry.DeathCause == DeathCause.None))
                    throw new ArgumentException($"{entry.Creature} has impossible ticks.", nameof(saved));
                genealogy.Add(entry);
            }

            var living = 0;
            foreach (var entry in genealogy.entries.Values)
            {
                if (entry.IsAlive)
                    living++;
            }

            foreach (var creature in world.Creatures)
            {
                if (!genealogy.entries.TryGetValue(creature.Id.Value, out var entry) || !entry.IsAlive ||
                    entry.Parent != creature.Parent || entry.Generation != creature.Generation)
                    throw new ArgumentException($"{creature.Id} does not match its saved genealogy.", nameof(saved));
            }

            if (living != world.Creatures.Count)
                throw new ArgumentException("Only the world's living creatures can be alive in its genealogy.", nameof(saved));
            return genealogy;
        }

        private void Add(GenealogyEntry entry)
        {
            entries.Add(entry.Creature.Value, entry);
            if (!entry.Parent.HasValue)
                return;
            if (!children.TryGetValue(entry.Parent.Value.Value, out var list))
                children.Add(entry.Parent.Value.Value, list = new List<int>());
            list.Add(entry.Creature.Value);
        }
    }
}
