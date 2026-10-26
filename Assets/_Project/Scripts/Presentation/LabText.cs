using System;
using System.Collections.Generic;
using System.Text;
using Eden.Application;
using Eden.Brains;
using Eden.Domain;
using Eden.Evolution;
using Eden.Simulation;

namespace Eden.Presentation
{
    /// <summary>HUD texts (Portuguese, user locale). Only reads state; numbers are display-only.</summary>
    internal static class LabText
    {
        private const int MaxAncestors = 6;

        /// <summary>Shared world summary; <paramref name="evolvedSource"/> names the evolved population, if used.</summary>
        public static string Run(WorldSimulation simulation, FixedStepClock clock, string brainName,
            ExperimentPreset preset, MetricsRecorder metrics, string runDirectory, string evolvedSource)
        {
            var world = simulation.World;
            var config = world.Config;
            var text = new StringBuilder();
            text.AppendLine("Mundo compartilhado: todos no mesmo mundo, disputando a comida (só observação, sem seleção)");
            text.AppendLine($"Tick {world.Tick}  ·  {world.Tick * (double)config.TickDuration:0.0} s simulados");
            AppendSpeed(text, clock);
            if (simulation.CorrectedInputValues > 0)
                text.AppendLine($"Entradas de sensor corrigidas (fora do contrato): {simulation.CorrectedInputValues}");
            if (config.Reproduction)
            {
                var generation = 0;
                for (var i = 0; i < world.Creatures.Count; i++)
                    generation = Math.Max(generation, world.Creatures[i].Generation);
                text.AppendLine($"População {world.Creatures.Count} (teto {config.PopulationCap})  ·  Comida {world.Food.Count}");
                text.AppendLine($"Reprodução local ligada: {world.NextCreatureId - 1 - config.InitialPopulation} nascimentos, " +
                                $"geração genealógica máxima {generation}; sem reposição se todos morrerem");
            }
            else
            {
                text.AppendLine($"População {world.Creatures.Count} de {config.InitialPopulation}  ·  Comida {world.Food.Count}");
            }
            text.AppendLine($"Preset {PresetLabel(preset)}  ·  Seed {config.Seed}");
            text.AppendLine(evolvedSource == null
                ? "Cérebro: " + BrainLabel(brainName)
                : $"Cérebro: rede 12-16-3 da população evoluída ({evolvedSource}), um indivíduo por criatura; " +
                  "sem memória nem aprendizado");
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

        /// <summary>Evolution summary: what is being evaluated now, the last generation and the validation.</summary>
        public static string Evolution(EvolutionSession session, FixedStepClock clock, ExperimentPreset preset,
            string runDirectory)
        {
            var plan = session.Plan;
            var job = session.Job;
            var episode = job.Current;
            var text = new StringBuilder();
            text.AppendLine("Evolução por gerações · AVALIAÇÃO ISOLADA");
            text.AppendLine($"Cada genoma sozinho no mesmo mundo de cada seed ({plan.EpisodeSeconds:0} s); sem competição. " +
                            "Seleção artificial por torneio na fitness de treino, não seleção natural.");
            text.AppendLine($"Geração {job.Generation}  ·  {PhaseLabel(session.Phase)}: " +
                            $"{CandidateLabel(session)} ({episode.Candidate + 1}/{job.CandidateCount}), " +
                            $"seed {episode.SeedIndex + 1}/{job.SeedCount}");
            text.AppendLine($"Episódio {episode.Tick * (double)episode.Simulation.World.Config.TickDuration:0.0} s " +
                            $"de {plan.EpisodeSeconds:0} s  ·  episódios {job.CompletedEpisodes}/{job.TotalEpisodes}");
            AppendSpeed(text, clock);
            text.AppendLine($"Preset {PresetLabel(preset)}  ·  Seed do experimento {plan.ExperimentSeed}  ·  " +
                            $"validação a cada {session.ValidationInterval} gerações em {plan.ValidationSeedCount} seeds separadas");
            text.AppendLine(plan.ExpressedBodyGenes == BodyGeneSet.None
                ? "Corpo fixo (controle): todos com o corpo de referência"
                : "Corpo evolutivo: tamanho, velocidade, sensor e eficiência herdados e mutados, cada um com custo");

            var last = session.LastTraining?.Summary;
            if (last != null)
                text.AppendLine($"Treino g{last.Generation}: melhor {last.Best:0.00}  ·  mediana {last.Median:0.00}  ·  " +
                                $"sobrevivência {last.MeanSurvivalRate:0%}  ·  diversidade {last.WeightStdDev:0.000}");
            else
                text.AppendLine("Treino: nenhuma geração completa ainda");

            var validation = session.Validation;
            if (validation.Count > 0)
            {
                var point = validation[validation.Count - 1];
                var heuristic = session.HeuristicBaseline.MeanFitness;
                var random = session.RandomBaseline.MeanFitness;
                text.AppendLine($"Validação g{point.Generation}: mediana {point.Validation.Median:0.00}  ·  " +
                                $"campeão #{point.ChampionId} {point.ChampionFitness:0.00}");
                text.AppendLine($"Referências na validação: aleatório {random:0.00}  ·  heurístico {heuristic:0.00}");
                text.AppendLine($"Campeão {Compare(point.ChampionFitness, random, "aleatório")} e " +
                                $"{Compare(point.ChampionFitness, heuristic, "heurístico")}");
            }
            else
            {
                text.AppendLine("Validação: ainda não feita (sem comparação com as referências)");
            }

            text.Append(runDirectory == null ? "Log da evolução: desligado" : "Log: " + runDirectory);
            return text.ToString();
        }

        /// <summary>Which isolated episode the selected creature is in.</summary>
        public static string Episode(EvolutionSession session, SimulationConfig config)
        {
            var job = session.Job;
            var episode = job.Current;
            return $"Episódio isolado: {PhaseLabel(session.Phase)} · geração {job.Generation} · " +
                   $"seed {episode.SeedIndex + 1} de {job.SeedCount}\n" +
                   $"{episode.Tick * (double)config.TickDuration:0.0} s de {session.Plan.EpisodeSeconds:0} s · " +
                   (session.CurrentIndividual == null
                       ? "cérebro de referência"
                       : "fitness = comida ingerida / duração do episódio");
        }

        /// <summary>Lineage of an individual as seen from the generation it is being evaluated in.</summary>
        public static string Lineage(Individual individual, Lineage lineage, int generation)
        {
            var text = new StringBuilder();
            text.AppendLine("Linhagem");
            text.AppendLine($"Indivíduo #{individual.Id} · nasceu na geração {individual.BirthGeneration}");
            if (individual.ParentIds.Count == 0)
                text.AppendLine("Fundador: genoma aleatório da geração 0, sem pais");
            else
                text.AppendLine($"Pai #{individual.ParentIds[0]} · {individual.MutatedWeights} pesos mutados ao nascer");
            if (individual.BirthGeneration < generation)
                text.AppendLine($"Elite: passou sem nova mutação para a geração {generation} e é avaliado de novo");
            text.Append("Ancestrais: " + Ancestors(lineage.Ancestry(individual.Id)));
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
            text.AppendLine($"Energia {creature.Energy:0.0} / {creature.Body.MaxEnergy:0.#}");
            text.AppendLine($"Idade {creature.AgeTicks * (double)config.TickDuration:0.0} s de {config.MaxAgeSeconds:0} s");
            text.AppendLine($"Comida ingerida {creature.FoodEnergyEaten:0} de energia");
            text.AppendLine($"Posição ({creature.Position.X:0.0}; {creature.Position.Y:0.0}) — só inspeção");
            AppendGenealogy(text, creature, config);
            text.AppendLine();
            AppendBody(text, creature, config);
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

        private static void AppendGenealogy(StringBuilder text, CreatureState creature, SimulationConfig config)
        {
            if (creature.Parent.HasValue)
                text.AppendLine($"Nasceu no mundo: filha de {creature.Parent.Value}, geração {creature.Generation}");
            else if (config.Reproduction)
                text.AppendLine("Fundadora (geração 0)");
            if (!config.Reproduction)
                return;
            var ticks = Math.Max(config.MaturityTicks - creature.AgeTicks, creature.ReproductionCooldownTicks);
            var needed = config.ReproductionEnergyFraction * creature.Body.MaxEnergy;
            text.AppendLine(ticks > 0
                ? $"Reprodução: possível em {ticks * (double)config.TickDuration:0.0} s (maturidade/intervalo)"
                : creature.Energy < needed
                    ? $"Reprodução: precisa de {needed:0} de energia"
                    : "Reprodução: pronta");
        }

        // Each trait with its factor of the reference, then what the body spends per second at rest and at full push.
        private static void AppendBody(StringBuilder text, CreatureState creature, SimulationConfig config)
        {
            var body = creature.Body;
            if (creature.Genes.IsNeutral)
            {
                text.AppendLine("Corpo de referência (genes neutros)");
            }
            else
            {
                var genes = creature.Genes;
                text.AppendLine($"Corpo (genes tam. {genes.Size:+0.00;-0.00;0} vel. {genes.Speed:+0.00;-0.00;0} " +
                                $"sensor {genes.Sensor:+0.00;-0.00;0} efic. {genes.Efficiency:+0.00;-0.00;0})");
                text.AppendLine($"raio {body.EatRadius:0.00} (×{body.EatRadius / config.EatRadius:0.00})  " +
                                $"capacidade {body.MaxEnergy:0} (×{body.MaxEnergy / config.MaxEnergy:0.00})");
                text.AppendLine($"vel. máx. {body.MaxSpeed:0.0} (×{body.MaxSpeed / config.MaxSpeed:0.00})  " +
                                $"aceleração {body.MaxAcceleration:0.0} (×{body.MaxAcceleration / config.MaxAcceleration:0.00})");
                text.AppendLine($"alcance {body.SensorRange:0.0} (×{body.SensorRange / config.SensorRange:0.00})");
            }

            var resting = body.MetabolismPerSecond + body.SensorCostPerSecond;
            text.AppendLine($"Gasto/s: repouso {resting:0.00} (metabolismo {body.MetabolismPerSecond:0.00} + sensor " +
                            $"{body.SensorCostPerSecond:0.00}), propulsão máxima +{body.MovementCostPerSecond:0.00}");
        }

        public static string DeadCreature(CreatureId id, CreatureDeath? death)
        {
            if (!death.HasValue)
                return $"Criatura {id}\nnão está mais no mundo";
            var cause = death.Value.Cause == DeathCause.Starvation ? "fome" : "idade";
            return $"Criatura {id}\nmorreu de {cause} no tick {death.Value.Tick}";
        }

        private static void AppendSpeed(StringBuilder text, FixedStepClock clock)
        {
            text.Append(clock.IsUnpaced
                ? $"Velocidade máxima ({LabRunner.MaxSpeedBudgetSeconds * 1000:0} ms de ticks por quadro): " +
                  $"{clock.EffectiveSpeed:0.#}× efetiva"
                : $"Velocidade {clock.RequestedSpeed:0.###}× pedida, {clock.EffectiveSpeed:0.##}× efetiva");
            text.AppendLine(clock.IsPaused ? "  ·  PAUSADO" : string.Empty);
            if (clock.DroppedTicks > 0)
                text.AppendLine($"Ticks descartados por sobrecarga: {clock.DroppedTicks}");
        }

        private static string Ancestors(IReadOnlyList<long> chain)
        {
            var text = new StringBuilder();
            for (var i = 0; i < chain.Count && i < MaxAncestors; i++)
                text.Append(i == 0 ? "#" : " ← #").Append(chain[i]);
            if (chain.Count > MaxAncestors)
                text.Append($" ← … ({chain.Count - MaxAncestors} mais até o fundador #{chain[chain.Count - 1]})");
            return text.ToString();
        }

        private static string Compare(float value, float reference, string name) =>
            value > reference ? "supera o " + name : value < reference ? "fica abaixo do " + name : "empata com o " + name;

        private static string PhaseLabel(EvolutionPhase phase)
        {
            switch (phase)
            {
                case EvolutionPhase.Baselines: return "referências na validação";
                case EvolutionPhase.Validation: return "validação";
                default: return "treino";
            }
        }

        private static string CandidateLabel(EvolutionSession session)
        {
            var individual = session.CurrentIndividual;
            if (individual != null)
                return $"indivíduo #{individual.Id}";
            return session.CurrentReference == RandomBrain.BrainName ? "cérebro aleatório" : "cérebro heurístico";
        }

        public static string CheckpointSaved(string path, long tick, EvolutionSession evolution) =>
            $"Checkpoint salvo ({Position(tick, evolution)}): {path}";

        /// <summary><paramref name="problem"/> says why the backup was used instead of the checkpoint (null if it was not).</summary>
        public static string CheckpointLoaded(string path, long tick, EvolutionSession evolution, string problem) =>
            problem == null
                ? $"Checkpoint carregado ({Position(tick, evolution)}): {path}"
                : $"Checkpoint principal recusado ({problem}); carregado o backup ({Position(tick, evolution)})";

        public static string CheckpointFailed(string outcome, string reason) => $"Checkpoint {outcome}: {reason}";

        private static string Position(long tick, EvolutionSession evolution) =>
            evolution == null ? $"tick {tick}" : $"geração {evolution.Job.Generation}, tick {tick} do episódio";

        private static string PresetLabel(ExperimentPreset preset)
        {
            var index = 0;
            for (var i = 0; i < ExperimentPreset.All.Count; i++)
                if (ExperimentPreset.All[i] == preset) index = i;
            return LabHud.PresetChoices[index];
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
