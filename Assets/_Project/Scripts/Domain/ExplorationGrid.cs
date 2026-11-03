using System;
using System.Collections.Generic;

namespace Eden.Domain
{
    /// <summary>Version 1 exploration mask: clipped cell centres connected to the founder by four neighbours.
    /// A fixed reference disc must fit at each centre and along each connecting segment. Partial edge cells are clipped.</summary>
    public sealed class ExplorationGrid
    {
        private readonly bool[] eligible;
        private readonly float width, height;
        public ExplorationGrid(SimulationConfig config, Float2 founder, float side)
        {
            Side = side; width = config.ArenaWidth; height = config.ArenaHeight;
            Columns = (int)System.Math.Ceiling(width / side); Rows = (int)System.Math.Ceiling(height / side);
            eligible = new bool[Columns * Rows];
            var free = new bool[eligible.Length];
            var obstacles = new System.Collections.Generic.List<Obstacle>();
            foreach (var o in config.Barriers)
                obstacles.Add(new Obstacle(o.MinX - config.BodyRadius, o.MinY - config.BodyRadius,
                    o.MaxX + config.BodyRadius, o.MaxY + config.BodyRadius));
            for (var i = 0; i < free.Length; i++)
            {
                var p = Centre(i);
                var fits = p.X >= config.BodyRadius && p.X <= width - config.BodyRadius &&
                    p.Y >= config.BodyRadius && p.Y <= height - config.BodyRadius;
                foreach (var o in config.Barriers) fits &= o.DistanceTo(p) >= config.BodyRadius;
                free[i] = fits;
            }
            Initial = CellAt(founder);
            // The founder can stand near a blocked centre. Attach to the nearest free centre with a clear segment.
            var start = -1; var distance = float.PositiveInfinity;
            for (var i = 0; i < free.Length; i++)
            {
                if (!free[i]) continue;
                var p = Centre(i); var dx = p.X - founder.X; var dy = p.Y - founder.Y;
                var d = dx * dx + dy * dy; var clear = true;
                foreach (var o in obstacles) clear &= !o.Blocks(founder, p);
                if (clear && d < distance) { start = i; distance = d; }
            }
            if (start < 0) throw new System.ArgumentException("Exploration mask has no founder-connected cell.");
            var queue = new System.Collections.Generic.Queue<int>(); queue.Enqueue(start); eligible[start] = true;
            while (queue.Count > 0)
            {
                var at = queue.Dequeue();
                foreach (var next in Neighbours(at))
                {
                    if (!free[next] || eligible[next]) continue;
                    var clear = true;
                    foreach (var o in obstacles) clear &= !o.Blocks(Centre(at), Centre(next));
                    if (clear) { eligible[next] = true; queue.Enqueue(next); }
                }
            }
            var hash = StateHasher.Create(); hash.Add(1); hash.Add(width); hash.Add(height); hash.Add(side);
            hash.Add(config.BodyRadius); hash.Add(Initial);
            for (var i = 0; i < eligible.Length; i++)
            {
                hash.Add(eligible[i] ? 1 : 0);
                if (eligible[i] && i != Initial) EligibleCount++;
            }
            Identity = hash.Value;
            if (EligibleCount == 0) throw new System.ArgumentException("Exploration mask has no eligible cell beyond the founder.");
        }
        public int Columns { get; }
        public int Rows { get; }
        public int Initial { get; }
        public int EligibleCount { get; }
        public ulong Identity { get; }
        public float Side { get; }
        public int CellAt(Float2 p) => System.Math.Min(Rows - 1, System.Math.Max(0, (int)(p.Y / Side))) * Columns +
            System.Math.Min(Columns - 1, System.Math.Max(0, (int)(p.X / Side)));
        public bool Eligible(int cell) => cell >= 0 && cell < eligible.Length && cell != Initial && eligible[cell];
        public Float2 Centre(int i) => new Float2((i % Columns * Side + System.Math.Min(width, (i % Columns + 1) * Side)) / 2,
            (i / Columns * Side + System.Math.Min(height, (i / Columns + 1) * Side)) / 2);
        public bool Interior(Float2 p, int cell, float margin)
        {
            var x = cell % Columns * Side; var y = cell / Columns * Side;
            var right = System.Math.Min(width, x + Side); var top = System.Math.Min(height, y + Side);
            var mx = System.Math.Min(margin, (right - x) / 2); var my = System.Math.Min(margin, (top - y) / 2);
            return p.X >= x + mx && p.X <= right - mx && p.Y >= y + my && p.Y <= top - my;
        }
        private System.Collections.Generic.IEnumerable<int> Neighbours(int cell)
        {
            if (cell % Columns > 0) yield return cell - 1;
            if (cell % Columns + 1 < Columns) yield return cell + 1;
            if (cell >= Columns) yield return cell - Columns;
            if (cell + Columns < eligible.Length) yield return cell + Columns;
        }
    }
}
