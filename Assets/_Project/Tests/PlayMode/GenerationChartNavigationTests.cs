using System.Collections.Generic;
using Eden.Application;
using Eden.Evolution;
using Eden.Presentation;
using NUnit.Framework;

namespace Eden.Tests.PlayMode
{
    public class GenerationChartNavigationTests
    {
        private static GenerationSummary Point(int generation, float value) => GenerationSummary.Restore(generation,
            EvaluationMode.Training, 4, float.IsInfinity(value) ? 0 : 4, value, value, value, value, value, value,
            1, 0.5f, 20, 1, 0.1f, 0.1f, 3, 1, 2, 1);
        private static EvolutionSession Session()
        {
            var session = new EvolutionSession(new EvolutionSettings { PopulationSize = 4, EliteCount = 1 });
            var points = (List<GenerationSummary>)session.Training;
            for (var i = 0; i < 1000; i++) points.Add(Point(i, i == 998 ? float.NegativeInfinity : -2));
            return session;
        }
        [Test]
        public void NegativeValuesRemainNegativeAndInvalidSamplesAreUnavailable()
        {
            var chart = new GenerationChart(); chart.Show(Session());
            Assert.That(chart.Maximum, Is.LessThan(0)); Assert.That(chart.Minimum, Is.LessThan(-2));
            StringAssert.Contains("indisponível", chart.Describe(998));
            StringAssert.Contains("Validação não realizada", chart.Describe(997));
            var csv = chart.Csv().Split('\n');
            StringAssert.Contains("998,,,,,,,0.5,20,0.1", csv[999]);
        }
        [Test]
        public void NewResultsDoNotMoveManualRangeOrHistoricalSelection()
        {
            var session = Session(); var chart = new GenerationChart(); chart.Show(session); chart.ShowAll();
            chart.Zoom(0.2f); chart.Pan(-500); chart.SelectGeneration(42);
            var first = chart.FirstGeneration; var last = chart.LastGeneration;
            ((List<GenerationSummary>)session.Training).Add(Point(1000, 5)); chart.Show(session);
            Assert.That(chart.FirstGeneration, Is.EqualTo(first)); Assert.That(chart.LastGeneration, Is.EqualTo(last));
            Assert.That(chart.SelectedGeneration, Is.EqualTo(42)); Assert.That(chart.Following, Is.False);
            chart.FollowLatest(); Assert.That(chart.LastGeneration, Is.EqualTo(1000));
            Assert.That(chart.SelectedGeneration, Is.EqualTo(42));
        }
    }
}
