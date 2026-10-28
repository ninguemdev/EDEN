using System;
using System.Collections.Generic;

namespace Eden.Domain
{
    /// <summary>
    /// The places of the arena where the reference body can stand, on a grid of cells half its radius wide (wider in
    /// very large arenas, so the grid stays within about a million cells), built once per config with obstacles. A cell
    /// is free when the reference disc centred on it fits between the walls and the obstacles. <see cref="Regions"/>
    /// counts the free areas that do not connect (4-neighbour), so a scenario split by its obstacles is visible.
    /// </summary>
    public sealed class FreeSpace
    {
        private const int MaxCells = 1 << 20;

        private readonly bool[] free;
        private readonly int columns;
        private readonly int rows;

        public FreeSpace(float width, float height, float bodyRadius, IReadOnlyList<Obstacle> obstacles)
        {
            if (obstacles == null)
                throw new ArgumentNullException(nameof(obstacles));
            Cell = Math.Max(bodyRadius * 0.5f, MathF.Sqrt(width * height / MaxCells));
            columns = Math.Max(1, (int)(width / Cell));
            rows = Math.Max(1, (int)(height / Cell));
            free = new bool[columns * rows];
            for (var row = 0; row < rows; row++)
            {
                for (var column = 0; column < columns; column++)
                {
                    var centre = Centre(column, row);
                    var fits = centre.X >= bodyRadius && centre.X <= width - bodyRadius && centre.Y >= bodyRadius &&
                               centre.Y <= height - bodyRadius;
                    for (var i = 0; fits && i < obstacles.Count; i++)
                        fits = obstacles[i].DistanceTo(centre) >= bodyRadius;
                    free[row * columns + column] = fits;
                }
            }

            Regions = CountRegions();
        }

        /// <summary>Width of a grid cell.</summary>
        public float Cell { get; }

        /// <summary>Separate free areas: 1 when every free place connects, 0 when the reference body fits nowhere.</summary>
        public int Regions { get; }

        /// <summary>Whether some free cell's centre lies within <paramref name="reach"/> of the point with a clear line.</summary>
        public bool Reaches(Float2 point, float reach, IReadOnlyList<Obstacle> obstacles)
        {
            var first = Math.Max(0, (int)((point.X - reach) / Cell));
            var last = Math.Min(columns - 1, (int)((point.X + reach) / Cell));
            var bottom = Math.Max(0, (int)((point.Y - reach) / Cell));
            var top = Math.Min(rows - 1, (int)((point.Y + reach) / Cell));
            for (var row = bottom; row <= top; row++)
            {
                for (var column = first; column <= last; column++)
                {
                    if (!free[row * columns + column])
                        continue;
                    var centre = Centre(column, row);
                    var dx = centre.X - point.X;
                    var dy = centre.Y - point.Y;
                    if (dx * dx + dy * dy > reach * reach)
                        continue;
                    var clear = true;
                    for (var i = 0; clear && i < obstacles.Count; i++)
                        clear = !obstacles[i].Blocks(centre, point);
                    if (clear)
                        return true;
                }
            }

            return false;
        }

        private Float2 Centre(int column, int row) => new Float2((column + 0.5f) * Cell, (row + 0.5f) * Cell);

        private int CountRegions()
        {
            var seen = new bool[free.Length];
            var pending = new Stack<int>();
            var regions = 0;
            for (var start = 0; start < free.Length; start++)
            {
                if (!free[start] || seen[start])
                    continue;
                regions++;
                seen[start] = true;
                pending.Push(start);
                while (pending.Count > 0)
                {
                    var cell = pending.Pop();
                    var column = cell % columns;
                    var row = cell / columns;
                    Visit(column > 0 ? cell - 1 : -1, seen, pending);
                    Visit(column < columns - 1 ? cell + 1 : -1, seen, pending);
                    Visit(row > 0 ? cell - columns : -1, seen, pending);
                    Visit(row < rows - 1 ? cell + columns : -1, seen, pending);
                }
            }

            return regions;
        }

        private void Visit(int cell, bool[] seen, Stack<int> pending)
        {
            if (cell < 0 || !free[cell] || seen[cell])
                return;
            seen[cell] = true;
            pending.Push(cell);
        }
    }
}
