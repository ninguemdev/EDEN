using System;
using System.Globalization;
using Eden.Application;
using Eden.Evolution;
using UnityEngine;
using UnityEngine.UIElements;

namespace Eden.Presentation
{
    /// <summary>
    /// Fitness per generation of an <see cref="EvolutionSession"/> (energy per simulated second, one shared axis):
    /// training median with its interquartile band and training best, validation median and the champion's
    /// validation score, and the random and heuristic brains on the same validation seeds as reference lines, so a
    /// curve that only beats random is visible as such. Redrawn only when a generation or validation is added.
    /// Series colors are the validated dark categorical slots 1–4 of the dataviz palette.
    /// </summary>
    public sealed class GenerationChart : VisualElement
    {
        public static readonly Color TrainingMedianColor = new Color32(0x39, 0x87, 0xE5, 255);
        public static readonly Color TrainingBestColor = new Color32(0xD9, 0x59, 0x26, 255);
        public static readonly Color ValidationMedianColor = new Color32(0x19, 0x9E, 0x70, 255);
        public static readonly Color ChampionColor = new Color32(0xC9, 0x85, 0x00, 255);
        public static readonly Color SearchStageColor = new Color32(0xB0, 0x6A, 0xD8, 255);
        public static readonly Color ResourcesStageColor = new Color32(0x6F, 0xB8, 0x3A, 255);

        private const float LeftGutter = 34f;
        private const float RightGutter = 92f;
        private const float TopGutter = 8f;
        private const float BottomGutter = 16f;
        private const float LineWidth = 2f;
        private const float MarkerRadius = 4f;

        private static readonly Color AxisColor = new Color32(70, 88, 94, 255);
        private static readonly Color ReferenceColor = new Color32(150, 166, 160, 255);
        private static readonly Color InkColor = new Color32(160, 190, 180, 255);

        private readonly Label topLabel;
        private readonly Label bottomLabel;
        private readonly Label firstGenerationLabel;
        private readonly Label lastGenerationLabel;
        private readonly Label heuristicLabel;
        private readonly Label randomLabel;
        private readonly Label emptyLabel;
        private EvolutionSession session;
        private int shownTraining = -1;
        private int shownValidation = -1;
        private float top = 1f;

        public GenerationChart()
        {
            name = "generation-chart";
            pickingMode = PickingMode.Ignore;
            style.flexGrow = 1f;
            topLabel = AddLabel(TextAnchor.UpperRight);
            bottomLabel = AddLabel(TextAnchor.LowerRight);
            firstGenerationLabel = AddLabel(TextAnchor.UpperLeft);
            lastGenerationLabel = AddLabel(TextAnchor.UpperRight);
            heuristicLabel = AddLabel(TextAnchor.MiddleLeft);
            randomLabel = AddLabel(TextAnchor.MiddleLeft);
            emptyLabel = AddLabel(TextAnchor.MiddleCenter);
            emptyLabel.text = "aguardando a primeira geração completa";
            generateVisualContent += Generate;
            RegisterCallback<GeometryChangedEvent>(_ => PlaceLabels());
        }

        /// <summary>Number of times the chart was drawn; grows only when new results arrive.</summary>
        public int RepaintCount { get; private set; }

        /// <summary>Generations currently plotted.</summary>
        public int PlottedGenerations => shownTraining < 0 ? 0 : shownTraining;

        /// <summary>Plots the session; does nothing when it has no new results since the last call.</summary>
        public void Show(EvolutionSession data)
        {
            var training = data?.Training.Count ?? 0;
            var validation = data?.Validation.Count ?? 0;
            if (data == session && training == shownTraining && validation == shownValidation)
                return;
            session = data;
            shownTraining = training;
            shownValidation = validation;
            top = ComputeTop();
            PlaceLabels();
            MarkDirtyRepaint();
        }

        private float ComputeTop()
        {
            var max = 0f;
            if (session != null)
            {
                foreach (var summary in session.Training)
                    max = Mathf.Max(max, Finite(summary.Best));
                foreach (var point in session.Validation)
                    max = Mathf.Max(max, Mathf.Max(Finite(point.Validation.Median), Finite(point.ChampionFitness)));
                if (session.HeuristicBaseline != null)
                    max = Mathf.Max(max, Finite(session.HeuristicBaseline.MeanFitness));
                if (session.RandomBaseline != null)
                    max = Mathf.Max(max, Finite(session.RandomBaseline.MeanFitness));
            }

            return max <= 0f ? 1f : max * 1.1f;
        }

        private Label AddLabel(TextAnchor anchor)
        {
            var label = new Label { pickingMode = PickingMode.Ignore };
            label.style.position = Position.Absolute;
            label.style.fontSize = 11f;
            label.style.unityTextAlign = anchor;
            label.style.color = InkColor;
            label.style.paddingLeft = 0f;
            label.style.paddingRight = 0f;
            label.style.marginLeft = 0f;
            label.style.marginRight = 0f;
            Add(label);
            return label;
        }

        private void PlaceLabels()
        {
            var plot = PlotRect();
            var generations = shownTraining;
            emptyLabel.style.display = generations > 0 ? DisplayStyle.None : DisplayStyle.Flex;
            emptyLabel.style.left = plot.xMin;
            emptyLabel.style.width = plot.width;
            emptyLabel.style.top = plot.center.y - 7f;

            SetLabel(topLabel, generations > 0, top.ToString("0.0", CultureInfo.CurrentCulture), 0f, plot.yMin - 2f,
                LeftGutter - 4f);
            SetLabel(bottomLabel, generations > 0, "0", 0f, plot.yMax - 12f, LeftGutter - 4f);
            SetLabel(firstGenerationLabel, generations > 0, "g0", plot.xMin, plot.yMax + 2f, 40f);
            SetLabel(lastGenerationLabel, generations > 1, "g" + (generations - 1), plot.xMax - 40f, plot.yMax + 2f, 40f);

            var heuristic = session?.HeuristicBaseline;
            var random = session?.RandomBaseline;
            SetLabel(heuristicLabel, heuristic != null, heuristic == null ? null :
                "heurístico " + heuristic.MeanFitness.ToString("0.00", CultureInfo.CurrentCulture),
                plot.xMax + 4f, (heuristic == null ? 0f : Y(plot, heuristic.MeanFitness)) - 7f, RightGutter - 4f);
            var randomY = random == null ? 0f : Y(plot, random.MeanFitness) - 7f;
            if (heuristic != null && Mathf.Abs(randomY - (Y(plot, heuristic.MeanFitness) - 7f)) < 14f)
                randomY = Y(plot, heuristic.MeanFitness) + 7f;
            SetLabel(randomLabel, random != null, random == null ? null :
                "aleatório " + random.MeanFitness.ToString("0.00", CultureInfo.CurrentCulture),
                plot.xMax + 4f, randomY, RightGutter - 4f);
        }

        private static void SetLabel(Label label, bool visible, string text, float left, float topPosition, float width)
        {
            label.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (!visible)
                return;
            label.text = text;
            label.style.left = left;
            label.style.top = topPosition;
            label.style.width = width;
            label.style.height = 14f;
        }

        private Rect PlotRect()
        {
            var rect = contentRect;
            return Rect.MinMaxRect(LeftGutter, TopGutter, Mathf.Max(LeftGutter + 1f, rect.width - RightGutter),
                Mathf.Max(TopGutter + 1f, rect.height - BottomGutter));
        }

        private float X(Rect plot, int generation) =>
            plot.xMin + plot.width * (shownTraining <= 1 ? 0.5f : generation / (float)(shownTraining - 1));

        private float Y(Rect plot, float fitness) => plot.yMax - plot.height * Mathf.Clamp01(Finite(fitness) / top);

        private void Generate(MeshGenerationContext context)
        {
            var plot = PlotRect();
            if (session == null || shownTraining <= 0 || plot.width < 4f || plot.height < 4f)
                return;

            RepaintCount++;
            var painter = context.painter2D;
            painter.lineWidth = 1f;
            painter.strokeColor = AxisColor;
            painter.BeginPath();
            painter.MoveTo(new Vector2(plot.xMin, plot.yMin));
            painter.LineTo(new Vector2(plot.xMin, plot.yMax));
            painter.LineTo(new Vector2(plot.xMax, plot.yMax));
            painter.Stroke();

            DrawReference(painter, plot, session.HeuristicBaseline);
            DrawReference(painter, plot, session.RandomBaseline);

            var training = session.Training;
            var band = new Color(TrainingMedianColor.r, TrainingMedianColor.g, TrainingMedianColor.b, 0.25f);
            if (training.Count > 1)
            {
                painter.fillColor = band;
                painter.BeginPath();
                painter.MoveTo(new Vector2(X(plot, 0), Y(plot, training[0].UpperQuartile)));
                for (var g = 1; g < training.Count; g++)
                    painter.LineTo(new Vector2(X(plot, g), Y(plot, training[g].UpperQuartile)));
                for (var g = training.Count - 1; g >= 0; g--)
                    painter.LineTo(new Vector2(X(plot, g), Y(plot, training[g].LowerQuartile)));
                painter.ClosePath();
                painter.Fill();
            }

            DrawSeries(painter, plot, training.Count, g => training[g].Generation, g => training[g].Median,
                TrainingMedianColor, false);
            DrawSeries(painter, plot, training.Count, g => training[g].Generation, g => training[g].Best,
                TrainingBestColor, false);
            var validation = session.Validation;
            DrawSeries(painter, plot, validation.Count, v => validation[v].Generation, v => validation[v].Validation.Median,
                ValidationMedianColor, true);
            DrawSeries(painter, plot, validation.Count, v => validation[v].Generation, v => validation[v].ChampionFitness,
                ChampionColor, true);

            // Stage medians (S1 search, S2 resources) of a profile with stages, for the generations this session saw.
            var stages = session.StageMedians;
            if (stages.Count == 0)
                return;
            var offset = training.Count - stages.Count;
            for (var k = 0; k < session.Plan.Stages.Count; k++)
            {
                var stage = k;
                DrawSeries(painter, plot, stages.Count, i => training[offset + i].Generation, i => stages[i][stage],
                    session.Plan.Stages[stage].IsSearch ? SearchStageColor : ResourcesStageColor, false);
            }
        }

        private void DrawReference(Painter2D painter, Rect plot, CandidateEvaluation baseline)
        {
            if (baseline == null)
                return;
            var y = Y(plot, baseline.MeanFitness);
            painter.lineWidth = 1f;
            painter.strokeColor = ReferenceColor;
            const float dash = 6f;
            for (var x = plot.xMin; x < plot.xMax; x += 2f * dash)
            {
                painter.BeginPath();
                painter.MoveTo(new Vector2(x, y));
                painter.LineTo(new Vector2(Mathf.Min(x + dash, plot.xMax), y));
                painter.Stroke();
            }
        }

        private void DrawSeries(Painter2D painter, Rect plot, int count, Func<int, int> generationAt,
            Func<int, float> valueAt, Color color, bool markers)
        {
            if (count == 0)
                return;
            painter.lineWidth = LineWidth;
            painter.strokeColor = color;
            painter.lineJoin = LineJoin.Round;
            if (count > 1)
            {
                painter.BeginPath();
                painter.MoveTo(Point(plot, generationAt(0), valueAt(0)));
                for (var i = 1; i < count; i++)
                    painter.LineTo(Point(plot, generationAt(i), valueAt(i)));
                painter.Stroke();
            }

            if (!markers && count > 1)
                return;
            painter.fillColor = color;
            for (var i = 0; i < count; i++)
            {
                painter.BeginPath();
                painter.Arc(Point(plot, generationAt(i), valueAt(i)), MarkerRadius, 0f, 360f);
                painter.Fill();
            }
        }

        private Vector2 Point(Rect plot, int generation, float fitness) => new Vector2(X(plot, generation), Y(plot, fitness));

        // Invalid fitness (-inf) is drawn at zero; the text report shows the real value.
        private static float Finite(float value) => float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;

        /// <summary>Legend in text ink with a colored mark per series (identity never by color alone).</summary>
        public static string Legend() =>
            Mark(TrainingMedianColor) + " mediana treino (faixa: quartis)   " + Mark(TrainingBestColor) + " melhor treino\n" +
            Mark(ValidationMedianColor) + " mediana validação   " + Mark(ChampionColor) + " campeão na validação   ┄ referências\n" +
            Mark(SearchStageColor) + " S1 busca   " + Mark(ResourcesStageColor) + " S2 recursos (só com perfil de etapas)";

        private static string Mark(Color color) => $"<color=#{ColorUtility.ToHtmlStringRGB(color)}>■</color>";
    }
}
