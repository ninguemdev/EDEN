using System;
using System.Collections.Generic;
using System.Globalization;
using Eden.Application;
using Eden.Domain;
using Eden.Simulation;

namespace Eden.Persistence
{
    /// <summary>
    /// Events of a shared world's genealogy in <c>genealogy.csv</c>, in tick order: <c>founder</c> rows for the creatures
    /// a run starts with, then per tick its deaths and its births, and an <c>extinction</c> row when the last creature
    /// dies. A log opened for a restored run first writes the saved history, so the file always holds the whole line of
    /// every creature. Invariant numbers; each row is flushed.
    /// </summary>
    public sealed class GenealogyCsvLog : IDisposable
    {
        public const string Header = "tick,event,creature,parent,generation,cause";

        private CsvFile file;

        public GenealogyCsvLog(string path, Genealogy history)
        {
            if (history == null)
                throw new ArgumentNullException(nameof(history));
            file = new CsvFile(path, Header);
            FilePath = path;

            var events = new List<(long Tick, int Order, string Row)>();
            foreach (var entry in history.Entries)
            {
                events.Add((entry.BirthTick, entry.Parent.HasValue ? 2 : 0,
                    Row(entry.BirthTick, entry.Parent.HasValue ? "birth" : "founder", entry, DeathCause.None)));
                if (!entry.IsAlive)
                    events.Add((entry.DeathTick, 1, Row(entry.DeathTick, "death", entry, entry.DeathCause)));
            }

            foreach (var row in StableSort(events))
                Write(row);
        }

        public string FilePath { get; }
        public int RowCount { get; private set; }

        /// <summary>Deaths, then births, of the tick <paramref name="simulation"/> just ran, as the genealogy records them.</summary>
        public void Append(WorldSimulation simulation, Genealogy genealogy)
        {
            if (simulation == null)
                throw new ArgumentNullException(nameof(simulation));
            if (genealogy == null)
                throw new ArgumentNullException(nameof(genealogy));
            foreach (var death in simulation.LastDeaths)
            {
                if (genealogy.TryGet(death.Creature, out var entry))
                    Write(Row(death.Tick, "death", entry, death.Cause));
            }

            foreach (var birth in simulation.LastBirths)
            {
                if (genealogy.TryGet(birth.Child, out var entry))
                    Write(Row(birth.Tick, "birth", entry, DeathCause.None));
            }
        }

        public void AppendExtinction(long tick) =>
            Write(string.Join(",", tick.ToString(CultureInfo.InvariantCulture), "extinction", string.Empty, string.Empty,
                string.Empty, string.Empty));

        public void Dispose()
        {
            file?.Dispose();
            file = null;
        }

        public static string CauseName(DeathCause cause) =>
            cause == DeathCause.Starvation ? "starvation" : cause == DeathCause.OldAge ? "old_age" : string.Empty;

        private void Write(string row)
        {
            if (file == null)
                throw new ObjectDisposedException(nameof(GenealogyCsvLog));
            file.Append(row);
            RowCount++;
        }

        private static string Row(long tick, string kind, GenealogyEntry entry, DeathCause cause)
        {
            var culture = CultureInfo.InvariantCulture;
            return string.Join(",",
                tick.ToString(culture),
                kind,
                entry.Creature.Value.ToString(culture),
                entry.Parent.HasValue ? entry.Parent.Value.Value.ToString(culture) : string.Empty,
                entry.Generation.ToString(culture),
                CauseName(cause));
        }

        // By tick, then founders, deaths, births; List.Sort is not stable, so the index keeps id order within those.
        private static List<string> StableSort(List<(long Tick, int Order, string Row)> events)
        {
            var indexed = new List<(long Tick, int Order, int Index, string Row)>(events.Count);
            for (var i = 0; i < events.Count; i++)
                indexed.Add((events[i].Tick, events[i].Order, i, events[i].Row));
            indexed.Sort((a, b) => a.Tick != b.Tick ? a.Tick.CompareTo(b.Tick)
                : a.Order != b.Order ? a.Order.CompareTo(b.Order)
                : a.Index.CompareTo(b.Index));
            return indexed.ConvertAll(e => e.Row);
        }
    }
}
