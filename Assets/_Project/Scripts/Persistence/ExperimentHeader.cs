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
        /// <param name="scenario">Arena size and resource scaling (<see cref="ScenarioSettings.Describe"/>), or null for the control.</param>
        public ExperimentHeader(EvolutionSettings settings, int validationInterval, string appVersion, DateTime createdUtc,
            string preset = null, ResumeOrigin resumedFrom = null, string scenario = null)
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
            Scenario = scenario;
            ConfigHash = CheckpointCodec.Hash(SettingsJson().ToCompactString());
        }

        public EvaluationPlan Plan { get; }
        public ReproductionRules Rules { get; }
        public int ValidationInterval { get; }
        public string AppVersion { get; }
        public DateTime CreatedUtc { get; }
        public string Preset { get; }
        public ResumeOrigin ResumedFrom { get; }

        /// <summary>Arena size and resource scaling of a scenario other than the control; null otherwise.</summary>
        public string Scenario { get; }

        /// <summary>FNV-1a 64 of the canonical settings text.</summary>
        public ulong ConfigHash { get; }

        /// <summary>Whether the experiment breeds NEAT networks (otherwise the fixed-topology MLP).</summary>
        public bool IsNeat => Plan.Algorithm == EvolutionAlgorithm.Neat;

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
                "fitness_profile=" + Fitness.Profile,
                "neural_contract=" + Plan.Contract.Id,
                "network=" + (IsNeat ? NeatNetwork.Kind : NetworkTopology.Mlp12x16x3.ToString()),
                "evolution_algorithm=" + ExperimentJson.EvolutionAlgorithmOf(Plan.Algorithm),
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
                "population=" + Rules.PopulationSize.ToString(culture)
            };
            if (Plan.Senses == Senses.Eyes)
            {
                var eye = Eye.Frontal;
                lines.Add("senses=eyes;founders 1 frontal eye, aperture " + eye.Aperture.ToString(culture) + " rad, range " +
                          eye.Range.ToString(culture) + "x sensor range;no wall radar, no circular food sense");
                var neatEyes = settings.Neat;
                lines.Add(Plan.EvolvingEyes
                    ? "eyes=evolving;probability " + neatEyes.EyeMutationProbability.ToString(culture) + ";orientation_sigma " +
                      neatEyes.EyeOrientationSigma.ToString(culture) + ";aperture_sigma " +
                      neatEyes.EyeApertureSigma.ToString(culture) + ";range_sigma " + neatEyes.EyeRangeSigma.ToString(culture) +
                      ";add " + neatEyes.EyeAddProbability.ToString(culture) + ";remove " +
                      neatEyes.EyeRemoveProbability.ToString(culture)
                    : "eyes=fixed");
            }

            if (IsNeat)
            {
                // NEAT selects by species: elites and tournaments do not apply; the chance and noise below mutate only
                // the expressed body genes.
                var neat = settings.Neat;
                lines.Add("body_mutation_probability=" + Rules.MutationProbability.ToString(culture));
                lines.Add("body_mutation_sigma=" + Rules.MutationSigma.ToString(culture));
                lines.Add("crossover=neat_innovation_aligned;probability " + neat.CrossoverProbability.ToString(culture));
                lines.Add("neat=" + string.Join(";",
                    "initial_weight_limit " + neat.InitialWeightLimit.ToString(culture),
                    "weight_mutation " + neat.WeightMutationProbability.ToString(culture),
                    "weight_perturb " + neat.WeightPerturbProbability.ToString(culture),
                    "weight_sigma " + neat.WeightSigma.ToString(culture),
                    "add_node " + neat.AddNodeProbability.ToString(culture),
                    "add_connection " + neat.AddConnectionProbability.ToString(culture),
                    "toggle " + neat.ToggleProbability.ToString(culture),
                    "add_connection_attempts " + neat.AddConnectionAttempts.ToString(culture),
                    "disabled_inherit " + neat.DisabledInheritProbability.ToString(culture),
                    "c1 " + neat.ExcessCoefficient.ToString(culture),
                    "c2 " + neat.DisjointCoefficient.ToString(culture),
                    "c3 " + neat.WeightDifferenceCoefficient.ToString(culture),
                    "normalization_threshold " + neat.NormalizationThreshold.ToString(culture),
                    "c4_eyes " + neat.EyeCompatibilityCoefficient.ToString(culture),
                    "compatibility_threshold " + neat.CompatibilityThreshold.ToString(culture),
                    "stagnation " + neat.StagnationGenerations.ToString(culture),
                    "protected_species " + neat.ProtectedSpecies.ToString(culture),
                    "champion_minimum_size " + neat.ChampionMinimumSize.ToString(culture),
                    "survival_fraction " + neat.SurvivalFraction.ToString(culture),
                    "interspecies " + neat.InterspeciesProbability.ToString(culture)));
            }
            else
            {
                lines.Add("elites=" + Rules.EliteCount.ToString(culture));
                lines.Add("tournament_size=" + Rules.TournamentSize.ToString(culture));
                lines.Add("mutation_probability=" + Rules.MutationProbability.ToString(culture));
                lines.Add("mutation_sigma=" + Rules.MutationSigma.ToString(culture));
                lines.Add("crossover=off");
            }

            lines.AddRange(new[]
            {
                "body_genes=" + BodyGenesText(Plan.ExpressedBodyGenes),
                "plasticity=" + (Plan.Plasticity ? "hebbian_output_layer" : "off"),
                "food_shift_seconds=" + world.FoodShiftSeconds.ToString(culture),
                "body_cost_exponents=size " + world.SizeCostExponent.ToString(culture) +
                ";speed " + world.SpeedCostExponent.ToString(culture) +
                ";sensor " + world.SensorCostExponent.ToString(culture) +
                ";efficiency_power " + world.EfficiencyPowerExponent.ToString(culture)
            });
            if (world.SolidBodies)
                lines.Add("bodies=solid;radius " + world.BodyRadius.ToString(culture) + ";substeps " +
                          world.SolidSubsteps.ToString(culture) + ";contact_passes " +
                          SimulationConfig.ContactPasses.ToString(culture));
            if (world.Obstacles.Count > 0)
            {
                var obstacles = new List<string>();
                foreach (var o in world.Obstacles)
                {
                    obstacles.Add(o.MinX.ToString(culture) + " " + o.MinY.ToString(culture) + " " +
                                  o.MaxX.ToString(culture) + " " + o.MaxY.ToString(culture));
                }

                lines.Add("obstacles=" + world.Obstacles.Count.ToString(culture) + ";" + string.Join(";", obstacles));
                lines.Add("obstacle_layout=" + LayoutName(world));
                lines.Add("occlusion=obstacles block movement, eyes and feeding;the legacy senses do not see them");
                lines.Add("free_regions=" + world.FreeSpace.Regions.ToString(culture));
            }

            lines.AddRange(WaterLines(world));
            if (world.HasWater)
                lines.Add("water_senses=" + (Plan.Contract.SensesWater
                    ? "eyes see water (not through obstacles; water hides nothing);hydration is a brain input"
                    : "none;the legacy senses neither see water nor feel thirst"));
            lines.AddRange(ResourceLines(world, 1));
            if (Scenario != null)
                lines.Add("scenario=" + Scenario);
            if (Preset != null)
                lines.Add("preset=" + Preset);
            if (ResumedFrom != null)
                lines.AddRange(ResumedFrom.Describe());
            return lines;
        }

        /// <summary>
        /// The food and reserve rules and what the scenario offers (<see cref="ResourceSupply"/>) to its
        /// <paramref name="creatures"/>: densities per 100 square units and the most energy (and water) per second, in
        /// all and per creature, with what a creature spends at rest.
        /// </summary>
        internal static IEnumerable<string> ResourceLines(SimulationConfig world, int creatures)
        {
            var culture = CultureInfo.InvariantCulture;
            var supply = new ResourceSupply(world, creatures);
            yield return "resources=food_energy " + world.FoodEnergy.ToString(culture) + ";food_respawn_seconds " +
                         world.FoodRespawnSeconds.ToString(culture) + ";initial_energy " +
                         world.InitialEnergy.ToString(culture) + ";max_age_seconds " +
                         world.MaxAgeSeconds.ToString(culture);
            var line = "supply=creatures " + supply.Creatures.ToString(culture) +
                       ";creature_density " + Number(supply.CreatureDensity) +
                       ";food_density " + Number(supply.FoodDensity) +
                       ";food_stock " + Number(supply.FoodStock) +
                       ";food_energy_per_second " + Number(supply.FoodEnergyPerSecond) +
                       ";per_creature " + Number(supply.FoodEnergyPerCreaturePerSecond) +
                       ";resting_cost_per_second " + Number(supply.RestingCostPerSecond) +
                       ";creatures_fed_at_rest " + Number(supply.CreaturesFedAtRest) +
                       ";densities per 100 square units;energy per second if every item is eaten when it respawns";
            if (supply.WaterSources > 0)
                line += supply.InexhaustibleWater
                    ? ";water inexhaustible"
                    : ";water_stock " + Number(supply.WaterStock) + ";water_per_second " + Number(supply.WaterPerSecond) +
                      ";water_per_creature_per_second " + Number(supply.WaterPerCreaturePerSecond) +
                      ";hydration_loss_per_second " + Number(supply.HydrationLossPerSecond);
            yield return line;

            string Number(double value) => value.ToString("0.####", culture);
        }

        /// <summary>
        /// The water of a run, when it has any: each source (area, then capacity and recharge, or "inexhaustible"), whether
        /// the supply is limited, and the hydration rules. Nothing for a run without water.
        /// </summary>
        internal static IEnumerable<string> WaterLines(SimulationConfig world)
        {
            if (!world.HasWater)
                yield break;
            var culture = CultureInfo.InvariantCulture;
            var sources = new List<string>();
            var limited = 0;
            foreach (var source in world.Water)
            {
                var a = source.Area;
                var area = a.MinX.ToString(culture) + " " + a.MinY.ToString(culture) + " " + a.MaxX.ToString(culture) + " " +
                           a.MaxY.ToString(culture);
                if (!source.Inexhaustible)
                    limited++;
                sources.Add(area + (source.Inexhaustible
                    ? " inexhaustible"
                    : " capacity " + source.Capacity.ToString(culture) + " recharge " +
                      source.RechargePerSecond.ToString(culture)));
            }

            yield return "water=" + world.Water.Count.ToString(culture) + ";" + string.Join(";", sources);
            yield return "water_supply=" + (limited == world.Water.Count ? "finite"
                : limited == 0 ? "inexhaustible (control)"
                : "mixed");
            yield return "hydration=max " + world.MaxHydration.ToString(culture) + ";loss_per_second " +
                         world.HydrationLossPerSecond.ToString(culture) + ";drink_rate_per_second " +
                         world.DrinkRatePerSecond.ToString(culture) +
                         ";drink at the edge within eat radius;shortage split as equal fractions of each ask;" +
                         "children get the offspring share;no energy from water, no water from food;death at 0";
        }

        /// <summary>Name of the config's obstacle layout (none, pillars, corridor), or "custom" for hand-placed obstacles.</summary>
        internal static string LayoutName(SimulationConfig world)
        {
            var layout = ObstacleLayouts.Identify(world);
            return layout.HasValue ? ObstacleLayouts.Name(layout.Value) : "custom";
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
