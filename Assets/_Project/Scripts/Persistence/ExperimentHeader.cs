using System;
using System.Collections.Generic;
using System.Globalization;
using Eden.Application;
using Eden.Domain;
using Eden.Evolution;

namespace Eden.Persistence
{
    /// <summary>
    /// What an experiment export says about its experiment: the settings and their hash, the validation schedule, the
    /// preset and app that ran it and, for a run resumed from a checkpoint, where it came from. The same canonical
    /// settings text is hashed here and in checkpoints, so equal hashes mean the same experiment definition.
    /// </summary>
    public sealed class ExperimentHeader
    {
        private readonly EvolutionSettings settings;

        /// <param name="validationInterval">Generations between validations; 0 when the run is never validated.</param>
        /// <param name="preset">Lab preset name, or null outside the lab.</param>
        public ExperimentHeader(EvolutionSettings settings, int validationInterval, string appVersion, DateTime createdUtc,
            string preset = null, ResumeOrigin resumedFrom = null)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));
            if (validationInterval < 0)
                throw new ArgumentOutOfRangeException(nameof(validationInterval));

            this.settings = settings.WithExperimentSeed(settings.ExperimentSeed);
            Plan = new EvaluationPlan(this.settings);
            Rules = new ReproductionRules(this.settings);
            ValidationInterval = validationInterval;
            AppVersion = appVersion ?? string.Empty;
            CreatedUtc = createdUtc.ToUniversalTime();
            Preset = preset;
            ResumedFrom = resumedFrom;
            ConfigHash = CheckpointCodec.Hash(SettingsJson().ToCompactString());
        }

        public EvaluationPlan Plan { get; }
        public ReproductionRules Rules { get; }
        public int ValidationInterval { get; }
        public string AppVersion { get; }
        public DateTime CreatedUtc { get; }
        public string Preset { get; }
        public ResumeOrigin ResumedFrom { get; }

        /// <summary>FNV-1a 64 of the canonical settings text.</summary>
        public ulong ConfigHash { get; }

        /// <summary>Plain key=value lines of <c>experiment.txt</c>.</summary>
        public IReadOnlyList<string> Describe()
        {
            var culture = CultureInfo.InvariantCulture;
            var world = Plan.EpisodeConfig(0);
            var lines = new List<string>
            {
                "experiment_seed=" + Plan.ExperimentSeed.ToString(culture),
                "app_version=" + AppVersion,
                "created_utc=" + Utc(CreatedUtc),
                "config_hash=" + CheckpointCodec.Hex(ConfigHash),
                "evaluation_mode=isolated",
                "fitness_unit=" + ExperimentJson.FitnessUnit,
                "seed_scheme=" + EvaluationPlan.SeedSchemeVersion.ToString(culture),
                "training_seeds_per_generation=" + Plan.TrainingSeedsPerGeneration.ToString(culture),
                "validation_seed=" + Plan.ValidationSeed.ToString(culture),
                "validation_seeds=" + string.Join(";", Array.ConvertAll(Plan.ValidationSeeds(), s => s.ToString(culture))),
                "validation_interval=" + ValidationInterval.ToString(culture),
                "episode_seconds=" + Plan.EpisodeSeconds.ToString(culture),
                "episode_ticks=" + Plan.EpisodeTicks.ToString(culture),
                "config_schema=" + world.SchemaVersion.ToString(culture),
                "ticks_per_second=" + world.TicksPerSecond.ToString(culture),
                "arena=" + world.ArenaWidth.ToString(culture) + "x" + world.ArenaHeight.ToString(culture),
                "episode_food=" + world.InitialFood.ToString(culture),
                "population=" + Rules.PopulationSize.ToString(culture),
                "elites=" + Rules.EliteCount.ToString(culture),
                "tournament_size=" + Rules.TournamentSize.ToString(culture),
                "mutation_probability=" + Rules.MutationProbability.ToString(culture),
                "mutation_sigma=" + Rules.MutationSigma.ToString(culture),
                "crossover=off",
                "body_genes=" + BodyGenesText(Plan.ExpressedBodyGenes),
                "body_cost_exponents=size " + world.SizeCostExponent.ToString(culture) +
                ";speed " + world.SpeedCostExponent.ToString(culture) +
                ";sensor " + world.SensorCostExponent.ToString(culture) +
                ";efficiency_power " + world.EfficiencyPowerExponent.ToString(culture)
            };
            if (Preset != null)
                lines.Add("preset=" + Preset);
            if (ResumedFrom != null)
                lines.AddRange(ResumedFrom.Describe());
            return lines;
        }

        // "fixed" for the fixed-body control, otherwise the expressed genes in genome order, e.g. "size;sensor".
        private static string BodyGenesText(BodyGeneSet expressed)
        {
            if (expressed == BodyGeneSet.None)
                return "fixed";
            var names = new List<string>();
            for (var g = 0; g < BodyGenes.Count; g++)
            {
                if (((int)expressed & (1 << g)) != 0)
                    names.Add(((BodyGene)g).ToString().ToLowerInvariant());
            }

            return string.Join(";", names);
        }

        internal JsonValue SettingsJson() => CheckpointCodec.EncodeEvolutionSettings(settings, ValidationInterval);

        internal static string Utc(DateTime utc) =>
            utc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The checkpoint a run was resumed from and the evaluation it was in the middle of. The episodes that evaluation had
    /// already finished are taken from the checkpoint, not simulated again, and the export of the resumed run records
    /// how many there were and how far the episode in progress had run.
    /// </summary>
    public sealed class ResumeOrigin
    {
        public ResumeOrigin(string checkpointPath, CheckpointInfo checkpoint, EvolutionSnapshot saved)
        {
            if (checkpoint == null)
                throw new ArgumentNullException(nameof(checkpoint));
            if (saved == null)
                throw new ArgumentNullException(nameof(saved));

            CheckpointPath = checkpointPath ?? string.Empty;
            SavedUtc = checkpoint.SavedUtc;
            StateHash = checkpoint.StateHash;
            Generation = saved.Generation;
            Phase = saved.Phase;
            TotalTicks = saved.TotalTicks;
            EvaluationGeneration = saved.Phase == EvolutionPhase.Training ? saved.Generation
                : saved.Phase == EvolutionPhase.Validation ? saved.Generation - 1
                : 0;
            RestoredEpisodes = saved.Job.Completed.Count;
            EpisodeTick = saved.Job.CurrentWorld.Tick;
        }

        public string CheckpointPath { get; }
        public DateTime SavedUtc { get; }
        public ulong StateHash { get; }

        /// <summary>Generation of the population when the checkpoint was saved.</summary>
        public int Generation { get; }

        public EvolutionPhase Phase { get; }
        public long TotalTicks { get; }

        /// <summary>Generation whose evaluation was in progress (0 for the reference brains).</summary>
        public int EvaluationGeneration { get; }

        /// <summary>Episodes of that evaluation finished before the checkpoint, written from it and not run again.</summary>
        public int RestoredEpisodes { get; }

        /// <summary>Ticks the episode in progress had already run; it continues from there.</summary>
        public long EpisodeTick { get; }

        internal IEnumerable<string> Describe()
        {
            var culture = CultureInfo.InvariantCulture;
            yield return "resumed_from=" + CheckpointPath;
            yield return "resumed_checkpoint_saved_utc=" + ExperimentHeader.Utc(SavedUtc);
            yield return "resumed_checkpoint_state_hash=" + CheckpointCodec.Hex(StateHash);
            yield return "resumed_at_generation=" + Generation.ToString(culture);
            yield return "resumed_at_phase=" + CheckpointCodec.PhaseName(Phase);
            yield return "resumed_at_total_ticks=" + TotalTicks.ToString(culture);
            yield return "resumed_evaluation_generation=" + EvaluationGeneration.ToString(culture);
            yield return "resumed_restored_episodes=" + RestoredEpisodes.ToString(culture);
            yield return "resumed_episode_tick=" + EpisodeTick.ToString(culture);
        }

        internal JsonValue ToJson() =>
            JsonValue.NewObject()
                .Add("checkpoint", JsonValue.Of(CheckpointPath))
                .Add("savedUtc", JsonValue.Of(ExperimentHeader.Utc(SavedUtc)))
                .Add("stateHash", JsonValue.Of(CheckpointCodec.Hex(StateHash)))
                .Add("generation", JsonValue.Of(Generation))
                .Add("phase", JsonValue.Of(CheckpointCodec.PhaseName(Phase)))
                .Add("totalTicks", JsonValue.Of(TotalTicks))
                .Add("evaluationGeneration", JsonValue.Of(EvaluationGeneration))
                .Add("restoredEpisodes", JsonValue.Of(RestoredEpisodes))
                .Add("episodeTick", JsonValue.Of(EpisodeTick));
    }
}
