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
            : this(width, height, bodyRadius, obstacles, bodyRadius * 0.5f)
        {
        }

        /// <summary>A grid of cells <paramref name="cellSize"/> wide (wider in very large arenas), e.g. a coarser one for path lengths.</summary>
        public FreeSpace(float width, float height, float bodyRadius, IReadOnlyList<Obstacle> obstacles, float cellSize)
        {
            if (obstacles == null)
                throw new ArgumentNullException(nameof(obstacles));
            Cell = Math.Max(cellSize, MathF.Sqrt(width * height / MaxCells));
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

        /// <summary>Index of the cell holding <paramref name="point"/>, or -1 outside the grid.</summary>
        public int CellAt(Float2 point)
        {
            if (!(point.X >= 0f && point.Y >= 0f))
                return -1;
            var column = (int)(point.X / Cell);
            var row = (int)(point.Y / Cell);
            return column < columns && row < rows ? row * columns + column : -1;
        }

        public bool IsFree(int cell) => cell >= 0 && cell < free.Length && free[cell];

        public Float2 CentreOf(int cell) => Centre(cell % columns, cell / columns);

        /// <summary>
        /// Free cells whose centre lies within <paramref name="reach"/> of <paramref name="area"/> with a clear line from
        /// the centre to the nearest point of the area (the cells a body can eat or drink there from), in index order.
        /// </summary>
        public List<int> CellsReaching(Obstacle area, float reach, IReadOnlyList<Obstacle> obstacles)
        {
            var cells = new List<int>();
            var first = Math.Max(0, (int)((area.MinX - reach) / Cell));
            var last = Math.Min(columns - 1, (int)((area.MaxX + reach) / Cell));
            var bottom = Math.Max(0, (int)((area.MinY - reach) / Cell));
            var top = Math.Min(rows - 1, (int)((area.MaxY + reach) / Cell));
            for (var row = bottom; row <= top; row++)
            {
                for (var column = first; column <= last; column++)
                {
                    var cell = row * columns + column;
                    if (!free[cell])
                        continue;
                    var centre = Centre(column, row);
                    if (area.DistanceTo(centre) > reach)
                        continue;
                    var nearest = new Float2(Math.Min(area.MaxX, Math.Max(area.MinX, centre.X)),
                        Math.Min(area.MaxY, Math.Max(area.MinY, centre.Y)));
                    var clear = true;
                    for (var i = 0; clear && i < obstacles.Count; i++)
                        clear = !obstacles[i].Blocks(centre, nearest);
                    if (clear)
                        cells.Add(cell);
                }
            }

            return cells;
        }

        /// <summary>
        /// Length of the shortest path over free cells (8 neighbours; a diagonal step only where both cells beside it are
        /// free, so paths never cut a corner) from the nearest of <paramref name="sources"/> to every cell; infinity where
        /// no path leads, such as another region or a blocked cell.
        /// </summary>
        public float[] DistanceField(IReadOnlyList<int> sources)
        {
            var distance = new float[free.Length];
            for (var i = 0; i < distance.Length; i++)
                distance[i] = float.PositiveInfinity;
            var heap = new CellHeap();
            foreach (var source in sources)
            {
                if (!IsFree(source) || distance[source] == 0f)
                    continue;
                distance[source] = 0f;
                heap.Push(source, 0f);
            }

            var diagonal = Cell * MathF.Sqrt(2f);
            while (heap.TryPop(out var cell, out var reached))
            {
                if (reached > distance[cell])
                    continue;
                var column = cell % columns;
                var row = cell / columns;
                for (var dy = -1; dy <= 1; dy++)
                {
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0)
                            continue;
                        var c = column + dx;
                        var r = row + dy;
                        if (c < 0 || r < 0 || c >= columns || r >= rows || !free[r * columns + c])
                            continue;
                        if (dx != 0 && dy != 0 && (!free[row * columns + c] || !free[r * columns + column]))
                            continue;
                        var next = r * columns + c;
                        var through = reached + (dx != 0 && dy != 0 ? diagonal : Cell);
                        if (through >= distance[next])
                            continue;
                        distance[next] = through;
                        heap.Push(next, through);
                    }
                }
            }

            return distance;
        }

        /// <summary>
        /// The distance of <paramref name="field"/> at <paramref name="point"/>: its cell's, or, when that cell is not free
        /// (a body pressed against a wall), the nearest neighbouring free cell's plus one step; infinity if none.
        /// </summary>
        public float DistanceAt(float[] field, Float2 point)
        {
            var cell = CellAt(point);
            if (cell < 0)
                return float.PositiveInfinity;
            if (free[cell])
                return field[cell];
            var column = cell % columns;
            var row = cell / columns;
            var best = float.PositiveInfinity;
            for (var dy = -1; dy <= 1; dy++)
            {
                for (var dx = -1; dx <= 1; dx++)
                {
                    var c = column + dx;
                    var r = row + dy;
                    if (c >= 0 && r >= 0 && c < columns && r < rows && free[r * columns + c])
                        best = Math.Min(best, field[r * columns + c] + Cell);
                }
            }

            return best;
        }

        private Float2 Centre(int column, int row) => new Float2((column + 0.5f) * Cell, (row + 0.5f) * Cell);

        // Binary min-heap of (cell, distance) in two arrays for the distance field; ties pop in any order, which does
        // not change the distances found.
        private sealed class CellHeap
        {
            private int[] cells = new int[1024];
            private float[] distances = new float[1024];
            private int count;

            public void Push(int cell, float distance)
            {
                if (count == cells.Length)
                {
                    Array.Resize(ref cells, count * 2);
                    Array.Resize(ref distances, count * 2);
                }

                var i = count++;
                while (i > 0)
                {
                    var parent = (i - 1) >> 1;
                    if (distances[parent] <= distance)
                        break;
                    cells[i] = cells[parent];
                    distances[i] = distances[parent];
                    i = parent;
                }

                cells[i] = cell;
                distances[i] = distance;
            }

            public bool TryPop(out int cell, out float distance)
            {
                if (count == 0)
                {
                    cell = -1;
                    distance = 0f;
                    return false;
                }

                cell = cells[0];
                distance = distances[0];
                count--;
                if (count == 0)
                    return true;
                var movedCell = cells[count];
                var moved = distances[count];
                var i = 0;
                while (true)
                {
                    var child = 2 * i + 1;
                    if (child >= count)
                        break;
                    if (child + 1 < count && distances[child + 1] < distances[child])
                        child++;
                    if (moved <= distances[child])
                        break;
                    cells[i] = cells[child];
                    distances[i] = distances[child];
                    i = child;
                }

                cells[i] = movedCell;
                distances[i] = moved;
                return true;
            }
        }

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
