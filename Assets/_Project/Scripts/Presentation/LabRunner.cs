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
    /// reaches the simulation. In evolution mode the world shown is the isolated episode being evaluated.
    /// Command line (builds): -eden-mode shared|evolution, -eden-preset minimal|standard|stress,
    /// -eden-brain heuristic|random|neural, -eden-seed N, -eden-select ID, -eden-max.
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
        [SerializeField] private bool writeMetrics = true;
        [SerializeField] private WorldView worldView;
        [SerializeField] private LabHud hud;

        private readonly NeuralSnapshot networkSnapshot = new NeuralSnapshot();
        private readonly Stopwatch budgetWatch = new Stopwatch();
        private BrainController controller;
        private MetricsRecorder metrics;
        private MetricsCsvLog metricsLog;
        private EvolutionCsvLog evolutionLog;
        private IReadOnlyList<Individual> sharedIndividuals;
        private int sharedGeneration;
        private CreatureDeath? selectedDeath;
        private float hudTimer;
        private float networkTimer;

        public WorldSimulation Simulation { get; private set; }
        public FixedStepClock Clock { get; private set; }
        public LabMode Mode => mode;
        public LabBrainKind BrainKind => brain;
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
        public LabHud Hud => hud;

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
            hud?.ShowChoice(new LabChoice(mode, preset, seed, brain), Evolution != null);
            RefreshHud();
        }

        /// <summary>Goes back from the shared world to the evolution where it stopped.</summary>
        public void ResumeEvolution()
        {
            if (Evolution == null)
                return;
            CloseMetricsLog();
            mode = LabMode.IsolatedEvolution;
            ClearSelection();
            ShowEpisode();
            hud?.ShowChoice(new LabChoice(mode, preset, seed, brain), true);
            RefreshHud();
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
        }

        private void Awake()
        {
            var selectAtStart = ApplyCommandLine(Environment.GetCommandLineArgs(), out var maxSpeed);
            if (hud != null)
            {
                hud.StartClicked += () => StartExperiment(hud.ReadChoice());
                hud.ResumeEvolutionClicked += ResumeEvolution;
                hud.PauseClicked += TogglePause;
                hud.StepClicked += RequestStep;
                hud.SlowerClicked += () => ScaleSpeed(0.5f);
                hud.FasterClicked += () => ScaleSpeed(2f);
                hud.MaxClicked += ToggleMaxSpeed;
            }

            StartRun();
            if (maxSpeed)
                ToggleMaxSpeed();
            if (selectAtStart > 0)
                Select(new CreatureId(selectAtStart));
        }

        private void OnDestroy() => CloseLogs();

        private void Update()
        {
            HandleInput();
            var realDelta = Time.unscaledDeltaTime;
            RunTicks(Clock.Advance(realDelta));
            if (Clock.IsUnpaced && !Clock.IsPaused)
                Clock.RecordUnpaced(realDelta, RunWithinBudget());
            if (mode == LabMode.IsolatedEvolution)
                ShowEpisode();

            worldView.Render(Simulation.World, Selected);
            if (Selected.HasValue)
            {
                networkTimer += Time.unscaledDeltaTime;
                if (networkTimer >= NetworkRefreshSeconds)
                    RefreshNetwork();
            }

            hudTimer += Time.unscaledDeltaTime;
            if (hudTimer >= HudRefreshSeconds)
                RefreshHud();
        }

        private void RunTicks(int ticks)
        {
            if (ticks <= 0)
                return;
            if (mode == LabMode.IsolatedEvolution)
            {
                Evolution.Advance(ticks);
                return;
            }

            for (var i = 0; i < ticks; i++)
                StepOnce();
        }

        // Whole ticks in chunks until the frame budget is used; checking the watch per chunk keeps its cost low.
        private int RunWithinBudget()
        {
            var chunk = mode == LabMode.IsolatedEvolution ? 256 : 8;
            var ran = 0;
            budgetWatch.Restart();
            do
            {
                RunTicks(chunk);
                ran += chunk;
            } while (budgetWatch.Elapsed.TotalSeconds < MaxSpeedBudgetSeconds && ran < int.MaxValue - chunk);

            return ran;
        }

        private void StepOnce()
        {
            Simulation.Step(controller);
            foreach (var death in Simulation.LastDeaths)
                if (Selected.HasValue && death.Creature == Selected.Value)
                    selectedDeath = death;
            controller.Forget(Simulation.LastDeaths);
            if (metrics.Record(Simulation))
                metricsLog?.Append(metrics.LastSample);
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
            Simulation = new WorldSimulation(WorldFactory.CreateInitial(config));
            controller = CreateController(config);
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
            Evolution = new EvolutionSession(Preset.Evolution((ulong)seed));
            BrainName = MlpBrain.BrainName;
            if (writeMetrics)
                OpenEvolutionLog();
            var episode = Evolution.Job.Current;
            Simulation = episode.Simulation;
            controller = episode.Controller;
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
                return text + LineageOf(creature);
            }

            return LabText.DeadCreature(id, selectedDeath);
        }

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
                return string.Empty;
            return "\n\n" + LabText.Lineage(sharedIndividuals[index], Evolution.Evolution.Lineage, sharedGeneration);
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

        private BrainController CreateController(SimulationConfig config)
        {
            switch (brain)
            {
                case LabBrainKind.Evolved:
                    BrainName = MlpBrain.BrainName;
                    var individuals = sharedIndividuals;
                    return new BrainController(config, id => new MlpBrain(individuals[id.Value - 1].Genome));
                case LabBrainKind.Neural:
                    BrainName = MlpBrain.BrainName;
                    return new BrainController(config, id => new MlpBrain(InitialGenomes.RandomFor(config, id)));
                case LabBrainKind.Random:
                    BrainName = RandomBrain.BrainName;
                    return BrainController.Shared(config, new RandomBrain());
                default:
                    BrainName = HeuristicBrain.BrainName;
                    return BrainController.Shared(config, new HeuristicBrain());
            }
        }

        private void OpenMetricsLog(SimulationConfig config)
        {
            try
            {
                var directory = CreateRunDirectory("SimulationRuns", $"seed{config.Seed}-{BrainName}");
                File.WriteAllLines(Path.Combine(directory, "run-info.txt"),
                    RunInfo.Describe(config, BrainName, UnityEngine.Application.version, DateTime.UtcNow));
                metricsLog = new MetricsCsvLog(Path.Combine(directory, "metrics.csv"));
                MetricsDirectory = directory;
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                // The lab keeps running without a log; the HUD shows that no directory is in use.
                Debug.LogWarning($"EDEN: metrics log disabled ({error.Message})");
                CloseMetricsLog();
            }
        }

        private void OpenEvolutionLog()
        {
            try
            {
                var directory = CreateRunDirectory("EvolutionRuns", $"lab-seed{seed}-{Preset.Name}");
                var evolution = Evolution.Evolution;
                var log = new EvolutionCsvLog(directory, EvolutionCsvLog.Describe(evolution.Plan, evolution.Rules,
                    UnityEngine.Application.version, DateTime.UtcNow));
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
