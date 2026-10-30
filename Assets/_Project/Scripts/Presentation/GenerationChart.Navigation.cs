using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Eden.Evolution;
using UnityEngine;
using UnityEngine.UIElements;

namespace Eden.Presentation
{
    public sealed partial class GenerationChart
    {
        private float bottom;
        private float first;
        private float last = 1;
        private bool following = true;
        private int metric;
        private int? selected;
        private Vector2? drag;
        private bool moved;
        private readonly HashSet<string> series = new HashSet<string> { "training", "validation" };
        public event Action<string> Selected;
        public float Minimum => bottom;
        public float Maximum => top;
        public float FirstGeneration => first;
        public float LastGeneration => last;
        public int? SelectedGeneration => selected;
        public bool Following => following;
        private void BindNavigation()
        {
            RegisterCallback<WheelEvent>(e => { Zoom(e.delta.y > 0 ? 1.25f : 0.8f); e.StopPropagation(); });
            RegisterCallback<PointerDownEvent>(e => { if (e.button != 0) return; drag = e.localPosition; moved = false; this.CapturePointer(e.pointerId); Focus(); e.StopPropagation(); });
            RegisterCallback<PointerMoveEvent>(e =>
            {
                if (drag.HasValue)
                { var shift = (e.localPosition.x - drag.Value.x) / PlotRect().width * (last - first); if (Math.Abs(shift) > 0.01f) { Pan(-shift); moved = true; } drag = e.localPosition; }
                tooltip = Describe(GenerationAt(e.localPosition.x));
            });
            RegisterCallback<PointerUpEvent>(e => { if (!drag.HasValue) return; if (!moved) SelectGeneration(GenerationAt(e.localPosition.x)); drag = null; this.ReleasePointer(e.pointerId); });
            RegisterCallback<PointerCaptureOutEvent>(_ => drag = null);
            RegisterCallback<KeyDownEvent>(e =>
            { if (e.keyCode == KeyCode.LeftArrow || e.keyCode == KeyCode.RightArrow) { SelectGeneration(Mathf.Clamp((selected ?? (int)first) + (e.keyCode == KeyCode.LeftArrow ? -1 : 1), 0, Math.Max(0, shownTraining - 1))); e.StopPropagation(); } });
        }
        private float Metric(GenerationSummary point) => metric == 1 ? point.MeanSurvivalRate : metric == 2 ? point.MeanFoodEnergy : metric == 3 ? point.WeightStdDev : point.Median;
        private static bool Valid(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private void RefreshRange() { top = ComputeRange(); PlaceLabels(); MarkDirtyRepaint(); }
        public void SetMetric(int value) { metric = Mathf.Clamp(value, 0, 3); RefreshRange(); }
        public void SetSeries(string name, bool visible) { if (visible) series.Add(name); else series.Remove(name); RefreshRange(); }
        public void ShowAll() { following = false; first = 0; last = Math.Max(1, shownTraining - 1); RefreshRange(); }
        public void FollowLatest() { following = true; last = Math.Max(1, shownTraining - 1); first = Math.Max(0, last - 100); RefreshRange(); }
        public void Zoom(float factor)
        {
            var middle = (last + first) / 2; var width = Mathf.Clamp((last - first) * factor, 1, Math.Max(1, shownTraining - 1));
            first = Mathf.Clamp(middle - width / 2, 0, Math.Max(0, shownTraining - 1 - width)); last = first + width; following = false; RefreshRange();
        }
        public void Pan(float generations) { var width = last - first; first = Mathf.Clamp(first + generations, 0, Math.Max(0, shownTraining - 1 - width)); last = first + width; following = false; RefreshRange(); }
        private int GenerationAt(float x) => Mathf.Clamp(Mathf.RoundToInt(first + Mathf.Clamp01((x - PlotRect().xMin) / PlotRect().width) * (last - first)), 0, Math.Max(0, shownTraining - 1));
        public void SelectGeneration(int generation) { selected = generation; Selected?.Invoke(Describe(generation)); MarkDirtyRepaint(); }
        private static string Number(float value) => Valid(value) ? value.ToString("R", CultureInfo.InvariantCulture) : "indisponível";
        public string Describe(int generation)
        {
            if (session == null || generation < 0 || generation >= session.Training.Count) return "Aguardando a primeira geração completa.";
            var point = session.Training[generation];
            var text = $"Consultando g{point.Generation}; execução g{session.Evolution.Generation}.\nMediana {Number(point.Median)} · Q1 {Number(point.LowerQuartile)} · Q3 {Number(point.UpperQuartile)}\nMelhor {Number(point.Best)} · válidos {point.ValidCount}/{point.PopulationSize}\nSobrevivência {point.MeanSurvivalRate:P1} · comida {Number(point.MeanFoodEnergy)} · diversidade {Number(point.WeightStdDev)}";
            foreach (var value in session.Validation) if (value.Generation == generation) return text + $"\nValidação {Number(value.Validation.Median)} · campeão validado {Number(value.ChampionFitness)}";
            return text + "\nValidação não realizada nesta geração.";
        }
        private float ComputeRange()
        {
            var max = float.NegativeInfinity; var min = float.PositiveInfinity;
            void Include(float value) { if (Valid(value)) { max = Mathf.Max(max, value); min = Mathf.Min(min, value); } }
            if (session != null)
            {
                foreach (var summary in session.Training) if (summary.Generation >= first && summary.Generation <= last)
                {
                    if (series.Contains("training")) { Include(Metric(summary)); if (metric == 0) { Include(summary.LowerQuartile); Include(summary.UpperQuartile); } }
                    if (metric == 0 && series.Contains("best")) Include(summary.Best);
                }
                foreach (var point in session.Validation) if (metric == 0 && point.Generation >= first && point.Generation <= last)
                { if (series.Contains("validation")) Include(point.Validation.Median); if (series.Contains("champion")) Include(point.ChampionFitness); }
                if (metric == 0 && series.Contains("references") && !RewardProfiles.IsActive(session.Plan.Profile))
                { if (session.HeuristicBaseline != null) Include(session.HeuristicBaseline.MeanFitness); if (session.RandomBaseline != null) Include(session.RandomBaseline.MeanFitness); }
                if (metric == 0 && series.Contains("stages")) foreach (var point in session.StageMedians) foreach (var value in point) Include(value);
            }
            if (!Valid(min) || !Valid(max)) { min = 0; max = 1; }
            var pad = Mathf.Max((max - min) * 0.1f, Mathf.Max(0.01f, Mathf.Abs(max) * 0.05f)); bottom = min - pad; return max + pad;
        }
        public List<string> TableRows()
        {
            var rows = new List<string>(); if (session == null) return rows;
            foreach (var p in session.Training) if (p.Generation >= first && p.Generation <= last)
                rows.Add($"g{p.Generation} · mediana {Number(p.Median)} · melhor {Number(p.Best)} · sobrevivência {Number(p.MeanSurvivalRate)} · alimento {Number(p.MeanFoodEnergy)} · diversidade {Number(p.WeightStdDev)}");
            return rows;
        }
        public string Csv()
        {
            var text = new StringBuilder("generation,training_median,training_q1,training_q3,training_best,validation_median,champion_validation,survival_fraction,food_energy,weight_stddev\n");
            if (session == null) return text.ToString();
            string Raw(float value) => Valid(value) ? value.ToString("R", CultureInfo.InvariantCulture) : "";
            foreach (var p in session.Training)
            {
                var validation = ""; var champion = ""; foreach (var v in session.Validation) if (v.Generation == p.Generation) { validation = Raw(v.Validation.Median); champion = Raw(v.ChampionFitness); break; }
                text.Append(string.Join(",", p.Generation.ToString(CultureInfo.InvariantCulture), Raw(p.Median), Raw(p.LowerQuartile), Raw(p.UpperQuartile), Raw(p.Best), validation, champion, Raw(p.MeanSurvivalRate), Raw(p.MeanFoodEnergy), Raw(p.WeightStdDev))).Append('\n');
            }
            return text.ToString();
        }
    }
}
