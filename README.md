# EDEN — Evolutionary Digital Ecosystem Network

EDEN é um laboratório visual de vida artificial feito em Unity. Uma população de criaturas simples vive numa arena 2D: cada uma percebe só o que está perto, decide o que fazer com um cérebro próprio, gasta energia para se mover e precisa encontrar comida para sobreviver. O objetivo do projeto é observar e medir como comportamentos surgem, se espalham ou desaparecem quando cérebros de redes neurais e, mais tarde, características do corpo passam de geração em geração com pequenas variações.

O foco está em experimentos reproduzíveis e inspecionáveis: com a mesma seed e a mesma configuração, a simulação produz o mesmo resultado, e qualquer criatura pode ser selecionada para ver o que ela sente e o que decidiu.

> Estado atual: os marcos **M1** (simulação determinística e baseline) e **M2** (rede neural fixa) estão concluídos e o **M3** (evolução por gerações) tem todas as tarefas implementadas: genomas evoluem em episódios isolados, com seeds de validação fora da seleção e comparação com os cérebros de referência. Veja o [roadmap](#roadmap).

## Como funciona

**Mundo.** Arena retangular fechada (padrão 50 × 50 unidades) com criaturas e pontos de comida. A simulação avança em ticks fixos (padrão 20 por segundo simulado). Acelerar a execução significa rodar mais ticks completos por frame, nunca aumentar o passo de tempo; o FPS da tela não altera o resultado.

**Um tick**, sempre na mesma ordem:

1. eventos agendados (reposição de comida);
2. observação de todas as criaturas a partir do mesmo estado;
3. decisão de cada criatura, em ordem crescente de id;
4. movimento cinemático (propulsão, giro, deslocamento lateral, amortecimento, velocidade máxima, presa à arena);
5. alimentação: cada comida é comida uma única vez, pela criatura mais próxima dentro do raio; em empate, a de menor id;
6. energia, idade e morte (por fome ou por idade máxima);
7. remoção da comida consumida e das criaturas mortas.

**Energia.** A cada tick a criatura paga metabolismo, custo de movimento (proporcional ao quadrado do esforço) e custo do sensor, e ganha a energia da comida que comeu. A energia nunca fica negativa nem passa do máximo. Comida consumida reaparece depois de alguns segundos numa posição sorteada, então o total de comida nunca cresce.

**Sensores.** Cada criatura recebe 12 valores normalizados, sempre no seu referencial local (x à frente, y à esquerda):

| # | Entrada |
| --- | --- |
| 1 | energia / energia máxima |
| 2 | idade / idade máxima |
| 3–4 | velocidade para frente e para a esquerda / velocidade máxima |
| 5–6 | seno e cosseno do ângulo até a comida visível mais próxima |
| 7 | proximidade dessa comida (1 − distância / alcance) |
| 8 | 1 se há comida ao alcance, senão 0 |
| 9–12 | distância até a parede à frente, à direita, atrás e à esquerda / alcance (limitada a 1) |

Sem comida ao alcance, as entradas 5–8 valem zero. Um cérebro nunca recebe posições globais.

**Ações.** Propulsão para frente [0, 1], giro [−1, 1] e propulsão lateral [−1, 1]. Valores fora da faixa são limitados e NaN vira zero. Por padrão os cérebros decidem a cada 2 ticks (10 Hz) e a ação é mantida entre as decisões.

**Cérebros.** Todos seguem o mesmo contrato (`IBrain`): recebem a observação e o próprio estado e devolvem uma ação.

- **aleatório:** ignora os sensores e sorteia ações;
- **heurístico:** regras fixas, sem memória nem aprendizado; vai até a comida visível e desvia das paredes;
- **rede neural:** perceptron 12 → 16 → 3 com `tanh` (259 parâmetros). Cada criatura tem o seu **genoma** (topologia e pesos-base, imutável e separado de energia, idade e posição) e a sua própria rede. No laboratório os genomas ainda são aleatórios; a evolução roda à parte, nos [experimentos por gerações](#evolução-por-gerações). A rede é feed-forward: não guarda memória entre decisões nem aprende durante a vida.

Os dois primeiros servem de linha de base para comparar as redes evoluídas. Baseline preliminar com a configuração padrão (64 criaturas, 5 seeds, 60 s simulados):

| Cérebro | Comida consumida | Sobreviventes (de 320) |
| --- | --- | --- |
| Aleatório | 1179 | 25 |
| Heurístico | 4117 | 175 |

**Contrato de entradas.** As 12 entradas têm faixas documentadas. Um valor fora do contrato (NaN, infinito, fora da faixa, máscara de comida inconsistente) é corrigido por uma regra fixa antes de chegar a qualquer cérebro, e cada correção é contada e mostrada no HUD; em execuções normais o total é zero.

**Determinismo.** Toda aleatoriedade vem de um gerador PCG32 com estado salvo e de fluxos separados derivados da seed (mundo, posicionamento, mutação, visual e fluxos por criatura para cérebros e genomas). Assim, nascimentos, mortes ou efeitos visuais nunca deslocam os sorteios de outra parte da simulação. A igualdade exata é garantida no mesmo ambiente; não há promessa de resultado bit a bit idêntico entre processadores ou versões da Unity.

## Requisitos

- Windows 10/11.
- Unity **6000.3.27f1** (Unity 6.3 LTS), instalada pelo Unity Hub, com o módulo Windows Build Support.
- Pacotes resolvidos automaticamente pelo Package Manager (URP 17.3, Input System 1.20, Test Framework 1.6).

## Abrir o projeto

1. Clone o repositório num **caminho curto**, por exemplo `D:\EDEN`. O Windows limita caminhos a 260 caracteres por padrão, e alguns arquivos internos de pacotes da Unity ficam bem fundo em `Library/PackageCache`; num caminho longo a importação falha. Como alternativa, habilite caminhos longos no Windows (`LongPathsEnabled`).

   ```powershell
   git clone https://github.com/ninguemdev/EDEN.git D:\EDEN
   ```

2. No Unity Hub, use **Add → Add project from disk** e escolha a pasta clonada. A primeira abertura importa os pacotes e pode demorar alguns minutos.
3. Abra a cena `Assets/_Project/Scenes/Bootstrap.unity` e pressione **Play**.

## Usar o laboratório

A cena Bootstrap mostra a arena ao vivo. À esquerda ficam o estado do experimento (tick, tempo simulado, velocidade pedida e efetiva, população, comida, cérebro e métricas do último segundo); à direita, o inspetor da criatura selecionada (energia, idade, comida ingerida, os 12 sensores e a ação atual).

| Tecla | Ação |
| --- | --- |
| Espaço | pausar / continuar |
| `.` | avançar um tick (pausa se estiver rodando) |
| `-` / `=` | metade / dobro da velocidade (0,125× a 64×) |
| clique, `[` / `]` | selecionar criatura (mais próxima do clique, ou anterior/próxima por id) |
| Esc | limpar seleção |
| R | reiniciar com a mesma seed |
| B | alternar entre cérebro heurístico, aleatório e rede neural (reinicia com a mesma seed) |

### Ver a rede neural de uma criatura

Com o cérebro **rede neural** ativo, selecionar uma criatura mostra, abaixo do inspetor, o diagrama da rede dela: 12 neurônios de entrada (com os nomes dos sensores), 16 ocultos e 3 de saída (propulsão, giro, lateral). A cor de cada neurônio é a sua ativação (verde positiva, vermelha negativa, cinza perto de zero) e a cor e a intensidade de cada conexão mostram o sinal que passa por ela (peso × ativação de origem). O diagrama é atualizado 10 vezes por segundo e corresponde exatamente à ação que a criatura está executando.

O diagrama só existe enquanto há uma criatura neural selecionada: sem seleção (ou com Esc), nada da rede é calculado nem desenhado, para não pesar na simulação.

Todos os parâmetros do run (seed, tamanho da arena, população, energia, custos, alcance do sensor, velocidade etc.) ficam editáveis no componente **LabRunner** do objeto `Lab`. Os valores padrão são pontos de partida para medição, não valores ajustados.

Se o computador não acompanhar a velocidade pedida, o excesso de trabalho é descartado e o HUD mostra a velocidade efetiva e os ticks descartados; as regras nunca são puladas.

### Métricas

Cada execução grava um diretório em `%USERPROFILE%\AppData\LocalLow\NinguemDev\EDEN\SimulationRuns\<data>-seed<n>-<cérebro>\` (fora do repositório), com:

- `run-info.txt`: seed, cérebro, versão e parâmetros principais;
- `metrics.csv`: uma linha por segundo simulado, com números em formato invariável (ponto decimal):
  `tick, simulated_seconds, population, food_available, food_eaten_window, starvation_deaths_window, old_age_deaths_window, mean_energy, total_food_eaten`.

## Evolução por gerações

Um experimento evolui uma população de genomas (padrão 64) sem a cena e sem competição entre eles:

1. **Avaliação isolada.** Cada genoma vive sozinho num episódio de 120 s simulados, uma vez para cada uma das 3 seeds de treino da geração. Todos os genomas da geração enfrentam exatamente as mesmas condições iniciais (comida, posição, direção).
2. **Fitness** = energia de comida consumida ÷ duração configurada do episódio (o denominador não diminui se a criatura morrer antes), em média nas 3 seeds. Sobrevivência, tempo vivo e energia final são medidos à parte, sem somar tudo numa nota só.
3. **Próxima geração.** Os 4 melhores ficam sem alteração (e são avaliados de novo, sem levar a nota antiga); as demais vagas vêm de torneios de 3 com reposição. Cada filho é uma cópia do pai em que cada peso tem 5% de chance de receber ruído gaussiano (σ = 0,10), sempre limitado a [−3, 3]. Não há cruzamento: cada filho tem um único pai.
4. **Linhagem.** Cada indivíduo novo recebe um id único, o id do pai e a geração em que nasceu; elites mantêm os seus. A partir de qualquer id dá para seguir a ascendência até a primeira geração.

Esses valores são propostas iniciais a medir, não garantias de melhoria. Com a mesma seed o experimento repete as mesmas gerações, ids e resultados.

Para rodar: menu **EDEN → Evolution → Run 20 generations (seed 1)** no Editor (o Editor fica ocupado durante a execução, cerca de 20 s) ou, com o Editor fechado:

```powershell
& "C:\Program Files\Unity\Hub\Editor\6000.3.27f1\Editor\Unity.exe" -batchmode -projectPath . `
  -executeMethod Eden.Editor.EvolutionBatch.RunFromCommandLine -eden-seed 1 -eden-generations 20 -logFile evolution.log
```

Opções: `-eden-seed N`, `-eden-generations N`, `-eden-food N` (comida do episódio, padrão 32) e `-eden-output DIR`. Sem `-eden-output`, o resultado vai para `%USERPROFILE%\AppData\LocalLow\NinguemDev\EDEN\EvolutionRuns\<data>-seed<n>\`:

- `experiment.txt`: todos os parâmetros do experimento;
- `generations.csv`: uma linha por geração com melhor, quartis, mediana, pior e média do fitness, sobrevivência, diversidade dos pesos (desvio padrão médio por peso e distância média ao centróide), indivíduos novos, elites e pais distintos;
- `individuals.csv`: cada indivíduo avaliado, com id, pai, geração de nascimento e resultados;
- `episodes.csv`: cada indivíduo × seed, antes de qualquer média.

Os três CSVs se ligam por geração, modo de avaliação e candidato. O fitness de treino sozinho não separa progresso real de sobreajuste; para isso servem a validação e a bateria abaixo.

### Validação e bateria de execuções

- **Seeds de validação:** 5 cenários fixos para o experimento inteiro, derivados de uma seed própria (`ValidationSeed`) e nunca usados na seleção, na escolha de elites nem no ajuste de parâmetros. Validar uma geração só observa: a evolução segue idêntica com ou sem validação.
- **Campeão do treino:** o indivíduo que a seleção colocaria em primeiro. O resultado dele na validação é uma estimativa honesta; o "melhor da validação" é escolhido pela própria validação e por isso é otimista.
- **Bateria:** execuções independentes (seeds de experimento consecutivas) que compartilham as mesmas seeds de validação, mais os cérebros aleatório e heurístico nos mesmos cenários e com o mesmo número de episódios. A bateria de aceitação proposta é 5 execuções × 30 gerações, 3 seeds de treino e 5 de validação; com orçamento menor o relatório se marca como **preliminar**.

```powershell
& "C:\Program Files\Unity\Hub\Editor\6000.3.27f1\Editor\Unity.exe" -batchmode -projectPath . `
  -executeMethod Eden.Editor.EvolutionBatch.RunBatteryFromCommandLine -eden-seed 1 -logFile battery.log
```

Opções extras: `-eden-runs N`, `-eden-generations N`, `-eden-validation-interval N` (padrão 5: valida a população inteira nas gerações 0, 5, 10… e na última), `-eden-food N` e `-eden-output DIR`. O diretório da bateria contém:

- `report.txt`: orçamento, referências, cada execução (geração 0 → final), média ± desvio entre execuções e as comparações contadas por execução (mediana e campeão contra aleatório, heurístico e a própria geração 0), inclusive quando não há ganho;
- `validation.csv` e `baselines.csv`: os mesmos números em CSV;
- uma pasta por execução com os CSVs de treino e validação descritos acima.

Primeira bateria de aceitação (valores propostos, 32 comidas por episódio, seeds de experimento 1–5, seed de validação 1):

| Na validação, geração final | Média ± desvio entre execuções | Mín – máx |
| --- | --- | --- |
| Mediana da população | 2,43 ± 0,93 | 1,53 – 3,76 |
| Campeão do treino | 2,53 ± 0,94 | 1,58 – 3,88 |
| Mediana na geração 0 | 0,05 ± 0,01 | 0,03 – 0,07 |
| Sobrevivência média | 0,61 ± 0,24 | 0,28 – 0,93 |

Referências nos mesmos cenários: aleatório 0,13 e heurístico 1,38 (nenhum dos dois sobrevive aos 120 s). A mediana final superou a da geração 0, o aleatório e o heurístico nas 5 execuções, mas a variação entre execuções é grande e a diversidade dos pesos cai depressa nas primeiras gerações. É um resultado com esta configuração, não uma garantia; os valores da economia de energia ainda estão em calibração.

## Testes

Pelo Editor: **Window → General → Test Runner**, abas EditMode e PlayMode.

- **EditMode:** gerador aleatório (com os vetores oficiais do PCG32 e do SplitMix64), configuração, relógio de ticks, agenda de eventos, sensores, regras do mundo, cérebros, rede neural, genoma, avaliação isolada, seleção, mutação, linhagem, validação separada do treino, bateria, métricas e CSV, além de execuções longas com verificação de invariantes e independência de FPS.
- **PlayMode:** a cena Bootstrap real: renderização, pausa, avanço de um tick, inspeção por id e gravação do CSV.

Pela linha de comando, com o Editor **fechado** para este projeto:

```powershell
& "C:\Program Files\Unity\Hub\Editor\6000.3.27f1\Editor\Unity.exe" -batchmode -projectPath . `
  -runTests -testPlatform EditMode -testResults TestResults\editmode.xml
```

Troque `EditMode` por `PlayMode` para os testes de cena.

## Build

No Editor, menu **EDEN → Build → Windows x64**. Pela linha de comando (Editor fechado):

```powershell
& "C:\Program Files\Unity\Hub\Editor\6000.3.27f1\Editor\Unity.exe" -batchmode -projectPath . `
  -executeMethod Eden.Editor.WindowsBuild.BuildFromCommandLine -logFile build.log
```

O executável sai em `Builds/Windows/EDEN.exe` e roda offline, sem o Editor. Opções de linha de comando do executável:

| Opção | Efeito |
| --- | --- |
| `-eden-brain heuristic\|random\|neural` | cérebro inicial |
| `-eden-seed N` | seed do run |
| `-eden-select ID` | já começa com a criatura `ID` selecionada |

Exemplo: `EDEN.exe -eden-brain neural -eden-select 5`.

## Estrutura

O código fica em `Assets/_Project` e é dividido em assemblies com responsabilidades separadas. As regras não dependem da Unity: a cena apenas mostra o estado calculado pela simulação.

| Assembly | Responsabilidade |
| --- | --- |
| `Eden.Domain` | ids, configuração, estado do mundo, observação, ação, gerador aleatório e seeds (sem Unity) |
| `Eden.Simulation` | tick fixo, relógio, agenda de eventos, sensores, movimento, alimentação, energia e morte (sem Unity) |
| `Eden.Brains` | contrato de cérebro e cérebros de referência (sem Unity) |
| `Eden.Evolution` | plano de avaliação, fitness, seleção, mutação, linhagem e resumo por geração (sem Unity) |
| `Eden.Application` | liga criaturas a cérebros, avaliação isolada, laço de gerações e métricas (sem Unity) |
| `Eden.Persistence` | gravação de métricas e experimentos em CSV e descrição do run (sem Unity) |
| `Eden.Presentation` | cena do laboratório: execução em tempo real, visualização, HUD e controles |
| `Eden.Editor` | build e execução de experimentos de evolução |
| `Eden.Tests.EditMode`, `Eden.Tests.PlayMode` | testes |

Outras pastas: `Scenes`, `UI` (UXML e tema do UI Toolkit), `Art` e `Settings` (assets do URP 2D).

## Roadmap

| Marco | Conteúdo | Estado |
| --- | --- | --- |
| M0 | projeto Unity, cena Bootstrap e build Windows | concluído |
| M1 | simulação determinística, regras do mundo, cérebros de referência e laboratório visual | concluído |
| M2 | rede neural fixa 12 → 16 → 3 na CPU, genoma por criatura e visualização da rede | concluído |
| M3 | evolução por gerações: avaliação isolada, elites, torneio, mutação, seeds de validação | em andamento |
| M4 | laboratório completo e checkpoints (MVP) | planejado |
| M5 | biologia parametrizada: genes de corpo com custos e trade-offs | planejado |
| M6 | ecossistema contínuo com reprodução local | planejado |
| M7 | adaptação individual (plasticidade) como experimento | planejado |
| M8 | escala orientada por perfilamento | planejado |

As tarefas de cada marco estão nas [issues](https://github.com/ninguemdev/EDEN/issues) e [milestones](https://github.com/ninguemdev/EDEN/milestones). Nenhum marco promete melhoria garantida a cada geração: estagnar ou extinguir também é um resultado válido de experimento, e os dados existem para investigar isso.

## Licença

Distribuído sob a licença MIT. Veja [LICENSE](LICENSE).
