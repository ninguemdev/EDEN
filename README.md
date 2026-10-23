# EDEN — Evolutionary Digital Ecosystem Network

EDEN é um laboratório visual de vida artificial feito em Unity. Uma população de criaturas simples vive numa arena 2D: cada uma percebe só o que está perto, decide o que fazer com um cérebro próprio, gasta energia para se mover e precisa encontrar comida para sobreviver. O objetivo do projeto é observar e medir como comportamentos surgem, se espalham ou desaparecem quando cérebros de redes neurais e, mais tarde, características do corpo passam de geração em geração com pequenas variações.

O foco está em experimentos reproduzíveis e inspecionáveis: com a mesma seed e a mesma configuração, a simulação produz o mesmo resultado, e qualquer criatura pode ser selecionada para ver o que ela sente e o que decidiu.

> Estado atual: o marco **M1** (simulação determinística e baseline) está concluído e o **M2** (rede neural fixa) está em andamento: cada criatura já pode ter a sua própria rede 12 → 16 → 3, ainda com pesos aleatórios. A evolução por gerações (M3) vem em seguida. Veja o [roadmap](#roadmap).

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
- **rede neural:** perceptron 12 → 16 → 3 com `tanh` (259 parâmetros). Cada criatura tem o seu **genoma** (topologia e pesos-base, imutável e separado de energia, idade e posição) e a sua própria rede. Por enquanto os genomas são aleatórios; selecionar e mutar genomas é o M3. A rede é feed-forward: não guarda memória entre decisões nem aprende durante a vida.

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

## Testes

Pelo Editor: **Window → General → Test Runner**, abas EditMode e PlayMode.

- **EditMode:** gerador aleatório (com os vetores oficiais do PCG32 e do SplitMix64), configuração, relógio de ticks, agenda de eventos, sensores, regras do mundo, cérebros, controlador, métricas e CSV, além de execuções longas com verificação de invariantes e independência de FPS.
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
| `Eden.Application` | liga criaturas a cérebros e agrega métricas (sem Unity) |
| `Eden.Persistence` | gravação de métricas em CSV e descrição do run (sem Unity) |
| `Eden.Presentation` | cena do laboratório: execução em tempo real, visualização, HUD e controles |
| `Eden.Editor` | ferramenta de build |
| `Eden.Tests.EditMode`, `Eden.Tests.PlayMode` | testes |

Outras pastas: `Scenes`, `UI` (UXML e tema do UI Toolkit), `Art` e `Settings` (assets do URP 2D).

## Roadmap

| Marco | Conteúdo | Estado |
| --- | --- | --- |
| M0 | projeto Unity, cena Bootstrap e build Windows | concluído |
| M1 | simulação determinística, regras do mundo, cérebros de referência e laboratório visual | concluído |
| M2 | rede neural fixa 12 → 16 → 3 na CPU, genoma por criatura e visualização da rede | em andamento |
| M3 | evolução por gerações: avaliação isolada, elites, torneio, mutação, seeds de validação | planejado |
| M4 | laboratório completo e checkpoints (MVP) | planejado |
| M5 | biologia parametrizada: genes de corpo com custos e trade-offs | planejado |
| M6 | ecossistema contínuo com reprodução local | planejado |
| M7 | adaptação individual (plasticidade) como experimento | planejado |
| M8 | escala orientada por perfilamento | planejado |

As tarefas de cada marco estão nas [issues](https://github.com/ninguemdev/EDEN/issues) e [milestones](https://github.com/ninguemdev/EDEN/milestones). Nenhum marco promete melhoria garantida a cada geração: estagnar ou extinguir também é um resultado válido de experimento, e os dados existem para investigar isso.

## Licença

Distribuído sob a licença MIT. Veja [LICENSE](LICENSE).
