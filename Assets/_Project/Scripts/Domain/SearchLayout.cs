using System;
using System.Collections.Generic;

namespace Eden.Domain
{
    /// <summary>What a search episode places: food and water targets and how far apart they must be.</summary>
    public sealed class SearchTargets
    {
        public SearchTargets(int food, int water, float minDistance, float waterSide, float maxArenaSide)
        {
            var errors = new List<string>();
            if (food < 0 || water < 0 || food + water < 1 || food + water > MaxTargets)
                errors.Add($"between 1 and {MaxTargets} targets are needed (were {food} food and {water} water)");
            if (!(minDistance >= 0f && minDistance <= SimulationConfig.MaxArenaSize))
                errors.Add($"the minimum distance must be in [0, {SimulationConfig.MaxArenaSize}] (was {minDistance})");
            if (!(waterSide > 0f && waterSide <= 100f))
                errors.Add($"the side of a water target must be in (0, 100] (was {waterSide})");
            if (!(maxArenaSide > 0f && maxArenaSide <= SimulationConfig.MaxArenaSize))
                errors.Add($"the largest arena must be in (0, {SimulationConfig.MaxArenaSize}] (was {maxArenaSide})");
            if (errors.Count > 0)
                throw new ArgumentException("Invalid search targets: " + string.Join("; ", errors));
            Food = food;
            Water = water;
            MinDistance = minDistance;
            WaterSide = waterSide;
            MaxArenaSide = maxArenaSide;
        }

        public const int MaxTargets = 16;

        public int Food { get; }
        public int Water { get; }
        public int Count => Food + Water;

        /// <summary>Shortest path allowed between two targets and between a target and the founder.</summary>
        public float MinDistance { get; }

        /// <summary>Side of the square pond of a water target.</summary>
        public float WaterSide { get; }

        /// <summary>Largest arena side tried before the targets are declared not to fit.</summary>
        public float MaxArenaSide { get; }
    }

    /// <summary>
    /// The world of one search episode: a founder and targets (food items, then square ponds that never run dry) all in
    /// the founder's free region, each at least <see cref="SearchTargets.MinDistance"/> away from the founder and from
    /// every other target along the free cells (<see cref="FreeSpace.DistanceField"/>). Drawn from a stream of its own
    /// derived from the episode seed, so every genome of a seed gets the same layout. It also keeps, per target, the
    /// path length to it from anywhere, which the scoring reads; the brain never sees it.
    /// </summary>
    public sealed class SearchLayout
    {
        private const int Restarts = 8;
        private const int Attempts = 256;
        private const ulong StreamTag = 0x73726368UL; // "srch"
        private const float FullTurn = 2f * MathF.PI;

        private static readonly float[] Ladder = { 50f, 75f, 100f, 150f, 200f, 300f, 400f, 600f, 800f, 1000f };

        private readonly float[][] fields;
        private readonly Float2[] points;
        private readonly FreeSpace paths;

        private SearchLayout(SimulationConfig config, FreeSpace paths, Float2 founder, float heading, Float2[] food,
            Obstacle[] water, float[][] fields, Float2[] points)
        {
            Config = config;
            this.paths = paths;
            Founder = founder;
            FounderHeading = heading;
            Food = food;
            Water = water;
            this.fields = fields;
            this.points = points;
        }

        /// <summary>The episode world: the arena with its obstacles, the water targets as inexhaustible sources and no food.</summary>
        public SimulationConfig Config { get; }

        public Float2 Founder { get; }
        public float FounderHeading { get; }
        public IReadOnlyList<Float2> Food { get; }
        public IReadOnlyList<Obstacle> Water { get; }

        /// <summary>Targets 0 to <c>Food.Count - 1</c> are the food items, the rest the ponds.</summary>
        public int TargetCount => Food.Count + Water.Count;

        public bool IsWater(int target) => target >= Food.Count;

        /// <summary>Where a target is reached: the food item, or the free place nearest the founder from which the pond is reached.</summary>
        public Float2 TargetPoint(int target) => points[target];

        /// <summary>Path length over the free cells from <paramref name="position"/> to where the target is reached.</summary>
        public float DistanceTo(int target, Float2 position) => paths.DistanceAt(fields[target], position);

        /// <summary>Path length of the greedy tour: from the founder, always on to the nearest target not yet reached.</summary>
        public float GreedyTourLength()
        {
            var left = new List<int>();
            for (var i = 0; i < TargetCount; i++)
                left.Add(i);
            var at = Founder;
            var length = 0f;
            while (left.Count > 0)
            {
                var best = 0;
                for (var i = 1; i < left.Count; i++)
                {
                    if (DistanceTo(left[i], at) < DistanceTo(left[best], at))
                        best = i;
                }

                length += DistanceTo(left[best], at);
                at = points[left[best]];
                left.RemoveAt(best);
            }

            return length;
        }

        /// <summary>
        /// The arena sides tried for a search world, smallest first: its own side, then the larger steps of
        /// 50, 75, 100, 150, 200, 300… up to <paramref name="maxSide"/>.
        /// </summary>
        public static IEnumerable<float> ArenaSides(float side, float maxSide)
        {
            yield return side;
            foreach (var step in Ladder)
            {
                if (step > side && step <= maxSide)
                    yield return step;
            }
        }

        /// <summary>
        /// The world at another square side: obstacles of a named layout are laid out again for it; hand-placed obstacles
        /// only fit their own arena. Water and food are left out (a search world places its own).
        /// </summary>
        public static SimulationSettings Sized(SimulationSettings world, float side)
        {
            var sized = world.Clone();
            sized.Water = Array.Empty<WaterSource>();
            sized.InitialFood = 0;
            if (side == world.ArenaWidth && side == world.ArenaHeight)
                return sized;
            var layout = ObstacleLayouts.Identify(world);
            if (layout == null)
                throw new ArgumentException("Hand-placed obstacles cannot be laid out in a larger arena.", nameof(world));
            sized.ArenaWidth = side;
            sized.ArenaHeight = side;
            sized.Obstacles = ObstacleLayouts.Create(layout.Value, sized);
            return sized;
        }

        /// <summary>
        /// The layout of the episode <paramref name="seed"/> in the world <paramref name="sized"/> (see <see cref="Sized"/>),
        /// or null when the targets do not fit after a bounded number of fresh draws.
        /// </summary>
        public static SearchLayout TryCreate(SimulationSettings sized, ulong seed, SearchTargets targets)
        {
            var space = Paths(sized, sized.Obstacles);
            var random = new Pcg32(SplitMix64.Output(seed, StreamTag), StreamTag);
            for (var restart = 0; restart < Restarts; restart++)
            {
                var layout = TryDraw(sized, space, random, seed, targets);
                if (layout != null)
                    return layout;
            }

            return null;
        }

        private static SearchLayout TryDraw(SimulationSettings sized, FreeSpace space, IRandomSource random, ulong seed,
            SearchTargets targets)
        {
            var obstacles = sized.Obstacles;
            var radius = sized.BodyRadius;
            var founder = default(Float2);
            var placed = false;
            for (var attempt = 0; attempt < Attempts && !placed; attempt++)
            {
                founder = new Float2(random.NextFloat(0f, sized.ArenaWidth), random.NextFloat(0f, sized.ArenaHeight));
                placed = space.IsFree(space.CellAt(founder)) && Clear(sized, founder, radius);
            }

            if (!placed)
                return null;
            var heading = random.NextFloat() * FullTurn;
            var founderField = space.DistanceField(new[] { space.CellAt(founder) });
            var areas = new List<Obstacle>();
            var cellsOf = new List<List<int>>();
            var fieldOf = new List<float[]>();
            var food = new List<Float2>();
            var water = new List<Obstacle>();
            for (var target = 0; target < targets.Count; target++)
            {
                var isWater = target >= targets.Food;
                var found = false;
                for (var attempt = 0; attempt < Attempts && !found; attempt++)
                {
                    var centre = new Float2(random.NextFloat(0f, sized.ArenaWidth), random.NextFloat(0f, sized.ArenaHeight));
                    var half = isWater ? targets.WaterSide * 0.5f : 0f;
                    var area = new Obstacle(centre.X - half, centre.Y - half, centre.X + half, centre.Y + half);
                    if (!Fits(sized, area, isWater, founder, areas))
                        continue;
                    // Paths are never shorter than straight lines: most draws are dropped before any path is read.
                    if (Straight(area, founder) < targets.MinDistance - sized.EatRadius - space.Cell ||
                        TooClose(area, areas, targets.MinDistance - 2f * (sized.EatRadius + space.Cell)))
                        continue;
                    var cells = space.CellsReaching(area, sized.EatRadius + space.Cell, obstacles);
                    if (cells.Count == 0 || Nearest(founderField, cells) < targets.MinDistance)
                        continue;
                    var apart = true;
                    for (var other = 0; apart && other < fieldOf.Count; other++)
                        apart = Nearest(fieldOf[other], cells) >= targets.MinDistance;
                    if (!apart)
                        continue;
                    found = true;
                    areas.Add(area);
                    cellsOf.Add(cells);
                    fieldOf.Add(space.DistanceField(cells));
                    if (isWater)
                        water.Add(area);
                    else
                        food.Add(centre);
                }

                if (!found)
                    return null;
            }

            // The episode world, whose ponds now also block the free cells; the paths are measured again in it.
            var world = sized.Clone();
            world.InitialPopulation = 1;
            world.Reproduction = false;
            world.Seed = seed;
            var sources = new WaterSource[water.Count];
            for (var i = 0; i < sources.Length; i++)
                sources[i] = new WaterSource(water[i], 1f, 0f, true);
            world.Water = sources;
            var config = new SimulationConfig(world);
            var final = Paths(sized, config.Barriers);
            var start = final.CellAt(founder);
            var fields = new float[targets.Count][];
            var points = new Float2[targets.Count];
            var from = start >= 0 && final.IsFree(start) ? final.DistanceField(new[] { start }) : founderField;
            for (var target = 0; target < targets.Count; target++)
            {
                var cells = final.CellsReaching(areas[target], sized.EatRadius + final.Cell, config.Obstacles);
                if (cells.Count == 0)
                    return null;
                fields[target] = final.DistanceField(cells);
                if (!(final.DistanceAt(fields[target], founder) < float.PositiveInfinity))
                    return null;
                var best = cells[0];
                foreach (var cell in cells)
                {
                    if (from[cell] < from[best])
                        best = cell;
                }

                points[target] = target < targets.Food ? food[target] : final.CentreOf(best);
            }

            return new SearchLayout(config, final, founder, heading, food.ToArray(), water.ToArray(), fields, points);
        }

        // Path lengths are measured on a grid one body radius wide: four times fewer cells than placement's, a body
        // radius of precision.
        private static FreeSpace Paths(SimulationSettings sized, IReadOnlyList<Obstacle> barriers) =>
            new FreeSpace(sized.ArenaWidth, sized.ArenaHeight, sized.BodyRadius, barriers, sized.BodyRadius);

        private static float Straight(Obstacle area, Float2 point) => area.DistanceTo(point);

        private static bool TooClose(Obstacle area, List<Obstacle> earlier, float distance)
        {
            foreach (var other in earlier)
            {
                if (Gap(area, other) < distance)
                    return true;
            }

            return false;
        }

        private static float Nearest(float[] field, List<int> cells)
        {
            var best = float.PositiveInfinity;
            foreach (var cell in cells)
                best = Math.Min(best, field[cell]);
            return best;
        }

        // The founder's disc stays clear of the obstacles.
        private static bool Clear(SimulationSettings sized, Float2 position, float radius)
        {
            foreach (var obstacle in sized.Obstacles)
            {
                if (obstacle.DistanceTo(position) < radius)
                    return false;
            }

            return true;
        }

        // A target lies in the arena, out of the obstacles and the earlier targets, and a pond also keeps a body's width
        // from the obstacles and the founder, so it never closes a passage or covers the founder.
        private static bool Fits(SimulationSettings sized, Obstacle area, bool isWater, Float2 founder, List<Obstacle> earlier)
        {
            var margin = isWater ? 2f * sized.BodyRadius : 0f;
            if (area.MinX < margin || area.MinY < margin || area.MaxX > sized.ArenaWidth - margin ||
                area.MaxY > sized.ArenaHeight - margin)
                return false;
            if (isWater && area.DistanceTo(founder) < 2f * sized.BodyRadius)
                return false;
            foreach (var obstacle in sized.Obstacles)
            {
                if (Gap(area, obstacle) < margin || !isWater && obstacle.Contains(new Float2(area.MinX, area.MinY)))
                    return false;
            }

            foreach (var other in earlier)
            {
                if (Gap(area, other) < 2f * sized.BodyRadius)
                    return false;
            }

            return true;
        }

        private static float Gap(Obstacle a, Obstacle b)
        {
            var dx = Math.Max(0f, Math.Max(a.MinX - b.MaxX, b.MinX - a.MaxX));
            var dy = Math.Max(0f, Math.Max(a.MinY - b.MaxY, b.MinY - a.MaxY));
            return MathF.Sqrt(dx * dx + dy * dy);
        }
    }
}
