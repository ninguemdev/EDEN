using System;
using System.Collections.Generic;
using Eden.Domain;
using Eden.Evolution;

namespace Eden.Application
{
    /// <summary>
    /// Size of a lab experiment (docs/07 budgets, to be measured): creatures and food of the shared world, and
    /// genomes per generation of the isolated evolution. The isolated episode world keeps its own proposed
    /// settings (one creature, 32 food), so a preset never changes how a genome is scored.
    /// </summary>
    public sealed class ExperimentPreset
    {
        public static readonly ExperimentPreset Minimal = new ExperimentPreset("minimal", 32, 64);
        public static readonly ExperimentPreset Standard = new ExperimentPreset("standard", 64, 128);
        public static readonly ExperimentPreset Stress = new ExperimentPreset("stress", 128, 256);
        public static readonly ExperimentPreset Thousand = new ExperimentPreset("thousand", 1000, 2000);

        /// <summary>Presets in the order the lab offers them; the index is what the lab stores.</summary>
        public static readonly IReadOnlyList<ExperimentPreset> All = new[] { Minimal, Standard, Stress, Thousand };

        private ExperimentPreset(string name, int population, int food)
        {
            Name = name;
            Population = population;
            Food = food;
        }

        public string Name { get; }

        /// <summary>Creatures of the shared world and genomes per generation.</summary>
        public int Population { get; }

        /// <summary>Food items of the shared world.</summary>
        public int Food { get; }

        /// <summary>Copy of <paramref name="template"/> with this preset's population and food and the given seed.</summary>
        public SimulationSettings SharedWorld(SimulationSettings template, ulong seed)
        {
            if (template == null)
                throw new ArgumentNullException(nameof(template));
            var settings = template.Clone();
            settings.Seed = seed;
            settings.InitialPopulation = Population;
            settings.InitialFood = Food;
            settings.PopulationCap = Math.Max(settings.PopulationCap, Population);
            return settings;
        }

        /// <summary>Proposed evolution settings with this preset's population and the given experiment seed.</summary>
        public EvolutionSettings Evolution(ulong experimentSeed) =>
            new EvolutionSettings { ExperimentSeed = experimentSeed, PopulationSize = Population };
    }
}
