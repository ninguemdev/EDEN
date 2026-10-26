using System;
using Eden.Domain;

namespace Eden.Brains
{
    /// <summary>
    /// Per-creature brain state. Never inherited: it starts fresh at birth and in every new episode. It holds the
    /// creature's own random stream for stochastic brains and, for a plastic network, what it learned during this life
    /// (<see cref="Plastic"/>).
    /// </summary>
    public sealed class BrainState
    {
        public BrainState(Pcg32 random)
        {
            Random = random ?? throw new ArgumentNullException(nameof(random));
        }

        public Pcg32 Random { get; }

        /// <summary>Lifetime weight changes of a plastic network; null until such a brain first decides.</summary>
        public PlasticMemory Plastic { get; private set; }

        public PlasticMemory EnsurePlastic() => Plastic ??= new PlasticMemory();
    }
}
