using System;
using System.Collections.Generic;
using Eden.Application;
using UnityEngine;
using UnityEngine.UIElements;

namespace Eden.Presentation
{
    /// <summary>What the experiment controls ask for: indices into <see cref="LabHud.ModeChoices"/>, the presets and brains.</summary>
    public readonly struct LabChoice
    {
        public LabChoice(LabMode mode, int preset, int seed, LabBrainKind brain)
        {
            Mode = mode;
            Preset = preset;
            Seed = seed;
            Brain = brain;
        }

        public LabMode Mode { get; }
        public int Preset { get; }
        public int Seed { get; }
        public LabBrainKind Brain { get; }
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

        private static readonly string[] BrainChoices =
            { "Heurístico", "Aleatório", "Rede aleatória", "População evoluída" };

        private Label runStats;
        private Label inspector;
        private VisualElement inspectorPanel;
        private VisualElement networkPanel;
        private VisualElement chartPanel;
        private VisualElement controls;
        private DropdownField modeField;
        private DropdownField presetField;
        private DropdownField brainField;
        private IntegerField seedField;
        private Button resumeButton;
        private Button pauseButton;
        private Button maxButton;
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

        public string RunText => Bind() ? runStats.text : string.Empty;
        public string InspectorText => Bind() && inspectorVisible ? inspector.text : string.Empty;
        public bool NetworkVisible { get; private set; }
        public bool ChartVisible { get; private set; }
        public NetworkGraph Graph { get; private set; }
        public GenerationChart Chart { get; private set; }

        /// <summary>Root of the UI, for tests that drive the controls like a user.</summary>
        public VisualElement Root => Bind() ? GetComponent<UIDocument>().rootVisualElement : null;

        public void ShowRun(string text)
        {
            if (Bind())
                runStats.text = text;
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

        /// <summary>Shows the diagram for a snapshot, or hides it (no drawing at all) when null.</summary>
        public void ShowNetwork(NeuralSnapshot snapshot)
        {
            if (!Bind())
                return;
            var visible = snapshot != null;
            if (visible != NetworkVisible)
            {
                networkPanel.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
                NetworkVisible = visible;
            }

            if (visible)
                Graph.Show(snapshot);
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
            return new LabChoice((LabMode)Math.Max(0, modeField.index), Math.Max(0, presetField.index),
                Math.Max(0, seedField.value), (LabBrainKind)brain);
        }

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
                    if (element == seedField)
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

        private void UpdateBrainField() =>
            brainField.SetEnabled(modeField.index == (int)LabMode.SharedWorld);

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
            if (stats == null || inspectorLabel == null || inspectorElement == null || networkElement == null ||
                host == null || chartElement == null || chartHost == null || legend == null || controlsElement == null)
                return false;

            inspector = inspectorLabel;
            inspectorPanel = inspectorElement;
            networkPanel = networkElement;
            chartPanel = chartElement;
            controls = controlsElement;
            Graph = new NetworkGraph();
            host.Add(Graph);
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
            seedField = root.Q<IntegerField>("seed-field");
            seedField.RegisterValueChangedCallback(change =>
            {
                if (change.newValue < 0)
                    seedField.SetValueWithoutNotify(0);
            });

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
            runStats = stats;
            ApplyChoice();
            return true;
        }
    }
}
