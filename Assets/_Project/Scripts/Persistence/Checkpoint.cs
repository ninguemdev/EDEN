using System;
using System.Collections.Generic;
using Eden.Application;
using Eden.Domain;
using Eden.Simulation;

namespace Eden.Persistence
{
    /// <summary>A checkpoint that cannot be used: unreadable, corrupted, inconsistent or from a newer schema.</summary>
    public sealed class CheckpointException : Exception
    {
        public CheckpointException(string message)
            : base(message)
        {
        }

        public CheckpointException(string message, Exception inner)
            : base(message, inner)
        {
        }
    }

    /// <summary>
    /// Lab experiment saved between ticks: what was chosen (mode, preset, seed and brain, as the lab names them), the
    /// evolution if there is one and the shared world if one is running. Holds detached snapshots only, never live
    /// objects, so saving it and changing the run afterwards cannot interfere.
    /// </summary>
    public sealed class Checkpoint
    {
        public Checkpoint(string mode, string preset, ulong seed, string brain, EvolutionSnapshot evolution,
            SharedWorldSnapshot sharedWorld)
        {
            Mode = mode ?? throw new ArgumentNullException(nameof(mode));
            Preset = preset ?? throw new ArgumentNullException(nameof(preset));
            Seed = seed;
            Brain = brain ?? throw new ArgumentNullException(nameof(brain));
            if (evolution == null && sharedWorld == null)
                throw new ArgumentException("A checkpoint holds an evolution, a shared world or both.");
            Evolution = evolution;
            SharedWorld = sharedWorld;
        }

        public string Mode { get; }
        public string Preset { get; }
        public ulong Seed { get; }
        public string Brain { get; }

        /// <summary>The lab's evolution; null if none was started.</summary>
        public EvolutionSnapshot Evolution { get; }

        /// <summary>The shared world being observed; null while the lab shows the evolution.</summary>
        public SharedWorldSnapshot SharedWorld { get; }
    }

    /// <summary>A shared world between ticks: its settings, its state and its creatures' brain state.</summary>
    public sealed class SharedWorldSnapshot
    {
        /// <param name="bornGenomes">Brain genomes of the creatures born in the world (none when brains have no genome).</param>
        public SharedWorldSnapshot(SimulationSettings settings, SimulationSnapshot world, IReadOnlyList<BrainSlotState> brains,
            IReadOnlyList<BornGenome> bornGenomes = null)
        {
            Settings = settings?.Clone() ?? throw new ArgumentNullException(nameof(settings));
            World = world ?? throw new ArgumentNullException(nameof(world));
            if (brains == null)
                throw new ArgumentNullException(nameof(brains));
            var copy = new BrainSlotState[brains.Count];
            for (var i = 0; i < copy.Length; i++)
                copy[i] = brains[i];
            Brains = copy;
            var genomes = new BornGenome[bornGenomes?.Count ?? 0];
            for (var i = 0; i < genomes.Length; i++)
                genomes[i] = bornGenomes[i];
            BornGenomes = genomes;
        }

        /// <summary>A copy of the settings the world was created with.</summary>
        public SimulationSettings Settings { get; }

        public SimulationSnapshot World { get; }
        public IReadOnlyList<BrainSlotState> Brains { get; }
        public IReadOnlyList<BornGenome> BornGenomes { get; }
    }

    /// <summary>Where and how a checkpoint was written; recorded for people, never used to decide results.</summary>
    public sealed class CheckpointInfo
    {
        public CheckpointInfo(int schemaVersion, string appVersion, string runtime, DateTime savedUtc, ulong configHash,
            ulong stateHash)
        {
            SchemaVersion = schemaVersion;
            AppVersion = appVersion ?? string.Empty;
            Runtime = runtime ?? string.Empty;
            SavedUtc = savedUtc;
            ConfigHash = configHash;
            StateHash = stateHash;
        }

        public int SchemaVersion { get; }
        public string AppVersion { get; }

        /// <summary>Engine version and platform, e.g. "6000.3.27f1 WindowsPlayer".</summary>
        public string Runtime { get; }

        public DateTime SavedUtc { get; }

        /// <summary>FNV-1a 64 of the canonical config text: same value, same experiment definition.</summary>
        public ulong ConfigHash { get; }

        /// <summary>FNV-1a 64 of the canonical state text, checked on load.</summary>
        public ulong StateHash { get; }
    }
}
