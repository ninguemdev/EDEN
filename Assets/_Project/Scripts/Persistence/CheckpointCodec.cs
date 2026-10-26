using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Eden.Application;
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
    /// fixed body, where costs change nothing) read as the same run.
    /// </summary>
    public static class CheckpointCodec
    {
        public const string Format = "eden-checkpoint";
        public const int SchemaVersion = 3;

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

            var config = EncodeConfig(checkpoint);
            var state = EncodeState(checkpoint);
            var configHash = Hash(config.ToCompactString());
            var stateHash = Hash(state.ToCompactString());
            var utc = savedUtc.ToUniversalTime();
            info = new CheckpointInfo(SchemaVersion, appVersion, runtime, utc, configHash, stateHash);

            var root = JsonValue.NewObject()
                .Add("format", JsonValue.Of(Format))
                .Add("schemaVersion", JsonValue.Of(SchemaVersion))
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
                if (actual.Kind == JsonKind.Number && (expected.Key == "genomeSchema" && actual.Text == "1" ||
                                                       expected.Key == "configSchema" && actual.Text == "3"))
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
            return settings;
        }

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
                ExpressedBodyGenes = DecodeGeneSet(saved.TryGet("expressedBodyGenes"))
            };
            // Both validate every value and report all problems together.
            new EvaluationPlan(settings);
            new ReproductionRules(settings);
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
            return settings;
        }

        private static void AddIfNotDefault(JsonValue settings, string name, float value, float defaultValue)
        {
            if (value != defaultValue)
                settings.Add(name, JsonValue.Of(value));
        }

        private static float FloatOr(JsonValue settings, string name, float defaultValue) =>
            settings.TryGet(name)?.Float() ?? defaultValue;

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
                EfficiencyPowerExponent = FloatOr(saved, "efficiencyPowerExponent", Defaults.EfficiencyPowerExponent)
            };
            new SimulationConfig(settings);
            return settings;
        }

        // ---- state ----

        private static JsonValue EncodeState(Checkpoint checkpoint)
        {
            var shared = checkpoint.SharedWorld;
            return JsonValue.NewObject()
                .Add("evolution", checkpoint.Evolution == null ? JsonValue.Null() : EncodeEvolution(checkpoint.Evolution))
                .Add("sharedWorld", shared == null
                    ? JsonValue.Null()
                    : JsonValue.NewObject()
                        .Add("world", EncodeWorld(shared.World))
                        .Add("brains", EncodeBrains(shared.Brains)));
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
                shared = new SharedWorldSnapshot(settings,
                    DecodeWorld(saved["world"], settings.InitialPopulation, settings.InitialFood,
                        new SimulationConfig(settings)),
                    DecodeBrains(saved["brains"]));
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
                WorldSimulation.Restore(new SimulationConfig(checkpoint.SharedWorld.Settings), checkpoint.SharedWorld.World);
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

            return JsonValue.NewObject()
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
                .Add("job", JsonValue.NewObject()
                    .Add("completed", EncodeEpisodes(evolution.Job.Completed))
                    .Add("world", EncodeWorld(evolution.Job.CurrentWorld))
                    .Add("brains", EncodeBrains(evolution.Job.CurrentBrains)));
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
            // Bodies do not depend on the episode seed.
            var progress = new EvaluationProgress(DecodeEpisodes(job["completed"], plan),
                DecodeWorld(job["world"], 1, settings.World.InitialFood, plan.EpisodeConfig(0)),
                DecodeBrains(job["brains"]));
            return new EvolutionSnapshot(settings, interval, saved["generation"].Int(0), Members(saved["population"], individuals),
                lineage, lastIndividuals, lastEpisodes, baselines.IsNull ? null : DecodeEpisodes(baselines, plan),
                summaries, points, saved["totalTicks"].Long(0), ParsePhase(saved["phase"]), progress);
        }

        private static JsonValue EncodeIndividual(Individual individual)
        {
            var genome = individual.Genome;
            var weights = JsonValue.NewArray();
            for (var w = 0; w < genome.WeightCount; w++)
                weights.Add(JsonValue.Of(genome.GetWeight(w)));
            var parents = JsonValue.NewArray();
            foreach (var parent in individual.ParentIds)
                parents.Add(JsonValue.Of(parent));
            var saved = JsonValue.NewObject()
                .Add("id", JsonValue.Of(individual.Id))
                .Add("parents", parents)
                .Add("birthGeneration", JsonValue.Of(individual.BirthGeneration))
                .Add("mutatedWeights", JsonValue.Of(individual.MutatedWeights))
                .Add("topology", JsonValue.NewArray()
                    .Add(JsonValue.Of(genome.Topology.Inputs))
                    .Add(JsonValue.Of(genome.Topology.Hidden))
                    .Add(JsonValue.Of(genome.Topology.Outputs)))
                .Add("weights", weights);
            if (!genome.Body.IsNeutral)
                saved.Add("body", EncodeBodyGenes(genome.Body));
            return saved;
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

        // Only the MVP network is accepted, so its size is known before the weights are read.
        private static Individual DecodeIndividual(JsonValue saved)
        {
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
                new Genome(expected, weights, DecodeBodyGenes(saved.TryGet("body"))),
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

        private static JsonValue EncodeEpisodes(IReadOnlyList<EpisodeResult> episodes)
        {
            var values = JsonValue.NewArray();
            foreach (var episode in episodes)
            {
                values.Add(JsonValue.NewObject()
                    .Add("mode", JsonValue.Of(EvaluationCsv.ModeName(episode.Mode)))
                    .Add("generation", JsonValue.Of(episode.Generation))
                    .Add("candidate", JsonValue.Of(episode.Candidate))
                    .Add("seedIndex", JsonValue.Of(episode.SeedIndex))
                    .Add("seed", JsonValue.Of(episode.EpisodeSeed))
                    .Add("foodEnergy", JsonValue.Of(episode.FoodEnergy))
                    .Add("survivedTicks", JsonValue.Of(episode.SurvivedTicks))
                    .Add("survived", JsonValue.Of(episode.Survived))
                    .Add("finalEnergy", JsonValue.Of(episode.FinalEnergy)));
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
                episodes[i] = new EpisodeResult(ParseMode(value["mode"]), value["generation"].Int(0),
                    value["candidate"].Int(0), value["seedIndex"].Int(0), value["seed"].ULong(), plan.EpisodeTicks,
                    plan.EpisodeSeconds, value["foodEnergy"].Float(), value["survivedTicks"].Long(0),
                    value["survived"].Boolean(), value["finalEnergy"].Float());
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
                if (!creature.Genes.IsNeutral)
                    saved.Add("bodyGenes", EncodeBodyGenes(creature.Genes));
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

            return JsonValue.NewObject()
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
                    FoodEnergyEaten = value["foodEnergyEaten"].Float()
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

            return new SimulationSnapshot(saved["tick"].Long(0), saved["nextCreatureId"].Int(1), saved["nextFoodId"].Int(1),
                DecodeRandom(saved["worldRandom"]), DecodeRandom(saved["spawnRandom"]), creatures, food, agenda,
                saved["nextEventSequence"].Long(0), saved["correctedInputValues"].Long(0));
        }

        private static JsonValue EncodeBrains(IReadOnlyList<BrainSlotState> brains)
        {
            var values = JsonValue.NewArray();
            foreach (var slot in brains)
            {
                values.Add(JsonValue.NewObject()
                    .Add("creature", JsonValue.Of(slot.Creature.Value))
                    .Add("brain", JsonValue.Of(slot.BrainName))
                    .Add("random", EncodeRandom(slot.Random))
                    .Add("action", JsonValue.NewArray()
                        .Add(JsonValue.Of(slot.Action.Thrust))
                        .Add(JsonValue.Of(slot.Action.Turn))
                        .Add(JsonValue.Of(slot.Action.Strafe)))
                    .Add("ticksUntilDecision", JsonValue.Of(slot.TicksUntilDecision)));
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
                    value["ticksUntilDecision"].Int(0));
            }

            return brains;
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
