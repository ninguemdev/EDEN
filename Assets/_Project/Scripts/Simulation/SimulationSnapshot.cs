using System;
using System.Collections.Generic;
using Eden.Domain;

namespace Eden.Simulation
{
    /// <summary>
    /// Everything a <see cref="WorldSimulation"/> carries from one tick to the next, apart from its config: the world
    /// with both random streams, the pending agenda and the run counters. It holds its own copies of the creatures and
    /// food, so it neither follows nor changes the simulation it was taken from. Taken between ticks, when every
    /// creature is alive and every food item uneaten; <see cref="WorldSimulation.Restore"/> checks that.
    /// </summary>
    public sealed class SimulationSnapshot
    {
        public SimulationSnapshot(long tick, int nextCreatureId, int nextFoodId, Pcg32State worldRandom,
            Pcg32State spawnRandom, IReadOnlyList<CreatureState> creatures, IReadOnlyList<FoodState> food,
            IReadOnlyList<ScheduledEvent<SimulationEvent>> agenda, long nextEventSequence, long correctedInputValues)
        {
            if (creatures == null)
                throw new ArgumentNullException(nameof(creatures));
            if (food == null)
                throw new ArgumentNullException(nameof(food));
            if (agenda == null)
                throw new ArgumentNullException(nameof(agenda));

            Tick = tick;
            NextCreatureId = nextCreatureId;
            NextFoodId = nextFoodId;
            WorldRandom = worldRandom;
            SpawnRandom = spawnRandom;
            Creatures = Array.ConvertAll(Copy(creatures), Clone);
            Food = Array.ConvertAll(Copy(food), Clone);
            Agenda = Copy(agenda);
            NextEventSequence = nextEventSequence;
            CorrectedInputValues = correctedInputValues;
        }

        /// <summary>Next tick to run.</summary>
        public long Tick { get; }

        public int NextCreatureId { get; }
        public int NextFoodId { get; }
        public Pcg32State WorldRandom { get; }
        public Pcg32State SpawnRandom { get; }

        /// <summary>Copies of the creatures in id order; changing them never affects a restored simulation.</summary>
        public IReadOnlyList<CreatureState> Creatures { get; }

        public IReadOnlyList<FoodState> Food { get; }

        /// <summary>Pending events in the order they will run.</summary>
        public IReadOnlyList<ScheduledEvent<SimulationEvent>> Agenda { get; }

        public long NextEventSequence { get; }
        public long CorrectedInputValues { get; }

        private static T[] Copy<T>(IReadOnlyList<T> items)
        {
            var copy = new T[items.Count];
            for (var i = 0; i < copy.Length; i++)
                copy[i] = items[i];
            return copy;
        }

        private static CreatureState Clone(CreatureState creature) =>
            creature == null
                ? null
                : new CreatureState(creature.Id, creature.Position, creature.Heading, creature.Energy, creature.Genes,
                    creature.Body)
                {
                    Velocity = creature.Velocity,
                    AgeTicks = creature.AgeTicks,
                    FoodEnergyEaten = creature.FoodEnergyEaten,
                    DeathCause = creature.DeathCause,
                    Parent = creature.Parent,
                    Generation = creature.Generation,
                    ReproductionCooldownTicks = creature.ReproductionCooldownTicks
                };

        private static FoodState Clone(FoodState item)
        {
            if (item == null)
                return null;
            var copy = new FoodState(item.Id, item.Position, item.Energy);
            if (item.IsEaten)
                copy.MarkEaten();
            return copy;
        }
    }
}
