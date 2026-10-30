using System;
using System.Collections.Generic;
using System.Globalization;
using Eden.Domain;
using Eden.Evolution;
using Eden.Simulation;

namespace Eden.Application
{
    /// <summary>Deterministic map-aware diagnostic, never a brain, candidate, neural input or selection participant.
    /// Uses the real tick, contact solver, body, eyes and consumption. A failure reports the seed and reserves;
    /// it never retries a different seed or modifies the scenario.</summary>
    public static class ActiveSearchDiagnostics
    {
        public sealed class Result
        {
            public ulong Seed;
            public float Arena, Seconds, FinalEnergy, FinalHydration;
            public double EnergyBeforeFood = double.PositiveInfinity, HydrationBeforeWater = double.PositiveInfinity;
            public int Collected, Visited, TargetCount, EyeCount, StageIndex;
            public string Role, ProfileHash;
            public string Route, Collections;
            public DeathCause Death;
            public bool Feasible => Collected == TargetCount && Death == DeathCause.None && EnergyBeforeFood >= 0.1 && HydrationBeforeWater >= 0.1;
            public override string ToString() => string.Format(CultureInfo.InvariantCulture,
                "seed={0};arena={1};seconds={2:R};collected={3};visited={4};energy={5:R};hydration={6:R};min_food_reserve={7:R};min_water_reserve={8:R};death={9};eyes={10};stage={11};role={12};profile_hash={13};collection_ticks={14}",
                Seed, Arena, Seconds, Collected, Visited, FinalEnergy, FinalHydration, EnergyBeforeFood, HydrationBeforeWater, Death, EyeCount, StageIndex, Role, ProfileHash, Collections);
        }
        public static Result Run(EvaluationStage stage, RewardProfile profile, ulong seed, EyeSet eyes, bool explore = true)
        {
            if (!stage.ActiveSearch) throw new ArgumentException("An active search stage is required.");
            var layout = stage.Search.Layout(seed); var config = layout.Config; var world = new WorldState(config);
            var creature = world.AddCreature(layout.Founder, layout.FounderHeading, config.InitialEnergy);
            creature.Eyes = eyes;
            foreach (var food in layout.Food) world.AddFood(food, config.FoodEnergy);
            var simulation = new WorldSimulation(world) { RespawnFood = false };
            var tracker = new EpisodeTracker { Active = new ActiveSearchState(layout, ActiveSearchOptions.Read(profile.Parameters), creature) };
            var route = new Route(simulation, layout, tracker, explore);
            var trace = new List<string>();
            var result = new Result { Seed = seed, Arena = config.ArenaWidth, TargetCount = layout.TargetCount, EyeCount = eyes?.Count ?? 0, StageIndex = stage.Index, Role = stage.Name, ProfileHash = profile.ParameterHash };
            while (world.Tick < stage.EpisodeTicks && creature.IsAlive)
            {
                if (world.Tick % 200 == 0) trace.Add(world.Tick + ":" + route.Describe(creature));
                var energy = creature.Energy / creature.Body.MaxEnergy;
                var hydration = creature.Hydration / config.MaxHydration;
                tracker.Active.BeforeTick(creature);
                simulation.Step(route);
                tracker.AfterTick(simulation, creature, layout);
                if (simulation.LastFoodConsumption.Count > 0) result.EnergyBeforeFood = Math.Min(result.EnergyBeforeFood, energy);
                // One planned refill is a continuous stay, rather than a new visit for every useful sip.
                if (simulation.LastWaterConsumption.Count > 0 && !route.WasDrinking)
                    result.HydrationBeforeWater = Math.Min(result.HydrationBeforeWater, hydration);
                route.WasDrinking = simulation.LastWaterConsumption.Count > 0;
            }
            result.Seconds = world.Tick * config.TickDuration; result.FinalEnergy = creature.Energy;
            result.FinalHydration = creature.Hydration; result.Collected = tracker.Collected.Count;
            result.Collections = string.Join(",", tracker.CollectionTicks); result.Route = route.Describe(creature) + ";collections=" + result.Collections + ";trace=" + string.Join("|", trace); result.Visited = tracker.Active.Unique; result.Death = creature.DeathCause;
            return result;
        }
        public static void Preflight(EvaluationPlan plan, int generations, Action<Result> report = null)
        {
            if (!RewardProfiles.IsActive(plan.Profile)) return;
            foreach (var stage in plan.Stages)
            {
                if (!stage.ActiveSearch) continue;
                var seeds = new HashSet<ulong>();
                for (var g = 0; g < generations; g++)
                    for (var i = 0; i < stage.TrainingSeedsPerGeneration; i++)
                        seeds.Add(plan.TrainingSeeds(g)[SeedOffset(plan, stage.Index, false) + i]);
                for (var i = 0; i < stage.ValidationSeedCount; i++) seeds.Add(plan.ValidationSeeds()[SeedOffset(plan, stage.Index, true) + i]);
                foreach (var seed in seeds)
                    foreach (var eyes in new[] { EyeSet.Founder, MaximumEyes() })
                    {
                        var result = Run(stage, plan.Profile, seed, eyes);
                        report?.Invoke(result);
                        if (!result.Feasible || result.Seconds < stage.EpisodeSeconds)
                            throw new ArgumentException("Active search diagnostic route did not demonstrate feasibility: " + result + ";route=" + result.Route);
                    }
            }
        }
        private static int SeedOffset(EvaluationPlan plan, int index, bool validation)
        {
            var offset = 0;
            for (var i = 0; i < index; i++) offset += validation ? plan.Stages[i].ValidationSeedCount : plan.Stages[i].TrainingSeedsPerGeneration;
            return offset;
        }
        public static void Require(EvaluationStage stage, RewardProfile profile, ulong seed)
        {
            foreach (var eyes in new[] { EyeSet.Founder, MaximumEyes() })
            {
                var result = Run(stage, profile, seed, eyes);
                if (!result.Feasible || result.Collected != stage.Search.Targets.Count || result.Seconds < stage.EpisodeSeconds)
                    throw new ArgumentException("Active search diagnostic route did not demonstrate feasibility: " + result + ";route=" + result.Route);
            }
        }
        public static EyeSet MaximumEyes() => new EyeSet(
            new Eye(0, Eye.MaxAperture, Eye.MaxRange), new Eye(MathF.PI / 2, Eye.MaxAperture, Eye.MaxRange),
            new Eye(MathF.PI, Eye.MaxAperture, Eye.MaxRange), new Eye(-MathF.PI / 2, Eye.MaxAperture, Eye.MaxRange));

        private sealed class Route : ICreatureController
        {
            private readonly WorldSimulation simulation;
            private readonly SearchLayout layout;
            private readonly EpisodeTracker tracker;
            private readonly FreeSpace paths;
            private readonly float[][] fields;
            private readonly Obstacle[] expanded;
            private readonly bool explore;
            private readonly ExplorationGrid coverage;
            private float[] roamingField;
            private Float2 roamingPoint;
            private int target = -1;
            private CreatureAction action;
            public bool WasDrinking;
            public Route(WorldSimulation simulation, SearchLayout layout, EpisodeTracker tracker, bool explore)
            {
                this.simulation = simulation; this.layout = layout; this.tracker = tracker; this.explore = explore;
                var c = layout.Config;
                expanded = new Obstacle[c.Barriers.Count + 4];
                for (var i = 0; i < c.Barriers.Count; i++)
                {
                    var o = c.Barriers[i]; var r = c.BodyRadius;
                    expanded[i] = new Obstacle(o.MinX - r, o.MinY - r, o.MaxX + r, o.MaxY + r);
                }
                var n = c.Barriers.Count; var radius = c.BodyRadius;
                expanded[n] = new Obstacle(-1, -1, c.ArenaWidth + 1, radius);
                expanded[n + 1] = new Obstacle(-1, c.ArenaHeight - radius, c.ArenaWidth + 1, c.ArenaHeight + 1);
                expanded[n + 2] = new Obstacle(-1, -1, radius, c.ArenaHeight + 1);
                expanded[n + 3] = new Obstacle(c.ArenaWidth - radius, -1, c.ArenaWidth + 1, c.ArenaHeight + 1);
                paths = new FreeSpace(c.ArenaWidth, c.ArenaHeight, 0.001f, expanded, 0.25f);
                fields = new float[layout.TargetCount][];
                for (var t = 0; t < fields.Length; t++)
                    fields[t] = paths.DistanceField(layout.IsWater(t)
                        ? paths.CellsReaching(layout.Water[t - layout.Food.Count], c.EatRadius * 0.95f, c.Obstacles)
                        : paths.CellsReaching(new Obstacle(layout.Food[t].X, layout.Food[t].Y, layout.Food[t].X, layout.Food[t].Y),
                            c.EatRadius * 0.95f, c.Obstacles));
                coverage = new ExplorationGrid(c, layout.Founder, (float)tracker.Active.Options.CellSide);
            }
            public CreatureAction Decide(CreatureId id, in Observation observation)
            {
                var world = simulation.World; var c = world.Creatures[0]; var config = world.Config;
                if (world.Tick % config.DecisionIntervalTicks != 0) return action;
                if (target >= 0 && layout.IsWater(target) && c.Hydration >= config.MaxHydration * 0.95f) target = -1;
                if (target >= 0 && !layout.IsWater(target) && tracker.IsCollected(target)) target = -1;
                var nearestWater = Nearest(c.Position, true, false);
                var waterDistance = paths.DistanceAt(fields[nearestWater], c.Position);
                var waterUrgent = c.Hydration / config.HydrationLossPerSecond < waterDistance / 3.5f + 18;
                if (waterUrgent && (target < 0 || !layout.IsWater(target))) target = nearestWater;
                if (target < 0)
                {
                    target = Nearest(c.Position, false, true);
                    if (target < 0) target = Nearest(c.Position, true, true);
                }
                Float2 point; float[] field;
                if (target >= 0)
                {
                    field = fields[target]; point = layout.TargetPoint(target);
                    if (layout.IsWater(target))
                    {
                        var cells = paths.CellsReaching(layout.Water[target - layout.Food.Count], config.EatRadius * 0.95f, config.Obstacles);
                        var best = float.PositiveInfinity;
                        foreach (var source in cells)
                        {
                            var candidate = paths.CentreOf(source); var distance = Distance(c.Position, candidate);
                            if (distance < best) { best = distance; point = candidate; }
                        }
                    }
                    if (layout.IsWater(target) && WorldSimulation.ReachesWater(config, c, layout.Water[target - layout.Food.Count]))
                        return action = Steer(c, config, c.Position, 0);
                }
                else if (!explore)
                    return action = Steer(c, config, c.Position, 0);
                else
                {
                    if (roamingField == null || Distance(c.Position, roamingPoint) < 0.3f)
                    {
                        var snapshot = tracker.Active.Clone(); var seen = new HashSet<int>(snapshot.Visited);
                        var best = float.PositiveInfinity; var chosen = -1;
                        for (var i = 0; i < coverage.Columns * coverage.Rows; i++)
                        {
                            if (!coverage.Eligible(i) || seen.Contains(i) || !paths.IsFree(paths.CellAt(coverage.Centre(i)))) continue;
                            var d = Distance(c.Position, coverage.Centre(i));
                            if (d < best) { best = d; chosen = i; }
                        }
                        if (chosen < 0) return action = CreatureAction.Idle;
                        roamingPoint = coverage.Centre(chosen);
                        roamingField = paths.DistanceField(new[] { paths.CellAt(roamingPoint) });
                    }
                    field = roamingField; point = roamingPoint;
                }
                var waypoint = point;
                if (Distance(c.Position, point) > 2 || !Clear(c.Position, point))
                {
                    var best = float.PositiveInfinity;
                    for (var y = -12; y <= 12; y++)
                        for (var x = -12; x <= 12; x++)
                        {
                            var sample = new Float2(c.Position.X + x * paths.Cell, c.Position.Y + y * paths.Cell);
                            var cell = paths.CellAt(sample);
                            if (!paths.IsFree(cell)) continue;
                            var candidate = paths.CentreOf(cell);
                            var distance = paths.DistanceAt(field, candidate);
                            if (distance < best && Distance(c.Position, candidate) < 3.3f && Clear(c.Position, candidate))
                            { best = distance; waypoint = candidate; }
                        }
                }
                return action = Steer(c, config, waypoint, 4.5f);
            }
            public string Describe(CreatureState c) => "target=" + target + ";pos=" + c.Position +
                ";nearest-water=" + Nearest(c.Position, true, false);
            private int Nearest(Float2 position, bool water, bool uncollected)
            {
                var best = float.PositiveInfinity; var target = -1;
                for (var i = 0; i < layout.TargetCount; i++)
                {
                    if (layout.IsWater(i) != water || uncollected && tracker.IsCollected(i)) continue;
                    var d = paths.DistanceAt(fields[i], position);
                    if (d < best) { best = d; target = i; }
                }
                return target;
            }
            private bool Clear(Float2 from, Float2 to)
            { foreach (var o in expanded) if (o.Blocks(from, to)) return false; return true; }
            private static float Distance(Float2 a, Float2 b) => MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
            private static CreatureAction Steer(CreatureState c, SimulationConfig config, Float2 point, float maxSpeed)
            {
                var dx = point.X - c.Position.X; var dy = point.Y - c.Position.Y;
                var d = MathF.Sqrt(dx * dx + dy * dy); var desired = Math.Min(maxSpeed, d * 3);
                var vx = d > 0 ? dx / d * desired : 0; var vy = d > 0 ? dy / d * desired : 0;
                var ax = (vx - c.Velocity.X) * 5 + vx * config.Damping;
                var ay = (vy - c.Velocity.Y) * 5 + vy * config.Damping;
                var heading = maxSpeed > 0 && d > 0 ? MathF.Atan2(dy, dx) : MathF.Atan2(ay, ax); var delta = heading - c.Heading;
                while (delta > MathF.PI) delta -= 2 * MathF.PI;
                while (delta < -MathF.PI) delta += 2 * MathF.PI;
                var turn = Math.Clamp(delta / (config.MaxTurnRate * config.TickDuration * config.DecisionIntervalTicks), -1, 1);
                var next = c.Heading + turn * config.MaxTurnRate * config.TickDuration;
                var forward = (ax * MathF.Cos(next) + ay * MathF.Sin(next)) / c.Body.MaxAcceleration;
                var left = (-ax * MathF.Sin(next) + ay * MathF.Cos(next)) / c.Body.MaxAcceleration;
                return new CreatureAction(Math.Clamp(forward, 0, 1), turn, Math.Clamp(left, -1, 1));
            }
        }
    }
}
