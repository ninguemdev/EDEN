using System.Text;
using Eden.Application;
using Eden.Brains;
using Eden.Domain;
using Eden.Simulation;

namespace Eden.Presentation
{
    /// <summary>HUD texts (Portuguese, user locale). Only reads state; numbers are display-only.</summary>
    internal static class LabText
    {
        public static string Run(WorldSimulation simulation, FixedStepClock clock, string brainName,
            MetricsRecorder metrics, string runDirectory)
        {
            var world = simulation.World;
            var config = world.Config;
            var text = new StringBuilder();
            text.AppendLine($"Tick {world.Tick}  ·  {world.Tick * (double)config.TickDuration:0.0} s simulados");
            text.Append($"Velocidade {clock.RequestedSpeed:0.###}× pedida, {clock.EffectiveSpeed:0.##}× efetiva");
            text.AppendLine(clock.IsPaused ? "  ·  PAUSADO" : string.Empty);
            if (clock.DroppedTicks > 0)
                text.AppendLine($"Ticks descartados por sobrecarga: {clock.DroppedTicks}");
            if (simulation.CorrectedInputValues > 0)
                text.AppendLine($"Entradas de sensor corrigidas (fora do contrato): {simulation.CorrectedInputValues}");
            text.AppendLine($"População {world.Creatures.Count} de {config.InitialPopulation}  ·  Comida {world.Food.Count}");
            text.AppendLine($"Seed {config.Seed}  ·  Cérebro: {BrainLabel(brainName)}");
            if (metrics.HasSample)
            {
                var sample = metrics.LastSample;
                text.AppendLine($"Último segundo: {sample.FoodEatenInWindow} comidas, " +
                                $"{sample.StarvationDeathsInWindow + sample.OldAgeDeathsInWindow} mortes, " +
                                $"energia média {sample.MeanEnergy:0.0}");
            }

            text.AppendLine($"Total comido: {metrics.TotalFoodEaten}");
            if (simulation.IsExtinct)
                text.AppendLine("EXTINÇÃO: nenhuma criatura viva. R reinicia.");
            text.Append(runDirectory == null ? "Log de métricas: desligado" : "Log: " + runDirectory);
            return text.ToString();
        }

        public static string Creature(CreatureState creature, Observation sensed, CreatureAction action,
            string brainName, SimulationConfig config)
        {
            var text = new StringBuilder();
            text.AppendLine($"Criatura {creature.Id}");
            text.AppendLine(brainName == MlpBrain.BrainName
                ? "Cérebro: rede 12-16-3 com genoma próprio"
                : "Cérebro: " + BrainLabel(brainName));
            text.AppendLine($"Energia {creature.Energy:0.0} / {config.MaxEnergy:0}");
            text.AppendLine($"Idade {creature.AgeTicks * (double)config.TickDuration:0.0} s de {config.MaxAgeSeconds:0} s");
            text.AppendLine($"Comida ingerida {creature.FoodEnergyEaten:0} de energia");
            text.AppendLine($"Posição ({creature.Position.X:0.0}; {creature.Position.Y:0.0}) — só inspeção");
            text.AppendLine();
            text.AppendLine("Sensores (entrada do cérebro)");
            text.AppendLine($"energia {sensed.Energy:0.00}  idade {sensed.Age:0.00}");
            text.AppendLine($"vel. frente {sensed.VelocityForward:0.00}  esq. {sensed.VelocityLeft:0.00}");
            text.AppendLine(sensed.FoodVisible > 0f
                ? $"comida: sen {sensed.FoodSin:0.00} cos {sensed.FoodCos:0.00} prox. {sensed.FoodProximity:0.00}"
                : "comida: nenhuma ao alcance");
            text.AppendLine($"paredes F {sensed.WallFront:0.00} D {sensed.WallRight:0.00} " +
                            $"T {sensed.WallBack:0.00} E {sensed.WallLeft:0.00}");
            text.AppendLine();
            text.AppendLine("Ação mantida");
            text.Append($"propulsão {action.Thrust:0.00}  giro {action.Turn:0.00}  lateral {action.Strafe:0.00}");
            return text.ToString();
        }

        public static string DeadCreature(CreatureId id, CreatureDeath? death)
        {
            if (!death.HasValue)
                return $"Criatura {id}\nnão está mais no mundo";
            var cause = death.Value.Cause == DeathCause.Starvation ? "fome" : "idade";
            return $"Criatura {id}\nmorreu de {cause} no tick {death.Value.Tick}";
        }

        private static string BrainLabel(string brainName)
        {
            switch (brainName)
            {
                case HeuristicBrain.BrainName: return "heurístico (regras fixas, sem memória nem aprendizado)";
                case RandomBrain.BrainName: return "aleatório (ignora os sensores)";
                case MlpBrain.BrainName:
                    return "rede neural 12-16-3, genoma aleatório por criatura (ainda sem evolução; sem memória nem aprendizado)";
                default: return brainName;
            }
        }
    }
}
