using System;
using System.IO;
using Eden.Application;
using Eden.Brains;
using Eden.Domain;
using Eden.Persistence;
using Eden.Simulation;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Eden.Presentation
{
    public enum BaselineBrainKind
    {
        Heuristic,
        Random
    }

    /// <summary>
    /// Composition root of the lab scene: builds a run from the Inspector settings, converts frame time into
    /// fixed ticks with <see cref="FixedStepClock"/>, handles lab controls and selection, and writes a metrics CSV
    /// under persistentDataPath/SimulationRuns. Frame rate never reaches the simulation.
    /// </summary>
    public sealed class LabRunner : MonoBehaviour
    {
        private const float HudRefreshSeconds = 0.25f;
        private const float SelectRadius = 1.5f;

        [SerializeField] private SimulationSettings settings = new SimulationSettings();
        [SerializeField, Min(0)] private int seed = 1;
        [SerializeField] private BaselineBrainKind brain = BaselineBrainKind.Heuristic;
        [SerializeField] private bool writeMetrics = true;
        [SerializeField] private WorldView worldView;
        [SerializeField] private LabHud hud;

        private BrainController controller;
        private MetricsRecorder metrics;
        private MetricsCsvLog metricsLog;
        private CreatureDeath? selectedDeath;
        private float hudTimer;

        public WorldSimulation Simulation { get; private set; }
        public FixedStepClock Clock { get; private set; }
        public IBrain Brain { get; private set; }
        public CreatureId? Selected { get; private set; }
        public string RunDirectory { get; private set; }
        public WorldView View => worldView;
        public LabHud Hud => hud;

        public void StartRun()
        {
            CloseMetricsLog();
            var runSettings = settings.Clone();
            runSettings.Seed = (ulong)seed;
            var config = new SimulationConfig(runSettings);

            Simulation = new WorldSimulation(WorldFactory.CreateInitial(config));
            Brain = brain == BaselineBrainKind.Heuristic ? (IBrain)new HeuristicBrain() : new RandomBrain();
            controller = BrainController.Shared(config, Brain);
            var speed = Clock?.RequestedSpeed ?? 1f;
            var paused = Clock?.IsPaused ?? false;
            Clock = new FixedStepClock(config.TicksPerSecond);
            Clock.SetSpeed(speed);
            if (paused)
                Clock.Pause();
            metrics = new MetricsRecorder(config, config.TicksPerSecond);
            Selected = null;
            selectedDeath = null;

            if (writeMetrics)
                OpenMetricsLog(config);
            worldView.Bind(Simulation.World);
            RefreshHud();
        }

        public void TogglePause()
        {
            if (Clock.IsPaused)
                Clock.Resume();
            else
                Clock.Pause();
        }

        public void RequestStep() => Clock.RequestStep();

        public void ScaleSpeed(float factor) =>
            Clock.SetSpeed(Mathf.Clamp(Clock.RequestedSpeed * factor, FixedStepClock.MinSpeed, FixedStepClock.MaxSpeed));

        public void Select(CreatureId creature)
        {
            Selected = creature;
            selectedDeath = null;
            RefreshHud();
        }

        public void ClearSelection()
        {
            Selected = null;
            selectedDeath = null;
            RefreshHud();
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

        public void CloseMetricsLog()
        {
            metricsLog?.Dispose();
            metricsLog = null;
        }

        private void Awake() => StartRun();

        private void OnDestroy() => CloseMetricsLog();

        private void Update()
        {
            HandleInput();
            var ticks = Clock.Advance(Time.unscaledDeltaTime);
            for (var i = 0; i < ticks; i++)
                StepOnce();

            worldView.Render(Simulation.World, Selected);
            hudTimer += Time.unscaledDeltaTime;
            if (hudTimer >= HudRefreshSeconds)
                RefreshHud();
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

        private void HandleInput()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.spaceKey.wasPressedThisFrame) TogglePause();
                if (keyboard.periodKey.wasPressedThisFrame) RequestStep();
                if (keyboard.equalsKey.wasPressedThisFrame || keyboard.numpadPlusKey.wasPressedThisFrame) ScaleSpeed(2f);
                if (keyboard.minusKey.wasPressedThisFrame || keyboard.numpadMinusKey.wasPressedThisFrame) ScaleSpeed(0.5f);
                if (keyboard.rightBracketKey.wasPressedThisFrame) SelectNeighbor(+1);
                if (keyboard.leftBracketKey.wasPressedThisFrame) SelectNeighbor(-1);
                if (keyboard.escapeKey.wasPressedThisFrame) ClearSelection();
                if (keyboard.rKey.wasPressedThisFrame) StartRun();
                if (keyboard.bKey.wasPressedThisFrame)
                {
                    brain = brain == BaselineBrainKind.Heuristic ? BaselineBrainKind.Random : BaselineBrainKind.Heuristic;
                    StartRun();
                }
            }

            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
                SelectNearest(worldView.ScreenToArena(mouse.position.ReadValue()));
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
            hud.ShowRun(LabText.Run(Simulation, Clock, Brain, metrics, RunDirectory));
            hud.ShowInspector(Selected.HasValue ? DescribeSelected(Selected.Value) : null);
        }

        private string DescribeSelected(CreatureId id)
        {
            foreach (var creature in Simulation.World.Creatures)
            {
                if (creature.Id != id)
                    continue;
                controller.TryGetLastAction(id, out var action);
                return LabText.Creature(creature, Sensors.Observe(Simulation.World, creature), action, Simulation.World.Config);
            }

            return LabText.DeadCreature(id, selectedDeath);
        }

        private void OpenMetricsLog(SimulationConfig config)
        {
            try
            {
                var root = Path.Combine(UnityEngine.Application.persistentDataPath, "SimulationRuns");
                var runId = $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-seed{config.Seed}-{Brain.Name}";
                var directory = Path.Combine(root, runId);
                for (var suffix = 2; Directory.Exists(directory); suffix++)
                    directory = Path.Combine(root, runId + "-" + suffix);

                Directory.CreateDirectory(directory);
                File.WriteAllLines(Path.Combine(directory, "run-info.txt"),
                    RunInfo.Describe(config, Brain.Name, UnityEngine.Application.version, DateTime.UtcNow));
                metricsLog = new MetricsCsvLog(Path.Combine(directory, "metrics.csv"));
                RunDirectory = directory;
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                // The lab keeps running without a log; the HUD shows that no directory is in use.
                Debug.LogWarning($"EDEN: metrics log disabled ({error.Message})");
                metricsLog = null;
                RunDirectory = null;
            }
        }
    }
}
