using System;
using Eden.Application;
using Eden.Brains;
using Eden.Domain;
using Eden.Evolution;
using Eden.Persistence;
using UnityEngine;
using UnityEngine.UIElements;

namespace Eden.Presentation
{
    /// <summary>Initial-state preview built from fresh, isolated RNG streams. Never advances a tick.</summary>
    public sealed class ExperimentPreview : VisualElement
    {
        public WorldState World { get; private set; }
        public string Description { get; private set; }
        public ExperimentPreview()
        {
            style.flexGrow = 1; pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }
        public void Build(LabChoice choice, SimulationSettings template, EvolutionSession evolution = null)
        {
            var preset = ExperimentPreset.All[Mathf.Clamp(choice.Preset, 0, ExperimentPreset.All.Count - 1)];
            var scenario = choice.Scenario?.Clone() ?? new ScenarioSettings();
            scenario.SolidBodies |= choice.SolidBodies; scenario.Obstacles = choice.Obstacles;
            if (choice.Mode == LabMode.IsolatedEvolution)
            {
                var settings = preset.Evolution((ulong)choice.Seed);
                settings.Algorithm = choice.Neat || choice.Eyes ? EvolutionAlgorithm.Neat : EvolutionAlgorithm.Mlp;
                settings.Senses = choice.Eyes ? Senses.Eyes : Senses.Legacy;
                settings.EvolvingEyes = choice.EvolvingEyes;
                settings.ExpressedBodyGenes = choice.EvolvingBody ? BodyGeneSet.All : BodyGeneSet.None;
                scenario.Apply(settings); (choice.Profile ?? new ProfileChoice()).Apply(settings, scenario);
                var plan = new EvaluationPlan(settings); var seed = plan.TrainingSeeds(0)[0];
                if (!string.IsNullOrWhiteSpace(choice.GenomeDirectory))
                    foreach (var record in GenomeFile.LoadPool(choice.GenomeDirectory))
                        if (record.Genome.Contract != plan.Contract || (record.Genome.Neat != null) != (settings.Algorithm == EvolutionAlgorithm.Neat))
                            throw new ArgumentException("Pool incompatível com o algoritmo ou contrato sensorial do rascunho.");
                var episode = new IsolatedEpisode(plan, () => new HeuristicBrain(), seed, 0, 0, 0, EvaluationMode.Training,
                    eyes: plan.Contract.HasEyes ? EyeSet.Founder : null);
                World = episode.Simulation.World;
                Description = $"{preset.Population} genomas por geração · 1 criatura por episódio\n" +
                    $"Etapa {plan.Stages[0].Name} · seed de treino {seed}\n{World.Config.ArenaWidth} × {World.Config.ArenaHeight} u · {World.Food.Count} alimentos";
            }
            else
            {
                var settings = preset.SharedWorld(template, (ulong)choice.Seed); scenario.Apply(settings);
                if (choice.Brain == LabBrainKind.Composition)
                {
                    var pool = GenomeFile.LoadPool(choice.GenomeDirectory, out var names);
                    var genomes = new Genome[pool.Length]; var fitness = new float[pool.Length];
                    for (var i = 0; i < pool.Length; i++) { genomes[i] = pool[i].Genome; fitness[i] = pool[i].FitnessFor(Fitness.Profile) ?? 0; }
                    var composition = GenomeComposition.Parse(choice.CompositionText, names, genomes, fitness, choice.CompositionFrozen);
                    composition.Apply(settings); World = composition.CreateWorld(new SimulationConfig(settings));
                }
                else if (choice.Brain == LabBrainKind.Evolved)
                {
                    if (evolution == null) throw new ArgumentException("Inicie uma evolução antes de usar sua população na arena.");
                    var individuals = evolution.LastTraining?.Individuals ?? evolution.Evolution.Individuals;
                    settings.InitialPopulation = individuals.Count; settings.PopulationCap = Math.Max(settings.PopulationCap, individuals.Count);
                    World = WorldFactory.CreateInitial(new SimulationConfig(settings), i => individuals[i].Genome.Body);
                }
                else if (choice.Brain == LabBrainKind.Neural)
                    World = new RandomNetworkSettings(choice.EvolvingBody, choice.Neat, choice.Eyes, choice.EvolvingEyes)
                        .CreateWorld(new SimulationConfig(settings));
                else World = WorldFactory.CreateInitial(new SimulationConfig(settings));
                Description = $"{World.Creatures.Count} criaturas simultâneas · seed {choice.Seed}\n" +
                    $"{World.Config.ArenaWidth} × {World.Config.ArenaHeight} u · {World.Food.Count} alimentos";
            }
            MarkDirtyRepaint();
        }
        public void Clear(string message) { World = null; Description = message; MarkDirtyRepaint(); }
        private void Draw(MeshGenerationContext context)
        {
            if (World == null || contentRect.width < 5 || contentRect.height < 5) return;
            var config = World.Config; var painter = context.painter2D;
            var scale = Mathf.Min((contentRect.width - 20) / config.ArenaWidth, (contentRect.height - 20) / config.ArenaHeight);
            var offset = new Vector2((contentRect.width - config.ArenaWidth * scale) / 2, (contentRect.height - config.ArenaHeight * scale) / 2);
            Vector2 Point(Float2 point) => offset + new Vector2(point.X * scale, (config.ArenaHeight - point.Y) * scale);
            void Rect(float x, float y, float w, float h, Color color)
            {
                painter.fillColor = color; painter.BeginPath(); painter.MoveTo(new Vector2(x, y));
                painter.LineTo(new Vector2(x + w, y)); painter.LineTo(new Vector2(x + w, y + h));
                painter.LineTo(new Vector2(x, y + h)); painter.ClosePath(); painter.Fill();
            }
            Rect(offset.x, offset.y, config.ArenaWidth * scale, config.ArenaHeight * scale, new Color32(23, 46, 52, 255));
            foreach (var o in config.Obstacles) Rect(offset.x + o.MinX * scale, offset.y + (config.ArenaHeight - o.MaxY) * scale,
                (o.MaxX - o.MinX) * scale, (o.MaxY - o.MinY) * scale, new Color32(87, 107, 113, 255));
            foreach (var water in config.Water) { var o = water.Area; Rect(offset.x + o.MinX * scale, offset.y + (config.ArenaHeight - o.MaxY) * scale,
                (o.MaxX - o.MinX) * scale, (o.MaxY - o.MinY) * scale, new Color32(58, 120, 196, 255)); }
            void Dot(Float2 at, float radius, Color color) { painter.fillColor = color; painter.BeginPath(); painter.Arc(Point(at), radius, 0, 360); painter.Fill(); }
            foreach (var food in World.Food) Dot(food.Position, Mathf.Max(1, scale * 0.2f), new Color32(240, 200, 92, 255));
            foreach (var creature in World.Creatures) Dot(creature.Position, Mathf.Max(2, scale * 0.5f), new Color32(94, 214, 168, 255));
        }
    }
}
