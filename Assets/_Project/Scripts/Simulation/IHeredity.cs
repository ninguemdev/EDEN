using Eden.Domain;

namespace Eden.Simulation
{
    /// <summary>
    /// What a child inherits from its parent at birth. The world decides who reproduces and moves the energy; heredity
    /// only gives the child's body genes (and keeps whatever else it inherits, such as a brain genome). Called once per
    /// accepted birth, in parent id order, before the child is added to the world; the child then gets the eyes of its
    /// genome (<see cref="EyesOf"/>). With solid bodies a conceived child may find no room and not be born: the world then
    /// calls <see cref="Cancel"/>, and the same id may be conceived again by another parent. Without one, a child
    /// expresses exactly its parent's body genes and eyes.
    /// </summary>
    public interface IHeredity
    {
        BodyGenes Conceive(CreatureState parent, CreatureId child);

        /// <summary>Forgets a child just conceived that is not born after all (no room for it).</summary>
        void Cancel(CreatureId child);

        /// <summary>The eyes a creature's inherited genome gives it; null for the legacy senses.</summary>
        EyeSet EyesOf(CreatureId creature);
    }
}
