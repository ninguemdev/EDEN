using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using Eden.Application;
using Eden.Brains;
using Eden.Domain;
using Eden.Evolution;
using Eden.Persistence;
using Eden.Simulation;
using UnityEngine;
using UnityEngine.InputSystem;
using Debug = UnityEngine.Debug;

namespace Eden.Presentation
{
    /// <summary>What the lab runs. Values are serialized in the scene: never renumber.</summary>
    public enum LabMode
    {
        /// <summary>One world shared by the whole population; observation only, no selection rule.</summary>
        SharedWorld = 0,

        /// <summary>Generational evolution: every genome alone in its own episodes, shown episode by episode.</summary>
        IsolatedEvolution = 1
    }

    /// <summary>Brains of the shared world. Values are serialized in the scene: never renumber.</summary>
    public enum LabBrainKind
    {
        Heuristic = 0,
        Random = 1,

        /// <summary>MLP 12-16-3, one random genome per creature (no evolution).</summary>
        Neural = 2,

        /// <summary>The last evaluated generation of the lab's evolution, one individual per creature.</summary>
        Evolved = 3
    }

    /// <summary>
    /// Composition root of the lab scene: builds a run from the chosen mode, preset and seed, converts frame time into
    /// fixed ticks with <see cref="FixedStepClock"/> (or, at maximum speed, runs whole ticks within a per-frame work
    /// budget), handles lab controls and selection, and writes logs under persistentDataPath. Frame rate never
    /// reaches the simulation. In evolution mode the world shown is the isolated episode being evaluated. The run can
    /// be saved to and loaded from a checkpoint between ticks.
    /// Command line (builds): -eden-mode shared|evolution, -eden-preset minimal|standard|stress,
    /// -eden-brain heuristic|random|neural, -eden-body fixed|evolving, -eden-reproduction on|off, -eden-seed N,
    /// -eden-select ID, -eden-max, and for a measured run
    /// -eden-benchmark SECONDS [-eden-benchmark-warmup SECONDS] [-eden-benchmark-output DIR] (see <see cref="LabBenchmark"/>).
    /// </summary>
    public sealed class LabRunner : MonoBehaviour
    {
        /// <summary>Real time per frame spent on ticks at maximum speed; the rest is left to rendering.</summary>
        public const double MaxSpeedBudgetSeconds = 0.010;

        private const float HudRefreshSeconds = 0.25f;
        private const float SelectRadius = 1.5f;
        private const float NetworkRefreshSeconds = 0.1f;

        [SerializeField] private SimulationSettings settings = new SimulationSettings();
        [SerializeField, Min(0)] private int seed = 1;
        [SerializeField] private LabMode mode = LabMode.SharedWorld;
        [SerializeField, Range(0, 2)] private int preset = 1;
        [SerializeField] private LabBrainKind brain = LabBrainKind.Heuristic;

        [Tooltip("A new evolution expresses and mutates all body genes; off keeps the fixed reference body (control).")]
        [SerializeField] private bool evolvingBody;
        [SerializeField] private bool writeMetrics = true;
        [SerializeField] private WorldView worldView;
        [SerializeField] private LabHud hud;

        private readonly NeuralSnapshot networkSnapshot = new NeuralSnapshot();
        private readonly Stopwatch budgetWatch = new Stopwatch();
        private BrainController controller;
        private MetricsRecorder metrics;
        private MetricsCsvLog metricsLog;
        private GenealogyCsvLog genealogyLog;
        private Genealogy genealogy;
        private EvolutionCsvLog evolutionLog;
        private SimulationSettings sharedSettings;
        private SharedGenomes sharedGenomes;
        private IReadOnlyList<Individual> sharedIndividuals;
        private int sharedGeneration;
        private CreatureDeath? selectedDeath;
        private float hudTimer;
        private float networkTimer;
        private LabBenchmark benchmark;

        public WorldSimulation Simulation { get; private set; }
        public FixedStepClock Clock { get; private set; }
        public LabMode Mode => mode;
        public LabBrainKind BrainKind => brain;

        /// <summary>Body choice for the next evolution (see <see cref="LabChoice.EvolvingBody"/>).</summary>
        public bool EvolvingBody => evolvingBody;

        /// <summary>
        /// World rules of the lab, as edited in the Inspector (or by -eden-reproduction); a preset sets the population and
        /// food of each shared world made from it. Changes take effect at the next start.
        /// </summary>
        public SimulationSettings WorldTemplate => settings;

        /// <summary>Genomes of the shared world's neural creatures, children included; null for other brains.</summary>
        public SharedGenomes SharedGenomes => sharedGenomes;

        /// <summary>Every creature of the shared world, dead ones included; null without reproduction.</summary>
        public Genealogy Genealogy => genealogy;

        /// <summary>Tick during which the shared world died out; null while anyone lives.</summary>
        public long? ExtinctionTick => mode == LabMode.SharedWorld ? metrics?.ExtinctionTick : null;
        public ExperimentPreset Preset => ExperimentPreset.All[preset];
        public int Seed => seed;

        /// <summary>Evolution of the lab; kept while the shared world shows its population.</summary>
        public EvolutionSession Evolution { get; private set; }

        /// <summary>Name recorded with the run (brain of the run, or the per-creature brain type).</summary>
        public string BrainName { get; private set; }
        public CreatureId? Selected { get; private set; }
        /// <summary>Log directory of what is shown: the evolution in evolution mode, the shared world otherwise.</summary>
        public string RunDirectory => mode == LabMode.IsolatedEvolution ? EvolutionDirectory : MetricsDirectory;

        /// <summary>Metrics log of the last shared world run; null when disabled.</summary>
        public string MetricsDirectory { get; private set; }

        /// <summary>Export of the lab's evolution; kept open while the shared world shows its population.</summary>
        public string EvolutionDirectory { get; private set; }
        public WorldView View => worldView;

        /// <summary>Directory of the performance report once a measured run has finished; null otherwise.</summary>
        public string BenchmarkDirectory => benchmark?.Directory;
        public LabHud Hud => hud;

        /// <summary>File of Salvar/Carregar (persistentDataPath/Checkpoints/lab.json by default); its backup sits next to it.</summary>
        public string CheckpointPath { get; set; }

        /// <summary>Outcome of the last save or load, as shown in the HUD; null before the first one.</summary>
        public string CheckpointStatus { get; private set; }

        /// <summary>Directory "Exportar genoma" writes to (persistentDataPath/Genomes by default), one file per click.</summary>
        public string GenomeExportDirectory { get; set; }

        /// <summary>Outcome of the last genome export, as shown in the HUD; null before the first one.</summary>
        public string GenomeExportStatus { get; private set; }

        /// <summary>Last captured network state of the selected neural creature (diagram data).</summary>
        public NeuralSnapshot Network => networkSnapshot;

        public bool TryGetHeldAction(CreatureId creature, out CreatureAction action) =>
            controller.TryGetLastAction(creature, out action);

        /// <summary>Starts a new run of the given choice (what the Start button does).</summary>
        public void StartExperiment(LabChoice choice)
        {
            mode = choice.Mode;
            preset = Mathf.Clamp(choice.Preset, 0, ExperimentPreset.All.Count - 1);
            seed = Math.Max(0, choice.Seed);
            brain = choice.Brain == LabBrainKind.Evolved && Evolution == null ? LabBrainKind.Heuristic : choice.Brain;
            evolvingBody = choice.EvolvingBody;
            StartRun();
        }

        /// <summary>Restarts the current choice from its seed; evolution mode starts a new evolution.</summary>
        public void StartRun()
        {
            CloseMetricsLog();
            Selected = null;
            selectedDeath = null;
            networkSnapshot.Clear();
            hud?.ShowNetwork(null);
            if (mode == LabMode.IsolatedEvolution)
                StartEvolution();
            else
                StartSharedWorld();
            ResetClock(Simulation.World.Config.TicksPerSecond);
            worldView.Bind(Simulation.World);
            hud?.ShowChoice(new LabChoice(mode, preset, seed, brain, evolvingBody), Evolution != null);
            RefreshHud();
        }

        /// <summary>Goes back from the shared world to the evolution where it stopped.</summary>
        public void ResumeEvolution()
        {
            if (Evolution == null)
                return;
            CloseMetricsLog();
            mode = LabMode.IsolatedEvolution;
            sharedGenomes = null;
            ClearSelection();
            ShowEpisode();
            hud?.ShowChoice(new LabChoice(mode, preset, seed, brain, evolvingBody), true);
            RefreshHud();
        }

        /// <summary>
        /// Saves the run to <see cref="CheckpointPath"/> (what Salvar does): the evolution, if any, including the episode
        /// being evaluated, and the shared world while it is shown. Called between ticks; the previous checkpoint
        /// becomes the backup. A failure leaves the files as they were and the run going.
        /// </summary>
        public bool SaveCheckpoint()
        {
            try
            {
                var shared = mode == LabMode.SharedWorld
                    ? new SharedWorldSnapshot(sharedSettings, Simulation.Capture(), controller.Capture(),
                        sharedGenomes?.Capture(), genealogy?.Capture())
                    : null;
                var checkpoint = new Checkpoint(ModeName(mode), Preset.Name, (ulong)seed, KindName(brain),
                    Evolution?.Capture(), shared);
                CheckpointFile.Save(CheckpointPath, checkpoint, UnityEngine.Application.version, Runtime(), DateTime.UtcNow);
                ShowCheckpointStatus(LabText.CheckpointSaved(CheckpointPath, Simulation.World.Tick, EvolutionShown));
                return true;
            }
            catch (CheckpointException error)
            {
                Debug.LogWarning($"EDEN: checkpoint not saved ({error.Message})");
                ShowCheckpointStatus(LabText.CheckpointFailed("não salvo", error.Message));
                return false;
            }
        }

        /// <summary>
        /// Replaces the run with the one saved at <see cref="CheckpointPath"/> (what Carregar does), or with its backup if
        /// the checkpoint itself is unusable. Everything is restored and checked before the current run is touched, so a
        /// refused file leaves it exactly as it was. The resumed run continues tick for tick as the saved one would have;
        /// its logs go to new directories.
        /// </summary>
        public bool LoadCheckpoint()
        {
            Checkpoint saved;
            CheckpointInfo savedInfo;
            string problem;
            LabMode loadedMode;
            int loadedPreset;
            LabBrainKind loadedBrain;
            EvolutionSession session;
            WorldSimulation world;
            BrainController brains;
            SharedGenomes genomes = null;
            Genealogy lineage = null;
            IReadOnlyList<Individual> individuals = null;
            var name = MlpBrain.BrainName;
            try
            {
                saved = CheckpointFile.LoadOrBackup(CheckpointPath, out savedInfo, out problem);
                loadedMode = ParseMode(saved.Mode);
                loadedPreset = PresetIndex(saved.Preset);
                loadedBrain = ParseKind(saved.Brain);
                if (saved.Seed > int.MaxValue)
                    throw new CheckpointException($"Seed {saved.Seed} is outside the lab's range.");
                session = saved.Evolution == null ? null : EvolutionSession.Restore(saved.Evolution);
                if (loadedMode == LabMode.IsolatedEvolution)
                {
                    if (session == null || saved.SharedWorld != null)
                        throw new CheckpointException("An evolution checkpoint holds the evolution only.");
                    world = session.Job.Current.Simulation;
                    brains = session.Job.Current.Controller;
                }
                else
                {
                    if (saved.SharedWorld == null)
                        throw new CheckpointException("A shared world checkpoint holds the shared world.");
                    var config = new SimulationConfig(saved.SharedWorld.Settings);
                    if (loadedBrain == LabBrainKind.Evolved)
                    {
                        individuals = session?.LastTraining?.Individuals ?? session?.Evolution.Individuals ??
                            throw new CheckpointException("The evolved population needs its evolution.");
                        if (individuals.Count != config.InitialPopulation)
                            throw new CheckpointException("The shared world does not match the evolved population.");
                    }

                    world = WorldSimulation.Restore(config, saved.SharedWorld.World);
                    genomes = CreateGenomes(config, loadedBrain, individuals, session);
                    if (genomes == null && saved.SharedWorld.BornGenomes.Count > 0)
                        throw new CheckpointException("Only neural brains have genomes to restore.");
                    genomes?.Restore(saved.SharedWorld.BornGenomes, world.World);
                    world.Heredity = genomes;
                    lineage = saved.SharedWorld.Genealogy.Count > 0
                        ? Genealogy.Restore(saved.SharedWorld.Genealogy, world.World)
                        : config.Reproduction ? new Genealogy(world.World) : null;
                    brains = CreateController(config, loadedBrain, genomes, out name);
                    brains.Restore(saved.SharedWorld.Brains);
                }
            }
            catch (Exception error) when (error is CheckpointException || error is ArgumentException ||
                                          error is InvalidOperationException)
            {
                Debug.LogWarning($"EDEN: checkpoint not loaded ({error.Message})");
                ShowCheckpointStatus(LabText.CheckpointFailed("recusado (a execução atual continua)", error.Message));
                return false;
            }

            CloseLogs();
            mode = loadedMode;
            preset = loadedPreset;
            seed = (int)saved.Seed;
            brain = loadedBrain;
            evolvingBody = session != null && session.Evolution.Plan.ExpressedBodyGenes != BodyGeneSet.None;
            Evolution = session;
            EvolutionDirectory = null;
            MetricsDirectory = null;
            Simulation = world;
            controller = brains;
            BrainName = name;
            sharedSettings = saved.SharedWorld?.Settings;
            sharedGenomes = genomes;
            genealogy = lineage;
            sharedIndividuals = individuals;
            sharedGeneration = session?.LastTraining?.Summary.Generation ?? 0;
            metrics = new MetricsRecorder(world.World.Config, world.World.Config.TicksPerSecond);
            if (writeMetrics && session != null)
            {
                var file = problem == null ? CheckpointPath : CheckpointFile.BackupPath(CheckpointPath);
                OpenEvolutionLog(new ResumeOrigin(file, savedInfo, saved.Evolution));
            }
            if (writeMetrics && mode == LabMode.SharedWorld)
                OpenMetricsLog(world.World.Config);

            Selected = null;
            selectedDeath = null;
            networkSnapshot.Clear();
            hud?.ShowNetwork(null);
            ResetClock(world.World.Config.TicksPerSecond);
            if (mode == LabMode.SharedWorld && world.IsExtinct)
                Clock.Pause();
            worldView.Bind(world.World);
            hud?.ShowChoice(new LabChoice(mode, preset, seed, brain, evolvingBody), Evolution != null);
            ShowCheckpointStatus(LabText.CheckpointLoaded(CheckpointPath, world.World.Tick, EvolutionShown, problem));
            return true;
        }

        /// <summary>
        /// Writes the selected creature's genome to <see cref="GenomeExportDirectory"/> (what "Exportar genoma" does),
        /// one file per click. Its fitness is included when a value for that exact individual is already on hand (a
        /// shared-world creature from the evolution's last trained generation); otherwise the file omits it, and it
        /// counts as 0 if the file is later used to seed a pool. Does nothing (and returns false) without a selected
        /// creature whose genome is known.
        /// </summary>
        public bool ExportGenome()
        {
            var record = ResolveExportableGenome();
            if (record == null)
                return false;
            try
            {
                var individualId = record.Source?.IndividualId ?? Selected.Value.Value;
                var path = Path.Combine(GenomeExportDirectory,
                    $"genome-{individualId}-{DateTime.UtcNow:yyyyMMdd-HHmmssfff}.json");
                GenomeFile.Save(path, record, UnityEngine.Application.version, DateTime.UtcNow);
                ShowGenomeExportStatus(LabText.GenomeExported(path));
                return true;
            }
            catch (CheckpointException error)
            {
                Debug.LogWarning($"EDEN: genome not exported ({error.Message})");
                ShowGenomeExportStatus(LabText.GenomeExportFailed(error.Message));
                return false;
            }
        }

        /// <summary>Copies <see cref="GenomeExportDirectory"/> to the system clipboard (what "Copiar pasta" does).</summary>
        public void CopyGenomeDirectory()
        {
            GUIUtility.systemCopyBuffer = GenomeExportDirectory;
            ShowGenomeExportStatus(LabText.GenomeDirectoryCopied(GenomeExportDirectory));
        }

        // What the selected creature's genome is, if it has one: the episode's candidate in Isolated Evolution, or a
        // shared-world creature with a neural brain (founder or born). Null otherwise, including no selection.
        private GenomeRecord ResolveExportableGenome()
        {
            if (!Selected.HasValue)
                return null;

            if (mode == LabMode.IsolatedEvolution)
            {
                var individual = Evolution?.CurrentIndividual;
                return individual == null
                    ? null
                    : new GenomeRecord(individual.Genome, null,
                        new GenomeSource(Evolution.Plan.ExperimentSeed, Evolution.Job.Generation, individual.Id));
            }

            if (sharedGenomes == null)
                return null;

            var genome = sharedGenomes.GenomeOf(Selected.Value);
            var index = Selected.Value.Value - 1;
            var training = Evolution?.LastTraining;
            var fitness = training != null && index >= 0 && index < training.Evaluation.Candidates.Count
                ? (float?)training.Evaluation.Candidates[index].MeanFitness
                : null;
            var individualId = sharedIndividuals != null && index >= 0 && index < sharedIndividuals.Count
                ? sharedIndividuals[index].Id
                : Selected.Value.Value;
            var source = Evolution == null
                ? (GenomeSource?)null
                : new GenomeSource(Evolution.Plan.ExperimentSeed, sharedGeneration, individualId);
            return new GenomeRecord(genome, fitness, source);
        }

        /// <summary>Switches the brain kind of the shared world and restarts it with the same seed.</summary>
        public void UseBrain(LabBrainKind kind)
        {
            mode = LabMode.SharedWorld;
            brain = kind == LabBrainKind.Evolved && Evolution == null ? LabBrainKind.Heuristic : kind;
            StartRun();
        }

        public void TogglePause()
        {
            if (Clock.IsPaused)
                Clock.Resume();
            else
                Clock.Pause();
            RefreshHud();
        }

        public void RequestStep() => Clock.RequestStep();

        /// <summary>Changes the requested speed; leaves maximum speed.</summary>
        public void ScaleSpeed(float factor)
        {
            Clock.SetUnpaced(false);
            Clock.SetSpeed(Mathf.Clamp(Clock.RequestedSpeed * factor, FixedStepClock.MinSpeed, FixedStepClock.MaxSpeed));
            RefreshHud();
        }

        /// <summary>Toggles maximum speed: as many whole ticks per frame as fit in <see cref="MaxSpeedBudgetSeconds"/>.</summary>
        public void ToggleMaxSpeed()
        {
            Clock.SetUnpaced(!Clock.IsUnpaced);
            if (Clock.IsUnpaced && Clock.IsPaused)
                Clock.Resume();
            RefreshHud();
        }

        public void Select(CreatureId creature)
        {
            Selected = creature;
            selectedDeath = null;
            controller.Watch(creature);
            networkSnapshot.Clear();
            RefreshNetwork();
            RefreshHud();
        }

        public void ClearSelection()
        {
            Selected = null;
            selectedDeath = null;
            controller.Watch(null);
            networkSnapshot.Clear();
            hud?.ShowNetwork(null);
            RefreshHud();
        }

        /// <summary>
        /// Captures the selected creature's network for its last decision (or its current sensors before the first
        /// decision) and shows the diagram; hides it for reference brains, dead creatures or no selection.
        /// </summary>
        public void RefreshNetwork()
        {
            networkTimer = 0f;
            if (hud == null)
                return;
            if (!Selected.HasValue || !controller.TryGetBrain(Selected.Value, out var selectedBrain) ||
                !(selectedBrain is MlpBrain network))
            {
                hud.ShowNetwork(null);
                return;
            }

            if (!controller.TryGetWatchedDecision(out var observation) && !TryObserve(Selected.Value, out observation))
            {
                hud.ShowNetwork(null);
                return;
            }

            networkSnapshot.Capture(Selected.Value, network, observation);
            hud.ShowNetwork(networkSnapshot);
        }

        /// <summary>Selects the living creature with the next higher (or lower) id, wrapping around.</summary>
        public void SelectNeighbor(int direction)
        {
            var creatures = Simulation.World.Creatures;
            if (creatures.Count == 0)
                return;
            var current = Selected?.Value ?? (direction > 0 ? 0 : int.MaxValue);
            CreatureState choice = null;
            if (direction > 0)
            {
                foreach (var creature in creatures)
                    if (creature.Id.Value > current) { choice = creature; break; }
                choice ??= creatures[0];
            }
            else
            {
                for (var i = creatures.Count - 1; i >= 0; i--)
                    if (creatures[i].Id.Value < current) { choice = creatures[i]; break; }
                choice ??= creatures[creatures.Count - 1];
            }

            Select(choice.Id);
        }

        /// <summary>Closes the metrics and evolution logs (the files stay).</summary>
        public void CloseLogs()
        {
            CloseMetricsLog();
            evolutionLog?.Dispose();
            evolutionLog = null;
        }

        private void CloseMetricsLog()
        {
            metricsLog?.Dispose();
            metricsLog = null;
            genealogyLog?.Dispose();
            genealogyLog = null;
        }

        /// <summary>
        /// Measures the current run: after <paramref name="warmupSeconds"/> of real time, records
        /// <paramref name="seconds"/> and writes a performance report (to <paramref name="output"/>, or under
        /// persistentDataPath/Benchmarks). Metrics logs are switched off so the measurement leaves disk writes out.
        /// </summary>
        public void StartBenchmark(double warmupSeconds, double seconds, string output = null)
        {
            benchmark?.Dispose();
            benchmark = new LabBenchmark(warmupSeconds, seconds, output);
            writeMetrics = false;
            CloseMetricsLog();
            MetricsDirectory = null;
            hud?.Root?.SetEnabled(false);
        }

        private void Awake()
        {
            CheckpointPath ??= Path.Combine(UnityEngine.Application.persistentDataPath, "Checkpoints", "lab.json");
            GenomeExportDirectory ??= Path.Combine(UnityEngine.Application.persistentDataPath, "Genomes");
            var arguments = Environment.GetCommandLineArgs();
            var selectAtStart = ApplyCommandLine(arguments, out var maxSpeed);
            benchmark = LabBenchmark.FromCommandLine(arguments);
            if (benchmark != null)
                writeMetrics = false;
            if (hud != null)
            {
                hud.StartClicked += () => StartExperiment(hud.ReadChoice());
                hud.ResumeEvolutionClicked += ResumeEvolution;
                hud.SaveClicked += () => SaveCheckpoint();
                hud.LoadClicked += () => LoadCheckpoint();
                hud.ExportGenomeClicked += () => ExportGenome();
                hud.CopyGenomeDirectoryClicked += CopyGenomeDirectory;
                hud.UseExportDirectoryClicked += () => hud.SetSeedGenomesDirectory(GenomeExportDirectory);
                hud.PauseClicked += TogglePause;
                hud.StepClicked += RequestStep;
                hud.SlowerClicked += () => ScaleSpeed(0.5f);
                hud.FasterClicked += () => ScaleSpeed(2f);
                hud.MaxClicked += ToggleMaxSpeed;
            }

            StartRun();
            if (maxSpeed)
                ToggleMaxSpeed();
            if (benchmark != null)
                hud?.Root?.SetEnabled(false);
            if (selectAtStart > 0)
                Select(new CreatureId(selectAtStart));
        }

        private void OnDestroy()
        {
            CloseLogs();
            benchmark?.Dispose();
        }

        private void Update()
        {
            HandleInput();
            var realDelta = Time.unscaledDeltaTime;
            benchmark?.BeginSimulation();
            var ticks = Clock.Advance(realDelta);
            RunTicks(ticks, rememberLast: true);
            if (Clock.IsUnpaced && !Clock.IsPaused)
            {
                var ran = RunWithinBudget();
                // An extinction during these ticks pauses the clock; there is no speed to record then.
                if (!Clock.IsPaused)
                    Clock.RecordUnpaced(realDelta, ran);
                ticks += ran;
            }

            benchmark?.EndSimulation(ticks);
            if (mode == LabMode.IsolatedEvolution)
                ShowEpisode();

            worldView.Render(Simulation.World, Selected, Clock.TickFraction);
            if (Selected.HasValue)
            {
                networkTimer += Time.unscaledDeltaTime;
                if (networkTimer >= NetworkRefreshSeconds)
                    RefreshNetwork();
            }

            hudTimer += Time.unscaledDeltaTime;
            if (hudTimer >= HudRefreshSeconds)
                RefreshHud();

            // Builds quit when the measurement is written; in the Editor (tests) the run goes on.
            if (benchmark != null && benchmark.EndFrame(realDelta, this))
            {
                hud?.Root?.SetEnabled(true);
                if (!UnityEngine.Application.isEditor)
                    UnityEngine.Application.Quit();
            }
        }

        // With rememberLast, the view keeps the poses from before the last tick so that it can draw between them and
        // the result; the ticks run are the same either way.
        // Returns the ticks run: fewer than asked only when the shared world has died out (it never runs past that).
        private int RunTicks(int ticks, bool rememberLast = false)
        {
            if (ticks <= 0)
                return 0;
            if (mode == LabMode.IsolatedEvolution)
            {
                if (rememberLast)
                {
                    Evolution.Advance(ticks - 1);
                    worldView.RememberPreviousTick(Evolution.Job.Current.Simulation.World);
                    Evolution.Advance(1);
                }
                else
                {
                    Evolution.Advance(ticks);
                }

                return ticks;
            }

            for (var i = 0; i < ticks; i++)
            {
                if (Simulation.IsExtinct)
                    return i;
                if (rememberLast && i == ticks - 1)
                    worldView.RememberPreviousTick(Simulation.World);
                StepOnce();
            }

            return ticks;
        }

        // Whole ticks in chunks until the frame budget is used; checking the watch per chunk keeps its cost low.
        private int RunWithinBudget()
        {
            var chunk = mode == LabMode.IsolatedEvolution ? 256 : 8;
            var ran = 0;
            budgetWatch.Restart();
            do
            {
                var done = RunTicks(chunk);
                ran += done;
                if (done < chunk)
                    break;
            } while (budgetWatch.Elapsed.TotalSeconds < MaxSpeedBudgetSeconds && ran < int.MaxValue - chunk);

            return ran;
        }

        private void StepOnce()
        {
            if (benchmark != null)
                Simulation.Timings = benchmark.Timings;
            Simulation.Step(controller);
            benchmark?.RecordTick();
            var deaths = Simulation.LastDeaths;
            for (var i = 0; i < deaths.Count; i++)
            {
                if (Selected.HasValue && deaths[i].Creature == Selected.Value)
                    selectedDeath = deaths[i];
            }

            controller.Forget(deaths);
            sharedGenomes?.Forget(deaths);
            genealogy?.Record(Simulation);
            genealogyLog?.Append(Simulation, genealogy);
            if (metrics.Record(Simulation))
                metricsLog?.Append(metrics.LastSample);
            if (!Simulation.IsExtinct || deaths.Count == 0)
                return;

            // Nobody is put back: the world stops here and only an explicit restart (Iniciar or R) goes on.
            genealogyLog?.AppendExtinction(deaths[0].Tick);
            Clock.Pause();
            RefreshHud();
        }

        private void StartSharedWorld()
        {
            MetricsDirectory = null;
            if (brain != LabBrainKind.Evolved)
                sharedIndividuals = null;
            var runSettings = Preset.SharedWorld(settings, (ulong)seed);
            if (brain == LabBrainKind.Evolved)
            {
                var source = Evolution.LastTraining;
                sharedIndividuals = source?.Individuals ?? Evolution.Evolution.Individuals;
                sharedGeneration = source?.Summary.Generation ?? 0;
                runSettings.InitialPopulation = sharedIndividuals.Count;
            }

            var config = new SimulationConfig(runSettings);
            sharedSettings = runSettings;
            // Evolved creatures live with the bodies their genes express in the evolution; any other brain gets the
            // reference body.
            var individuals = sharedIndividuals;
            var plan = Evolution?.Evolution.Plan;
            Simulation = new WorldSimulation(individuals == null
                ? WorldFactory.CreateInitial(config)
                : WorldFactory.CreateInitial(config, i => plan.ExpressedGenes(individuals[i].Genome.Body)));
            sharedGenomes = CreateGenomes(config, brain, individuals, Evolution);
            Simulation.Heredity = sharedGenomes;
            genealogy = config.Reproduction ? new Genealogy(Simulation.World) : null;
            controller = CreateController(config, brain, sharedGenomes, out var name);
            BrainName = name;
            metrics = new MetricsRecorder(config, config.TicksPerSecond);
            if (writeMetrics)
                OpenMetricsLog(config);
        }

        private void StartEvolution()
        {
            evolutionLog?.Dispose();
            evolutionLog = null;
            EvolutionDirectory = null;
            sharedIndividuals = null;
            sharedGenomes = null;
            var evolutionSettings = Preset.Evolution((ulong)seed);
            evolutionSettings.ExpressedBodyGenes = evolvingBody ? BodyGeneSet.All : BodyGeneSet.None;
            Evolution = CreateEvolution(evolutionSettings);
            BrainName = MlpBrain.BrainName;
            if (writeMetrics)
                OpenEvolutionLog();
            var episode = Evolution.Job.Current;
            Simulation = episode.Simulation;
            controller = episode.Controller;
        }

        // A non-empty seed-pool directory in the experiment panel starts generation 0 from it instead of random
        // weights; a bad directory falls back to random and reports why, rather than leaving the lab unable to start.
        private EvolutionSession CreateEvolution(EvolutionSettings evolutionSettings)
        {
            var directory = hud?.SeedGenomesDirectory;
            if (string.IsNullOrEmpty(directory))
                return new EvolutionSession(evolutionSettings);
            try
            {
                var pool = GenomeFile.LoadPool(directory);
                var genomes = new Genome[pool.Length];
                var fitness = new float[pool.Length];
                for (var i = 0; i < pool.Length; i++)
                {
                    genomes[i] = pool[i].Genome;
                    fitness[i] = pool[i].FitnessFor(Fitness.Profile) ?? 0f;
                }

                return EvolutionSession.FromSeedPool(evolutionSettings, genomes, fitness);
            }
            catch (CheckpointException error)
            {
                Debug.LogWarning($"EDEN: genome pool not used ({error.Message})");
                ShowGenomeExportStatus(LabText.GenomePoolFailed(error.Message));
                return new EvolutionSession(evolutionSettings);
            }
        }

        // Evolution mode: follows the episode being evaluated; a selection moves on to the new episode's creature.
        private void ShowEpisode()
        {
            var episode = Evolution.Job.Current;
            if (episode.Simulation == Simulation)
                return;
            Simulation = episode.Simulation;
            controller = episode.Controller;
            worldView.Bind(Simulation.World);
            if (!Selected.HasValue)
                return;
            Selected = episode.Creature.Id;
            controller.Watch(Selected);
            networkSnapshot.Clear();
        }

        private void ResetClock(int ticksPerSecond)
        {
            var speed = Clock?.RequestedSpeed ?? 1f;
            var paused = Clock?.IsPaused ?? false;
            var unpaced = Clock?.IsUnpaced ?? false;
            Clock = new FixedStepClock(ticksPerSecond);
            Clock.SetSpeed(speed);
            Clock.SetUnpaced(unpaced);
            if (paused)
                Clock.Pause();
        }

        private void HandleInput()
        {
            // A measured run keeps the scenario it was started with, whatever is typed or clicked meanwhile.
            if (benchmark != null && !benchmark.IsFinished)
                return;
            var keyboard = Keyboard.current;
            if (keyboard != null && (hud == null || !hud.IsTyping))
            {
                if (keyboard.spaceKey.wasPressedThisFrame) TogglePause();
                if (keyboard.periodKey.wasPressedThisFrame) RequestStep();
                if (keyboard.equalsKey.wasPressedThisFrame || keyboard.numpadPlusKey.wasPressedThisFrame) ScaleSpeed(2f);
                if (keyboard.minusKey.wasPressedThisFrame || keyboard.numpadMinusKey.wasPressedThisFrame) ScaleSpeed(0.5f);
                if (keyboard.mKey.wasPressedThisFrame) ToggleMaxSpeed();
                if (keyboard.rightBracketKey.wasPressedThisFrame) SelectNeighbor(+1);
                if (keyboard.leftBracketKey.wasPressedThisFrame) SelectNeighbor(-1);
                if (keyboard.escapeKey.wasPressedThisFrame) ClearSelection();
                if (keyboard.rKey.wasPressedThisFrame) StartRun();
                if (keyboard.f5Key.wasPressedThisFrame) SaveCheckpoint();
                if (keyboard.f9Key.wasPressedThisFrame) LoadCheckpoint();
                if (keyboard.bKey.wasPressedThisFrame && mode == LabMode.SharedWorld)
                    UseBrain(brain == LabBrainKind.Heuristic ? LabBrainKind.Random
                        : brain == LabBrainKind.Random ? LabBrainKind.Neural
                        : LabBrainKind.Heuristic);
            }

            var mouse = Mouse.current;
            if (mouse == null || !mouse.leftButton.wasPressedThisFrame)
                return;
            var position = mouse.position.ReadValue();
            if (hud == null || !hud.IsPointerOverControls(position))
                SelectNearest(worldView.ScreenToArena(position));
        }

        // Nearest living creature within the pick radius; ties go to the lower id.
        private void SelectNearest(Vector2 arenaPoint)
        {
            CreatureState best = null;
            var bestDistance = SelectRadius * SelectRadius;
            foreach (var creature in Simulation.World.Creatures)
            {
                var dx = creature.Position.X - arenaPoint.x;
                var dy = creature.Position.Y - arenaPoint.y;
                var distance = dx * dx + dy * dy;
                if (distance <= bestDistance && (best == null || distance < bestDistance))
                {
                    best = creature;
                    bestDistance = distance;
                }
            }

            if (best != null)
                Select(best.Id);
        }

        private void RefreshHud()
        {
            hudTimer = 0f;
            if (hud == null || Simulation == null)
                return;
            var evolving = mode == LabMode.IsolatedEvolution;
            hud.ShowPlayback(Clock.IsPaused, Clock.IsUnpaced);
            hud.ShowRun(evolving
                ? LabText.Evolution(Evolution, Clock, Preset, RunDirectory)
                : LabText.Run(Simulation, Clock, BrainName, Preset, metrics, RunDirectory, SharedSource()));
            hud.ShowEvolution(evolving || sharedIndividuals != null ? Evolution : null);
            hud.ShowInspector(Selected.HasValue ? DescribeSelected(Selected.Value) : null);
            hud.SetExportAvailable(ResolveExportableGenome() != null);
        }

        private string SharedSource() =>
            sharedIndividuals == null ? null
                : string.Format(CultureInfo.CurrentCulture, "geração {0} da evolução (seed {1})", sharedGeneration,
                    Evolution.Plan.ExperimentSeed);

        private string DescribeSelected(CreatureId id)
        {
            foreach (var creature in Simulation.World.Creatures)
            {
                if (creature.Id != id)
                    continue;
                controller.TryGetLastAction(id, out var action);
                controller.TryGetBrain(id, out var creatureBrain);
                var text = LabText.Creature(creature, Sensors.Observe(Simulation.World, creature), action,
                    creatureBrain?.Name ?? BrainName, Simulation.World.Config);
                if (creatureBrain is MlpBrain mlp)
                {
                    controller.TryGetState(id, out var state);
                    text += "\n" + LabText.Plasticity(mlp.Plasticity, state?.Plastic);
                }

                return text + LineageOf(creature);
            }

            // A creature selected after its death: the genealogy remembers how it died.
            var death = selectedDeath;
            if (!death.HasValue && genealogy != null && genealogy.TryGet(id, out var entry) && !entry.IsAlive)
                death = new CreatureDeath(id, entry.DeathTick, entry.DeathCause);
            return LabText.DeadCreature(id, death) + GenealogyOf(id);
        }

        private string GenealogyOf(CreatureId id) =>
            genealogy == null || mode != LabMode.SharedWorld ? string.Empty
                : "\n\n" + LabText.Genealogy(genealogy, id, Simulation.World.Config.TickDuration);

        private string LineageOf(CreatureState creature)
        {
            if (mode == LabMode.IsolatedEvolution)
            {
                return "\n\n" + LabText.Episode(Evolution, Simulation.World.Config) +
                       (Evolution.CurrentIndividual == null ? string.Empty
                           : "\n\n" + LabText.Lineage(Evolution.CurrentIndividual, Evolution.Evolution.Lineage,
                               Evolution.Job.Generation));
            }

            var index = creature.Id.Value - 1;
            if (sharedIndividuals == null || index < 0 || index >= sharedIndividuals.Count)
                return GenealogyOf(creature.Id);
            return GenealogyOf(creature.Id) + "\n\n" +
                   LabText.Lineage(sharedIndividuals[index], Evolution.Evolution.Lineage, sharedGeneration);
        }

        private bool TryObserve(CreatureId id, out Observation observation)
        {
            foreach (var creature in Simulation.World.Creatures)
            {
                if (creature.Id != id)
                    continue;
                observation = Sensors.Observe(Simulation.World, creature);
                return true;
            }

            observation = default;
            return false;
        }

        // Returns the creature id to select at start, or 0.
        private int ApplyCommandLine(string[] arguments, out bool maxSpeed)
        {
            var select = 0;
            maxSpeed = Array.IndexOf(arguments, "-eden-max") >= 0;
            for (var i = 0; i + 1 < arguments.Length; i++)
            {
                var value = arguments[i + 1];
                switch (arguments[i])
                {
                    case "-eden-mode" when value == "shared":
                        mode = LabMode.SharedWorld;
                        break;
                    case "-eden-mode" when value == "evolution":
                        mode = LabMode.IsolatedEvolution;
                        break;
                    case "-eden-body" when value == "fixed" || value == "evolving":
                        evolvingBody = value == "evolving";
                        break;
                    case "-eden-reproduction" when value == "on" || value == "off":
                        settings.Reproduction = value == "on";
                        break;
                    case "-eden-preset":
                        for (var p = 0; p < ExperimentPreset.All.Count; p++)
                            if (ExperimentPreset.All[p].Name == value) preset = p;
                        break;
                    case "-eden-brain" when Enum.TryParse(value, true, out LabBrainKind kind) &&
                                            Enum.IsDefined(typeof(LabBrainKind), kind) && kind != LabBrainKind.Evolved:
                        brain = kind;
                        break;
                    case "-eden-seed" when int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedSeed):
                        seed = parsedSeed;
                        break;
                    case "-eden-select" when int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id):
                        select = id;
                        break;
                }
            }

            return select;
        }

        // Founder genomes: evolved creature id i is individuals[i - 1] (the same individuals give the same brains after a
        // restart or a load), neural ones are random per id. Children's genomes come from their parents (SharedGenomes).
        private static SharedGenomes CreateGenomes(SimulationConfig config, LabBrainKind kind,
            IReadOnlyList<Individual> individuals, EvolutionSession evolution)
        {
            switch (kind)
            {
                case LabBrainKind.Evolved:
                    return new SharedGenomes(config, id => individuals[id.Value - 1].Genome,
                        evolution.Evolution.Plan.ExpressedBodyGenes, evolution.Evolution.Plan.Plasticity);
                case LabBrainKind.Neural:
                    return new SharedGenomes(config, id => InitialGenomes.RandomFor(config, id), BodyGeneSet.None);
                default:
                    return null;
            }
        }

        private static BrainController CreateController(SimulationConfig config, LabBrainKind kind, SharedGenomes genomes,
            out string brainName)
        {
            switch (kind)
            {
                case LabBrainKind.Evolved:
                case LabBrainKind.Neural:
                    brainName = MlpBrain.BrainName;
                    return new BrainController(config, id => new MlpBrain(genomes.GenomeOf(id)));
                case LabBrainKind.Random:
                    brainName = RandomBrain.BrainName;
                    return BrainController.Shared(config, new RandomBrain());
                default:
                    brainName = HeuristicBrain.BrainName;
                    return BrainController.Shared(config, new HeuristicBrain());
            }
        }

        private void ShowCheckpointStatus(string text)
        {
            CheckpointStatus = text;
            hud?.ShowCheckpointStatus(text);
            RefreshHud();
        }

        private void ShowGenomeExportStatus(string text)
        {
            GenomeExportStatus = text;
            hud?.ShowExportStatus(text);
        }

        // Names written in checkpoints; the same words as the command line.
        private static string ModeName(LabMode labMode) => labMode == LabMode.IsolatedEvolution ? "evolution" : "shared";

        private static LabMode ParseMode(string name) =>
            name == "evolution" ? LabMode.IsolatedEvolution
            : name == "shared" ? LabMode.SharedWorld
            : throw new CheckpointException($"Unknown lab mode \"{name}\".");

        private static string KindName(LabBrainKind kind) => kind.ToString().ToLowerInvariant();

        private static LabBrainKind ParseKind(string name)
        {
            foreach (LabBrainKind kind in Enum.GetValues(typeof(LabBrainKind)))
            {
                if (KindName(kind) == name)
                    return kind;
            }

            throw new CheckpointException($"Unknown brain \"{name}\".");
        }

        private static int PresetIndex(string name)
        {
            for (var p = 0; p < ExperimentPreset.All.Count; p++)
            {
                if (ExperimentPreset.All[p].Name == name)
                    return p;
            }

            throw new CheckpointException($"Unknown preset \"{name}\".");
        }

        private EvolutionSession EvolutionShown => mode == LabMode.IsolatedEvolution ? Evolution : null;

        private static string Runtime() =>
            UnityEngine.Application.unityVersion + " " + UnityEngine.Application.platform;

        private void OpenMetricsLog(SimulationConfig config)
        {
            try
            {
                var directory = CreateRunDirectory("SimulationRuns", $"seed{config.Seed}-{BrainName}");
                File.WriteAllLines(Path.Combine(directory, "run-info.txt"),
                    RunInfo.Describe(config, BrainName, UnityEngine.Application.version, DateTime.UtcNow));
                metricsLog = new MetricsCsvLog(Path.Combine(directory, "metrics.csv"));
                if (genealogy != null)
                    genealogyLog = new GenealogyCsvLog(Path.Combine(directory, "genealogy.csv"), genealogy);
                MetricsDirectory = directory;
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                // The lab keeps running without a log; the HUD shows that no directory is in use.
                Debug.LogWarning($"EDEN: metrics log disabled ({error.Message})");
                CloseMetricsLog();
            }
        }

        // A resumed evolution logs only the reports completed after the checkpoint, the one in progress included, and
        // records where it came from and which episodes of that report the checkpoint already held.
        private void OpenEvolutionLog(ResumeOrigin resumedFrom = null)
        {
            try
            {
                var directory = CreateRunDirectory("EvolutionRuns", $"lab-seed{seed}-{Preset.Name}");
                var header = new ExperimentHeader(Evolution.Evolution.Settings, Evolution.ValidationInterval,
                    UnityEngine.Application.version, DateTime.UtcNow, Preset.Name, resumedFrom);
                var log = new EvolutionCsvLog(directory, header);
                evolutionLog = log;
                EvolutionDirectory = directory;
                Evolution.ReportCompleted += report => AppendEvolution(log, report);
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                Debug.LogWarning($"EDEN: evolution log disabled ({error.Message})");
            }
        }

        // Reports reach only the log of their own session; a closed or replaced log receives nothing more.
        private void AppendEvolution(EvolutionCsvLog log, GenerationReport report)
        {
            if (log != evolutionLog)
                return;
            try
            {
                log.Append(report);
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                Debug.LogWarning($"EDEN: evolution log disabled ({error.Message})");
                evolutionLog.Dispose();
                evolutionLog = null;
                EvolutionDirectory = null;
            }
        }

        private static string CreateRunDirectory(string kind, string name)
        {
            var root = Path.Combine(UnityEngine.Application.persistentDataPath, kind);
            var runId = $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{name}";
            var directory = Path.Combine(root, runId);
            for (var suffix = 2; Directory.Exists(directory); suffix++)
                directory = Path.Combine(root, runId + "-" + suffix);
            Directory.CreateDirectory(directory);
            return directory;
        }
    }
}
