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

        /// <summary>
        /// Shared world summary; <paramref name="evolvedSource"/> names the evolved population, if used, and
        /// <paramref name="arena"/> holds the physical arena's totals since the start.
        /// </summary>
        public static string Run(WorldSimulation simulation, FixedStepClock clock, string brainName,
            ExperimentPreset preset, MetricsRecorder metrics, string runDirectory, string evolvedSource,
            ArenaTally arena = default, GenomeComposition composition = null, WorldAudit audit = null)
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
            text.AppendLine("Arena: " + ArenaLabel(config));
            text.AppendLine(Supply(config, config.InitialPopulation));
            if (config.SolidBodies)
            {
                text.AppendLine($"Contatos no último tick: {simulation.LastContacts} pares de corpos encostados");
                if (config.Reproduction)
                    text.AppendLine($"Nascimentos recusados desde o início: {arena.BlockedBirths} sem espaço ao lado do pai, " +
                                    $"{arena.CappedBirths} pelo teto (de graça: ninguém paga nem é posto longe)");
                text.AppendLine($"Reposições de comida adiadas (sem lugar livre): {arena.DelayedRespawns}");
            }

            if (composition != null)
                text.AppendLine($"Composição: {composition.Describe()} criaturas, cada cópia com estado próprio  ·  " +
                                (composition.Frozen
                                    ? "congelados: confronto sem reprodução, nenhum genoma muda"
                                    : "com descendência: filhos mutam e entram na genealogia"));
            else
                text.AppendLine(evolvedSource == null
                    ? "Cérebro: " + BrainLabel(brainName)
                    : $"Cérebro: rede 12-16-3 da população evoluída ({evolvedSource}), um indivíduo por criatura; " +
                      "sem memória nem aprendizado");
            if (metrics.HasSample)
            {
                var sample = metrics.LastSample;
                text.AppendLine($"Último segundo: {sample.FoodEatenInWindow} comidas, " +
                                $"{sample.DeathsInWindow} mortes, " +
                                $"energia média {sample.MeanEnergy:0.0}");
            }

            text.AppendLine($"Total comido: {metrics.TotalFoodEaten}");
            if (audit != null)
                text.AppendLine(Audit(audit, world));
            if (simulation.IsExtinct)
                text.AppendLine(metrics.ExtinctionTick.HasValue
                    ? $"EXTINÇÃO no tick {metrics.ExtinctionTick.Value} " +
                      $"({metrics.ExtinctionTick.Value * (double)config.TickDuration:0.0} s): nenhuma criatura viva e " +
                      "ninguém é reposto. A simulação parou; Iniciar ou R começa um mundo novo."
                    : "EXTINÇÃO: nenhuma criatura viva e ninguém é reposto. Iniciar ou R começa um mundo novo.");
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
            text.AppendLine(session.EvaluationWorkers > 1
                ? $"Treino paralelo (CPU): até {session.EvaluationWorkers} episódios independentes · a arena mostra um deles"
                : "Treino sequencial: um episódio por vez");
            var neat = plan.Algorithm == EvolutionAlgorithm.Neat;
            text.AppendLine($"Cada genoma sozinho no mesmo mundo de cada seed ({plan.EpisodeSeconds:0} s); sem competição. " +
                            (neat
                                ? "NEAT: redes começam mínimas e ganham nós e conexões; seleção artificial por espécies " +
                                  "na fitness de treino, não seleção natural."
                                : "MLP fixa 12-16-3 (controle): seleção artificial por torneio na fitness de treino, " +
                                  "não seleção natural."));
            text.AppendLine($"Geração {job.Generation}  ·  {PhaseLabel(session.Phase)}: " +
                            $"{CandidateLabel(session)} ({episode.Candidate + 1}/{job.CandidateCount}), " +
                            $"seed {episode.SeedIndex + 1}/{job.SeedCount}");
            text.AppendLine($"Episódio {episode.Tick * (double)episode.Simulation.World.Config.TickDuration:0.0} s " +
                            $"de {plan.Stages[episode.Stage].EpisodeSeconds:0} s  ·  episódios {job.CompletedEpisodes}/{job.TotalEpisodes}");
            AppendStage(text, plan, episode);
            AppendSpeed(text, clock);
            text.AppendLine($"Preset {PresetLabel(preset)}  ·  Seed do experimento {plan.ExperimentSeed}  ·  " +
                            $"validação a cada {session.ValidationInterval} gerações em {plan.ValidationSeedCount} seeds separadas");
            text.AppendLine(plan.ExpressedBodyGenes == BodyGeneSet.None
                ? "Corpo fixo (controle): todos com o corpo de referência"
                : "Corpo evolutivo: tamanho, velocidade, sensor e eficiência herdados e mutados, cada um com custo");
            text.AppendLine("Arena dos episódios: " + ArenaLabel(episode.Simulation.World.Config));
            text.AppendLine(Supply(episode.Simulation.World.Config, 1));

            AppendStageSummary(text, plan, session.LastTraining);
            var last = session.LastTraining?.Summary;
            var structure = session.LastTraining?.Neat;
            if (last != null && structure != null)
            {
                text.AppendLine($"Treino g{last.Generation}: melhor {last.Best:0.00}  ·  mediana {last.Median:0.00}  ·  " +
                                $"sobrevivência {last.MeanSurvivalRate:0%}");
                text.AppendLine($"NEAT g{last.Generation}: {structure.SpeciesCount} espécie(s), maior com " +
                                $"{structure.LargestSpecies}  ·  nós ocultos {structure.MeanHiddenNodes:0.0} em média " +
                                $"(máx. {structure.MaxHiddenNodes})  ·  conexões habilitadas {structure.MeanEnabledConnections:0.0}");
                if (structure.HasEyes)
                    text.AppendLine($"Olhos g{last.Generation} ({(plan.EvolvingEyes ? "evolutivos" : "fixos")}): " +
                                    $"{structure.MeanEyes:0.0} por genoma  ·  abertura média {structure.MeanEyeApertureDegrees:0}°  ·  " +
                                    $"alcance médio ×{structure.MeanEyeRange:0.00}");
            }
            else if (last != null)
            {
                text.AppendLine($"Treino g{last.Generation}: melhor {last.Best:0.00}  ·  mediana {last.Median:0.00}  ·  " +
                                $"sobrevivência {last.MeanSurvivalRate:0%}  ·  diversidade {last.WeightStdDev:0.000}");
            }
            else
            {
                text.AppendLine("Treino: nenhuma geração completa ainda");
            }

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
                   $"{episode.Tick * (double)config.TickDuration:0.0} s de {session.Plan.Stages[episode.Stage].EpisodeSeconds:0} s · " +
                   (session.CurrentIndividual == null
                       ? "cérebro de referência"
                       : session.Plan.Profile.IsLegacy
                           ? "fitness = comida ingerida / duração do episódio"
                           : $"nota do perfil {session.Plan.Profile.Id}") +
                   (StageLabel(session.Plan, episode.Stage) is string label ? "\n" + label : string.Empty);
        }

        /// <summary>
        /// "Etapa 1 — busca (sem gasto)" or "Etapa 2 — recursos" in a two-stage profile, "Recursos equilibrados" alone;
        /// null for the legacy profile.
        /// </summary>
        public static string StageLabel(EvaluationPlan plan, int stage)
        {
            var evaluated = plan.Stages[stage];
            if (evaluated.IsSearch)
                return "Etapa 1 — busca (sem gasto)";
            if (evaluated.Reward.Scoring == StageScoring.FoodEnergyPerSecond)
                return null;
            return plan.Stages.Count > 1 ? "Etapa 2 — recursos" : "Recursos equilibrados";
        }

        // The stage of the episode shown and what its scoring measured so far.
        private static void AppendStage(StringBuilder text, EvaluationPlan plan, IsolatedEpisode episode)
        {
            var label = StageLabel(plan, episode.Stage);
            if (label == null)
                return;
            text.AppendLine($"{label}  ·  perfil {plan.Profile.Id} ({plan.Profile.ParameterHash})");
            var tracker = episode.Tracker;
            if (tracker == null)
                return;
            var tickSeconds = (double)episode.Simulation.World.Config.TickDuration;
            if (episode.Layout != null)
            {
                var layout = episode.Layout;
                var reached = new List<string>();
                for (var i = 0; i < tracker.Collected.Count; i++)
                {
                    var target = tracker.Collected[i];
                    reached.Add($"{i + 1}º {(layout.IsWater(target) ? "água" : "comida")} em " +
                                $"{tracker.CollectionTicks[i] * tickSeconds:0.0} s (rapidez {tracker.Speeds[i]:0.00})");
                }

                text.AppendLine($"Alvos {tracker.Collected.Count}/{layout.TargetCount} (comida {layout.Food.Count}, " +
                                $"água {layout.Water.Count})  ·  arena {layout.Config.ArenaWidth:0} × {layout.Config.ArenaHeight:0}");
                if (reached.Count > 0)
                    text.AppendLine("Ordem: " + string.Join("; ", reached));
                if (tracker.Collected.Count < layout.TargetCount)
                    text.AppendLine($"Próximo alvo: caminho {tracker.BestDistance:0.0} (melhor no trecho) de " +
                                    $"{tracker.SegmentDistance:0.0}  ·  aproximação " +
                                    $"{StageScores.Approach(tracker.SegmentDistance, tracker.BestDistance):0.00}");
                text.AppendLine($"Gasto que seria cobrado: {tracker.UnchargedEnergy:0.0}");
            }
            else
            {
                text.AppendLine($"Energia absorvida {tracker.AbsorbedEnergy:0.0} (desperdiçada {tracker.WastedEnergy:0.0})  ·  " +
                                $"água bebida {tracker.Drunk:0.0}");
            }
        }

        // Median and spread of each stage's score in the last training generation, with a warning for a stage whose
        // scores barely differ (it hardly takes part in selection).
        private static void AppendStageSummary(StringBuilder text, EvaluationPlan plan, GenerationReport report)
        {
            if (plan.Profile.IsLegacy || report == null)
                return;
            var candidates = report.Evaluation.Candidates;
            var parts = new List<string>();
            var flat = new List<string>();
            for (var k = 0; k < plan.Stages.Count; k++)
            {
                var scores = new List<float>();
                foreach (var candidate in candidates)
                {
                    if (Fitness.IsValid(candidate.StageFitness[k]))
                        scores.Add(candidate.StageFitness[k]);
                }

                if (scores.Count == 0)
                    continue;
                scores.Sort();
                var median = Quantile(scores, 0.5);
                var spread = Quantile(scores, 0.75) - Quantile(scores, 0.25);
                var name = plan.Stages[k].IsSearch ? "S1 busca" : plan.Stages.Count > 1 ? "S2 recursos" : "recursos";
                parts.Add($"{name} mediana {median:0.000} (IQR {spread:0.000})");
                if (spread < 0.02)
                    flat.Add(name);
            }

            text.AppendLine($"Etapas g{report.Evaluation.Generation}: " + string.Join("  ·  ", parts));
            if (flat.Count > 0)
                text.AppendLine("Aviso: etapa sem poder de separação (IQR < 0,02): " + string.Join(", ", flat));
        }

        private static float Quantile(List<float> sorted, double q)
        {
            var position = q * (sorted.Count - 1);
            var low = (int)Math.Floor(position);
            var high = Math.Min(sorted.Count - 1, low + 1);
            return (float)(sorted[low] + (sorted[high] - sorted[low]) * (position - low));
        }

        /// <summary>
        /// Lineage of an individual as seen from the generation it is being evaluated in; <paramref name="species"/> is
        /// the NEAT species line, if any.
        /// </summary>
        public static string Lineage(Individual individual, Lineage lineage, int generation, string species = null)
        {
            var text = new StringBuilder();
            text.AppendLine("Linhagem");
            text.AppendLine($"Indivíduo #{individual.Id} · nasceu na geração {individual.BirthGeneration}");
            if (individual.ParentIds.Count == 0)
                text.AppendLine(individual.Genome.Neat == null
                    ? "Fundador: genoma aleatório da geração 0, sem pais"
                    : "Fundador: rede mínima da geração 0 (sem pais)");
            else if (individual.ParentIds.Count == 2)
                text.AppendLine($"Pais #{individual.ParentIds[0]} (mais apto) e #{individual.ParentIds[1]}: cruzamento " +
                                $"NEAT · {individual.MutatedWeights} pesos mutados ao nascer");
            else
                text.AppendLine($"Pai #{individual.ParentIds[0]} · {individual.MutatedWeights} pesos mutados ao nascer");
            var network = individual.Genome.Neat;
            if (network != null)
                text.AppendLine($"Rede NEAT: {network.HiddenNodes.Count} nós ocultos · {network.Connections.Count} " +
                                $"conexões ({network.EnabledConnectionCount} habilitadas)");
            if (species != null)
                text.AppendLine(species);
            if (individual.BirthGeneration < generation)
                text.AppendLine($"Elite: passou sem nova mutação para a geração {generation} e é avaliado de novo");
            text.Append("Ancestrais: " + Ancestors(lineage.Ancestry(individual.Id)));
            return text.ToString();
        }

        /// <summary>Species line of a NEAT individual placed at the end of <paramref name="generation"/> (or its parent).</summary>
        public static string Species(int species, int generation, long? parent) =>
            parent.HasValue
                ? $"Espécie do pai #{parent.Value}: {species} (fim da geração {generation}); a sua é definida ao fim desta"
                : $"Espécie {species} (fim da geração {generation})";

        public static string SpeciesPending() => "Espécie: definida ao fim da geração";

        /// <param name="sensed">What the creature senses now (with its eye inputs when it has eyes).</param>
        /// <param name="decided">
        /// For a creature with eyes, the observation its brain used for the action it holds, when known; shown apart from
        /// the current reading, which is the observer's, not the brain's input.
        /// </param>
        public static string Creature(CreatureState creature, Observation sensed, CreatureAction action,
            string brainName, SimulationConfig config, Observation? decided = null)
        {
            var text = new StringBuilder();
            text.AppendLine($"Criatura {creature.Id}");
            text.AppendLine(brainName == MlpBrain.BrainName
                ? "Cérebro: rede 12-16-3 com genoma próprio"
                : brainName == NeatBrain.BrainName
                    ? "Cérebro: rede NEAT com genoma próprio (topologia evolutiva, sem memória)"
                    : "Cérebro: " + BrainLabel(brainName));
            text.AppendLine($"Energia {creature.Energy:0.0} / {creature.Body.MaxEnergy:0.#}");
            if (config.HasWater)
                text.AppendLine($"Hidratação {creature.Hydration:0.0} / {config.MaxHydration:0.#} (bebe na margem da água; " +
                                "morre de sede em 0)");
            text.AppendLine($"Idade {creature.AgeTicks * (double)config.TickDuration:0.0} s de {config.MaxAgeSeconds:0} s");
            text.AppendLine($"Comida ingerida {creature.FoodEnergyEaten:0} de energia");
            text.AppendLine($"Posição ({creature.Position.X:0.0}; {creature.Position.Y:0.0}) — só inspeção");
            AppendGenealogy(text, creature, config);
            text.AppendLine();
            AppendBody(text, creature, config);
            text.AppendLine();
            if (creature.Eyes != null)
            {
                AppendEyes(text, creature, config, sensed, decided);
                text.AppendLine();
                text.AppendLine("Ação mantida");
                text.Append($"propulsão {action.Thrust:0.00}  giro {action.Turn:0.00}  lateral {action.Strafe:0.00}");
                return text.ToString();
            }

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

        /// <summary>
        /// The physical arena as one creature meets it: its solid disc and reach, what it touches now, what obstacles
        /// keep from it and its last refused birth. Null for the open arena (points that pass through each other).
        /// </summary>
        public static string Arena(WorldState world, CreatureState creature, List<CreatureId> touching,
            BirthRefusal? refusal)
        {
            var config = world.Config;
            if (!config.SolidBodies)
                return null;
            var text = new StringBuilder();
            text.AppendLine("Arena física");
            text.AppendLine($"Corpo sólido: disco de raio {creature.Body.Radius:0.00}; a boca alcança " +
                            $"{creature.Body.EatRadius:0.00} do centro (halo claro)");
            var contacts = PhysicalInspection.ContactsOf(world, creature, touching);
            if (contacts.Any)
            {
                var parts = new List<string>();
                if (contacts.Creatures > 0)
                    parts.Add((contacts.Creatures == 1 ? "criatura " : "criaturas ") + string.Join(", ", touching));
                if (contacts.Wall)
                    parts.Add("parede");
                if (contacts.Obstacle)
                    parts.Add("obstáculo");
                text.AppendLine("Encostada em: " + string.Join(" · ", parts) + " (empurra e é empurrada)");
            }
            else
            {
                text.AppendLine("Sem contato agora");
            }

            if (config.Obstacles.Count > 0)
            {
                var occlusion = PhysicalInspection.OcclusionOf(world, creature);
                if (creature.Eyes != null)
                    text.AppendLine(occlusion.HiddenFromEyes == 0
                        ? "Nenhuma comida dos cones escondida por obstáculos"
                        : $"Comida nos cones escondida por obstáculos: {occlusion.HiddenFromEyes}");
                else if (occlusion.LegacyTargetHidden)
                    text.AppendLine("Sentido legado: a comida que ele aponta está atrás de um obstáculo " +
                                    "(ele não vê obstáculos)");
                if (occlusion.BlockedFromMouth > 0)
                    text.AppendLine($"Comida ao alcance da boca, mas atrás de obstáculo: {occlusion.BlockedFromMouth}");
            }

            if (refusal.HasValue)
                text.AppendLine($"Último nascimento recusado (tick {refusal.Value.Tick}): " +
                                (refusal.Value.Reason == BirthRefusalReason.NoRoom
                                    ? "sem espaço para o filho ao lado"
                                    : "população no teto"));
            return text.ToString().TrimEnd();
        }

        /// <summary>A start that was refused, above everything else: the scenario and why; the run shown goes on.</summary>
        public static string StartRefused(string problem) =>
            $"CENÁRIO INVIÁVEL — não iniciado; a execução mostrada continua. Motivo: {problem}";

        /// <summary>
        /// Where energy and water went since the start or the load, split by cause, and how far each balance is from
        /// closing (float rounding only, when the audit is complete).
        /// </summary>
        public static string Audit(WorldAudit audit, WorldState world)
        {
            var e = audit.Energy;
            var text = new StringBuilder();
            text.AppendLine($"Energia em {audit.Ticks} ticks: comida surgiu {e.FoodAppeared:0}, comida comida {e.Food:0} " +
                            $"(absorvida {e.Absorbed:0}, desperdiçada {e.Overflow:0.#})");
            text.AppendLine($"Gastos cobrados {e.Charged:0.#}: manutenção {e.Maintenance:0.#} · sensor {e.SensorCosts:0.#} · " +
                            $"esforço (inclui empurrar) {e.Effort:0.#} · olhos {e.EyeCosts:0.#} · giro {e.TurnCosts:0.#} · " +
                            $"não pagos (sem energia) {e.Unpaid:0.#}");
            text.Append($"Reprodução: repassado {e.Transferred:0.#}, custo {e.BirthCosts:0.#} · levado por mortos " +
                        $"{e.CarriedAwayByDeaths:0.#} · fechamento: criaturas {audit.CreatureEnergyError(world):0.###}, " +
                        $"comida {audit.FoodEnergyError(world):0.###}");
            if (world.Config.HasWater)
            {
                var h = audit.Hydration;
                text.Append($"\nÁgua: bebida {h.Drunk:0.#} (de fontes inesgotáveis {h.DrunkFromInexhaustible:0.#}), " +
                            $"perdida {h.Lost:0.#}, repassada {h.Transferred:0.#}, levada por mortos {h.CarriedAwayByDeaths:0.#}, " +
                            $"recarga {h.Recharged:0.#} · fechamento: criaturas {audit.HydrationError(world):0.###}, " +
                            $"fontes {audit.WaterError(world):0.###}");
            }

            return text.ToString();
        }

        /// <summary>
        /// Size, densities and what the scenario offers its <paramref name="creatures"/> (see <see cref="ResourceSupply"/>):
        /// upper bounds of food energy and water per second, in all and per creature, next to what a creature spends.
        /// </summary>
        public static string Supply(SimulationConfig config, int creatures)
        {
            var supply = new ResourceSupply(config, creatures);
            var text = new StringBuilder();
            text.AppendLine($"Cenário {config.ArenaWidth:0.#}×{config.ArenaHeight:0.#}  ·  {supply.FoodItems} comidas " +
                            $"({supply.FoodDensity:0.###} por 100 u²)  ·  {supply.Creatures} criatura(s) " +
                            $"({supply.CreatureDensity:0.###} por 100 u²)");
            text.Append($"Oferta de comida: até {supply.FoodEnergyPerSecond:0.##} energia/s, " +
                        $"{supply.FoodEnergyPerCreaturePerSecond:0.##} por criatura (repouso gasta " +
                        $"{supply.RestingCostPerSecond:0.##}/s; daria para {supply.CreaturesFedAtRest:0.#} em repouso)  ·  " +
                        $"estoque inicial {supply.FoodStock:0} ({config.FoodEnergy:0.##} por item, volta em " +
                        $"{config.FoodRespawnSeconds:0.##} s)");
            if (supply.WaterSources > 0)
                text.Append("\nOferta de água: " + (supply.InexhaustibleWater
                    ? $"{supply.WaterSources} fonte(s) inesgotável(is) (controle)"
                    : $"{supply.WaterSources} fonte(s), recarga {supply.WaterPerSecond:0.##}/s, " +
                      $"{supply.WaterPerCreaturePerSecond:0.###} por criatura (sede {supply.HydrationLossPerSecond:0.##}/s)  ·  " +
                      $"reserva {supply.WaterStock:0}"));
            return text.ToString();
        }

        /// <summary>The arena in words: open or solid, and its obstacle layout.</summary>
        public static string ArenaLabel(SimulationConfig config)
        {
            if (!config.SolidBodies)
                return "aberta, criaturas sem colisão (controle)";
            var obstacles = config.Obstacles.Count;
            var layout = ObstacleLayouts.Identify(config);
            var name = obstacles == 0 ? "sem obstáculos"
                : layout == ObstacleLayout.Pillars ? $"pilares ({obstacles} obstáculos)"
                : layout == ObstacleLayout.Corridor ? $"corredor ({obstacles} obstáculos com uma passagem)"
                : $"{obstacles} obstáculos";
            return $"corpos sólidos (raio {config.BodyRadius:0.##}), {name}" +
                   (obstacles == 0 ? string.Empty : "; obstáculos barram corpo, olhos e boca");
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

            if (creature.Eyes != null)
            {
                var eyes = EyeCost.PerSecond(body, creature.Eyes);
                text.AppendLine($"Gasto/s: repouso {body.MetabolismPerSecond + eyes:0.00} (metabolismo " +
                                $"{body.MetabolismPerSecond:0.00} + olhos {eyes:0.000}), propulsão máxima " +
                                $"+{body.MovementCostPerSecond:0.00}, giro máximo +{body.MovementCostPerSecond * config.TurnCostFraction:0.00}");
                return;
            }

            var resting = body.MetabolismPerSecond + body.SensorCostPerSecond;
            text.AppendLine($"Gasto/s: repouso {resting:0.00} (metabolismo {body.MetabolismPerSecond:0.00} + sensor " +
                            $"{body.SensorCostPerSecond:0.00}), propulsão máxima +{body.MovementCostPerSecond:0.00}");
        }

        // Each slot's eye and upkeep, then what each eye reported: in the last decision when known, otherwise now.
        private static void AppendEyes(StringBuilder text, CreatureState creature, SimulationConfig config,
            Observation sensed, Observation? decided)
        {
            var eyes = creature.Eyes;
            text.AppendLine($"Olhos ({eyes.Count} de {EyeSet.MaxEyes}; sem círculo de comida nem radar de paredes)");
            for (var slot = 0; slot < EyeSet.MaxEyes; slot++)
            {
                if (!eyes.Has(slot))
                {
                    text.AppendLine($"olho {slot}: ausente");
                    continue;
                }

                var eye = eyes[slot];
                text.AppendLine($"olho {slot}: direção {eye.Orientation * 180f / MathF.PI:+0;-0;0}°  abertura " +
                                $"{eye.Aperture * 180f / MathF.PI:0}°  alcance {creature.Body.SensorRange * eye.Range:0.0}  " +
                                $"gasto {EyeCost.PerSecond(creature.Body, eye):0.000}/s");
            }

            var inputs = decided?.EyeInputs ?? sensed.EyeInputs;
            text.AppendLine(decided?.EyeInputs != null
                ? "Entrada do cérebro na última decisão"
                : "Leitura atual dos olhos (ainda não é uma entrada do cérebro)");
            if (inputs == null)
                return;
            var contract = NeuralContract.OfEyeInputs(inputs.Length);
            text.AppendLine($"energia {inputs[0]:0.00}  idade {inputs[1]:0.00}  vel. frente {inputs[2]:0.00}  esq. {inputs[3]:0.00}" +
                            (contract.SensesWater ? $"  hidratação {inputs[NeuralContract.HydrationInput]:0.00}" : ""));
            for (var slot = 0; slot < EyeSet.MaxEyes; slot++)
            {
                if (!eyes.Has(slot))
                    continue;
                text.AppendLine($"olho {slot}: " + Seen(contract, inputs, slot, EyeChannel.FoodVisible, "comida") + " · " +
                                Seen(contract, inputs, slot, EyeChannel.BarrierVisible, "barreira") +
                                (contract.SensesWater
                                    ? " · " + Seen(contract, inputs, slot, EyeChannel.WaterVisible, "água")
                                    : ""));
            }
        }

        private static string Seen(NeuralContract contract, float[] inputs, int slot, EyeChannel visible, string what)
        {
            var at = contract.EyeInput(slot, visible);
            return inputs[at] > 0f
                ? $"{what} sen {inputs[at + 1]:0.00} cos {inputs[at + 2]:0.00} prox. {inputs[at + 3]:0.00}"
                : what + " não vista";
        }

        /// <summary>
        /// Which genome of the composition a creature carries: a founder's entry and copy, or, for a descendant, the
        /// entry of the founder its line goes back to (when the genealogy knows it).
        /// </summary>
        public static string CompositionOrigin(GenomeComposition composition, Genealogy genealogy, CreatureId id)
        {
            var founder = id;
            if (id.Value > composition.Total)
            {
                if (genealogy == null || !genealogy.TryGet(id, out _))
                    return "Composição: descendente (linha não registrada)";
                var line = genealogy.Ancestry(id);
                founder = line[line.Count - 1].Creature;
                if (founder.Value > composition.Total)
                    return "Composição: descendente (fundadora anterior ao registro)";
            }

            var entry = composition.Entries[composition.EntryOf(founder)];
            return founder == id
                ? $"Composição: {entry.Source}, cópia {composition.CopyOf(id)} de {entry.Count}"
                : $"Composição: descende de {founder} ({entry.Source}); genoma mutado ao nascer";
        }

        /// <summary>
        /// Line of a creature back to its founder (the nearest ancestors and the founder when the line is long) and its
        /// descendants, from the genealogy of the shared world.
        /// </summary>
        public static string Genealogy(Genealogy genealogy, CreatureId id, float tickDuration)
        {
            if (!genealogy.TryGet(id, out var entry))
                return "Genealogia: sem registro (criatura anterior ao carregamento)";
            var line = genealogy.Ancestry(id);
            var text = new StringBuilder();
            text.AppendLine($"Genealogia (geração {entry.Generation}" +
                            (entry.Parent.HasValue ? $", nasceu em {entry.BirthTick * (double)tickDuration:0.0} s)" : ", fundadora)"));
            const int shown = 5;
            var names = new List<string>();
            for (var i = 0; i < line.Count && i < shown; i++)
                names.Add(line[i].Creature.ToString());
            if (line.Count > shown + 1)
                names.Add($"… {line.Count - shown - 1} …");
            if (line.Count > shown)
                names.Add(line[line.Count - 1].Creature.ToString());
            var founder = line[line.Count - 1];
            text.AppendLine(string.Join(" ← ", names) + (founder.Parent.HasValue ? " (linha anterior não registrada)" : " (fundadora)"));
            var descendants = genealogy.Descendants(id, out var living);
            text.Append(descendants == 0 ? "Sem descendentes" : $"{descendants} descendentes, {living} vivos");
            return text.ToString();
        }

        /// <summary>Whether a network changes during its life and how much it has changed so far.</summary>
        public static string Plasticity(PlasticityGenes genes, PlasticMemory memory)
        {
            if (!genes.IsPlastic)
                return "Rede fixa durante a vida (sem plasticidade)";
            return $"Plasticidade hebbiana (camada de saída): taxa {genes.LearningRate:0.###}, decaimento {genes.Decay:0.###}; " +
                   $"maior |ΔW| nesta vida {memory?.MaxAbsDelta ?? 0f:0.###} — começa em zero e não é herdado";
        }

        public static string DeadCreature(CreatureId id, CreatureDeath? death)
        {
            if (!death.HasValue)
                return $"Criatura {id}\nnão está mais no mundo";
            var cause = death.Value.Cause == DeathCause.Starvation ? "fome"
                : death.Value.Cause == DeathCause.Dehydration ? "sede"
                : "idade";
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

        public static string GenomeExported(string path) => $"Genoma exportado: {path}";

        public static string GenomeExportFailed(string reason) => $"Genoma não exportado: {reason}";

        public static string CompositionStarted(GenomeComposition composition) =>
            $"Composição: {composition.Describe()} criaturas " + (composition.Frozen ? "(congelados)" : "(com descendência)");

        public static string CompositionFailed(string reason) =>
            $"Composição não usada (cérebro heurístico no lugar): {reason}";

        public static string GenomePoolFailed(string reason) => $"Pool de genomas não usado (geração 0 aleatória): {reason}";

        public static string GenomeDirectoryCopied(string path) => $"Pasta copiada para a área de transferência: {path}";

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
                case NeatBrain.BrainName: return "rede NEAT (topologia evolutiva, sem memória)";
                default: return brainName;
            }
        }
    }
}
