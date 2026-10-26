using Eden.Domain;

namespace Eden.Simulation
{
    /// <summary>
    /// What a child inherits from its parent at birth. The world decides who reproduces and moves the energy; heredity
    /// only gives the child's body genes (and keeps whatever else it inherits, such as a brain genome). Called once per
    /// accepted birth, in parent id order, before the child is added to the world. Without one, a child expresses
    /// exactly its parent's body genes.
    /// </summary>
    public interface IHeredity
    {
        BodyGenes Conceive(CreatureState parent, CreatureId child);
    }
}
