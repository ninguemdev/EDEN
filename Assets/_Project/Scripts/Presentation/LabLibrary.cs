using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Eden.Persistence;
using UnityEngine;
using UnityEngine.UIElements;

namespace Eden.Presentation
{
    /// <summary>Async file discovery with a virtualized list; selection is read-only until Load is requested.</summary>
    public sealed class LabLibrary
    {
        private readonly LabRunner runner;
        private readonly VisualElement root;
        private readonly ListView list;
        private readonly List<CheckpointCatalogEntry> visible = new List<CheckpointCatalogEntry>();
        private CheckpointCatalogEntry[] entries = Array.Empty<CheckpointCatalogEntry>();
        private Task<CheckpointCatalogEntry[]> scan;
        private Task<string> import;
        private string category = "checkpoints";
        private string scanningCategory;
        public string Category => category;
        public Action<string> LoadRequested;
        public Action<string> PresetRequested;
        public string PresetDirectory => Path.Combine(UnityEngine.Application.persistentDataPath, "LabPresets");
        public LabLibrary(LabRunner runner, VisualElement root)
        {
            this.runner = runner; this.root = root; list = root.Q<ListView>("library-list");
            list.fixedItemHeight = 46; list.selectionType = SelectionType.Single;
            list.makeItem = () => new Label(); list.bindItem = (element, i) => ((Label)element).text = visible[i].ToString();
            list.itemsSource = visible;
            list.selectionChanged += _ => Select();
            root.Q<TextField>("library-search").RegisterValueChangedCallback(_ => Filter());
            root.Q<Button>("refresh-library").clicked += Refresh;
            foreach (var name in new[] { "checkpoints", "genomes", "presets", "results" })
            { var value = name; root.Q<Button>("library-" + name).clicked += () => Open(value); }
            root.Q<Button>("load-selected").clicked += () => { if (Selected?.CanLoad == true) LoadRequested?.Invoke(Selected.Path); };
            root.Q<Button>("use-library-item").clicked += () =>
            {
                var selected = Selected; if (selected == null) return;
                if (category == "presets") PresetRequested?.Invoke(selected.Path);
                else if (category == "genomes") { runner.Hud.SetSeedGenomesDirectory(Path.GetDirectoryName(selected.Path)); Status("Pasta aplicada ao rascunho de configuração."); }
                else if (category == "results") { GUIUtility.systemCopyBuffer = selected.Path; Status("Caminho do resultado copiado."); }
            };
            root.Q<Button>("import-checkpoint").clicked += () =>
            {
                if (import != null) return;
                var path = root.Q<TextField>("import-path").value; var directory = runner.CheckpointDirectory;
                Status("Conferindo e importando o arquivo…"); import = Task.Run(() => CheckpointCatalog.Import(path, directory));
            };
        }
        private CheckpointCatalogEntry Selected => list.selectedIndex >= 0 && list.selectedIndex < visible.Count ? visible[list.selectedIndex] : null;
        public void Open(string value = "checkpoints") { category = value; Refresh(); }
        public void Refresh()
        {
            if (scan != null) return;
            var selectedCategory = category; scanningCategory = category;
            var directory = category == "checkpoints" ? runner.CheckpointDirectory : category == "genomes" ? runner.GenomeExportDirectory :
                category == "presets" ? PresetDirectory : Path.Combine(UnityEngine.Application.persistentDataPath, "EvolutionRuns");
            root.Q<Label>("library-location").text = directory;
            Status("Buscando arquivos…");
            scan = Task.Run(() =>
            {
                if (selectedCategory == "checkpoints") return CheckpointCatalog.List(directory);
                if (!Directory.Exists(directory)) return Array.Empty<CheckpointCatalogEntry>();
                var paths = selectedCategory == "results" ? Directory.GetDirectories(directory) : Directory.GetFiles(directory, "*.json");
                return paths.Select(path => new CheckpointCatalogEntry(path, Path.GetFileNameWithoutExtension(path),
                    File.GetLastWriteTimeUtc(path), selectedCategory == "genomes" ? "Genoma: cria indivíduos novos; não retoma uma sessão.\n" + path :
                        selectedCategory == "presets" ? "Preset: recupera o rascunho do formulário, sem iniciar treino.\n" + path :
                        "Resultados arquivados: métricas e cenários da execução. Não são um checkpoint.\n" + path))
                    .OrderByDescending(e => e.SavedUtc).ToArray();
            });
        }
        public void Tick()
        {
            if (import?.IsCompleted == true)
            {
                if (import.IsFaulted) Status("Importação recusada: " + import.Exception.GetBaseException().Message);
                else { Status("Checkpoint importado; arquivo original preservado."); Refresh(); }
                import = null;
            }
            if (scan?.IsCompleted != true) return;
            var completed = scan; scan = null;
            if (scanningCategory != category) { Refresh(); return; }
            if (completed.IsFaulted) { Status("Não foi possível listar: " + completed.Exception.GetBaseException().Message); return; }
            entries = completed.Result; Filter(); Status(entries.Length == 0 ? "Nenhum item encontrado. Importe um arquivo ou crie uma gravação." : $"{entries.Length} itens. Selecionar não altera a sessão.");
        }
        private void Filter()
        {
            var selected = Selected?.Path; var search = root.Q<TextField>("library-search").value ?? "";
            visible.Clear(); visible.AddRange(entries.Where(e => (e.Name + " " + e.Path + " " + e.Details).IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0));
            list.Rebuild(); var index = visible.FindIndex(e => e.Path == selected);
            if (index >= 0) list.SetSelection(index); else { list.ClearSelection(); Select(); }
        }
        private void Select()
        {
            root.Q<Label>("library-detail").text = Selected?.Details ?? "Selecione uma gravação. O arquivo será conferido completamente ao carregar.";
            root.Q<Button>("load-selected").SetEnabled(category == "checkpoints" && Selected?.CanLoad == true);
            root.Q<Button>("use-library-item").SetEnabled(category != "checkpoints" && Selected != null);
        }
        private void Status(string text) => root.Q<Label>("library-status").text = text;
    }
}
