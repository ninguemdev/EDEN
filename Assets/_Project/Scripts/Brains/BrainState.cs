using System;
using Eden.Domain;

namespace Eden.Brains
{
    /// <summary>
    /// Per-creature brain state. Never inherited: it starts fresh at birth. Today it only holds the creature's
    /// own random stream for stochastic brains; memory and plasticity traces are a later experiment (M7).
    /// </summary>
    public sealed class BrainState
    {
        public BrainState(Pcg32 random)
        {
            Random = random ?? throw new ArgumentNullException(nameof(random));
        }

        public Pcg32 Random { get; }
    }
}
