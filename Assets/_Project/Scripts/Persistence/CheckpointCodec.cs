using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Eden.Application;
using Eden.Brains;
using Eden.Domain;
using Eden.Evolution;
using Eden.Simulation;

namespace Eden.Persistence
{
    /// <summary>
    /// JSON form of a <see cref="Checkpoint"/>, schema version 2. The envelope records the format, schema, app and
    /// runtime, the algorithms the state depends on and FNV-1a 64 hashes of the canonical (compact) text of its
    /// <c>config</c> and <c>state</c> sections. Decoding checks, in order: JSON syntax, format, schema version (newer
    /// ones are refused with an explicit message), algorithms, both hashes, every value's type, range and array size
    /// (before anything is allocated for it), that encoding the result again gives the very same text (so nothing was
    /// lost, added or rounded), and finally that the evolution and shared world restore. Floats are written with the
    /// invariant culture and the shortest text that reads back to the same bits. Body genes came later: expressed genes,
    /// a genome's and a creature's body genes and the world's body cost exponents are written only when they differ
    /// from the fixed body and the default costs, so older checkpoints (genome schema 1, config schema 3, always the
    /// fixed body, where costs change nothing) read as the same run. Reproduction came after that: the reproduction
    /// settings, a creature's parent, generation and cooldown, the genomes of creatures born in a shared world and its
    /// genealogy are written only when reproduction is on or they are set, so config schema 4 files read the same way.
    /// Plasticity (schema 5, genome schema 3) likewise: the experiment option, a genome's coefficients and a brain's
    /// lifetime weight changes are written only when used, so a genome schema 2 file reads as fixed networks.
    /// NEAT (schema 6): the algorithm and its parameters, NEAT genomes (their connections instead of topology and
    /// weights), the innovation counters, the species and the species of the last trained generation are written only
    /// for a NEAT experiment, and only such a file says schema 6, so a build without NEAT refuses it as newer while MLP
    /// files stay schema 5, byte for byte.
    /// Parallel evaluation (schema 7) additionally stores the worker count and all episodes ahead of the displayed
    /// world, including finished results awaiting ordered aggregation. Sequential files retain their old schemas.
    /// </summary>
    public static class CheckpointCodec
    {
        public const string Format = "eden-checkpoint";

        /// <summary>
        /// Newest schema: runs scored by a reward profile other than the legacy one (profile, stages, stage and components
        /// of every episode, what the running episodes measured). Legacy runs keep schema 7 with parallel evaluation,
        /// 6 for sequential NEAT and 5 for MLP.
        /// </summary>
        public const int SchemaVersion = 8;

        /// <summary>Schema of a legacy-profile run with parallel evaluation progress.</summary>
        public const int SchemaWithParallel = 7;

        /// <summary>Schema of every checkpoint without NEAT, unchanged since plasticity.</summary>
        public const int SchemaWithoutNeat = 5;

        /// <summary>Limits checked before allocating; generous for the lab, small enough to refuse absurd files.</summary>
        public const int MaxLineageEntries = 8000000;
        public const int MaxHistory = 1000000;

        private const int MaxIndividuals = 2 * ReproductionRules.MaxPopulationSize;
        private const int MaxEpisodes = ReproductionRules.MaxPopulationSize * EvaluationPlan.MaxTrainingSeeds;

        public static string Encode(Checkpoint checkpoint, string appVersion, string runtime, DateTime savedUtc) =>
            Encode(checkpoint, appVersion, runtime, savedUtc, out _);

        /// <summary>Text of the checkpoint; <paramref name="info"/> receives its header, hashes included.</summary>
        public static string Encode(Checkpoint checkpoint, string appVersion, string runtime, DateTime savedUtc,
            out CheckpointInfo info)
        {
            if (checkpoint == null)
                throw new ArgumentNullException(nameof(checkpoint));
            var schema = checkpoint.Evolution?.Settings.Profile?.IsLegacy == false ? SchemaVersion
                : checkpoint.Evolution?.Job.WorkerCount > 1 ? SchemaWithParallel
                : checkpoint.Evolution?.Neat != null ? 6 : SchemaWithoutNeat;
            var config = EncodeConfig(checkpoint);
            var state = EncodeState(checkpoint);
            var configHash = Hash(config.ToCompactString());
            var stateHash = Hash(state.ToCompactString());
            var utc = savedUtc.ToUniversalTime();
            info = new CheckpointInfo(schema, appVersion, runtime, utc, configHash, stateHash);

            var root = JsonValue.NewObject()
                .Add("format", JsonValue.Of(Format))
                .Add("schemaVersion", JsonValue.Of(schema))
                .Add("app", JsonValue.NewObject()
                    .Add("version", JsonValue.Of(info.AppVersion))
                    .Add("runtime", JsonValue.Of(info.Runtime)))
                .Add("savedUtc", JsonValue.Of(utc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)))
                .Add("algorithms", EncodeAlgorithms())
                .Add("configHash", JsonValue.Of(Hex(configHash)))
                .Add("stateHash", JsonValue.Of(Hex(stateHash)))
                .Add("config", config)
                .Add("state", state);
            return JsonText.Write(root);
        }

        /// <summary>Reads and checks a checkpoint; anything wrong raises a <see cref="CheckpointException"/>.</summary>
        public static Checkpoint Decode(string text, out CheckpointInfo info)
        {
            if (text == null)
                throw new ArgumentNullException(nameof(text));

            var root = JsonText.Parse(text);
            if (root.Kind != JsonKind.Object || TryString(root, "format") != Format)
                throw new CheckpointException("Not an EDEN checkpoint.");
            var schema = root["schemaVersion"].Long(1);
            if (schema > SchemaVersion)
                throw new CheckpointException($"Checkpoint schema version {schema} is newer than version {SchemaVersion}, " +
                                              "the newest this build reads; open it with a newer EDEN.");
            CheckAlgorithms(root["algorithms"]);

            var config = root["config"];
            var state = root["state"];
            var configText = config.ToCompactString();
            var stateText = state.ToCompactString();
            var configHash = ParseHex(root["configHash"]);
            var stateHash = ParseHex(root["stateHash"]);
            if (Hash(configText) != configHash)
                throw new CheckpointException("Config hash mismatch: the file is corrupted or was edited.");
            if (Hash(stateText) != stateHash)
                throw new CheckpointException("State hash mismatch: the file is corrupted or was edited.");

            var app = root["app"];
            info = new CheckpointInfo((int)schema, app["version"].String(), app["runtime"].String(),
                ParseUtc(root["savedUtc"]), configHash, stateHash);

            var checkpoint = Guard(() => DecodeCheckpoint(config, state));
            if (EncodeConfig(checkpoint).ToCompactString() != configText ||
                EncodeState(checkpoint).ToCompactString() != stateText)
                throw new CheckpointException("The checkpoint does not read back exactly (unknown or non-canonical values).");
            Guard(() => CheckRestorable(checkpoint));
            return checkpoint;
        }

        internal static JsonValue EncodeAlgorithms() =>
            JsonValue.NewObject()
                .Add("random", JsonValue.Of(Pcg32.AlgorithmName))
                .Add("gaussian", JsonValue.Of(GaussianSampler.AlgorithmName))
                .Add("seedScheme", JsonValue.Of(SeedDerivation.SchemeVersion))
                .Add("evaluationSeedScheme", JsonValue.Of(EvaluationPlan.SeedSchemeVersion))
                .Add("configSchema", JsonValue.Of(SimulationConfig.CurrentSchemaVersion))
                .Add("genomeSchema", JsonValue.Of(Genome.CurrentSchemaVersion));

        private static void CheckAlgorithms(JsonValue saved)
        {
            foreach (var expected in EncodeAlgorithms().Members)
            {
                var actual = saved[expected.Key];
                // A schema 1 genome is today's genome with neutral body genes, and a schema 3 config is today's config
                // with the default body costs, which do nothing for the fixed body those files always have.
                if (actual.Kind == JsonKind.Number && (expected.Key == "genomeSchema" && (actual.Text == "1" || actual.Text == "2") ||
                                                       expected.Key == "configSchema" && (actual.Text == "3" || actual.Text == "4" || actual.Text == "5")))
                    continue;
                if (actual.Kind != expected.Value.Kind || actual.Text != expected.Value.Text)
                    throw new CheckpointException($"Saved with {expected.Key} {actual.Text}; this build uses " +
                                                  $"{expected.Value.Text} and cannot continue that run exactly.");
            }
        }

        // ---- config ----

        private static JsonValue EncodeConfig(Checkpoint checkpoint)
        {
            var evolution = checkpoint.Evolution;
            return JsonValue.NewObject()
                .Add("lab", JsonValue.NewObject()
                    .Add("mode", JsonValue.Of(checkpoint.Mode))
                    .Add("preset", JsonValue.Of(checkpoint.Preset))
                    .Add("seed", JsonValue.Of(checkpoint.Seed))
                    .Add("brain", JsonValue.Of(checkpoint.Brain)))
                .Add("evolution", evolution == null ? JsonValue.Null() : EncodeEvolutionSettings(evolution))
                .Add("sharedWorld", checkpoint.SharedWorld == null
                    ? JsonValue.Null()
                    : EncodeSettings(checkpoint.SharedWorld.Settings));
        }

        private static JsonValue EncodeEvolutionSettings(EvolutionSnapshot evolution) =>
            EncodeEvolutionSettings(evolution.Settings, evolution.ValidationInterval);

        /// <summary>Canonical form of an experiment definition, shared by checkpoints and experiment exports.</summary>
        internal static JsonValue EncodeEvolutionSettings(EvolutionSettings s, int validationInterval)
        {
            // Without expressed body genes the text is the same as before they existed, and so is its hash.
            var settings = JsonValue.NewObject()
                .Add("experimentSeed", JsonValue.Of(s.ExperimentSeed))
                .Add("episodeSeconds", JsonValue.Of(s.EpisodeSeconds))
                .Add("trainingSeedsPerGeneration", JsonValue.Of(s.TrainingSeedsPerGeneration))
                .Add("validationSeed", JsonValue.Of(s.ValidationSeed))
                .Add("validationSeedCount", JsonValue.Of(s.ValidationSeedCount))
                .Add("validationInterval", JsonValue.Of(validationInterval))
                .Add("populationSize", JsonValue.Of(s.PopulationSize))
                .Add("eliteCount", JsonValue.Of(s.EliteCount))
                .Add("tournamentSize", JsonValue.Of(s.TournamentSize))
                .Add("mutationProbability", JsonValue.Of(s.MutationProbability))
                .Add("mutationSigma", JsonValue.Of(s.MutationSigma))
                .Add("world", EncodeSettings(s.World));
            if (s.ExpressedBodyGenes != BodyGeneSet.None)
                settings.Add("expressedBodyGenes", EncodeGeneSet(s.ExpressedBodyGenes));
            if (s.Plasticity)
                settings.Add("plasticity", JsonValue.Of(true));
            if (s.Algorithm == EvolutionAlgorithm.Neat)
                settings.Add("algorithm", JsonValue.Of(AlgorithmName(s.Algorithm))).Add("neat", EncodeNeat(s.Neat));
            if (s.Senses == Senses.Eyes)
                settings.Add("senses", JsonValue.Of("eyes"));
            if (s.EvolvingEyes)
                settings.Add("evolvingEyes", JsonValue.Of(true));
            // The legacy profile is implied, so the text and hash of every earlier experiment stay the same.
            if (s.Profile != null && !s.Profile.IsLegacy)
            {
                var parameters = JsonValue.NewArray();
                foreach (var parameter in s.Profile.Parameters)
                    parameters.Add(JsonValue.NewObject().Add("name", JsonValue.Of(parameter.Name))
                        .Add("value", JsonValue.Of(parameter.Value)));
                settings.Add("rewardProfile", JsonValue.NewObject()
                    .Add("id", JsonValue.Of(s.Profile.Id))
                    .Add("hash", JsonValue.Of(s.Profile.ParameterHash))
                    .Add("parameters", parameters));
            }
            // Only profiles of several stages have further stages, so single-stage texts stay the same.
            if (s.FurtherStages != null && s.FurtherStages.Count > 0)
            {
                var stages = JsonValue.NewArray();
                foreach (var stage in s.FurtherStages)
                    stages.Add(JsonValue.NewObject()
                        .Add("episodeSeconds", JsonValue.Of(stage.EpisodeSeconds))
                        .Add("trainingSeedsPerGeneration", JsonValue.Of(stage.TrainingSeedsPerGeneration))
                        .Add("validationSeedCount", JsonValue.Of(stage.ValidationSeedCount))
                        .Add("world", EncodeSettings(stage.World)));
                settings.Add("furtherStages", stages);
            }

            return settings;
        }

        /// <summary>Name of an evolution algorithm in files: "mlp" or "neat".</summary>
        internal static string AlgorithmName(EvolutionAlgorithm algorithm) =>
            algorithm == EvolutionAlgorithm.Neat ? NeatNetwork.Kind : Genome.MlpNetwork;

        private static NeatSettings DecodeNeat(JsonValue saved) =>
            new NeatSettings
            {
                InitialWeightLimit = saved["initialWeightLimit"].Float(),
                WeightMutationProbability = saved["weightMutationProbability"].Float(),
                WeightPerturbProbability = saved["weightPerturbProbability"].Float(),
                WeightSigma = saved["weightSigma"].Float(),
                AddNodeProbability = saved["addNodeProbability"].Float(),
                AddConnectionProbability = saved["addConnectionProbability"].Float(),
                ToggleProbability = saved["toggleProbability"].Float(),
                AddConnectionAttempts = saved["addConnectionAttempts"].Int(),
                EyeMutationProbability = saved["eyeMutationProbability"].Float(),
                EyeOrientationSigma = saved["eyeOrientationSigma"].Float(),
                EyeApertureSigma = saved["eyeApertureSigma"].Float(),
                EyeRangeSigma = saved["eyeRangeSigma"].Float(),
                EyeAddProbability = saved["eyeAddProbability"].Float(),
                EyeRemoveProbability = saved["eyeRemoveProbability"].Float(),
                DisabledInheritProbability = saved["disabledInheritProbability"].Float(),
                ExcessCoefficient = saved["excessCoefficient"].Float(),
                DisjointCoefficient = saved["disjointCoefficient"].Float(),
                WeightDifferenceCoefficient = saved["weightDifferenceCoefficient"].Float(),
                NormalizationThreshold = saved["normalizationThreshold"].Int(),
                EyeCompatibilityCoefficient = saved["eyeCompatibilityCoefficient"].Float(),
                CompatibilityThreshold = saved["compatibilityThreshold"].Float(),
                StagnationGenerations = saved["stagnationGenerations"].Int(),
                ProtectedSpecies = saved["protectedSpecies"].Int(),
                ChampionMinimumSize = saved["championMinimumSize"].Int(),
                SurvivalFraction = saved["survivalFraction"].Float(),
                CrossoverProbability = saved["crossoverProbability"].Float(),
                InterspeciesProbability = saved["interspeciesProbability"].Float()
            };

        // Every NEAT parameter, so experiments that differ in any of them have different hashes.
        private static JsonValue EncodeNeat(NeatSettings neat) =>
            JsonValue.NewObject()
                .Add("initialWeightLimit", JsonValue.Of(neat.InitialWeightLimit))
                .Add("weightMutationProbability", JsonValue.Of(neat.WeightMutationProbability))
                .Add("weightPerturbProbability", JsonValue.Of(neat.WeightPerturbProbability))
                .Add("weightSigma", JsonValue.Of(neat.WeightSigma))
                .Add("addNodeProbability", JsonValue.Of(neat.AddNodeProbability))
                .Add("addConnectionProbability", JsonValue.Of(neat.AddConnectionProbability))
                .Add("toggleProbability", JsonValue.Of(neat.ToggleProbability))
                .Add("addConnectionAttempts", JsonValue.Of(neat.AddConnectionAttempts))
                .Add("eyeMutationProbability", JsonValue.Of(neat.EyeMutationProbability))
                .Add("eyeOrientationSigma", JsonValue.Of(neat.EyeOrientationSigma))
                .Add("eyeApertureSigma", JsonValue.Of(neat.EyeApertureSigma))
                .Add("eyeRangeSigma", JsonValue.Of(neat.EyeRangeSigma))
                .Add("eyeAddProbability", JsonValue.Of(neat.EyeAddProbability))
                .Add("eyeRemoveProbability", JsonValue.Of(neat.EyeRemoveProbability))
                .Add("disabledInheritProbability", JsonValue.Of(neat.DisabledInheritProbability))
                .Add("excessCoefficient", JsonValue.Of(neat.ExcessCoefficient))
                .Add("disjointCoefficient", JsonValue.Of(neat.DisjointCoefficient))
                .Add("weightDifferenceCoefficient", JsonValue.Of(neat.WeightDifferenceCoefficient))
                .Add("normalizationThreshold", JsonValue.Of(neat.NormalizationThreshold))
                .Add("eyeCompatibilityCoefficient", JsonValue.Of(neat.EyeCompatibilityCoefficient))
                .Add("compatibilityThreshold", JsonValue.Of(neat.CompatibilityThreshold))
                .Add("stagnationGenerations", JsonValue.Of(neat.StagnationGenerations))
                .Add("protectedSpecies", JsonValue.Of(neat.ProtectedSpecies))
                .Add("championMinimumSize", JsonValue.Of(neat.ChampionMinimumSize))
                .Add("survivalFraction", JsonValue.Of(neat.SurvivalFraction))
                .Add("crossoverProbability", JsonValue.Of(neat.CrossoverProbability))
                .Add("interspeciesProbability", JsonValue.Of(neat.InterspeciesProbability));

        // Absent means off; an explicit false does not read back the same and is refused.
        private static bool DecodePlasticityOption(JsonValue saved) => saved != null && saved.Boolean();

        private static readonly string[] GeneNames = { "size", "speed", "sensor", "efficiency" };

        private static JsonValue EncodeGeneSet(BodyGeneSet set)
        {
            var names = JsonValue.NewArray();
            for (var g = 0; g < BodyGenes.Count; g++)
            {
                if (((int)set & (1 << g)) != 0)
                    names.Add(JsonValue.Of(GeneNames[g]));
            }

            return names;
        }

        // Missing means the fixed body; on read-back the canonical check refuses repeats and any other order.
        private static BodyGeneSet DecodeGeneSet(JsonValue saved)
        {
            if (saved == null)
                return BodyGeneSet.None;
            var names = saved.ArrayOf(BodyGenes.Count);
            var set = BodyGeneSet.None;
            for (var i = 0; i < names.Count; i++)
            {
                var gene = Array.IndexOf(GeneNames, names[i].String());
                if (gene < 0)
                    throw new CheckpointException($"{names[i].Path}: unknown body gene \"{names[i].Text}\".");
                set |= (BodyGeneSet)(1 << gene);
            }

            return set;
        }

        private static EvolutionSettings DecodeEvolutionSettings(JsonValue saved, out int validationInterval)
        {
            validationInterval = saved["validationInterval"].Int(1);
            var settings = new EvolutionSettings
            {
                ExperimentSeed = saved["experimentSeed"].ULong(),
                EpisodeSeconds = saved["episodeSeconds"].Float(),
                TrainingSeedsPerGeneration = saved["trainingSeedsPerGeneration"].Int(),
                ValidationSeed = saved["validationSeed"].ULong(),
                ValidationSeedCount = saved["validationSeedCount"].Int(),
                PopulationSize = saved["populationSize"].Int(),
                EliteCount = saved["eliteCount"].Int(),
                TournamentSize = saved["tournamentSize"].Int(),
                MutationProbability = saved["mutationProbability"].Float(),
                MutationSigma = saved["mutationSigma"].Float(),
                World = DecodeSettings(saved["world"]),
                ExpressedBodyGenes = DecodeGeneSet(saved.TryGet("expressedBodyGenes")),
                Plasticity = DecodePlasticityOption(saved.TryGet("plasticity"))
            };
            var algorithm = saved.TryGet("algorithm");
            if (algorithm != null)
            {
                if (algorithm.String() != NeatNetwork.Kind)
                    throw new CheckpointException($"{algorithm.Path}: unknown evolution algorithm \"{algorithm.String()}\".");
                settings.Algorithm = EvolutionAlgorithm.Neat;
                settings.Neat = DecodeNeat(saved["neat"]);
            }

            var senses = saved.TryGet("senses");
            if (senses != null)
            {
                if (senses.String() != "eyes")
                    throw new CheckpointException($"{senses.Path}: unknown senses \"{senses.String()}\".");
                settings.Senses = Senses.Eyes;
            }

            // Absent means fixed eyes; an explicit false does not read back the same and is refused.
            settings.EvolvingEyes = saved.TryGet("evolvingEyes")?.Boolean() ?? false;

            var profile = saved.TryGet("rewardProfile");
            if (profile != null)
            {
                var values = profile["parameters"].ArrayOf(256);
                var parameters = new RewardParameter[values.Count];
                for (var i = 0; i < parameters.Length; i++)
                    parameters[i] = new RewardParameter(values[i]["name"].String(), values[i]["value"].Double());
                var id = profile["id"].String();
                var rebuilt = Guard(() => RewardProfiles.Create(id, parameters));
                if (rebuilt == null || rebuilt.IsLegacy)
                    throw new CheckpointException($"{profile.Path}: reward profile \"{id}\" is unknown to this build.");
                if (rebuilt.ParameterHash != profile["hash"].String())
                    throw new CheckpointException($"{profile.Path}: reward profile \"{id}\" with these parameters has " +
                                                  $"hash {rebuilt.ParameterHash}, not the saved {profile["hash"].String()}.");
                settings.Profile = rebuilt;
            }

            var stages = saved.TryGet("furtherStages");
            if (stages != null)
            {
                settings.FurtherStages = new List<StageSettings>();
                foreach (var stage in stages.ArrayOf(16).Items)
                    settings.FurtherStages.Add(new StageSettings
                    {
                        EpisodeSeconds = stage["episodeSeconds"].Float(),
                        TrainingSeedsPerGeneration = stage["trainingSeedsPerGeneration"].Int(),
                        ValidationSeedCount = stage["validationSeedCount"].Int(),
                        World = DecodeSettings(stage["world"])
                    });
            }

            // They validate every value and report all problems together.
            new EvaluationPlan(settings);
            new ReproductionRules(settings);
            if (settings.Algorithm == EvolutionAlgorithm.Neat)
                new NeatRules(settings.Neat);
            return settings;
        }

        private static readonly SimulationSettings Defaults = new SimulationSettings();

        // Body cost exponents are written only when they differ from the defaults (see the class summary).
        private static JsonValue EncodeSettings(SimulationSettings s)
        {
            var settings = JsonValue.NewObject()
                .Add("seed", JsonValue.Of(s.Seed))
                .Add("arenaWidth", JsonValue.Of(s.ArenaWidth))
                .Add("arenaHeight", JsonValue.Of(s.ArenaHeight))
                .Add("ticksPerSecond", JsonValue.Of(s.TicksPerSecond))
                .Add("decisionIntervalTicks", JsonValue.Of(s.DecisionIntervalTicks))
                .Add("initialPopulation", JsonValue.Of(s.InitialPopulation))
                .Add("initialFood", JsonValue.Of(s.InitialFood))
                .Add("foodEnergy", JsonValue.Of(s.FoodEnergy))
                .Add("foodRespawnSeconds", JsonValue.Of(s.FoodRespawnSeconds))
                .Add("maxEnergy", JsonValue.Of(s.MaxEnergy))
                .Add("initialEnergy", JsonValue.Of(s.InitialEnergy))
                .Add("maxAgeSeconds", JsonValue.Of(s.MaxAgeSeconds))
                .Add("metabolismPerSecond", JsonValue.Of(s.MetabolismPerSecond))
                .Add("movementCostPerSecond", JsonValue.Of(s.MovementCostPerSecond))
                .Add("sensorCostPerSecond", JsonValue.Of(s.SensorCostPerSecond))
                .Add("sensorRange", JsonValue.Of(s.SensorRange))
                .Add("eatRadius", JsonValue.Of(s.EatRadius))
                .Add("maxSpeed", JsonValue.Of(s.MaxSpeed))
                .Add("maxAcceleration", JsonValue.Of(s.MaxAcceleration))
                .Add("maxTurnRate", JsonValue.Of(s.MaxTurnRate))
                .Add("damping", JsonValue.Of(s.Damping));
            AddIfNotDefault(settings, "sizeCostExponent", s.SizeCostExponent, Defaults.SizeCostExponent);
            AddIfNotDefault(settings, "speedCostExponent", s.SpeedCostExponent, Defaults.SpeedCostExponent);
            AddIfNotDefault(settings, "sensorCostExponent", s.SensorCostExponent, Defaults.SensorCostExponent);
            AddIfNotDefault(settings, "efficiencyPowerExponent", s.EfficiencyPowerExponent, Defaults.EfficiencyPowerExponent);
            AddIfNotDefault(settings, "foodShiftSeconds", s.FoodShiftSeconds, Defaults.FoodShiftSeconds);
            AddIfNotDefault(settings, "turnCostFraction", s.TurnCostFraction, Defaults.TurnCostFraction);
            if (s.SolidBodies)
                settings.Add("solidBodies", JsonValue.Of(true));
            AddIfNotDefault(settings, "bodyRadius", s.BodyRadius, Defaults.BodyRadius);
            if (s.Obstacles != null && s.Obstacles.Length > 0)
            {
                var obstacles = JsonValue.NewArray();
                foreach (var o in s.Obstacles)
                {
                    obstacles.Add(JsonValue.NewArray().Add(JsonValue.Of(o.MinX)).Add(JsonValue.Of(o.MinY))
                        .Add(JsonValue.Of(o.MaxX)).Add(JsonValue.Of(o.MaxY)));
                }

                settings.Add("obstacles", obstacles);
            }

            if (s.Water != null && s.Water.Length > 0)
            {
                var water = JsonValue.NewArray();
                foreach (var source in s.Water)
                {
                    var a = source.Area;
                    var saved = JsonValue.NewObject()
                        .Add("area", JsonValue.NewArray().Add(JsonValue.Of(a.MinX)).Add(JsonValue.Of(a.MinY))
                            .Add(JsonValue.Of(a.MaxX)).Add(JsonValue.Of(a.MaxY)))
                        .Add("capacity", JsonValue.Of(source.Capacity))
                        .Add("rechargePerSecond", JsonValue.Of(source.RechargePerSecond));
                    if (source.Inexhaustible)
                        saved.Add("inexhaustible", JsonValue.Of(true));
                    water.Add(saved);
                }

                settings.Add("water", water);
            }

            AddIfNotDefault(settings, "maxHydration", s.MaxHydration, Defaults.MaxHydration);
            AddIfNotDefault(settings, "hydrationLossPerSecond", s.HydrationLossPerSecond, Defaults.HydrationLossPerSecond);
            AddIfNotDefault(settings, "drinkRatePerSecond", s.DrinkRatePerSecond, Defaults.DrinkRatePerSecond);
            if (s.Reproduction)
            {
                settings.Add("reproduction", JsonValue.NewObject()
                    .Add("populationCap", JsonValue.Of(s.PopulationCap))
                    .Add("maturitySeconds", JsonValue.Of(s.MaturitySeconds))
                    .Add("energyFraction", JsonValue.Of(s.ReproductionEnergyFraction))
                    .Add("offspringEnergyFraction", JsonValue.Of(s.OffspringEnergyFraction))
                    .Add("cost", JsonValue.Of(s.ReproductionCost))
                    .Add("cooldownSeconds", JsonValue.Of(s.ReproductionCooldownSeconds))
                    .Add("birthDistance", JsonValue.Of(s.BirthDistance))
                    .Add("mutationProbability", JsonValue.Of(s.BirthMutationProbability))
                    .Add("mutationSigma", JsonValue.Of(s.BirthMutationSigma)));
            }

            return settings;
        }

        private static void AddIfNotDefault(JsonValue settings, string name, float value, float defaultValue)
        {
            if (value != defaultValue)
                settings.Add(name, JsonValue.Of(value));
        }

        private static float FloatOr(JsonValue settings, string name, float defaultValue) =>
            settings.TryGet(name)?.Float() ?? defaultValue;

        private static Obstacle[] DecodeObstacles(JsonValue saved)
        {
            if (saved == null)
                return Array.Empty<Obstacle>();
            saved.ArrayOf(SimulationConfig.MaxObstacles);
            var obstacles = new Obstacle[saved.Count];
            for (var i = 0; i < obstacles.Length; i++)
            {
                var corners = saved[i].ArrayOf(4);
                if (corners.Count != 4)
                    throw new CheckpointException($"{corners.Path}: an obstacle needs minX, minY, maxX and maxY.");
                obstacles[i] = new Obstacle(corners[0].Float(), corners[1].Float(), corners[2].Float(), corners[3].Float());
            }

            return obstacles;
        }

        private static WaterSource[] DecodeWater(JsonValue saved)
        {
            if (saved == null)
                return Array.Empty<WaterSource>();
            saved.ArrayOf(SimulationConfig.MaxWaterSources);
            var water = new WaterSource[saved.Count];
            for (var i = 0; i < water.Length; i++)
            {
                var source = saved[i];
                var corners = source["area"].ArrayOf(4);
                if (corners.Count != 4)
                    throw new CheckpointException($"{corners.Path}: a water source needs minX, minY, maxX and maxY.");
                // Absent means limited; an explicit false does not read back the same and is refused.
                water[i] = new WaterSource(
                    new Obstacle(corners[0].Float(), corners[1].Float(), corners[2].Float(), corners[3].Float()),
                    source["capacity"].Float(), source["rechargePerSecond"].Float(),
                    source.TryGet("inexhaustible")?.Boolean() ?? false);
            }

            return water;
        }

        private static SimulationSettings DecodeSettings(JsonValue saved)
        {
            var settings = new SimulationSettings
            {
                Seed = saved["seed"].ULong(),
                ArenaWidth = saved["arenaWidth"].Float(),
                ArenaHeight = saved["arenaHeight"].Float(),
                TicksPerSecond = saved["ticksPerSecond"].Int(),
                DecisionIntervalTicks = saved["decisionIntervalTicks"].Int(),
                InitialPopulation = saved["initialPopulation"].Int(),
                InitialFood = saved["initialFood"].Int(),
                FoodEnergy = saved["foodEnergy"].Float(),
                FoodRespawnSeconds = saved["foodRespawnSeconds"].Float(),
                MaxEnergy = saved["maxEnergy"].Float(),
                InitialEnergy = saved["initialEnergy"].Float(),
                MaxAgeSeconds = saved["maxAgeSeconds"].Float(),
                MetabolismPerSecond = saved["metabolismPerSecond"].Float(),
                MovementCostPerSecond = saved["movementCostPerSecond"].Float(),
                SensorCostPerSecond = saved["sensorCostPerSecond"].Float(),
                SensorRange = saved["sensorRange"].Float(),
                EatRadius = saved["eatRadius"].Float(),
                MaxSpeed = saved["maxSpeed"].Float(),
                MaxAcceleration = saved["maxAcceleration"].Float(),
                MaxTurnRate = saved["maxTurnRate"].Float(),
                Damping = saved["damping"].Float(),
                SizeCostExponent = FloatOr(saved, "sizeCostExponent", Defaults.SizeCostExponent),
                SpeedCostExponent = FloatOr(saved, "speedCostExponent", Defaults.SpeedCostExponent),
                SensorCostExponent = FloatOr(saved, "sensorCostExponent", Defaults.SensorCostExponent),
                EfficiencyPowerExponent = FloatOr(saved, "efficiencyPowerExponent", Defaults.EfficiencyPowerExponent),
                FoodShiftSeconds = FloatOr(saved, "foodShiftSeconds", Defaults.FoodShiftSeconds),
                TurnCostFraction = FloatOr(saved, "turnCostFraction", Defaults.TurnCostFraction),
                // Absent means point bodies; an explicit false does not read back the same and is refused.
                SolidBodies = saved.TryGet("solidBodies")?.Boolean() ?? false,
                BodyRadius = FloatOr(saved, "bodyRadius", Defaults.BodyRadius),
                // Absent means the open arena; an empty list does not read back the same and is refused.
                Obstacles = DecodeObstacles(saved.TryGet("obstacles")),
                // Absent means no water; an empty list does not read back the same and is refused.
                Water = DecodeWater(saved.TryGet("water")),
                MaxHydration = FloatOr(saved, "maxHydration", Defaults.MaxHydration),
                HydrationLossPerSecond = FloatOr(saved, "hydrationLossPerSecond", Defaults.HydrationLossPerSecond),
                DrinkRatePerSecond = FloatOr(saved, "drinkRatePerSecond", Defaults.DrinkRatePerSecond)
            };
            // Absent means off; the other reproduction values then keep their defaults and change nothing.
            var reproduction = saved.TryGet("reproduction");
            if (reproduction != null)
            {
                settings.Reproduction = true;
                settings.PopulationCap = reproduction["populationCap"].Int();
                settings.MaturitySeconds = reproduction["maturitySeconds"].Float();
                settings.ReproductionEnergyFraction = reproduction["energyFraction"].Float();
                settings.OffspringEnergyFraction = reproduction["offspringEnergyFraction"].Float();
                settings.ReproductionCost = reproduction["cost"].Float();
                settings.ReproductionCooldownSeconds = reproduction["cooldownSeconds"].Float();
                settings.BirthDistance = reproduction["birthDistance"].Float();
                settings.BirthMutationProbability = reproduction["mutationProbability"].Float();
                settings.BirthMutationSigma = reproduction["mutationSigma"].Float();
            }

            new SimulationConfig(settings);
            return settings;
        }

        // ---- state ----

        private static JsonValue EncodeState(Checkpoint checkpoint)
        {
            var shared = checkpoint.SharedWorld;
            JsonValue sharedWorld = JsonValue.Null();
            if (shared != null)
            {
                sharedWorld = JsonValue.NewObject()
                    .Add("world", EncodeWorld(shared.World))
                    .Add("brains", EncodeBrains(shared.Brains));
                if (shared.BornGenomes.Count > 0)
                {
                    var genomes = JsonValue.NewArray();
                    foreach (var entry in shared.BornGenomes)
                        genomes.Add(EncodeGenome(entry.Genome, JsonValue.NewObject().Add("creature", JsonValue.Of(entry.Creature.Value))));
                    sharedWorld.Add("bornGenomes", genomes);
                }

                if (shared.Composition != null)
                {
                    var entries = JsonValue.NewArray();
                    foreach (var entry in shared.Composition.Entries)
                        entries.Add(EncodeGenome(entry.Genome, JsonValue.NewObject()
                            .Add("count", JsonValue.Of(entry.Count))
                            .Add("source", JsonValue.Of(entry.Source))));
                    sharedWorld.Add("composition", JsonValue.NewObject()
                        .Add("frozen", JsonValue.Of(shared.Composition.Frozen))
                        .Add("entries", entries));
                }

                if (shared.Genealogy.Count > 0)
                {
                    // [id, parent (0: founder), generation, birth tick, death tick (-1: alive), death cause]
                    var entries = JsonValue.NewArray();
                    foreach (var entry in shared.Genealogy)
                    {
                        entries.Add(JsonValue.NewArray()
                            .Add(JsonValue.Of(entry.Creature.Value))
                            .Add(JsonValue.Of(entry.Parent?.Value ?? 0))
                            .Add(JsonValue.Of(entry.Generation))
                            .Add(JsonValue.Of(entry.BirthTick))
                            .Add(JsonValue.Of(entry.DeathTick))
                            .Add(JsonValue.Of((int)entry.DeathCause)));
                    }

                    sharedWorld.Add("genealogy", entries);
                }
            }

            return JsonValue.NewObject()
                .Add("evolution", checkpoint.Evolution == null ? JsonValue.Null() : EncodeEvolution(checkpoint.Evolution))
                .Add("sharedWorld", sharedWorld);
        }

        // Weights (only the MVP network) and body genes when they are not neutral, added to <paramref name="saved"/>.
        private static JsonValue EncodeGenome(Genome genome, JsonValue saved)
        {
            if (genome.Neat != null)
                return EncodeNeatGenome(genome, saved);
            var weights = JsonValue.NewArray();
            for (var w = 0; w < genome.WeightCount; w++)
                weights.Add(JsonValue.Of(genome.GetWeight(w)));
            saved.Add("weights", weights);
            if (!genome.Body.IsNeutral)
                saved.Add("body", EncodeBodyGenes(genome.Body));
            AddPlasticity(saved, genome.Plasticity);
            return saved;
        }

        private static Genome DecodeGenome(JsonValue saved)
        {
            if (saved.TryGet("network") != null)
                return DecodeNeatGenome(saved);
            var expected = NetworkTopology.Mlp12x16x3;
            var values = saved["weights"].ArrayOf(expected.ParameterCount);
            if (values.Count != expected.ParameterCount)
                throw new CheckpointException($"{values.Path}: {expected} needs {expected.ParameterCount} weights.");
            var weights = new float[values.Count];
            for (var w = 0; w < weights.Length; w++)
                weights[w] = values[w].Float();
            return new Genome(expected, weights, DecodeBodyGenes(saved.TryGet("body")), DecodePlasticity(saved.TryGet("plasticity")));
        }

        private static Checkpoint DecodeCheckpoint(JsonValue config, JsonValue state)
        {
            var lab = config["lab"];
            EvolutionSnapshot evolution = null;
            if (!config["evolution"].IsNull)
            {
                var settings = DecodeEvolutionSettings(config["evolution"], out var interval);
                evolution = DecodeEvolution(state["evolution"], settings, interval);
            }
            else if (!state["evolution"].IsNull)
            {
                throw new CheckpointException("state.evolution: saved without its config.");
            }

            SharedWorldSnapshot shared = null;
            if (!config["sharedWorld"].IsNull)
            {
                var settings = DecodeSettings(config["sharedWorld"]);
                var saved = state["sharedWorld"];
                var most = settings.Reproduction ? settings.PopulationCap : settings.InitialPopulation;
                var born = saved.TryGet("bornGenomes");
                var genomes = new List<BornGenome>();
                if (born != null)
                {
                    var values = born.ArrayOf(most);
                    for (var i = 0; i < values.Count; i++)
                        genomes.Add(new BornGenome(new CreatureId(values[i]["creature"].Int(1)), DecodeGenome(values[i])));
                }

                var genealogy = new List<GenealogyEntry>();
                var lineage = saved.TryGet("genealogy");
                if (lineage != null)
                {
                    var values = lineage.ArrayOf(MaxLineageEntries);
                    for (var i = 0; i < values.Count; i++)
                    {
                        var item = values[i].ArrayOf(6);
                        if (item.Count != 6)
                            throw new CheckpointException($"{item.Path}: expected [id, parent, generation, birth, death, cause].");
                        var parent = item[1].Int(0);
                        genealogy.Add(new GenealogyEntry(new CreatureId(item[0].Int(1)),
                            parent == 0 ? (CreatureId?)null : new CreatureId(parent), item[2].Int(0), item[3].Long(0),
                            item[4].Long(-1), (DeathCause)item[5].Int(0, (int)DeathCause.Dehydration)));
                    }
                }

                GenomeComposition composition = null;
                var composed = saved.TryGet("composition");
                if (composed != null)
                {
                    var values = composed["entries"].ArrayOf(SimulationConfig.MaxPopulation);
                    var entries = new List<CompositionEntry>(values.Count);
                    for (var i = 0; i < values.Count; i++)
                        entries.Add(new CompositionEntry(DecodeGenome(values[i]),
                            values[i]["count"].Int(1, SimulationConfig.MaxPopulation), values[i]["source"].String()));
                    try
                    {
                        composition = new GenomeComposition(entries, composed["frozen"].Boolean());
                    }
                    catch (ArgumentException error)
                    {
                        throw new CheckpointException($"{composed.Path}: {error.Message}", error);
                    }

                    if (composition.Total != settings.InitialPopulation)
                        throw new CheckpointException($"{composed.Path}: {composition.Total} founders for a world of " +
                                                      $"{settings.InitialPopulation}.");
                }

                shared = new SharedWorldSnapshot(settings,
                    DecodeWorld(saved["world"], most, settings.InitialFood, new SimulationConfig(settings)),
                    DecodeBrains(saved["brains"]), genomes, genealogy, composition);
            }
            else if (!state["sharedWorld"].IsNull)
            {
                throw new CheckpointException("state.sharedWorld: saved without its config.");
            }

            return new Checkpoint(lab["mode"].String(), lab["preset"].String(), lab["seed"].ULong(), lab["brain"].String(),
                evolution, shared);
        }

        private static void CheckRestorable(Checkpoint checkpoint)
        {
            if (checkpoint.Evolution != null)
                EvolutionSession.Restore(checkpoint.Evolution);
            if (checkpoint.SharedWorld != null)
            {
                var shared = checkpoint.SharedWorld;
                var world = WorldSimulation.Restore(new SimulationConfig(shared.Settings), shared.World);
                if (shared.Genealogy.Count > 0)
                    Genealogy.Restore(shared.Genealogy, world.World);
            }
        }

        private static JsonValue EncodeEvolution(EvolutionSnapshot evolution)
        {
            var known = new SortedDictionary<long, Individual>();
            foreach (var member in evolution.Population)
                known[member.Id] = member;
            if (evolution.LastTrainingIndividuals != null)
            {
                foreach (var member in evolution.LastTrainingIndividuals)
                    known[member.Id] = member;
            }

            var individuals = JsonValue.NewArray();
            foreach (var member in known.Values)
                individuals.Add(EncodeIndividual(member));

            var lineage = JsonValue.NewArray();
            foreach (var entry in evolution.Lineage)
            {
                var item = JsonValue.NewArray().Add(JsonValue.Of(entry.BirthGeneration));
                foreach (var parent in entry.ParentIds)
                    item.Add(JsonValue.Of(parent));
                lineage.Add(item);
            }

            var training = JsonValue.NewArray();
            foreach (var summary in evolution.Training)
                training.Add(EncodeSummary(summary));
            var validation = JsonValue.NewArray();
            foreach (var point in evolution.Validation)
            {
                validation.Add(JsonValue.NewObject()
                    .Add("training", EncodeSummary(point.Training))
                    .Add("validation", EncodeSummary(point.Validation))
                    .Add("championId", JsonValue.Of(point.ChampionId))
                    .Add("championFitness", JsonValue.OfAnyFloat(point.ChampionFitness))
                    .Add("championSurvivalRate", JsonValue.OfAnyFloat(point.ChampionSurvivalRate)));
            }

            var lastTraining = evolution.LastTrainingIndividuals == null
                ? JsonValue.Null()
                : JsonValue.NewObject()
                    .Add("individuals", Ids(evolution.LastTrainingIndividuals))
                    .Add("episodes", EncodeEpisodes(evolution.LastTrainingEpisodes));

            var encoded = JsonValue.NewObject()
                .Add("generation", JsonValue.Of(evolution.Generation))
                .Add("phase", JsonValue.Of(PhaseName(evolution.Phase)))
                .Add("totalTicks", JsonValue.Of(evolution.TotalTicks))
                .Add("individuals", individuals)
                .Add("population", Ids(evolution.Population))
                .Add("lineage", lineage)
                .Add("lastTraining", lastTraining)
                .Add("baselines", evolution.BaselineEpisodes == null ? JsonValue.Null() : EncodeEpisodes(evolution.BaselineEpisodes))
                .Add("training", training)
                .Add("validation", validation)
                .Add("job", EncodeProgress(evolution.Job));
            if (evolution.Neat != null)
                encoded.Add("neat", EncodeNeatState(evolution.Neat));
            return encoded;
        }

        private static JsonValue EncodeProgress(EvaluationProgress progress)
        {
            var job = JsonValue.NewObject()
                .Add("completed", EncodeEpisodes(progress.Completed))
                .Add("world", EncodeWorld(progress.CurrentWorld))
                .Add("brains", EncodeBrains(progress.CurrentBrains));
            if (progress.CurrentTracker != null)
                job.Add("tracker", EncodeTracker(progress.CurrentTracker));
            if (progress.WorkerCount == 1) return job;
            var ahead = JsonValue.NewArray();
            foreach (var episode in progress.Ahead)
            {
                if (episode.Completed.HasValue)
                {
                    ahead.Add(JsonValue.NewObject().Add("result", EncodeEpisodes(new[] { episode.Completed.Value })));
                    continue;
                }

                var running = JsonValue.NewObject().Add("world", EncodeWorld(episode.World))
                    .Add("brains", EncodeBrains(episode.Brains));
                if (episode.Tracker != null)
                    running.Add("tracker", EncodeTracker(episode.Tracker));
                ahead.Add(running);
            }

            return job.Add("workers", JsonValue.Of(progress.WorkerCount)).Add("ahead", ahead);
        }

        private static JsonValue EncodeTracker(EpisodeTracker tracker)
        {
            var collected = JsonValue.NewArray();
            for (var i = 0; i < tracker.Collected.Count; i++)
                collected.Add(JsonValue.NewObject()
                    .Add("target", JsonValue.Of(tracker.Collected[i]))
                    .Add("tick", JsonValue.Of(tracker.CollectionTicks[i]))
                    .Add("speed", JsonValue.Of(tracker.Speeds[i])));
            return JsonValue.NewObject()
                .Add("absorbedEnergy", JsonValue.Of(tracker.AbsorbedEnergy))
                .Add("wastedEnergy", JsonValue.Of(tracker.WastedEnergy))
                .Add("drunk", JsonValue.Of(tracker.Drunk))
                .Add("unchargedEnergy", JsonValue.Of(tracker.UnchargedEnergy))
                .Add("collected", collected)
                .Add("segmentStartTick", JsonValue.Of(tracker.SegmentStartTick))
                .Add("segmentStart", Pair(tracker.SegmentStart))
                .Add("segmentDistance", JsonValue.OfAnyFloat(tracker.SegmentDistance))
                .Add("bestDistance", JsonValue.OfAnyFloat(tracker.BestDistance));
        }

        private static EpisodeTracker DecodeTracker(JsonValue saved)
        {
            if (saved == null)
                return null;
            var values = saved["collected"].ArrayOf(SearchTargets.MaxTargets);
            var targets = new int[values.Count];
            var ticks = new long[values.Count];
            var speeds = new float[values.Count];
            for (var i = 0; i < values.Count; i++)
            {
                targets[i] = values[i]["target"].Int(0, SearchTargets.MaxTargets - 1);
                ticks[i] = values[i]["tick"].Long(0);
                speeds[i] = values[i]["speed"].Float();
            }

            return new EpisodeTracker(saved["absorbedEnergy"].Double(), saved["wastedEnergy"].Double(),
                saved["drunk"].Double(), saved["unchargedEnergy"].Double(), targets, ticks, speeds,
                saved["segmentStartTick"].Long(0), ReadPair(saved["segmentStart"]), saved["segmentDistance"].AnyFloat(),
                saved["bestDistance"].AnyFloat());
        }

        private static JsonValue EncodeNeatState(NeatEvolutionState neat)
        {
            var species = JsonValue.NewArray();
            foreach (var state in neat.Species)
            {
                species.Add(EncodeNeatGenome(state.Representative, JsonValue.NewObject()
                    .Add("id", JsonValue.Of(state.Id))
                    .Add("created", JsonValue.Of(state.CreatedGeneration))
                    .Add("bestFitness", JsonValue.OfAnyFloat(state.BestFitness))
                    .Add("lastImproved", JsonValue.Of(state.LastImprovedGeneration))));
            }

            JsonValue lastSpecies;
            if (neat.LastTrainingSpecies == null)
            {
                lastSpecies = JsonValue.Null();
            }
            else
            {
                lastSpecies = JsonValue.NewArray();
                foreach (var id in neat.LastTrainingSpecies)
                    lastSpecies.Add(JsonValue.Of(id));
            }

            return JsonValue.NewObject()
                .Add("innovations", JsonValue.NewObject()
                    .Add("contract", JsonValue.Of(neat.Innovations.Contract))
                    .Add("generation", JsonValue.Of(neat.Innovations.Generation))
                    .Add("nextInnovation", JsonValue.Of(neat.Innovations.NextInnovation))
                    .Add("nextNodeId", JsonValue.Of(neat.Innovations.NextNodeId)))
                .Add("nextSpeciesId", JsonValue.Of(neat.NextSpeciesId))
                .Add("species", species)
                .Add("lastTrainingSpecies", lastSpecies);
        }

        private static NeatEvolutionState DecodeNeatState(JsonValue saved, int populationSize)
        {
            var innovations = saved["innovations"];
            var history = new InnovationHistoryState(innovations["contract"].String(), innovations["generation"].Int(0),
                innovations["nextInnovation"].Int(0), innovations["nextNodeId"].Int(0));
            var values = saved["species"].ArrayOf(populationSize);
            var species = new NeatSpeciesState[values.Count];
            for (var i = 0; i < species.Length; i++)
            {
                var value = values[i];
                species[i] = new NeatSpeciesState(value["id"].Int(1), DecodeNeatGenome(value), value["created"].Int(0),
                    value["bestFitness"].AnyFloat(), value["lastImproved"].Int(0));
            }

            int[] lastSpecies = null;
            var last = saved["lastTrainingSpecies"];
            if (!last.IsNull)
            {
                var ids = last.ArrayOf(populationSize);
                lastSpecies = new int[ids.Count];
                for (var i = 0; i < lastSpecies.Length; i++)
                    lastSpecies[i] = ids[i].Int(1);
            }

            return new NeatEvolutionState(history, species, saved["nextSpeciesId"].Int(1), lastSpecies);
        }

        // A NEAT genome inside a checkpoint: the network kind, its contract unless legacy, its connections, body genes
        // when not neutral and, for the eye senses, its eyes.
        private static JsonValue EncodeNeatGenome(Genome genome, JsonValue saved)
        {
            saved.Add("network", JsonValue.Of(NeatNetwork.Kind));
            if (genome.Contract != NeuralContract.Legacy)
                saved.Add("contract", JsonValue.Of(genome.Contract.Id));
            saved.Add("connections", GenomeCodec.EncodeConnections(genome.Neat));
            if (!genome.Body.IsNeutral)
                saved.Add("body", EncodeBodyGenes(genome.Body));
            if (genome.Eyes != null)
                saved.Add("eyes", GenomeCodec.EncodeEyes(genome.Eyes));
            return saved;
        }

        private static Genome DecodeNeatGenome(JsonValue saved)
        {
            var network = saved["network"];
            if (network.String() != NeatNetwork.Kind)
                throw new CheckpointException($"{network.Path}: expected a {NeatNetwork.Kind} genome.");
            var contractId = saved.TryGet("contract")?.String() ?? NeuralContract.LegacyId;
            if (!NeuralContract.TryFind(contractId, out var contract) || contract == NeuralContract.Legacy &&
                saved.TryGet("contract") != null)
                throw new CheckpointException($"Unknown or non-canonical neural contract \"{contractId}\".");
            return new Genome(new NeatNetwork(contract, GenomeCodec.DecodeConnections(saved["connections"])),
                DecodeBodyGenes(saved.TryGet("body")), GenomeCodec.DecodeEyes(saved.TryGet("eyes"), contract));
        }

        private static EvolutionSnapshot DecodeEvolution(JsonValue saved, EvolutionSettings settings, int interval)
        {
            if (saved.IsNull)
                throw new CheckpointException($"{saved.Path}: missing state for the saved config.");
            var plan = new EvaluationPlan(settings);

            var individuals = new Dictionary<long, Individual>();
            var table = saved["individuals"].ArrayOf(MaxIndividuals);
            for (var i = 0; i < table.Count; i++)
            {
                var individual = DecodeIndividual(table[i]);
                if (individuals.ContainsKey(individual.Id))
                    throw new CheckpointException($"{table[i].Path}: individual {individual.Id} is listed twice.");
                individuals.Add(individual.Id, individual);
            }

            var lineageValues = saved["lineage"].ArrayOf(MaxLineageEntries);
            var lineage = new LineageEntry[lineageValues.Count];
            for (var i = 0; i < lineage.Length; i++)
            {
                var item = lineageValues[i].ArrayOf(1 + 2);
                if (item.Count == 0)
                    throw new CheckpointException($"{item.Path}: expected [birth generation, parent ids…].");
                var parents = new long[item.Count - 1];
                for (var p = 0; p < parents.Length; p++)
                    parents[p] = item[p + 1].Long(1);
                lineage[i] = new LineageEntry(i + 1, parents, item[0].Int(0));
            }

            var training = saved["training"].ArrayOf(MaxHistory);
            var summaries = new GenerationSummary[training.Count];
            for (var i = 0; i < summaries.Length; i++)
                summaries[i] = DecodeSummary(training[i]);
            var validation = saved["validation"].ArrayOf(MaxHistory);
            var points = new ValidationPoint[validation.Count];
            for (var i = 0; i < points.Length; i++)
            {
                var point = validation[i];
                points[i] = new ValidationPoint(DecodeSummary(point["training"]), DecodeSummary(point["validation"]),
                    point["championId"].Long(1), point["championFitness"].AnyFloat(),
                    point["championSurvivalRate"].AnyFloat());
            }

            Individual[] lastIndividuals = null;
            EpisodeResult[] lastEpisodes = null;
            var lastTraining = saved["lastTraining"];
            if (!lastTraining.IsNull)
            {
                lastIndividuals = Members(lastTraining["individuals"], individuals);
                lastEpisodes = DecodeEpisodes(lastTraining["episodes"], plan);
            }

            var baselines = saved["baselines"];
            var job = saved["job"];
            var workers = job.TryGet("workers")?.Int(1, EvaluationJob.MaxWorkers) ?? 1;
            var ahead = new List<EvaluationAhead>();
            var pending = job.TryGet("ahead");
            if (pending != null)
            {
                var values = pending.ArrayOf(workers - 1);
                for (var i = 0; i < values.Count; i++)
                {
                    var episode = values[i];
                    var completed = episode.TryGet("result");
                    if (completed != null)
                    {
                        if (completed.ArrayOf(1).Count != 1)
                            throw new CheckpointException("A finished parallel episode needs exactly one result.");
                        ahead.Add(new EvaluationAhead(DecodeEpisodes(completed, plan)[0]));
                    }
                    else
                        ahead.Add(new EvaluationAhead(
                            DecodeWorld(episode["world"], 1, MaxEpisodeFood(settings), plan.EpisodeConfig(0)),
                            DecodeBrains(episode["brains"]), DecodeTracker(episode.TryGet("tracker"))));
                }
            }
            // Bodies do not depend on the episode seed nor, as every stage shares the body rules, on the stage.
            var progress = new EvaluationProgress(DecodeEpisodes(job["completed"], plan),
                DecodeWorld(job["world"], 1, MaxEpisodeFood(settings), plan.EpisodeConfig(0)),
                DecodeBrains(job["brains"]), workers, ahead, DecodeTracker(job.TryGet("tracker")));
            var neat = settings.Algorithm == EvolutionAlgorithm.Neat
                ? DecodeNeatState(saved["neat"], settings.PopulationSize)
                : null;
            return new EvolutionSnapshot(settings, interval, saved["generation"].Int(0), Members(saved["population"], individuals),
                lineage, lastIndividuals, lastEpisodes, baselines.IsNull ? null : DecodeEpisodes(baselines, plan),
                summaries, points, saved["totalTicks"].Long(0), ParsePhase(saved["phase"]), progress, neat);
        }

        private static JsonValue EncodeIndividual(Individual individual)
        {
            var genome = individual.Genome;
            var parents = JsonValue.NewArray();
            foreach (var parent in individual.ParentIds)
                parents.Add(JsonValue.Of(parent));
            var saved = JsonValue.NewObject()
                .Add("id", JsonValue.Of(individual.Id))
                .Add("parents", parents)
                .Add("birthGeneration", JsonValue.Of(individual.BirthGeneration))
                .Add("mutatedWeights", JsonValue.Of(individual.MutatedWeights));
            if (genome.Neat != null)
                return EncodeNeatGenome(genome, saved);

            var weights = JsonValue.NewArray();
            for (var w = 0; w < genome.WeightCount; w++)
                weights.Add(JsonValue.Of(genome.GetWeight(w)));
            saved.Add("topology", JsonValue.NewArray()
                    .Add(JsonValue.Of(genome.Topology.Inputs))
                    .Add(JsonValue.Of(genome.Topology.Hidden))
                    .Add(JsonValue.Of(genome.Topology.Outputs)))
                .Add("weights", weights);
            if (!genome.Body.IsNeutral)
                saved.Add("body", EncodeBodyGenes(genome.Body));
            AddPlasticity(saved, genome.Plasticity);
            return saved;
        }

        private static void AddPlasticity(JsonValue saved, PlasticityGenes plasticity)
        {
            if (!plasticity.IsFixed)
                saved.Add("plasticity", JsonValue.NewArray()
                    .Add(JsonValue.Of(plasticity.LearningRate))
                    .Add(JsonValue.Of(plasticity.Decay)));
        }

        // Missing means a fixed network; an explicit [0, 0] does not read back the same and is refused.
        private static PlasticityGenes DecodePlasticity(JsonValue saved)
        {
            if (saved == null)
                return PlasticityGenes.Fixed;
            var values = saved.ArrayOf(2);
            if (values.Count != 2)
                throw new CheckpointException($"{values.Path}: expected [learning rate, decay].");
            return new PlasticityGenes(values[0].Float(), values[1].Float());
        }

        private static JsonValue EncodeBodyGenes(BodyGenes genes)
        {
            var values = JsonValue.NewArray();
            for (var g = 0; g < BodyGenes.Count; g++)
                values.Add(JsonValue.Of(genes[(BodyGene)g]));
            return values;
        }

        // Missing means neutral genes; an explicit neutral list does not read back the same and is refused.
        private static BodyGenes DecodeBodyGenes(JsonValue saved)
        {
            if (saved == null)
                return BodyGenes.Neutral;
            var values = saved.ArrayOf(BodyGenes.Count);
            if (values.Count != BodyGenes.Count)
                throw new CheckpointException($"{values.Path}: expected [size, speed, sensor, efficiency].");
            return new BodyGenes(values[0].Float(), values[1].Float(), values[2].Float(), values[3].Float());
        }

        // Only the MVP network is accepted, so its size is known before the weights are read; a NEAT genome names its kind.
        private static Individual DecodeIndividual(JsonValue saved)
        {
            if (saved.TryGet("network") != null)
            {
                var neatParents = saved["parents"].ArrayOf(2);
                var ids = new long[neatParents.Count];
                for (var p = 0; p < ids.Length; p++)
                    ids[p] = neatParents[p].Long(1);
                return new Individual(saved["id"].Long(1), ids, saved["birthGeneration"].Int(0), DecodeNeatGenome(saved),
                    saved["mutatedWeights"].Int(0, NeatNetwork.MaxConnections));
            }

            var topology = saved["topology"].ArrayOf(3);
            var expected = NetworkTopology.Mlp12x16x3;
            if (topology.Count != 3 || topology[0].Int() != expected.Inputs || topology[1].Int() != expected.Hidden ||
                topology[2].Int() != expected.Outputs)
                throw new CheckpointException($"{topology.Path}: only the {expected} network is supported.");
            var values = saved["weights"].ArrayOf(expected.ParameterCount);
            if (values.Count != expected.ParameterCount)
                throw new CheckpointException($"{values.Path}: {expected} needs {expected.ParameterCount} weights.");
            var weights = new float[values.Count];
            for (var w = 0; w < weights.Length; w++)
                weights[w] = values[w].Float();

            var parentValues = saved["parents"].ArrayOf(2);
            var parents = new long[parentValues.Count];
            for (var p = 0; p < parents.Length; p++)
                parents[p] = parentValues[p].Long(1);
            return new Individual(saved["id"].Long(1), parents, saved["birthGeneration"].Int(0),
                new Genome(expected, weights, DecodeBodyGenes(saved.TryGet("body")), DecodePlasticity(saved.TryGet("plasticity"))),
                saved["mutatedWeights"].Int(0, expected.ParameterCount));
        }

        private static JsonValue Ids(IReadOnlyList<Individual> members)
        {
            var ids = JsonValue.NewArray();
            foreach (var member in members)
                ids.Add(JsonValue.Of(member.Id));
            return ids;
        }

        private static Individual[] Members(JsonValue saved, Dictionary<long, Individual> individuals)
        {
            var ids = saved.ArrayOf(ReproductionRules.MaxPopulationSize);
            var members = new Individual[ids.Count];
            for (var i = 0; i < members.Length; i++)
            {
                if (!individuals.TryGetValue(ids[i].Long(1), out members[i]))
                    throw new CheckpointException($"{ids[i].Path}: individual {ids[i].Text} is not in the table.");
            }

            return members;
        }

        // Food an episode world may hold: the experiment's, or the targets of a search stage.
        private static int MaxEpisodeFood(EvolutionSettings settings)
        {
            var most = Math.Max(settings.World.InitialFood, SearchTargets.MaxTargets);
            foreach (var stage in settings.FurtherStages ?? new List<StageSettings>())
                most = Math.Max(most, stage.World.InitialFood);
            return most;
        }

        private static JsonValue EncodeEpisodes(IReadOnlyList<EpisodeResult> episodes)
        {
            var values = JsonValue.NewArray();
            foreach (var episode in episodes)
            {
                var value = JsonValue.NewObject()
                    .Add("mode", JsonValue.Of(EvaluationCsv.ModeName(episode.Mode)))
                    .Add("generation", JsonValue.Of(episode.Generation))
                    .Add("candidate", JsonValue.Of(episode.Candidate))
                    .Add("seedIndex", JsonValue.Of(episode.SeedIndex))
                    .Add("seed", JsonValue.Of(episode.EpisodeSeed))
                    .Add("foodEnergy", JsonValue.Of(episode.FoodEnergy))
                    .Add("survivedTicks", JsonValue.Of(episode.SurvivedTicks))
                    .Add("survived", JsonValue.Of(episode.Survived))
                    .Add("finalEnergy", JsonValue.Of(episode.FinalEnergy));
                // Only a profile other than the legacy one scores and stages episodes; legacy texts stay the same.
                if (episode.HasScore || episode.Stage != 0)
                {
                    var components = JsonValue.NewArray();
                    foreach (var component in episode.Components ?? Array.Empty<float>())
                        components.Add(JsonValue.OfAnyFloat(component));
                    value.Add("stage", JsonValue.Of(episode.Stage))
                        .Add("score", JsonValue.OfAnyFloat(episode.Fitness))
                        .Add("components", components);
                }

                values.Add(value);
            }

            return values;
        }

        private static EpisodeResult[] DecodeEpisodes(JsonValue saved, EvaluationPlan plan)
        {
            var values = saved.ArrayOf(MaxEpisodes);
            var episodes = new EpisodeResult[values.Count];
            for (var i = 0; i < episodes.Length; i++)
            {
                var value = values[i];
                var stage = value.TryGet("stage")?.Int(0, plan.Stages.Count - 1) ?? 0;
                float? score = null;
                float[] components = null;
                if (value.TryGet("score") is JsonValue savedScore)
                {
                    score = savedScore.AnyFloat();
                    var componentValues = value["components"].ArrayOf(64);
                    components = new float[componentValues.Count];
                    for (var c = 0; c < components.Length; c++)
                        components[c] = componentValues[c].AnyFloat();
                }

                episodes[i] = new EpisodeResult(ParseMode(value["mode"]), value["generation"].Int(0),
                    value["candidate"].Int(0), value["seedIndex"].Int(0), value["seed"].ULong(),
                    plan.Stages[stage].EpisodeTicks, plan.Stages[stage].EpisodeSeconds, value["foodEnergy"].Float(),
                    value["survivedTicks"].Long(0), value["survived"].Boolean(), value["finalEnergy"].Float(), components,
                    stage, score);
            }

            return episodes;
        }

        internal static JsonValue EncodeSummary(GenerationSummary s) =>
            JsonValue.NewObject()
                .Add("generation", JsonValue.Of(s.Generation))
                .Add("mode", JsonValue.Of(EvaluationCsv.ModeName(s.Mode)))
                .Add("population", JsonValue.Of(s.PopulationSize))
                .Add("valid", JsonValue.Of(s.ValidCount))
                .Add("best", JsonValue.OfAnyFloat(s.Best))
                .Add("upperQuartile", JsonValue.OfAnyFloat(s.UpperQuartile))
                .Add("median", JsonValue.OfAnyFloat(s.Median))
                .Add("lowerQuartile", JsonValue.OfAnyFloat(s.LowerQuartile))
                .Add("worst", JsonValue.OfAnyFloat(s.Worst))
                .Add("meanFitness", JsonValue.OfAnyFloat(s.MeanFitness))
                .Add("bestIndividualId", JsonValue.Of(s.BestIndividualId))
                .Add("meanSurvivalRate", JsonValue.OfAnyFloat(s.MeanSurvivalRate))
                .Add("meanFoodEnergy", JsonValue.OfAnyFloat(s.MeanFoodEnergy))
                .Add("meanSurvivedSeconds", JsonValue.OfAnyFloat(s.MeanSurvivedSeconds))
                .Add("weightStdDev", JsonValue.OfAnyFloat(s.WeightStdDev))
                .Add("meanDistanceToCentroid", JsonValue.OfAnyFloat(s.MeanDistanceToCentroid))
                .Add("newIndividuals", JsonValue.Of(s.NewIndividuals))
                .Add("elites", JsonValue.Of(s.Elites))
                .Add("distinctParents", JsonValue.Of(s.DistinctParents))
                .Add("meanMutatedWeights", JsonValue.OfAnyFloat(s.MeanMutatedWeights));

        private static GenerationSummary DecodeSummary(JsonValue s) =>
            GenerationSummary.Restore(s["generation"].Int(0), ParseMode(s["mode"]), s["population"].Int(1),
                s["valid"].Int(0), s["best"].AnyFloat(), s["upperQuartile"].AnyFloat(), s["median"].AnyFloat(),
                s["lowerQuartile"].AnyFloat(), s["worst"].AnyFloat(), s["meanFitness"].AnyFloat(),
                s["bestIndividualId"].Long(1), s["meanSurvivalRate"].AnyFloat(), s["meanFoodEnergy"].AnyFloat(),
                s["meanSurvivedSeconds"].AnyFloat(), s["weightStdDev"].AnyFloat(), s["meanDistanceToCentroid"].AnyFloat(),
                s["newIndividuals"].Int(0), s["elites"].Int(0), s["distinctParents"].Int(0),
                s["meanMutatedWeights"].AnyFloat());

        // A creature's body genes are written only when they are not neutral; its body follows from them and the config.
        private static JsonValue EncodeWorld(SimulationSnapshot world)
        {
            var creatures = JsonValue.NewArray();
            foreach (var creature in world.Creatures)
            {
                if (!creature.IsAlive)
                    throw new InvalidOperationException("Worlds are saved between ticks, when every creature is alive.");
                var saved = JsonValue.NewObject()
                    .Add("id", JsonValue.Of(creature.Id.Value))
                    .Add("position", Pair(creature.Position))
                    .Add("velocity", Pair(creature.Velocity))
                    .Add("heading", JsonValue.Of(creature.Heading))
                    .Add("energy", JsonValue.Of(creature.Energy))
                    .Add("ageTicks", JsonValue.Of(creature.AgeTicks))
                    .Add("foodEnergyEaten", JsonValue.Of(creature.FoodEnergyEaten));
                if (creature.Parent.HasValue)
                    saved.Add("parent", JsonValue.Of(creature.Parent.Value.Value));
                if (creature.Generation != 0)
                    saved.Add("generation", JsonValue.Of(creature.Generation));
                if (creature.ReproductionCooldownTicks != 0)
                    saved.Add("reproductionCooldownTicks", JsonValue.Of(creature.ReproductionCooldownTicks));
                if (!creature.Genes.IsNeutral)
                    saved.Add("bodyGenes", EncodeBodyGenes(creature.Genes));
                if (creature.Eyes != null)
                    saved.Add("eyes", GenomeCodec.EncodeEyes(creature.Eyes));
                // Hydration exists only with water, where it is never 0 between ticks.
                if (creature.Hydration != 0f)
                    saved.Add("hydration", JsonValue.Of(creature.Hydration));
                creatures.Add(saved);
            }

            var food = JsonValue.NewArray();
            foreach (var item in world.Food)
            {
                if (item.IsEaten)
                    throw new InvalidOperationException("Worlds are saved between ticks, when no food is eaten.");
                food.Add(JsonValue.NewObject()
                    .Add("id", JsonValue.Of(item.Id.Value))
                    .Add("position", Pair(item.Position))
                    .Add("energy", JsonValue.Of(item.Energy)));
            }

            var agenda = JsonValue.NewArray();
            foreach (var pending in world.Agenda)
            {
                agenda.Add(JsonValue.NewObject()
                    .Add("dueTick", JsonValue.Of(pending.DueTick))
                    .Add("sequence", JsonValue.Of(pending.Sequence))
                    .Add("kind", JsonValue.Of((int)pending.Payload.Kind)));
            }

            var encoded = JsonValue.NewObject()
                .Add("tick", JsonValue.Of(world.Tick))
                .Add("nextCreatureId", JsonValue.Of(world.NextCreatureId))
                .Add("nextFoodId", JsonValue.Of(world.NextFoodId))
                .Add("worldRandom", EncodeRandom(world.WorldRandom))
                .Add("spawnRandom", EncodeRandom(world.SpawnRandom))
                .Add("creatures", creatures)
                .Add("food", food)
                .Add("agenda", agenda)
                .Add("nextEventSequence", JsonValue.Of(world.NextEventSequence))
                .Add("correctedInputValues", JsonValue.Of(world.CorrectedInputValues));
            if (world.WaterReserves.Count > 0)
            {
                var reserves = JsonValue.NewArray();
                foreach (var reserve in world.WaterReserves)
                    reserves.Add(JsonValue.Of(reserve));
                encoded.Add("waterReserves", reserves);
            }

            return encoded;
        }

        // Creatures never outnumber the initial population yet (no births), and food never exceeds the initial count
        // plus its pending respawns, which are themselves bounded by the food eaten.
        private static SimulationSnapshot DecodeWorld(JsonValue saved, int maxCreatures, int maxFood,
            SimulationConfig config)
        {
            var creatureValues = saved["creatures"].ArrayOf(Math.Min(maxCreatures, SimulationConfig.MaxPopulation));
            var creatures = new CreatureState[creatureValues.Count];
            for (var i = 0; i < creatures.Length; i++)
            {
                var value = creatureValues[i];
                var genes = DecodeBodyGenes(value.TryGet("bodyGenes"));
                creatures[i] = new CreatureState(new CreatureId(value["id"].Int(1)), ReadPair(value["position"]),
                    value["heading"].Float(), value["energy"].Float(), genes, CreatureBody.Express(config, genes))
                {
                    Velocity = ReadPair(value["velocity"]),
                    AgeTicks = value["ageTicks"].Long(0),
                    FoodEnergyEaten = value["foodEnergyEaten"].Float(),
                    Parent = value.TryGet("parent") is JsonValue parent ? new CreatureId(parent.Int(1)) : (CreatureId?)null,
                    Generation = value.TryGet("generation")?.Int(0) ?? 0,
                    ReproductionCooldownTicks = value.TryGet("reproductionCooldownTicks")?.Long(0) ?? 0L,
                    Eyes = value.TryGet("eyes") is JsonValue eyes ? GenomeCodec.DecodeEyes(eyes, NeuralContract.Eyes) : null,
                    Hydration = value.TryGet("hydration")?.Float() ?? 0f
                };
            }

            var foodValues = saved["food"].ArrayOf(Math.Min(maxFood, SimulationConfig.MaxFood));
            var food = new FoodState[foodValues.Count];
            for (var i = 0; i < food.Length; i++)
            {
                var value = foodValues[i];
                food[i] = new FoodState(new FoodId(value["id"].Int(1)), ReadPair(value["position"]), value["energy"].Float());
            }

            var agendaValues = saved["agenda"].ArrayOf(Math.Min(maxFood, SimulationConfig.MaxFood));
            var agenda = new ScheduledEvent<SimulationEvent>[agendaValues.Count];
            for (var i = 0; i < agenda.Length; i++)
            {
                var value = agendaValues[i];
                agenda[i] = new ScheduledEvent<SimulationEvent>(value["dueTick"].Long(0), value["sequence"].Long(0),
                    new SimulationEvent((SimulationEventKind)value["kind"].Int(0, byte.MaxValue)));
            }

            var reserveValues = saved.TryGet("waterReserves")?.ArrayOf(SimulationConfig.MaxWaterSources);
            var reserves = new float[reserveValues?.Count ?? 0];
            for (var i = 0; i < reserves.Length; i++)
                reserves[i] = reserveValues[i].Float();

            return new SimulationSnapshot(saved["tick"].Long(0), saved["nextCreatureId"].Int(1), saved["nextFoodId"].Int(1),
                DecodeRandom(saved["worldRandom"]), DecodeRandom(saved["spawnRandom"]), creatures, food, agenda,
                saved["nextEventSequence"].Long(0), saved["correctedInputValues"].Long(0), reserves);
        }

        private static JsonValue EncodeBrains(IReadOnlyList<BrainSlotState> brains)
        {
            var values = JsonValue.NewArray();
            foreach (var slot in brains)
            {
                var saved = JsonValue.NewObject()
                    .Add("creature", JsonValue.Of(slot.Creature.Value))
                    .Add("brain", JsonValue.Of(slot.BrainName))
                    .Add("random", EncodeRandom(slot.Random))
                    .Add("action", JsonValue.NewArray()
                        .Add(JsonValue.Of(slot.Action.Thrust))
                        .Add(JsonValue.Of(slot.Action.Turn))
                        .Add(JsonValue.Of(slot.Action.Strafe)))
                    .Add("ticksUntilDecision", JsonValue.Of(slot.TicksUntilDecision));
                if (slot.Plastic != null)
                    saved.Add("plastic", EncodePlastic(slot.Plastic));
                values.Add(saved);
            }

            return values;
        }

        private static BrainSlotState[] DecodeBrains(JsonValue saved)
        {
            var values = saved.ArrayOf(SimulationConfig.MaxPopulation);
            var brains = new BrainSlotState[values.Count];
            for (var i = 0; i < brains.Length; i++)
            {
                var value = values[i];
                var action = value["action"].ArrayOf(3);
                if (action.Count != 3)
                    throw new CheckpointException($"{action.Path}: expected [thrust, turn, strafe].");
                brains[i] = new BrainSlotState(new CreatureId(value["creature"].Int(1)), value["brain"].String(),
                    DecodeRandom(value["random"]), new CreatureAction(action[0].Float(), action[1].Float(), action[2].Float()),
                    value["ticksUntilDecision"].Int(0), DecodePlastic(value.TryGet("plastic")));
            }

            return brains;
        }

        private static JsonValue EncodePlastic(PlasticMemory memory) =>
            JsonValue.NewObject()
                .Add("deltaWeights", Floats(memory.DeltaWeights))
                .Add("lastHidden", Floats(memory.LastHidden))
                .Add("lastOutput", Floats(memory.LastOutput))
                .Add("lastEnergy", JsonValue.Of(memory.LastEnergy))
                .Add("hasPrevious", JsonValue.Of(memory.HasPrevious));

        // Limits are checked when the brains are restored.
        private static PlasticMemory DecodePlastic(JsonValue saved)
        {
            if (saved == null)
                return null;
            var memory = new PlasticMemory();
            ReadFloats(saved["deltaWeights"], memory.DeltaWeights);
            ReadFloats(saved["lastHidden"], memory.LastHidden);
            ReadFloats(saved["lastOutput"], memory.LastOutput);
            memory.LastEnergy = saved["lastEnergy"].Float();
            memory.HasPrevious = saved["hasPrevious"].Boolean();
            return memory;
        }

        private static JsonValue Floats(float[] values)
        {
            var array = JsonValue.NewArray();
            foreach (var value in values)
                array.Add(JsonValue.Of(value));
            return array;
        }

        private static void ReadFloats(JsonValue saved, float[] destination)
        {
            var values = saved.ArrayOf(destination.Length);
            if (values.Count != destination.Length)
                throw new CheckpointException($"{values.Path}: expected {destination.Length} values.");
            for (var i = 0; i < destination.Length; i++)
                destination[i] = values[i].Float();
        }

        private static JsonValue EncodeRandom(Pcg32State state) =>
            JsonValue.NewObject().Add("state", JsonValue.Of(state.State)).Add("increment", JsonValue.Of(state.Increment));

        private static Pcg32State DecodeRandom(JsonValue saved)
        {
            var increment = saved["increment"].ULong();
            if ((increment & 1UL) == 0)
                throw new CheckpointException($"{saved.Path}: a PCG32 increment is odd.");
            return new Pcg32State(saved["state"].ULong(), increment);
        }

        private static JsonValue Pair(Float2 value) =>
            JsonValue.NewArray().Add(JsonValue.Of(value.X)).Add(JsonValue.Of(value.Y));

        private static Float2 ReadPair(JsonValue saved)
        {
            var pair = saved.ArrayOf(2);
            if (pair.Count != 2)
                throw new CheckpointException($"{pair.Path}: expected [x, y].");
            return new Float2(pair[0].Float(), pair[1].Float());
        }

        internal static string PhaseName(EvolutionPhase phase) =>
            phase == EvolutionPhase.Baselines ? "baselines" : phase == EvolutionPhase.Validation ? "validation" : "training";

        private static EvolutionPhase ParsePhase(JsonValue saved)
        {
            switch (saved.String())
            {
                case "training": return EvolutionPhase.Training;
                case "baselines": return EvolutionPhase.Baselines;
                case "validation": return EvolutionPhase.Validation;
                default: throw new CheckpointException($"{saved.Path}: unknown phase \"{saved.Text}\".");
            }
        }

        private static EvaluationMode ParseMode(JsonValue saved)
        {
            foreach (EvaluationMode mode in Enum.GetValues(typeof(EvaluationMode)))
            {
                if (EvaluationCsv.ModeName(mode) == saved.String())
                    return mode;
            }

            throw new CheckpointException($"{saved.Path}: unknown evaluation mode \"{saved.Text}\".");
        }

        private static string TryString(JsonValue root, string name)
        {
            foreach (var member in root.Members)
            {
                if (member.Key == name)
                    return member.Value.Kind == JsonKind.String ? member.Value.Text : null;
            }

            return null;
        }

        private static DateTime ParseUtc(JsonValue saved)
        {
            if (!DateTime.TryParseExact(saved.String(), "yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var utc))
                throw new CheckpointException($"{saved.Path}: expected a UTC time like 2026-01-31T12:00:00Z.");
            return utc;
        }

        private static ulong ParseHex(JsonValue saved)
        {
            var text = saved.String();
            if (text.Length != 16 || !ulong.TryParse(text, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture,
                    out var value))
                throw new CheckpointException($"{saved.Path}: expected 16 hexadecimal digits.");
            return value;
        }

        internal static string Hex(ulong value) => value.ToString("x16", CultureInfo.InvariantCulture);

        /// <summary>64-bit FNV-1a of the UTF-8 text.</summary>
        internal static ulong Hash(string text)
        {
            var hash = 14695981039346656037UL;
            foreach (var b in Encoding.UTF8.GetBytes(text))
            {
                hash ^= b;
                hash = unchecked(hash * 1099511628211UL);
            }

            return hash;
        }

        // Domain constructors and restores report invalid content with ArgumentException (and InvalidOperationException
        // for impossible states); a checkpoint reader reports all of them the same way.
        private static T Guard<T>(Func<T> action)
        {
            try
            {
                return action();
            }
            catch (Exception error) when (error is ArgumentException || error is InvalidOperationException)
            {
                throw new CheckpointException("Invalid checkpoint content: " + error.Message, error);
            }
        }

        private static void Guard(Action action) => Guard(() =>
        {
            action();
            return true;
        });
    }
}
