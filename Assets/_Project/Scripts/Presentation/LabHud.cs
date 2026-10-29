using System;
using System.Collections.Generic;
using Eden.Application;
using Eden.Domain;
using Eden.Evolution;
using UnityEngine;
using UnityEngine.UIElements;

namespace Eden.Presentation
{
    /// <summary>What the experiment controls ask for: indices into <see cref="LabHud.ModeChoices"/>, the presets and brains.</summary>
    public readonly struct LabChoice
    {
        public LabChoice(LabMode mode, int preset, int seed, LabBrainKind brain, bool evolvingBody = false,
            bool neat = false, bool eyes = false, bool evolvingEyes = false, bool solidBodies = false,
            ObstacleLayout obstacles = ObstacleLayout.None, ScenarioSettings scenario = null)
        {
            Scenario = scenario;
            SolidBodies = solidBodies || obstacles != ObstacleLayout.None;
            Obstacles = obstacles;
            Eyes = eyes || evolvingEyes;
            EvolvingEyes = evolvingEyes;
            Mode = mode;
            Preset = preset;
            Seed = seed;
            Brain = brain;
            EvolvingBody = evolvingBody;
            Neat = neat;
        }

        public LabMode Mode { get; }
        public int Preset { get; }
        public int Seed { get; }
        public LabBrainKind Brain { get; }

        /// <summary>The evolution expresses and mutates all body genes; otherwise every body is the fixed control.</summary>
        public bool EvolvingBody { get; }

        /// <summary>The evolution breeds NEAT networks; otherwise the fixed-topology MLP (the control).</summary>
        public bool Neat { get; }

        /// <summary>Creatures of the evolution sense through eyes (NEAT only); otherwise the legacy senses.</summary>
        public bool Eyes { get; }

        /// <summary>The eyes' orientation, aperture and range are inherited with mutation.</summary>
        public bool EvolvingEyes { get; }

        /// <summary>
        /// Creatures are solid bodies that push each other (the arena of the shared world and of the evolution's episodes
        /// alike); otherwise points that pass through each other (the control).
        /// </summary>
        public bool SolidBodies { get; }

        /// <summary>Obstacle layout of the arena; any layout but none implies <see cref="SolidBodies"/>.</summary>
        public ObstacleLayout Obstacles { get; }

        /// <summary>
        /// Arena size and resources of both modes (its solid bodies and obstacles are the two properties above); null
        /// for the 50 × 50 control with the template's resources.
        /// </summary>
        public ScenarioSettings Scenario { get; }
    }

    /// <summary>
    /// Thin binding to the lab UXML: experiment and playback controls with the run summary on the left (and the
    /// generation chart below it in evolution mode), creature inspector on the right and, for a selected neural
    /// creature, the network diagram below the inspector. Controls only raise events; the runner decides.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class LabHud : MonoBehaviour
    {
        public static readonly string[] ModeChoices = { "Mundo compartilhado", "Evolução (avaliação isolada)" };
        public static readonly string[] PresetChoices = { "Mínimo (32)", "Padrão (64)", "Estresse (128)" };
        public static readonly string[] BodyChoices = { "Fixo (controle)", "Evolutivo (4 genes)" };
        public static readonly string[] AlgorithmChoices = { "MLP fixa (controle)", "NEAT (topologia evolui)" };
        public static readonly string[] SensesChoices =
            { "Legados (círculo + radar)", "Olhos fixos (requer NEAT)", "Olhos evolutivos (requer NEAT)" };

        /// <summary>Arena of both modes; obstacles come only with solid bodies.</summary>
        public static readonly string[] ArenaChoices =
            { "Aberta (sem colisão; controle)", "Corpos sólidos", "Sólidos + pilares", "Sólidos + corredor" };

        /// <summary>Arena sizes, in the order of <see cref="ScenarioSettings.PresetSides"/>.</summary>
        public static readonly string[] SizeChoices = { "50 × 50 (controle)", "100 × 100", "200 × 200" };

        /// <summary>How resources follow the size, in <see cref="ResourceScaling"/> order.</summary>
        public static readonly string[] ResourceChoices =
            { "Mantém a densidade (×área)", "Mantém a quantidade" };

        /// <summary>Water layouts, in <see cref="WaterLayout"/> order; water implies solid bodies.</summary>
        public static readonly string[] WaterChoices = { "Sem água", "Um lago", "Dois lagos" };

        public static readonly string[] WaterSupplyChoices = { "Limitada (recarrega)", "Inesgotável (controle)" };

        private const string MlpNetworkTitle = "Rede neural (feed-forward: sem memória nem aprendizado)";
        private const string MlpNetworkLegend = "Neurônios: ativação (verde +, vermelho −). Linhas: peso × sinal.";
        private const string NeatNetworkTitle = "Rede NEAT (topologia evolutiva; feed-forward, sem memória)";
        private const string NeatNetworkLegend =
            "Colunas por profundidade. Nós: valor (verde +, vermelho −). Linhas: peso × sinal; cinza = conexão desabilitada.";

        private static readonly string[] BrainChoices =
            { "Heurístico", "Aleatório", "Rede aleatória", "População evoluída" };

        private Label runStats;
        private Label checkpointStatus;
        private Label inspector;
        private Label exportStatus;
        private VisualElement inspectorPanel;
        private VisualElement networkPanel;
        private VisualElement chartPanel;
        private VisualElement controls;
        private VisualElement exportGenomeRow;
        private DropdownField modeField;
        private DropdownField presetField;
        private DropdownField brainField;
        private DropdownField bodyField;
        private DropdownField algorithmField;
        private DropdownField sensesField;
        private DropdownField arenaField;
        private DropdownField sizeField;
        private DropdownField resourcesField;
        private IntegerField foodField;
        private FloatField foodEnergyField;
        private FloatField foodRespawnField;
        private DropdownField waterField;
        private DropdownField waterSupplyField;
        private FloatField waterCapacityField;
        private FloatField waterRechargeField;
        private FloatField episodeField;
        private FloatField maxAgeField;
        private FloatField initialEnergyField;
        private float customSide = ScenarioSettings.ControlSide;
        private ScenarioSettings shownScenario = new ScenarioSettings();
        private Label networkTitle;
        private Label networkLegend;
        private IntegerField seedField;
        private TextField seedGenomesField;
        private Button resumeButton;
        private Button pauseButton;
        private Button maxButton;
        private Button exportGenomeButton;
        private Button copyGenomeDirectoryButton;
        private Button useExportDirectoryButton;
        private bool inspectorVisible;
        private LabChoice? shownChoice;
        private bool shownEvolution;
        private bool evolvedAvailable = true;

        public event Action StartClicked;
        public event Action ResumeEvolutionClicked;
        public event Action PauseClicked;
        public event Action StepClicked;
        public event Action SlowerClicked;
        public event Action FasterClicked;
        public event Action MaxClicked;
        public event Action SaveClicked;
        public event Action LoadClicked;
        public event Action ExportGenomeClicked;
        public event Action CopyGenomeDirectoryClicked;
        public event Action UseExportDirectoryClicked;

        public string RunText => Bind() ? runStats.text : string.Empty;
        public string CheckpointText => Bind() ? checkpointStatus.text : string.Empty;
        public string InspectorText => Bind() && inspectorVisible ? inspector.text : string.Empty;

        /// <summary>
        /// Directory typed into the seed-pool field, trimmed, or empty when blank; read once when a new evolution
        /// starts. Only meaningful in <see cref="LabMode.IsolatedEvolution"/>, where the field is enabled.
        /// </summary>
        public string SeedGenomesDirectory => Bind() ? seedGenomesField.value?.Trim() ?? string.Empty : string.Empty;

        /// <summary>Fills the seed-pool field (what "Usar exportação" does), e.g. with the export directory.</summary>
        public void SetSeedGenomesDirectory(string path)
        {
            if (Bind())
                seedGenomesField.SetValueWithoutNotify(path ?? string.Empty);
        }

        public bool NetworkVisible { get; private set; }
        public bool ChartVisible { get; private set; }
        public NetworkGraph Graph { get; private set; }

        /// <summary>Diagram of a NEAT network, shown instead of <see cref="Graph"/> for NEAT creatures.</summary>
        public NeatGraph NeatGraph { get; private set; }
        public GenerationChart Chart { get; private set; }

        /// <summary>Root of the UI, for tests that drive the controls like a user.</summary>
        public VisualElement Root => Bind() ? GetComponent<UIDocument>().rootVisualElement : null;

        public void ShowRun(string text)
        {
            if (Bind())
                runStats.text = text;
        }

        /// <summary>Outcome of the last save or load, under the checkpoint buttons; hidden when null.</summary>
        public void ShowCheckpointStatus(string text)
        {
            if (!Bind())
                return;
            checkpointStatus.text = text ?? string.Empty;
            checkpointStatus.style.display = text == null ? DisplayStyle.None : DisplayStyle.Flex;
        }

        /// <summary>Shows the inspector with <paramref name="text"/>, or hides it when the text is null.</summary>
        public void ShowInspector(string text)
        {
            if (!Bind())
                return;
            inspectorVisible = text != null;
            inspectorPanel.style.display = inspectorVisible ? DisplayStyle.Flex : DisplayStyle.None;
            inspector.text = text ?? string.Empty;
        }

        /// <summary>Shows or hides "Exportar genoma"/"Copiar pasta" (the selected creature has, or has not, an exportable genome).</summary>
        public void SetExportAvailable(bool available)
        {
            if (!Bind())
                return;
            exportGenomeRow.style.display = available ? DisplayStyle.Flex : DisplayStyle.None;
            if (!available)
                ShowExportStatus(null);
        }

        /// <summary>Outcome of the last genome export, under the export button; hidden when null.</summary>
        public void ShowExportStatus(string text)
        {
            if (!Bind())
                return;
            exportStatus.text = text ?? string.Empty;
            exportStatus.style.display = text == null ? DisplayStyle.None : DisplayStyle.Flex;
        }

        /// <summary>Shows the diagram for a snapshot, or hides it (no drawing at all) when null.</summary>
        public void ShowNetwork(NeuralSnapshot snapshot)
        {
            if (!Bind())
                return;
            SetNetworkVisible(snapshot != null, false);
            if (snapshot != null)
                Graph.Show(snapshot);
        }

        /// <summary>Shows the NEAT diagram for a snapshot (instead of the MLP one), or hides the panel when null.</summary>
        public void ShowNeatNetwork(NeatSnapshot snapshot)
        {
            if (!Bind())
                return;
            SetNetworkVisible(snapshot != null, true);
            if (snapshot != null)
                NeatGraph.Show(snapshot);
        }

        /// <summary>Whether the NEAT diagram (rather than the MLP one) is the one shown.</summary>
        public bool NeatNetworkShown { get; private set; }

        private void SetNetworkVisible(bool visible, bool neat)
        {
            if (visible != NetworkVisible)
            {
                networkPanel.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
                NetworkVisible = visible;
            }

            if (!visible || neat == NeatNetworkShown)
                return;
            NeatNetworkShown = neat;
            Graph.style.display = neat ? DisplayStyle.None : DisplayStyle.Flex;
            NeatGraph.style.display = neat ? DisplayStyle.Flex : DisplayStyle.None;
            networkTitle.text = neat ? NeatNetworkTitle : MlpNetworkTitle;
            networkLegend.text = neat ? NeatNetworkLegend : MlpNetworkLegend;
        }

        /// <summary>Plots a session's generations, or hides the chart when null.</summary>
        public void ShowEvolution(EvolutionSession session)
        {
            if (!Bind())
                return;
            var visible = session != null;
            if (visible != ChartVisible)
            {
                chartPanel.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
                ChartVisible = visible;
            }

            Chart.Show(session);
        }

        /// <summary>
        /// Puts the controls in line with the run that was just started (applied as soon as the UI exists). Called
        /// only when a run starts, so a choice the user is still editing is never overwritten.
        /// </summary>
        public void ShowChoice(LabChoice choice, bool evolutionAvailable)
        {
            shownChoice = choice;
            shownEvolution = evolutionAvailable;
            if (Bind())
                ApplyChoice();
        }

        private void ApplyChoice()
        {
            if (!shownChoice.HasValue)
                return;
            var choice = shownChoice.Value;
            var evolutionAvailable = shownEvolution;
            SetEvolvedAvailable(evolutionAvailable);
            modeField.SetValueWithoutNotify(ModeChoices[(int)choice.Mode]);
            presetField.SetValueWithoutNotify(PresetChoices[choice.Preset]);
            brainField.SetValueWithoutNotify(BrainChoices[(int)choice.Brain]);
            bodyField.SetValueWithoutNotify(BodyChoices[choice.EvolvingBody ? 1 : 0]);
            algorithmField.SetValueWithoutNotify(AlgorithmChoices[choice.Neat || choice.Eyes ? 1 : 0]);
            sensesField.SetValueWithoutNotify(SensesChoices[choice.EvolvingEyes ? 2 : choice.Eyes ? 1 : 0]);
            arenaField.SetValueWithoutNotify(ArenaChoices[ArenaIndex(choice.SolidBodies, choice.Obstacles)]);
            ShowScenario(choice.Scenario ?? new ScenarioSettings());
            seedField.SetValueWithoutNotify(choice.Seed);
            UpdateBrainField();
            resumeButton.style.display = evolutionAvailable && choice.Mode == LabMode.SharedWorld
                ? DisplayStyle.Flex
                : DisplayStyle.None;
        }

        public void ShowPlayback(bool paused, bool unpaced)
        {
            if (!Bind())
                return;
            pauseButton.text = paused ? "Continuar" : "Pausar";
            maxButton.EnableInClassList("lab-button--active", unpaced);
        }

        /// <summary>Choice currently set in the controls (not necessarily started yet).</summary>
        public LabChoice ReadChoice()
        {
            if (!Bind())
                return default;
            var brain = Math.Max(0, brainField.index);
            var arena = Math.Max(0, arenaField.index);
            return new LabChoice((LabMode)Math.Max(0, modeField.index), Math.Max(0, presetField.index),
                Math.Max(0, seedField.value), (LabBrainKind)brain, bodyField.index == 1, algorithmField.index == 1,
                sensesField.index >= 1, sensesField.index == 2, arena >= 1,
                arena == 2 ? ObstacleLayout.Pillars : arena == 3 ? ObstacleLayout.Corridor : ObstacleLayout.None,
                ReadScenario());
        }

        // The scenario fields; a food count of 0 keeps the mode's own (the preset's or the episodes'), and a size that is
        // not one of the presets (from a loaded run or the command line) stays as it was. A number still showing what
        // the HUD put there keeps what it stood for, so an untouched field never overrides the run's own rules.
        private ScenarioSettings ReadScenario()
        {
            var shown = shownScenario;
            var defaults = new SimulationSettings();
            return new ScenarioSettings
            {
                ArenaSide = sizeField.index >= 0 ? ScenarioSettings.PresetSides[sizeField.index] : customSide,
                Scaling = resourcesField.index == 1 ? ResourceScaling.KeepQuantity : ResourceScaling.KeepDensity,
                Food = foodField.value > 0 ? foodField.value : (int?)null,
                FoodEnergy = Edited(foodEnergyField, shown.FoodEnergy, defaults.FoodEnergy),
                FoodRespawnSeconds = Edited(foodRespawnField, shown.FoodRespawnSeconds, defaults.FoodRespawnSeconds),
                Water = (WaterLayout)Math.Max(0, waterField.index),
                InexhaustibleWater = waterSupplyField.index == 1,
                WaterCapacity = Edited(waterCapacityField, shown.WaterCapacity, WaterLayouts.DefaultCapacity),
                WaterRechargePerSecond = Edited(waterRechargeField, shown.WaterRechargePerSecond,
                    WaterLayouts.DefaultRechargePerSecond),
                EpisodeSeconds = Edited(episodeField, shown.EpisodeSeconds, new EvolutionSettings().EpisodeSeconds),
                MaxAgeSeconds = Edited(maxAgeField, shown.MaxAgeSeconds, defaults.MaxAgeSeconds),
                InitialEnergy = Edited(initialEnergyField, shown.InitialEnergy, defaults.InitialEnergy)
            };

            float? Edited(FloatField field, float? shownValue, float shownDefault) =>
                field.value == (shownValue ?? shownDefault) ? shownValue : field.value;
        }

        private void ShowScenario(ScenarioSettings scenario)
        {
            shownScenario = scenario.Clone();
            var size = Array.IndexOf(ScenarioSettings.PresetSides, scenario.ArenaSide);
            customSide = scenario.ArenaSide;
            sizeField.SetValueWithoutNotify(size >= 0 ? SizeChoices[size] : $"{scenario.ArenaSide:0.#} × {scenario.ArenaSide:0.#}");
            resourcesField.SetValueWithoutNotify(ResourceChoices[(int)scenario.Scaling]);
            foodField.SetValueWithoutNotify(scenario.Food ?? 0);
            var defaults = new SimulationSettings();
            foodEnergyField.SetValueWithoutNotify(scenario.FoodEnergy ?? defaults.FoodEnergy);
            foodRespawnField.SetValueWithoutNotify(scenario.FoodRespawnSeconds ?? defaults.FoodRespawnSeconds);
            waterField.SetValueWithoutNotify(WaterChoices[(int)scenario.Water]);
            waterSupplyField.SetValueWithoutNotify(WaterSupplyChoices[scenario.InexhaustibleWater ? 1 : 0]);
            waterCapacityField.SetValueWithoutNotify(scenario.WaterCapacity ?? WaterLayouts.DefaultCapacity);
            waterRechargeField.SetValueWithoutNotify(scenario.WaterRechargePerSecond ?? WaterLayouts.DefaultRechargePerSecond);
            episodeField.SetValueWithoutNotify(scenario.EpisodeSeconds ?? new EvolutionSettings().EpisodeSeconds);
            maxAgeField.SetValueWithoutNotify(scenario.MaxAgeSeconds ?? defaults.MaxAgeSeconds);
            initialEnergyField.SetValueWithoutNotify(scenario.InitialEnergy ?? defaults.InitialEnergy);
        }

        // Index into ArenaChoices: the open control, solid bodies, then one entry per obstacle layout.
        private static int ArenaIndex(bool solidBodies, ObstacleLayout obstacles) =>
            obstacles == ObstacleLayout.Pillars ? 2
            : obstacles == ObstacleLayout.Corridor ? 3
            : solidBodies ? 1
            : 0;

        /// <summary>True while the user types into a text field, so key shortcuts must be ignored.</summary>
        public bool IsTyping
        {
            get
            {
                if (!Bind())
                    return false;
                for (var element = controls.panel?.focusController?.focusedElement as VisualElement;
                     element != null;
                     element = element.parent)
                {
                    if (element is TextField || element is IntegerField || element is FloatField)
                        return true;
                }

                return false;
            }
        }

        /// <summary>True when a screen point (pixels, origin bottom-left) is over a control, not over the world.</summary>
        public bool IsPointerOverControls(Vector2 screenPosition)
        {
            if (!Bind() || controls.panel == null)
                return false;
            var panelPosition = RuntimePanelUtils.ScreenToPanel(controls.panel,
                new Vector2(screenPosition.x, Screen.height - screenPosition.y));
            var picked = controls.panel.Pick(panelPosition);
            if (picked == null)
                return false;
            // The document root and its ancestors fill the screen; only elements drawn above the world count.
            for (var element = GetComponent<UIDocument>().rootVisualElement; element != null; element = element.parent)
            {
                if (element == picked)
                    return false;
            }

            return true;
        }

        private void SetEvolvedAvailable(bool available)
        {
            if (available == evolvedAvailable)
                return;
            evolvedAvailable = available;
            var choices = new List<string>(BrainChoices);
            if (!available)
                choices.RemoveAt((int)LabBrainKind.Evolved);
            brainField.choices = choices;
        }

        // The brain applies to the shared world; the body choice and the seed-pool directory to a new evolution.
        private void UpdateBrainField()
        {
            brainField.SetEnabled(modeField.index == (int)LabMode.SharedWorld);
            bodyField.SetEnabled(modeField.index == (int)LabMode.IsolatedEvolution);
            algorithmField.SetEnabled(modeField.index == (int)LabMode.IsolatedEvolution);
            sensesField.SetEnabled(modeField.index == (int)LabMode.IsolatedEvolution);
            seedGenomesField.SetEnabled(modeField.index == (int)LabMode.IsolatedEvolution);
            episodeField.SetEnabled(modeField.index == (int)LabMode.IsolatedEvolution);
        }

        // The scenario fields start at the control's values. A value the simulation would refuse (not a number, zero or
        // negative where a positive one is needed, above the limits) goes back to the previous one, so Start never fails.
        private void BindScenario(VisualElement root)
        {
            var defaults = new SimulationSettings();
            sizeField = root.Q<DropdownField>("scenario-size-field");
            sizeField.choices = new List<string>(SizeChoices);
            sizeField.index = 0;
            resourcesField = root.Q<DropdownField>("scenario-resources-field");
            resourcesField.choices = new List<string>(ResourceChoices);
            resourcesField.index = 0;
            foodField = root.Q<IntegerField>("scenario-food-field");
            foodField.RegisterValueChangedCallback(change =>
            {
                // At most what still fits 200 × 200 with density scaling (×16).
                if (change.newValue < 0 || change.newValue > SimulationConfig.MaxFood / 16)
                    foodField.SetValueWithoutNotify(change.previousValue);
            });
            foodEnergyField = Bounded("scenario-food-energy-field", defaults.FoodEnergy, false, SimulationConfig.MaxWater);
            foodRespawnField = Bounded("scenario-food-respawn-field", defaults.FoodRespawnSeconds, false,
                SimulationConfig.MaxDurationSeconds);
            waterField = root.Q<DropdownField>("scenario-water-field");
            waterField.choices = new List<string>(WaterChoices);
            waterField.index = 0;
            waterSupplyField = root.Q<DropdownField>("scenario-water-supply-field");
            waterSupplyField.choices = new List<string>(WaterSupplyChoices);
            waterSupplyField.index = 0;
            waterCapacityField = Bounded("scenario-water-capacity-field", WaterLayouts.DefaultCapacity, false,
                SimulationConfig.MaxWater / 16f);
            waterRechargeField = Bounded("scenario-water-recharge-field", WaterLayouts.DefaultRechargePerSecond, true,
                SimulationConfig.MaxWater / 16f);
            episodeField = Bounded("scenario-episode-field", new EvolutionSettings().EpisodeSeconds, false,
                EvaluationPlan.MaxEpisodeSeconds);
            maxAgeField = Bounded("scenario-max-age-field", defaults.MaxAgeSeconds, false,
                SimulationConfig.MaxDurationSeconds);
            initialEnergyField = Bounded("scenario-initial-energy-field", defaults.InitialEnergy, false, defaults.MaxEnergy);

            FloatField Bounded(string name, float initial, bool zeroAllowed, float max)
            {
                var field = root.Q<FloatField>(name);
                field.SetValueWithoutNotify(initial);
                field.RegisterValueChangedCallback(change =>
                {
                    var value = change.newValue;
                    if (float.IsNaN(value) || value > max || value < 0f || (value == 0f && !zeroAllowed))
                        field.SetValueWithoutNotify(change.previousValue);
                });
                return field;
            }
        }

        private bool Bind()
        {
            if (runStats != null)
                return true;
            var root = GetComponent<UIDocument>().rootVisualElement;
            if (root == null)
                return false;
            var stats = root.Q<Label>("run-stats");
            var inspectorLabel = root.Q<Label>("inspector");
            var inspectorElement = root.Q<VisualElement>("inspector-panel");
            var networkElement = root.Q<VisualElement>("network-panel");
            var host = root.Q<VisualElement>("network-graph-host");
            var chartElement = root.Q<VisualElement>("chart-panel");
            var chartHost = root.Q<VisualElement>("generation-chart-host");
            var legend = root.Q<Label>("chart-legend");
            var controlsElement = root.Q<VisualElement>("experiment-panel");
            var checkpointLabel = root.Q<Label>("checkpoint-status");
            var exportStatusLabel = root.Q<Label>("export-status");
            var exportRowElement = root.Q<VisualElement>("export-genome-row");
            var titleLabel = root.Q<Label>("network-title");
            var legendLabel = root.Q<Label>("network-legend");
            if (stats == null || inspectorLabel == null || inspectorElement == null || networkElement == null ||
                host == null || chartElement == null || chartHost == null || legend == null || controlsElement == null ||
                checkpointLabel == null || exportStatusLabel == null || exportRowElement == null || titleLabel == null ||
                legendLabel == null)
                return false;

            inspector = inspectorLabel;
            inspectorPanel = inspectorElement;
            networkPanel = networkElement;
            chartPanel = chartElement;
            controls = controlsElement;
            exportStatus = exportStatusLabel;
            exportGenomeRow = exportRowElement;
            Graph = new NetworkGraph();
            host.Add(Graph);
            NeatGraph = new NeatGraph();
            NeatGraph.style.display = DisplayStyle.None;
            host.Add(NeatGraph);
            networkTitle = titleLabel;
            networkLegend = legendLabel;
            Chart = new GenerationChart();
            chartHost.Add(Chart);
            legend.text = GenerationChart.Legend();

            modeField = root.Q<DropdownField>("mode-field");
            modeField.choices = new List<string>(ModeChoices);
            modeField.index = 0;
            modeField.RegisterValueChangedCallback(_ => UpdateBrainField());
            presetField = root.Q<DropdownField>("preset-field");
            presetField.choices = new List<string>(PresetChoices);
            presetField.index = 1;
            brainField = root.Q<DropdownField>("brain-field");
            brainField.choices = new List<string>(BrainChoices);
            brainField.index = 0;
            bodyField = root.Q<DropdownField>("body-field");
            bodyField.choices = new List<string>(BodyChoices);
            bodyField.index = 0;
            algorithmField = root.Q<DropdownField>("algorithm-field");
            algorithmField.choices = new List<string>(AlgorithmChoices);
            algorithmField.index = 0;
            sensesField = root.Q<DropdownField>("senses-field");
            sensesField.choices = new List<string>(SensesChoices);
            sensesField.index = 0;
            arenaField = root.Q<DropdownField>("arena-field");
            arenaField.choices = new List<string>(ArenaChoices);
            arenaField.index = 0;
            BindScenario(root);
            // Eyes need NEAT: choosing eyes picks NEAT, and choosing the MLP control goes back to the legacy senses.
            sensesField.RegisterValueChangedCallback(change =>
            {
                if (sensesField.index >= 1)
                    algorithmField.index = 1;
            });
            algorithmField.RegisterValueChangedCallback(change =>
            {
                if (algorithmField.index == 0)
                    sensesField.index = 0;
            });
            seedField = root.Q<IntegerField>("seed-field");
            seedField.RegisterValueChangedCallback(change =>
            {
                if (change.newValue < 0)
                    seedField.SetValueWithoutNotify(0);
            });
            seedGenomesField = root.Q<TextField>("seed-genomes-field");
            useExportDirectoryButton = root.Q<Button>("use-export-directory-button");
            useExportDirectoryButton.clicked += () => UseExportDirectoryClicked?.Invoke();

            root.Q<Button>("start-button").clicked += () => StartClicked?.Invoke();
            resumeButton = root.Q<Button>("resume-evolution-button");
            resumeButton.clicked += () => ResumeEvolutionClicked?.Invoke();
            pauseButton = root.Q<Button>("pause-button");
            pauseButton.clicked += () => PauseClicked?.Invoke();
            root.Q<Button>("step-button").clicked += () => StepClicked?.Invoke();
            root.Q<Button>("slower-button").clicked += () => SlowerClicked?.Invoke();
            root.Q<Button>("faster-button").clicked += () => FasterClicked?.Invoke();
            maxButton = root.Q<Button>("max-button");
            maxButton.clicked += () => MaxClicked?.Invoke();
            root.Q<Button>("save-button").clicked += () => SaveClicked?.Invoke();
            root.Q<Button>("load-button").clicked += () => LoadClicked?.Invoke();
            exportGenomeButton = root.Q<Button>("export-genome-button");
            exportGenomeButton.clicked += () => ExportGenomeClicked?.Invoke();
            copyGenomeDirectoryButton = root.Q<Button>("copy-genome-directory-button");
            copyGenomeDirectoryButton.clicked += () => CopyGenomeDirectoryClicked?.Invoke();
            checkpointStatus = checkpointLabel;
            runStats = stats;
            ApplyChoice();
            return true;
        }
    }
}
