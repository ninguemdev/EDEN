using System;
using System.Collections.Generic;
using Eden.Domain;

namespace Eden.Evolution
{
    /// <summary>
    /// The world of a search stage: the smallest arena side, from the stage's own up to
    /// <see cref="SearchTargets.MaxArenaSide"/>, in which the targets of every probe seed fit and the greedy tour at full
    /// speed takes at most the declared share of the stage; and the layout of each episode seed, built once.
    /// </summary>
    public sealed class SearchWorld
    {
        private const int Probes = 4;
        private const ulong ProbeTag = 0x70726F62UL; // "prob"
        private const int CachedLayouts = 64;

        private readonly SimulationSettings world;
        private readonly List<float> sides;
        private readonly Dictionary<ulong, SearchLayout> layouts = new Dictionary<ulong, SearchLayout>();
        private readonly object gate = new object();

        private SearchWorld(SimulationSettings world, SearchTargets targets, List<float> sides, SimulationConfig template)
        {
            this.world = world;
            Targets = targets;
            this.sides = sides;
            Template = template;
        }

        public SearchTargets Targets { get; }

        /// <summary>Side of the square arena the stage runs in.</summary>
        public float ArenaSide => sides[0];

        /// <summary>The stage world without targets, for what does not depend on the seed (tick rate, body).</summary>
        public SimulationConfig Template { get; }

        /// <summary>
        /// The layout of an episode seed. A seed whose targets do not fit the chosen side (none of the probes failed)
        /// gets the next larger side; past the largest a <see cref="ScenarioInfeasibleException"/> is thrown.
        /// </summary>
        public SearchLayout Layout(ulong seed)
        {
            lock (gate)
            {
                if (layouts.TryGetValue(seed, out var known))
                    return known;
            }

            SearchLayout layout = null;
            foreach (var side in sides)
            {
                layout = SearchLayout.TryCreate(SearchLayout.Sized(world, side), seed, Targets);
                if (layout != null)
                    break;
            }

            if (layout == null)
                throw new ScenarioInfeasibleException("search targets", Targets.Count, 0);
            lock (gate)
            {
                if (layouts.Count >= CachedLayouts)
                    layouts.Clear();
                layouts[seed] = layout;
            }

            return layout;
        }

        // Plans of the same stage (a session, its checkpoints and records) share the choice and the layouts.
        private static readonly Dictionary<string, (SearchWorld World, string[] Errors)> Chosen =
            new Dictionary<string, (SearchWorld, string[])>();

        /// <summary>The search world of a stage, or null with the reasons added to <paramref name="errors"/>.</summary>
        public static SearchWorld Choose(SimulationSettings world, SearchTargets targets, float seconds,
            double feasibleFraction, string stage, List<string> errors)
        {
            var key = string.Join("|", stage, Describe(world), targets.Food, targets.Water,
                targets.MinDistance.ToString("R"), targets.WaterSide.ToString("R"), targets.MaxArenaSide.ToString("R"),
                seconds.ToString("R"), feasibleFraction.ToString("R"));
            lock (Chosen)
            {
                if (Chosen.TryGetValue(key, out var known))
                {
                    errors.AddRange(known.Errors);
                    return known.World;
                }
            }

            var found = new List<string>();
            var chosen = ChooseNew(world, targets, seconds, feasibleFraction, stage, found);
            errors.AddRange(found);
            lock (Chosen)
            {
                if (Chosen.Count >= 32)
                    Chosen.Clear();
                Chosen[key] = (chosen, found.ToArray());
            }

            return chosen;
        }

        // Every public field of the world settings, so two settings with the same text describe the same world.
        private static string Describe(SimulationSettings world)
        {
            var text = new System.Text.StringBuilder();
            foreach (var field in typeof(SimulationSettings).GetFields())
            {
                var value = field.GetValue(world);
                text.Append(field.Name).Append('=');
                if (value is System.Collections.IEnumerable items && !(value is string))
                {
                    foreach (var item in items)
                        text.Append(item).Append(';');
                }
                else
                {
                    text.Append(value is float f ? f.ToString("R") : value);
                }

                text.Append(',');
            }

            return text.ToString();
        }

        private static SearchWorld ChooseNew(SimulationSettings world, SearchTargets targets, float seconds,
            double feasibleFraction, string stage, List<string> errors)
        {
            if (world.Water != null && world.Water.Length > 0)
            {
                errors.Add($"{stage} is a search stage, which places its own water targets; its world has water");
                return null;
            }

            if (world.ArenaWidth != world.ArenaHeight)
            {
                errors.Add($"{stage} is a search stage and needs a square arena (was {world.ArenaWidth} × {world.ArenaHeight})");
                return null;
            }

            var tried = new List<float>(SearchLayout.ArenaSides(world.ArenaWidth, targets.MaxArenaSide));
            for (var i = 0; i < tried.Count; i++)
            {
                SimulationSettings sized;
                try
                {
                    sized = SearchLayout.Sized(world, tried[i]);
                }
                catch (ArgumentException e)
                {
                    errors.Add($"{stage}: {e.Message}");
                    return null;
                }

                var fits = true;
                var slowest = 0f;
                for (var probe = 0; fits && probe < Probes; probe++)
                {
                    var layout = SearchLayout.TryCreate(sized, SplitMix64.Output(ProbeTag, (ulong)probe + 1), targets);
                    if (layout == null)
                        fits = false;
                    else
                        slowest = Math.Max(slowest, layout.GreedyTourLength() / layout.Config.MaxSpeed);
                }

                if (!fits)
                    continue;
                if (slowest > feasibleFraction * seconds)
                {
                    errors.Add($"{stage} is too short: the greedy tour of a probe takes {slowest:0.#} s at full speed, " +
                               $"more than {feasibleFraction:0.##} of its {seconds} s");
                    return null;
                }

                sized.InitialPopulation = 1;
                sized.Reproduction = false;
                return new SearchWorld(world, targets, tried.GetRange(i, tried.Count - i), new SimulationConfig(sized));
            }

            errors.Add($"{stage}: {targets.Count} search targets {targets.MinDistance} apart do not fit an arena up to " +
                       $"{targets.MaxArenaSide} wide");
            return null;
        }
    }
}
