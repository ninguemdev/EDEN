using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Eden.Domain;
using Eden.Persistence;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Eden.Presentation
{
    /// <summary>Presentation navigation, draft, layout and file actions. Never owns simulation rules.</summary>
    public sealed class LabWorkspace
    {
        [Serializable] public sealed class Value { public string name; public string value; public string kind; }
        [Serializable] public sealed class Draft { public int version = 1; public List<Value> fields = new List<Value>(); }
        private readonly LabRunner runner;
        private readonly LabHud hud;
        private readonly VisualElement root;
        private readonly ExperimentPreview preview;
        private readonly LabLibrary library;
        private readonly ListView population;
        private readonly List<string> populationRows = new List<string>();
        private readonly List<long> populationIds = new List<long>();
        private readonly List<int> populationRanks = new List<int>();
        private string screen = "home";
        private string tool = "overview";
        private string layout = "observe";
        private bool sideOpen = true;
        private bool analysisOpen;
        private bool analysisExpanded;
        private bool menuWasRunning;
        private bool waitingSave;
        private Action replacing;
        private float previewAt;
        private bool previewPending;
        private bool restoring;
        private string lastInspector;
        private int inspectorTab;
        private Vector2? cameraDrag;
        private bool movingCamera;
        private bool resizingSide;
        private bool resizingChart;
        private float sideWidth = 340;
        private float chartHeight = 330;
        private bool sideLeft;
        private bool narrowPreview;
        private readonly Queue<string> events = new Queue<string>();
        private string lastEvent;
        private Task<(Checkpoint State, CheckpointInfo Info)> loading;
        private string loadingPath;
        public string Screen => screen;
        public bool BlocksSimulationShortcuts => screen != "simulation" || replacing != null || loading != null;
        public bool IsLoading => loading != null;
        public bool ConsumesArenaClick => movingCamera;
        public bool IsModal => replacing != null;
        public ExperimentPreview Preview => preview;

        public LabWorkspace(LabRunner runner, LabHud hud)
        {
            this.runner = runner; this.hud = hud; root = hud.Root;
            preview = new ExperimentPreview(); root.Q("preview-host").Add(preview);
            library = new LabLibrary(runner, root);
            library.LoadRequested = path => ConfirmReplacement(() =>
            {
                if (loading != null) return;
                runner.Clock.Pause(); loadingPath = path;
                Message("Conferindo o checkpoint selecionado…");
                loading = Task.Run(() => { var state = CheckpointFile.Load(path, out var info); return (state, info); });
            });
            library.PresetRequested = path =>
            {
                try { RestoreDraft(File.ReadAllText(path)); Open("configure"); }
                catch (Exception error) when (error is IOException || error is UnauthorizedAccessException) { Message("Não foi possível abrir o preset: " + error.Message); }
            };
            foreach (var name in new[] { "home", "configure", "simulation", "library" })
            { var destination = name; Button("nav-" + name, () => Open(destination)); }
            Button("new-experiment", () => Open("configure")); Button("resume-session", () => Open("simulation"));
            Button("open-checkpoints", OpenLibrary); Button("config-back", () => Open("home"));
            Button("refresh-preview", RefreshPreview);
            Button("random-seed", () => root.Q<IntegerField>("seed-field").value = unchecked((int)(DateTime.UtcNow.Ticks & int.MaxValue)));
            Button("save-preset", SavePreset);
            var previewToggle = new Button(() => { narrowPreview = !narrowPreview; ApplyLayout(); }) { text = "Prévia / formulário", name = "toggle-preview" };
            previewToggle.AddToClassList("lab-button"); root.Q("config-tabs").Add(previewToggle);
            var placement = new DropdownField("Painel", new List<string> { "Direita", "Esquerda" }, 0) { name = "panel-placement" };
            placement.RegisterValueChangedCallback(_ => { sideLeft = placement.index == 1; ApplyLayout(); SaveLayout(); });
            root.Q("simulation-tools").Add(placement);
            for (var i = 0; i < 5; i++) { var page = i; Button("config-tab-" + i, () => Page(page)); }
            foreach (var name in new[] { "overview", "population", "inspector", "events" })
            { var value = name; Button(name == "inspector" ? "show-inspector" : name, () => { tool = value; sideOpen = true; analysisExpanded = false; analysisOpen = false; if (value == "population") RefreshPopulation(); ApplyLayout(); }); }
            Button("close-side", () => { sideOpen = false; ApplyLayout(); });
            Button("analysis", () => { analysisOpen = !analysisOpen; ApplyLayout(); });
            foreach (var name in new[] { "observe", "evolve", "analyze" }) { var value = name; Button("layout-" + name, () => SetLayout(value)); }
            Button("restore-layout", () => { sideWidth = 340; chartHeight = 330; sideLeft = false; placement.SetValueWithoutNotify("Direita"); SetLayout("observe"); });
            Button("frame-arena", () => runner.View.FrameArena());
            Button("move-camera", () => { movingCamera = !movingCamera; root.Q<Button>("move-camera").EnableInClassList("lab-button--active", movingCamera); });
            Button("chart-expand", () => { analysisExpanded = !analysisExpanded; analysisOpen = true; ApplyLayout(); });
            Button("chart-zoom-in", () => hud.Chart.Zoom(0.7f)); Button("chart-zoom-out", () => hud.Chart.Zoom(1.4f));
            Button("chart-all", () => hud.Chart.ShowAll()); Button("chart-latest", () => hud.Chart.FollowLatest());
            Button("chart-table", () => { var table = root.Q<ListView>("chart-data-table"); table.style.display = table.resolvedStyle.display == DisplayStyle.None ? DisplayStyle.Flex : DisplayStyle.None; RefreshChartTable(); });
            Button("chart-export", ExportChart);
            foreach (var name in new[] { "training", "validation", "best", "champion", "references", "stages" })
            { var series = name; root.Q<Toggle>("series-" + name).RegisterValueChangedCallback(e => hud.Chart.SetSeries(series, e.newValue)); }
            var metric = root.Q<DropdownField>("analysis-metric"); metric.choices = new List<string> { "Desempenho", "Sobrevivência", "Comida (energia)", "Diversidade" }; metric.index = 0;
            metric.RegisterValueChangedCallback(_ => { hud.Chart.SetMetric(metric.index); root.Q<Label>("chart-title").text = metric.value + " por geração"; RefreshChartTable(); });
            hud.Chart.Selected += text => { var label = root.Q<Label>("chart-selection"); label.text = text; label.tooltip = text; RefreshChartTable(); };
            for (var i = 0; i < 4; i++) { var tab = i; Button("inspector-tab-" + i, () => { inspectorTab = tab; ApplyInspector(); }); }
            population = root.Q<ListView>("population-list"); population.fixedItemHeight = 36;
            population.makeItem = () => new Label(); population.bindItem = (e, i) => ((Label)e).text = populationRows[i]; population.itemsSource = populationRows;
            population.selectionChanged += _ =>
            {
                var index = population.selectedIndex;
                if (index < 0 || index >= populationIds.Count) return;
                if (runner.Mode == LabMode.SharedWorld) runner.Select(new CreatureId((int)populationIds[index]));
                else runner.ConfigureFollow(true, runner.Evolution.LastTraining.Individuals.Count, populationRanks[index], 1);
                tool = "inspector"; ApplyLayout();
            };
            Button("refresh-population", RefreshPopulation); root.Q<TextField>("population-search").RegisterValueChangedCallback(_ => RefreshPopulation());
            Button("confirm-cancel", () => { replacing = null; waitingSave = false; Display("confirm-modal", false); });
            Button("confirm-discard", CompleteReplacement);
            Button("confirm-save", () => { runner.RequestCheckpoint(); waitingSave = true; Message("Salvando antes de continuar…"); });
            root.RegisterCallback<ChangeEvent<string>>(_ => DraftChanged()); root.RegisterCallback<ChangeEvent<int>>(_ => DraftChanged()); root.RegisterCallback<ChangeEvent<float>>(_ => DraftChanged());
            SetupResize("side-divider", true); SetupResize("chart-divider", false);
            root.RegisterCallback<GeometryChangedEvent>(_ => ApplyLayout());
            var settings = UnityEngine.Application.isBatchMode ? "observe" : PlayerPrefs.GetString("eden.workspace.layout", "observe");
            sideWidth = UnityEngine.Application.isBatchMode ? 340 : Mathf.Clamp(PlayerPrefs.GetFloat("eden.workspace.side", 340), 250, 650);
            chartHeight = UnityEngine.Application.isBatchMode ? 330 : Mathf.Clamp(PlayerPrefs.GetFloat("eden.workspace.chart", 330), 170, 650);
            sideLeft = !UnityEngine.Application.isBatchMode && PlayerPrefs.GetInt("eden.workspace.sideLeft", 0) == 1;
            placement.SetValueWithoutNotify(sideLeft ? "Esquerda" : "Direita");
            Page(0); SetLayout(settings);
            var draft = UnityEngine.Application.isBatchMode ? "" : PlayerPrefs.GetString("eden.workspace.draft", "");
            if (!string.IsNullOrEmpty(draft)) RestoreDraft(draft);
            Display("confirm-modal", false); Display("chart-data-table", false); Message(null);
            menuWasRunning = false; runner.Clock.Pause(); Open("home");
        }

        private void Button(string name, Action action) => root.Q<Button>(name).clicked += action;
        private void Display(string name, bool show) => root.Q(name).style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
        public void OpenLibrary() { Open("library"); library.Open(); }
        public void Open(string destination)
        {
            if ((destination == "home" || destination == "configure") && (screen == "simulation" || screen == "library")) { menuWasRunning = !runner.Clock.IsPaused; runner.Clock.Pause(); }
            screen = destination;
            foreach (var name in new[] { "home", "configure", "simulation", "library" })
            { Display(name + "-screen", name == screen); root.Q<Button>("nav-" + name).EnableInClassList("lab-button--active", name == screen); }
            root.Q("playback-controls").SetEnabled(screen == "simulation" || screen == "library");
            if (screen == "simulation" && menuWasRunning) { runner.Clock.Resume(); menuWasRunning = false; }
            if (screen == "configure") { previewPending = true; previewAt = Time.unscaledTime + 0.2f; }
            if (screen == "library") library.Refresh();
            ApplyLayout();
        }
        public void BeginExperiment()
        {
            RefreshPreview();
            if (preview.World == null) return;
            ConfirmReplacement(() =>
            {
                runner.StartExperiment(hud.ReadChoice());
                if (runner.StartProblem != null) { Message(runner.StartProblem); return; }
                runner.Clock.Pause(); menuWasRunning = false; Open("simulation");
                root.Q<Label>("recent-session").text = Name + " · seed " + runner.Seed;
                Message("Experimento iniciado e pausado. Use Continuar quando estiver pronto.");
            });
        }
        public string Name => root.Q<TextField>("experiment-name").value;
        private bool HasProgress => runner.Evolution?.TotalTicks > 0 || runner.Simulation?.World.Tick > 0;
        public void ConfirmReplacement(Action action)
        {
            if (runner.CheckpointIsBusy || loading != null) { Message("Aguarde a operação do checkpoint terminar."); return; }
            if (!HasProgress) { action(); return; }
            runner.Clock.Pause(); replacing = action; Display("confirm-modal", true);
        }
        private void CompleteReplacement()
        { var action = replacing; replacing = null; waitingSave = false; menuWasRunning = false; Display("confirm-modal", false); action?.Invoke(); }
        public bool Escape()
        {
            if (replacing != null) { replacing = null; Display("confirm-modal", false); return true; }
            if (screen != "simulation") { Open("simulation"); return true; }
            if (sideOpen) { sideOpen = false; ApplyLayout(); return true; }
            if (analysisExpanded) { analysisExpanded = false; ApplyLayout(); return true; }
            Open("home"); return true;
        }
        private void Page(int index)
        {
            for (var i = 0; i < 5; i++) { Display("config-page-" + i, i == index); root.Q<Button>("config-tab-" + i).EnableInClassList("lab-button--active", i == index); }
            if (index == 4) RefreshPreview();
        }
        private void DraftChanged()
        {
            if (restoring || screen != "configure") return;
            previewPending = true; previewAt = Time.unscaledTime + 0.35f;
            root.Q<Label>("draft-status").text = "Personalizado · origem: " + root.Q<DropdownField>("preset-field").value;
            root.Q<Label>("preview-summary").text = "Atualizando prévia…"; root.Q<Button>("start-button").SetEnabled(false);
        }
        public void RefreshPreview()
        {
            previewPending = false;
            try
            {
                preview.Build(hud.ReadChoice(), runner.WorldTemplate, runner.Evolution);
                root.Q<Label>("preview-summary").text = preview.Description;
                root.Q<Label>("review-summary").text = Name + "\n" + preview.Description + "\n" +
                    root.Q<DropdownField>("algorithm-field").value + " · " + root.Q<DropdownField>("senses-field").value + "\n" + root.Q<DropdownField>("profile-field").value;
                root.Q<Label>("review-validation").text = "Estado inicial válido. A viabilidade fisiológica será conferida ao iniciar.";
                root.Q<Button>("start-button").SetEnabled(true);
            }
            catch (Exception error) when (error is ArgumentException || error is InvalidOperationException || error is CheckpointException || error is ScenarioInfeasibleException || error is IOException)
            {
                preview.Clear(error.Message); root.Q<Label>("preview-summary").text = error.Message;
                root.Q<Label>("review-summary").text = "Configuração não validada. Corrija os campos antes de iniciar.";
                root.Q<Label>("review-validation").text = error.Message;
                root.Q<Button>("start-button").SetEnabled(false);
            }
        }
        private void SetLayout(string value)
        { layout = value == "analyze" || value == "evolve" ? value : "observe"; analysisOpen = layout != "observe"; analysisExpanded = layout == "analyze"; ApplyLayout(); SaveLayout(); }
        public void ApplyLayout()
        {
            var width = root.worldBound.width; var height = root.worldBound.height;
            var compact = width > 0 && width < 1350 || height > 0 && height < 780;
            var narrow = width > 0 && width < 850;
            Display("preview-panel", !narrow || narrowPreview); Display("experiment-panel", !narrow || !narrowPreview);
            root.Q("simulation-main").style.flexDirection = sideLeft ? FlexDirection.RowReverse : FlexDirection.Row;
            var chart = screen == "simulation" && analysisOpen && hud.ChartVisible;
            var expanded = chart && (analysisExpanded || compact);
            Display("chart-panel", chart); Display("simulation-main", !expanded);
            root.Q("chart-panel").style.flexGrow = expanded ? 1 : 0;
            root.Q("chart-panel").style.height = expanded ? new StyleLength(StyleKeyword.Auto) : Mathf.Clamp(chartHeight, 170, Mathf.Max(170, height * 0.55f));
            Display("chart-legend", false); // The clickable series controls provide the legend without repeating it.
            root.Q("generation-chart-host").style.minHeight = compact ? 150 : 120;
            root.Q("chart-selection").style.maxHeight = compact ? 56 : 110;
            root.Q("chart-selection").style.overflow = Overflow.Hidden;
            Display("workspace-side", sideOpen); Display("side-divider", sideOpen);
            root.Q("workspace-side").style.width = Mathf.Clamp(sideWidth, 250, Mathf.Max(250, width * 0.45f));
            foreach (var name in new[] { "overview", "population", "events" }) Display(name + "-content", tool == name);
            Display("inspector-panel", tool == "inspector");
            root.Q<Label>("side-title").text = tool == "overview" ? "Visão geral" : tool == "population" ? "População" : tool == "events" ? "Eventos" : "Inspeção";
            root.Q("simulation-tools").style.display = screen == "simulation" ? DisplayStyle.Flex : DisplayStyle.None;
            ApplyInspector();
        }
        private void SetupResize(string name, bool side)
        {
            var handle = root.Q(name);
            handle.RegisterCallback<PointerDownEvent>(e => { if (side) resizingSide = true; else resizingChart = true; handle.CapturePointer(e.pointerId); e.StopPropagation(); });
            handle.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!(side ? resizingSide : resizingChart)) return;
                if (side) sideWidth = Mathf.Clamp(sideWidth + (sideLeft ? e.deltaPosition.x : -e.deltaPosition.x), 250, 650);
                else chartHeight = Mathf.Clamp(chartHeight - e.deltaPosition.y, 170, 650);
                ApplyLayout(); e.StopPropagation();
            });
            handle.RegisterCallback<PointerUpEvent>(e => { resizingSide = resizingChart = false; handle.ReleasePointer(e.pointerId); SaveLayout(); });
            handle.RegisterCallback<PointerCaptureOutEvent>(_ => { resizingSide = resizingChart = false; });
        }
        private void SaveLayout()
        { if (UnityEngine.Application.isBatchMode) return; PlayerPrefs.SetString("eden.workspace.layout", layout); PlayerPrefs.SetFloat("eden.workspace.side", sideWidth); PlayerPrefs.SetFloat("eden.workspace.chart", chartHeight); PlayerPrefs.SetInt("eden.workspace.sideLeft", sideLeft ? 1 : 0); }
        public void ShowInspector(string text) { lastInspector = text; if (text != null && tool == "overview") { tool = "inspector"; sideOpen = true; } ApplyLayout(); }
        private void ApplyInspector()
        {
            var label = root.Q<Label>("inspector"); var text = lastInspector ?? "Nenhuma criatura selecionada. Clique na arena ou escolha um indivíduo na população.";
            var senses = text.IndexOf("Sensores", StringComparison.Ordinal); var lineage = text.IndexOf("\n\n", StringComparison.Ordinal);
            label.text = inspectorTab == 1 ? (senses >= 0 ? text.Substring(senses) : text) : inspectorTab == 3 ?
                (lineage >= 0 ? text.Substring(lineage + 2) : "Linhagem detalhada indisponível para esta seleção.") : text;
            if (inspectorTab == 2 && !hud.NetworkVisible) label.text = "Esta seleção não tem uma captura de rede neural disponível. Cérebros de referência não possuem um genoma neural para desenhar.";
            label.style.display = inspectorTab == 2 && hud.NetworkVisible ? DisplayStyle.None : DisplayStyle.Flex;
            root.Q("network-panel").style.display = inspectorTab == 2 && hud.NetworkVisible ? DisplayStyle.Flex : DisplayStyle.None;
            for (var i = 0; i < 4; i++) root.Q<Button>("inspector-tab-" + i).EnableInClassList("lab-button--active", inspectorTab == i);
        }
        public void RefreshIndicators()
        {
            var world = runner.Simulation?.World; if (world == null) return;
            root.Q<Label>("indicator-mode").text = runner.Mode == LabMode.SharedWorld ? "Arena" : "Evolução";
            root.Q<Label>("indicator-generation").text = runner.Evolution == null ? "—" : runner.Evolution.Evolution.Generation.ToString();
            root.Q<Label>("indicator-population").text = runner.Mode == LabMode.SharedWorld ? world.Creatures.Count.ToString() : runner.Evolution.Evolution.Individuals.Count + " genomas";
            root.Q<Label>("indicator-tick").text = world.Tick.ToString();
            root.Q<Label>("indicator-speed").text = (runner.Clock.IsUnpaced ? "Máx." : runner.Clock.RequestedSpeed + "×") + " · " + runner.Clock.EffectiveSpeed.ToString("0.0") + "× efetivo";
            root.Q<Label>("session-state").text = runner.Clock.IsPaused ? "Pausado" : runner.Mode == LabMode.IsolatedEvolution ? runner.Evolution.Phase.ToString() : "Executando";
            root.Q<Label>("events-text").text = string.Join("\n\n", events);
            ApplyLayout();
        }
        public void Record(string text)
        {
            if (string.IsNullOrEmpty(text) || text == lastEvent) return;
            lastEvent = text; events.Enqueue(DateTime.Now.ToString("HH:mm:ss") + " · " + text); while (events.Count > 64) events.Dequeue();
        }
        public void Tick()
        {
            library.Tick();
            if (loading?.IsCompleted == true)
            {
                var completed = loading; loading = null;
                if (completed.IsFaulted) Message("Checkpoint recusado; sessão preservada: " + completed.Exception.GetBaseException().Message);
                else if (runner.ApplyCheckpoint(loadingPath, completed.Result.State, completed.Result.Info))
                { runner.Clock.Pause(); menuWasRunning = false; Open("simulation"); Message("Checkpoint retomado e pausado. Confira o estado antes de continuar."); }
                else Message(runner.CheckpointStatus);
            }
            if (previewPending && Time.unscaledTime >= previewAt) RefreshPreview();
            if (waitingSave && !runner.CheckpointIsBusy)
            {
                waitingSave = false;
                if (runner.LastManualCheckpointPath != null && runner.CheckpointStatus?.StartsWith("Checkpoint salvo", StringComparison.Ordinal) == true) CompleteReplacement();
                else Message("Não foi possível salvar. A sessão foi preservada; cancele ou escolha continuar sem salvar.");
            }
            if (screen != "simulation") return;
            var viewport = root.Q("arena-viewport");
            if (viewport.resolvedStyle.display == DisplayStyle.None || analysisExpanded) return;
            runner.View.SetViewport(viewport.worldBound, root.worldBound);
            var mouse = Mouse.current; if (mouse == null) return;
            var pos = mouse.position.ReadValue();
            if (hud.IsPointerOverControls(pos)) { cameraDrag = null; return; }
            if (Mathf.Abs(mouse.scroll.ReadValue().y) > 0) runner.View.ZoomArena(Mathf.Exp(-mouse.scroll.ReadValue().y * 0.0015f));
            var pressed = mouse.middleButton.isPressed || movingCamera && mouse.leftButton.isPressed;
            if (pressed && cameraDrag.HasValue) runner.View.PanArena(pos - cameraDrag.Value);
            cameraDrag = pressed ? pos : (Vector2?)null;
        }
        private void RefreshPopulation()
        {
            var search = root.Q<TextField>("population-search").value ?? ""; populationRows.Clear(); populationIds.Clear(); populationRanks.Clear();
            if (runner.Mode == LabMode.SharedWorld)
            {
                foreach (var c in runner.Simulation.World.Creatures)
                    if (c.Id.ToString().Contains(search)) { populationRows.Add($"#{c.Id} · energia {c.Energy:0.0} · idade {c.AgeTicks}"); populationIds.Add(c.Id.Value); }
            }
            else
            {
                var report = runner.Evolution.LastTraining;
                var rank = 0;
                if (report != null)
                    foreach (var candidate in report.Evaluation.Candidates.OrderByDescending(c => c.MeanFitness).ThenBy(c => report.Individuals[c.Candidate].Id))
                    {
                        if (!Eden.Evolution.Fitness.IsValid(candidate.MeanFitness)) continue;
                        rank++; var id = report.Individuals[candidate.Candidate].Id;
                        if (id.ToString().Contains(search)) { populationRows.Add($"{rank}º · #{id} · média {candidate.MeanFitness:R}"); populationIds.Add(id); populationRanks.Add(rank); }
                    }
            }
            population.Rebuild(); root.Q<Label>("population-detail").text = "Lista estável até atualizar. Nota parcial não define o ranking.\n" + populationRows.Count + " indivíduos encontrados.";
        }
        private void RefreshChartTable()
        {
            var table = root.Q<ListView>("chart-data-table"); table.fixedItemHeight = 28; table.makeItem = () => new Label();
            var rows = hud.Chart.TableRows(); table.bindItem = (e, i) => ((Label)e).text = rows[i]; table.itemsSource = rows; table.Rebuild();
        }
        private void ExportChart()
        {
            try
            {
                var directory = Path.Combine(UnityEngine.Application.persistentDataPath, "AnalysisExports"); Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff") + ".csv");
                File.WriteAllText(path, hud.Chart.Csv()); Message("Dados completos do gráfico exportados: " + path);
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException) { Message("Falha ao exportar: " + error.Message); }
        }
        private string CaptureDraft()
        {
            var draft = new Draft();
            foreach (var e in root.Q("experiment-panel").Query<VisualElement>().ToList())
            {
                if (string.IsNullOrEmpty(e.name)) continue;
                if (e is DropdownField drop) draft.fields.Add(new Value { name = e.name, kind = "choice", value = drop.value });
                else if (e is IntegerField integer) draft.fields.Add(new Value { name = e.name, kind = "int", value = integer.value.ToString(CultureInfo.InvariantCulture) });
                else if (e is FloatField number) draft.fields.Add(new Value { name = e.name, kind = "float", value = number.value.ToString("R", CultureInfo.InvariantCulture) });
                else if (e is TextField text) draft.fields.Add(new Value { name = e.name, kind = "text", value = text.value });
            }
            return JsonUtility.ToJson(draft);
        }
        public void RestoreDraft(string json)
        {
            try
            {
                var draft = JsonUtility.FromJson<Draft>(json); if (draft == null || draft.version != 1) throw new ArgumentException("Preset incompatível.");
                restoring = true;
                foreach (var field in draft.fields)
                {
                    var element = root.Q(field.name);
                    if (element is DropdownField drop && drop.choices.Contains(field.value)) drop.value = field.value;
                    else if (element is IntegerField integer && int.TryParse(field.value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)) integer.value = i;
                    else if (element is FloatField number && float.TryParse(field.value, NumberStyles.Float, CultureInfo.InvariantCulture, out var f)) number.value = f;
                    else if (element is TextField text) text.value = field.value;
                }
                previewPending = true; previewAt = Time.unscaledTime + 0.1f;
            }
            catch (Exception e) when (e is ArgumentException || e is IOException) { Message("Preset recusado: " + e.Message); }
            finally { restoring = false; }
        }
        private void SavePreset()
        {
            try { Directory.CreateDirectory(library.PresetDirectory); var path = CheckpointCatalog.NewPath(library.PresetDirectory, Name); File.WriteAllText(path, CaptureDraft()); Message("Preset salvo: " + path); }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException) { Message("Falha ao salvar preset: " + error.Message); }
        }
        public void Dispose() { if (UnityEngine.Application.isBatchMode) return; PlayerPrefs.SetString("eden.workspace.draft", CaptureDraft()); SaveLayout(); PlayerPrefs.Save(); }
        public void Message(string text) { root.Q<Label>("workspace-message").text = text ?? ""; Display("workspace-message", !string.IsNullOrEmpty(text)); Record(text); }
    }
}
