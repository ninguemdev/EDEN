using Eden.Domain;
using Eden.Evolution;
using Eden.Simulation;

namespace Eden.Application
{
    /// <summary>Deterministic random founders and the traits their descendants may mutate.</summary>
    public sealed class RandomNetworkSettings
    {
        public RandomNetworkSettings(bool evolvingBody = false, bool neat = false, bool eyes = false,
            bool evolvingEyes = false)
        {
            EvolvingBody = evolvingBody;
            EvolvingEyes = evolvingEyes;
            Eyes = eyes || evolvingEyes;
            Neat = neat || Eyes;
        }

        public bool EvolvingBody { get; }
        public bool Neat { get; }
        public bool Eyes { get; }
        public bool EvolvingEyes { get; }
        public BodyGeneSet BodyGenes => EvolvingBody ? BodyGeneSet.All : BodyGeneSet.None;

        public Genome Founder(SimulationConfig config, CreatureId id)
        {
            var random = SeedDerivation.CreateCreatureStream(config.Seed, RandomStream.Genome, id);
            var genome = Neat
                ? new Genome(NeatNetwork.CreateMinimal(Eyes ? Sensors.EyeContract(config) : NeuralContract.Legacy,
                    random, InitialGenomes.WeightLimit), Eden.Domain.BodyGenes.Neutral, Eyes ? EyeSet.Founder : null)
                : Genome.CreateRandom(NetworkTopology.Mlp12x16x3, random, InitialGenomes.WeightLimit);
            var body = Eden.Domain.BodyGenes.CreateRandom(random).Masked(BodyGenes);
            return genome.WithBody(body);
        }

        public SharedGenomes CreateGenomes(SimulationConfig config) =>
            new SharedGenomes(config, id => Founder(config, id), BodyGenes,
                eyeRules: EvolvingEyes ? new NeatRules(new NeatSettings()).EyeMutation : default);

        public WorldState CreateWorld(SimulationConfig config)
        {
            var world = WorldFactory.CreateInitial(config, i => Founder(config, new CreatureId(i + 1)).Body);
            if (Eyes)
                foreach (var creature in world.Creatures)
                    creature.Eyes = EyeSet.Founder;
            return world;
        }
    }
}
