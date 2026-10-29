using System;
using System.Collections.Generic;
using Eden.Domain;

namespace Eden.Evolution
{
    /// <summary>
    /// Crossover and compatibility of NEAT genomes, aligned by historical markings, never by position in a list
    /// (Stanley and Miikkulainen, 2002). Connections with the same innovation in both parents match; the others are
    /// disjoint (inside the other parent's range of innovations) or excess (beyond it).
    /// </summary>
    public static class NeatCrossover
    {
        /// <summary>
        /// Child of <paramref name="fitter"/> and <paramref name="other"/>. The child has exactly the fitter parent's
        /// connections, so its structure is always one that already was valid (no cycle can come from mixing two
        /// structures): when the parents tie in fitness the caller still names one of them as the fitter (e.g. the
        /// better ranked). For every matching connection, in innovation order, one coin chooses which parent gives the
        /// weight and enabled state; if the connection is disabled in either parent, a second coin keeps it disabled
        /// with <see cref="NeatRules.DisabledInheritProbability"/> and enables it otherwise. Disjoint and excess
        /// connections come from the fitter parent unchanged, and so do the body genes; eyes are aligned by slot
        /// (<see cref="Reproduction.CrossEyes"/>), drawn after the connections.
        /// </summary>
        public static Genome Cross(Genome fitter, Genome other, NeatRules rules, IRandomSource random)
        {
            var a = RequireNeat(fitter, nameof(fitter));
            var b = RequireNeat(other, nameof(other));
            if (rules == null)
                throw new ArgumentNullException(nameof(rules));
            if (random == null)
                throw new ArgumentNullException(nameof(random));
            if (a.Contract != b.Contract)
                throw new ArgumentException($"Parents use different contracts ({a.Contract.Id}, {b.Contract.Id}).",
                    nameof(other));

            var genes = new NeatConnection[a.Connections.Count];
            for (var i = 0; i < genes.Length; i++)
            {
                var gene = a.Connections[i];
                var match = b.IndexOfInnovation(gene.Innovation);
                if (match >= 0)
                {
                    var partner = b.Connections[match];
                    var disabledInEither = !gene.Enabled || !partner.Enabled;
                    if (random.NextFloat() >= 0.5f)
                        gene = partner;
                    if (disabledInEither)
                        gene = gene.WithEnabled(random.NextFloat() >= rules.DisabledInheritProbability);
                }

                genes[i] = gene;
            }

            return new Genome(new NeatNetwork(a.Contract, genes), fitter.Body,
                Reproduction.CrossEyes(fitter.Eyes, other.Eyes, random));
        }

        /// <summary>
        /// Compatibility distance δ = c1·E/N + c2·D/N + c3·W̄: E excess and D disjoint connections, W̄ the mean
        /// absolute weight difference of matching connections (0 when none match), N the larger connection count, or 1
        /// when both genomes have fewer than <see cref="NeatRules.NormalizationThreshold"/> connections.
        /// </summary>
        public static float Distance(Genome first, Genome second, NeatRules rules)
        {
            var a = RequireNeat(first, nameof(first));
            var b = RequireNeat(second, nameof(second));
            if (rules == null)
                throw new ArgumentNullException(nameof(rules));

            Count(a.Connections, b.Connections, out var excess, out var disjoint, out var matching, out var weightDifference);
            var larger = Math.Max(a.Connections.Count, b.Connections.Count);
            var n = larger < rules.NormalizationThreshold || larger == 0 ? 1f : larger;
            var meanDifference = matching == 0 ? 0f : (float)(weightDifference / matching);
            return rules.ExcessCoefficient * excess / n + rules.DisjointCoefficient * disjoint / n +
                   rules.WeightDifferenceCoefficient * meanDifference;
        }

        // Both lists are in innovation order: a merge walk classifies every connection once.
        private static void Count(IReadOnlyList<NeatConnection> a, IReadOnlyList<NeatConnection> b, out int excess,
            out int disjoint, out int matching, out double weightDifference)
        {
            excess = disjoint = matching = 0;
            weightDifference = 0.0;
            int i = 0, j = 0;
            while (i < a.Count && j < b.Count)
            {
                if (a[i].Innovation == b[j].Innovation)
                {
                    matching++;
                    weightDifference += Math.Abs((double)a[i].Weight - b[j].Weight);
                    i++;
                    j++;
                }
                else
                {
                    disjoint++;
                    if (a[i].Innovation < b[j].Innovation)
                        i++;
                    else
                        j++;
                }
            }

            excess = a.Count - i + (b.Count - j);
        }

        private static NeatNetwork RequireNeat(Genome genome, string name)
        {
            if (genome == null)
                throw new ArgumentNullException(name);
            return genome.Neat ?? throw new ArgumentException($"Only NEAT genomes cross here, got a {genome.Network} one.", name);
        }
    }
}
