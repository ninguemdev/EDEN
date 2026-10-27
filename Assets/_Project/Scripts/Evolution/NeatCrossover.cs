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
        /// Compatibility distance δ = c1·E/N + c2·D/N + c3·W̄ (+ c4·O): E excess and D disjoint connections, W̄ the mean
        /// absolute weight difference of matching connections (0 when none match), N the larger connection count, or 1
        /// when both genomes have fewer than <see cref="NeatRules.NormalizationThreshold"/> connections. O is the eye
        /// difference (<see cref="EyeDifference"/>), weighted by <see cref="NeatRules.EyeCompatibilityCoefficient"/>: an
        /// experimental extension of the EDEN, off (0) by default, when δ is the purely neural distance of NEAT.
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
            var neural = rules.ExcessCoefficient * excess / n + rules.DisjointCoefficient * disjoint / n +
                         rules.WeightDifferenceCoefficient * meanDifference;
            if (rules.EyeCompatibilityCoefficient == 0f || first.Eyes == null || second.Eyes == null)
                return neural;
            return neural + rules.EyeCompatibilityCoefficient * EyeDifference(first.Eyes, second.Eyes);
        }

        /// <summary>
        /// How different two sets of eyes are, slot by slot (eyes are compared by identity, never by angle): 1 for a slot
        /// only one set fills, and for a slot both fill the mean of their normalized differences in orientation (the
        /// shorter way around, / 180°), aperture (/ its range of limits) and range (/ its range of limits). Between 0 and
        /// <see cref="EyeSet.MaxEyes"/>.
        /// </summary>
        public static float EyeDifference(EyeSet a, EyeSet b)
        {
            if (a == null)
                throw new ArgumentNullException(nameof(a));
            if (b == null)
                throw new ArgumentNullException(nameof(b));
            var total = 0f;
            for (var slot = 0; slot < EyeSet.MaxEyes; slot++)
            {
                if (a.Has(slot) != b.Has(slot))
                {
                    total += 1f;
                    continue;
                }

                if (!a.Has(slot))
                    continue;
                var x = a[slot];
                var y = b[slot];
                var turn = Math.Abs(x.Orientation - y.Orientation);
                if (turn > MathF.PI)
                    turn = 2f * MathF.PI - turn;
                total += (turn / MathF.PI + Math.Abs(x.Aperture - y.Aperture) / (Eye.MaxAperture - Eye.MinAperture) +
                          Math.Abs(x.Range - y.Range) / (Eye.MaxRange - Eye.MinRange)) / 3f;
            }

            return total;
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
