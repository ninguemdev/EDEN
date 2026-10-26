# EDEN — Evolutionary Digital Ecosystem Network

EDEN é um laboratório visual de vida artificial feito em Unity. Uma população de criaturas simples vive numa arena 2D: cada uma percebe só o que está perto, decide o que fazer com um cérebro próprio, gasta energia para se mover e precisa encontrar comida para sobreviver. O objetivo do projeto é observar e medir como comportamentos surgem, se espalham ou desaparecem quando cérebros de redes neurais e, mais tarde, características do corpo passam de geração em geração com pequenas variações.

O foco está em experimentos reproduzíveis e inspecionáveis: com a mesma seed e a mesma configuração, a simulação produz o mesmo resultado, e qualquer criatura pode ser selecionada para ver o que ela sente e o que decidiu.

> Estado atual: os marcos **M1** (simulação determinística e baseline), **M2** (rede neural fixa), **M3** (evolução por gerações), **M4** (laboratório completo e checkpoints, o MVP) e **M5** (biologia parametrizada) estão concluídos. Genomas evoluem em episódios isolados, com seeds de validação fora da seleção e comparação com os cérebros de referência; na primeira bateria a evolução superou o aleatório, mas ainda não o heurístico. O laboratório inicia, mostra e inspeciona uma evolução pela interface, salva e retoma o experimento em checkpoints, exporta os dados com unidades, compara presets e mede o próprio desempenho no build. O genoma carrega genes de corpo limitados, cada característica com custo explícito; o laboratório pode evoluir o corpo junto com o cérebro (o corpo fixo continua o padrão e o controle), e uma ablação por gene compara as variantes com a variação entre execuções. O **M6** (ecossistema contínuo) está em andamento: no mundo compartilhado as criaturas já podem se reproduzir localmente, sem torneio, com energia conservada e teto de população. Veja o [roadmap](#roadmap).

## Como funciona

**Mundo.** Arena retangular fechada (padrão 50 × 50 unidades) com criaturas e pontos de comida. A simulação avança em ticks fixos (padrão 20 por segundo simulado). Acelerar a execução significa rodar mais ticks completos por frame, nunca aumentar o passo de tempo; o FPS da tela não altera o resultado. Na tela, cada criatura é desenhada entre a posição do tick anterior e a do atual, conforme o tempo real já passado do próximo tick, para que o movimento seja contínuo a 60 ou 144 FPS. O desenho só lê o estado: a simulação é a mesma com ou sem tela. Pausado, avançando um tick ou na velocidade máxima, aparece o último estado exato.

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
- **heurístico:** regras fixas, sem memória nem aprendizado; vai até a comida visível (reduz a velocidade para girar quando ela está de lado, senão ficaria orbitando a comida sem alcançá-la) e desvia das paredes;
- **rede neural:** perceptron 12 → 16 → 3 com `tanh` (259 parâmetros). Cada criatura tem o seu **genoma** (topologia, pesos-base e [genes de corpo](#genes-de-corpo); imutável e separado de energia, idade e posição) e a sua própria rede. No mundo compartilhado os genomas são aleatórios ou vêm da população evoluída no próprio laboratório (veja [experimentos por gerações](#evolução-por-gerações)). A rede é feed-forward: não guarda memória entre decisões nem aprende durante a vida.

Os dois primeiros servem de linha de base para comparar as redes evoluídas. Baseline preliminar com a configuração padrão (64 criaturas, 5 seeds, 60 s simulados):

| Cérebro | Comida consumida | Sobreviventes (de 320) |
| --- | --- | --- |
| Aleatório | 1179 | 25 |
| Heurístico | 4839 | 260 |

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

A cena Bootstrap mostra a arena ao vivo. À esquerda ficam os controles do experimento e o seu estado (tick, tempo simulado, velocidade pedida e efetiva, população, comida, cérebro e métricas); à direita, o inspetor da criatura selecionada (energia, idade, comida ingerida, os 12 sensores, a ação atual e, quando houver, a linhagem).

Tudo se escolhe pela interface, sem editar código:

- **Modo:** *Mundo compartilhado* (todas as criaturas na mesma arena, disputando a comida; só observação, sem regra de seleção) ou *Evolução (avaliação isolada)* (veja [abaixo](#no-laboratório)).
- **Preset:** Mínimo (32 criaturas, 64 comidas), Padrão (64, 128) ou Estresse (128, 256). Na evolução o preset define quantos genomas há por geração; o mundo de cada episódio isolado não muda.
- **Cérebro** (mundo compartilhado): heurístico, aleatório, rede aleatória ou *população evoluída* (a última geração avaliada da evolução do laboratório, um indivíduo por criatura, cada um com o seu corpo).
- **Corpo** (evolução): *Fixo (controle)*, todos com o corpo de referência, ou *Evolutivo*, em que os quatro [genes de corpo](#genes-de-corpo) são herdados e mutados. O tamanho de cada criatura na tela segue o seu raio; a selecionada mostra o alcance do sensor, e o inspetor mostra os genes, cada característica em relação à referência e o gasto por segundo.
- **Seed** e o botão **Iniciar**. Os botões de baixo pausam, avançam um tick, mudam a velocidade e ligam a velocidade **Máx.**, que roda quantos ticks inteiros couberem em 10 ms por quadro e mostra a velocidade efetiva alcançada.
- **Salvar** e **Carregar** gravam e retomam o experimento num [checkpoint](#checkpoints).

| Tecla | Ação |
| --- | --- |
| Espaço | pausar / continuar |
| `.` | avançar um tick (pausa se estiver rodando) |
| `-` / `=` | metade / dobro da velocidade (0,125× a 64×) |
| M | velocidade máxima liga/desliga |
| clique, `[` / `]` | selecionar criatura (mais próxima do clique, ou anterior/próxima por id) |
| Esc | limpar seleção |
| R | reiniciar com a mesma seed |
| B | alternar entre cérebro heurístico, aleatório e rede neural (mundo compartilhado; reinicia com a mesma seed) |
| F5 / F9 | salvar / carregar o checkpoint |

As teclas são ignoradas enquanto o campo de seed está sendo editado.

### Ver a rede neural de uma criatura

Com o cérebro **rede neural** ativo, selecionar uma criatura mostra, abaixo do inspetor, o diagrama da rede dela: 12 neurônios de entrada (com os nomes dos sensores), 16 ocultos e 3 de saída (propulsão, giro, lateral). A cor de cada neurônio é a sua ativação (verde positiva, vermelha negativa, cinza perto de zero) e a cor e a intensidade de cada conexão mostram o sinal que passa por ela (peso × ativação de origem). O diagrama é atualizado 10 vezes por segundo e corresponde exatamente à ação que a criatura está executando.

O diagrama só existe enquanto há uma criatura neural selecionada: sem seleção (ou com Esc), nada da rede é calculado nem desenhado, para não pesar na simulação.

Os demais parâmetros do mundo (tamanho da arena, energia, custos, alcance do sensor, velocidade etc.) ficam editáveis no componente **LabRunner** do objeto `Lab`. Os valores padrão são pontos de partida para medição, não valores ajustados.

Se o computador não acompanhar a velocidade pedida, o excesso de trabalho é descartado e o HUD mostra a velocidade efetiva e os ticks descartados; as regras nunca são puladas.

### Métricas

Cada execução do mundo compartilhado grava um diretório em `%USERPROFILE%\AppData\LocalLow\NinguemDev\EDEN\SimulationRuns\<data>-seed<n>-<cérebro>\` (fora do repositório), com:

- `run-info.txt`: seed, cérebro, modo de avaliação (`shared_inspection`), versão e parâmetros principais;
- `metrics.csv`: uma linha por segundo simulado, com números em formato invariável (ponto decimal):
  `tick, simulated_seconds, population, food_available, food_eaten_window, starvation_deaths_window, old_age_deaths_window, mean_energy, total_food_eaten`.

### Checkpoints

**Salvar** grava o experimento entre dois ticks em `%USERPROFILE%\AppData\LocalLow\NinguemDev\EDEN\Checkpoints\lab.json` (fora do repositório): a evolução inteira (configuração, população com os genomas, linhagem, histórico do gráfico, resultados já obtidos na geração em curso e o episódio em andamento) e, no mundo compartilhado, o mundo com a agenda de reposição, os geradores aleatórios e o estado do cérebro de cada criatura. **Carregar** volta exatamente a esse ponto: a execução retomada continua tick a tick igual à que foi salva (no mesmo computador e na mesma versão). Os logs da execução retomada vão para um diretório novo, que registra de onde ela veio: o arquivo, o hash do estado salvo, a geração, a fase e quantos episódios da avaliação em andamento já estavam prontos no checkpoint (esses vêm do arquivo e não são rodados de novo; o episódio interrompido continua do tick em que estava). Somando as linhas exportadas antes de salvar e depois de carregar, os CSVs ficam idênticos aos de uma execução sem interrupção.

O arquivo é JSON legível, com versão do formato, versão do app, configuração e hashes. A gravação escreve primeiro um arquivo temporário na mesma pasta, confere que ele pode ser lido e só então troca o checkpoint; o anterior fica como `lab.json.bak`. Ao carregar, um arquivo corrompido, editado, incoerente ou de uma versão mais nova do formato é recusado com o motivo no painel, e a execução atual continua intacta; se o principal estiver estragado, o backup é usado.

## Ecossistema contínuo: reprodução local

No mundo compartilhado as criaturas podem se reproduzir sozinhas, sem torneio nem geração global: quem nasce e quem morre depende só do que acontece no mundo. A reprodução é desligada por padrão (o mundo compartilhado continua igual ao de antes) e nunca acontece nos episódios isolados da evolução. Liga-se nas configurações do mundo do `LabRunner` no Inspector (`Reproduction`) ou com `-eden-reproduction on` no executável.

Regras (aplicadas em fase própria, depois de energia, idade e mortes, com os pais em ordem de id):

- uma criatura viva se reproduz quando já é **madura** (20 s de idade), tem pelo menos **70% da própria capacidade** de energia, terminou o **intervalo** desde o último filho (15 s) e ainda fica com energia depois de pagar;
- o filho recebe **40% da energia** do pai (no máximo a própria capacidade) e o pai perde isso **mais um custo** de 5 que não vai para ninguém: um nascimento nunca cria energia;
- o filho nasce a 1 unidade do pai, dentro da arena, com o próximo id, o pai como único ascendente e **geração genealógica** = geração do pai + 1 (as criaturas iniciais são a geração 0);
- no **teto de população** (256) o nascimento é recusado antes de qualquer sorteio ou débito; ninguém é descartado para abrir espaço, e uma população extinta não é reposta.

Com cérebros de rede (aleatória ou da população evoluída), o filho herda uma cópia mutada do genoma do pai (5% por peso ou gene de corpo expresso, σ = 0,10), sorteada de um fluxo próprio do filho, e ganha o seu cérebro. Com cérebros de referência o filho só copia os genes de corpo. O HUD mostra o teto, os nascimentos e a maior geração genealógica viva; o inspetor mostra o pai, a geração e quando a criatura poderá se reproduzir. Salvar e Carregar guardam pais, gerações, intervalos e os genomas dos nascidos. Todos esses valores são propostas a medir.

Numa primeira observação (mínimo, heurístico, seed 3, 7 563 s simulados) houve 2 346 nascimentos, a maior geração viva chegou a 109 e a população se manteve perto de 30, limitada pela comida, bem abaixo do teto. Métricas por janela, genealogia e extinção explícita na interface vêm na próxima tarefa do M6.

## Evolução por gerações

Um experimento evolui uma população de genomas (padrão 64) sem competição entre eles, no laboratório ou sem a cena:

1. **Avaliação isolada.** Cada genoma vive sozinho num episódio de 120 s simulados, uma vez para cada uma das 3 seeds de treino da geração. Todos os genomas da geração enfrentam exatamente as mesmas condições iniciais (comida, posição, direção).
2. **Fitness** = energia de comida consumida ÷ duração configurada do episódio (o denominador não diminui se a criatura morrer antes), em média nas 3 seeds. Sobrevivência, tempo vivo e energia final são medidos à parte, sem somar tudo numa nota só.
3. **Próxima geração.** Os 4 melhores ficam sem alteração (e são avaliados de novo, sem levar a nota antiga); as demais vagas vêm de torneios de 3 com reposição. Cada filho é uma cópia do pai em que cada peso tem 5% de chance de receber ruído gaussiano (σ = 0,10), sempre limitado a [−3, 3]. Não há cruzamento: cada filho tem um único pai.
4. **Linhagem.** Cada indivíduo novo recebe um id único, o id do pai e a geração em que nasceu; elites mantêm os seus. A partir de qualquer id dá para seguir a ascendência até a primeira geração.

Esses valores são propostas iniciais a medir, não garantias de melhoria. Com a mesma seed o experimento repete as mesmas gerações, ids e resultados.

### Genes de corpo

Além dos pesos, o genoma guarda quatro genes de corpo, cada um em [−1, 1]. Cada gene g dá um fator f = 1 + 0,5·g, entre 0,5 e 1,5, e nenhuma característica vem sem custo:

| Gene | Ganho | Custo |
| --- | --- | --- |
| tamanho (f<sub>t</sub>) | raio do corpo (a comida dentro dele é consumida) e capacidade de energia × f<sub>t</sub> | metabolismo e custo de movimento × f<sub>t</sub><sup>1</sup> |
| velocidade (f<sub>v</sub>) | velocidade máxima × f<sub>v</sub> | custo de movimento × f<sub>v</sub><sup>2</sup> |
| sensor (f<sub>s</sub>) | alcance da percepção de comida e paredes × f<sub>s</sub> | custo do sensor × f<sub>s</sub><sup>2</sup> (a área percebida) |
| eficiência (m = 1 − 0,5·g) | metabolismo × m | aceleração × m<sup>1</sup> (metabolismo poupador, menos potência) |

Os expoentes de custo ficam na configuração (`SizeCostExponent`, `SpeedCostExponent`, `SensorCostExponent`, `EfficiencyPowerExponent`, entre 0 e 4; 0 desliga aquele custo, só para experimentos de controle) e são propostas a medir. Um gene negativo inverte a troca: corpo menor gasta menos mas alcança e guarda menos, e assim por diante. Os testes de regra conferem que, com os valores padrão, nenhum corpo é pelo menos tão bom quanto o de referência em tudo, e que cada custo aparece no gasto de energia do mundo. O corpo é calculado quando a criatura nasce e não muda durante a vida; ela começa com a energia inicial ou com a sua capacidade, se for menor. As leituras dos sensores são relativas ao próprio corpo (energia pela própria capacidade, proximidade pelo próprio alcance, velocidade pela própria velocidade máxima).

Um experimento escolhe quais genes são expressos (`ExpressedBodyGenes`; no laboratório, **Corpo**; sem a cena, `-eden-body`). O padrão é **nenhum**: todas as criaturas têm o corpo de referência, exatamente como antes dos genes existirem (os mesmos sorteios e os mesmos resultados), e isso continua disponível como controle. Os genes expressos começam sorteados dentro dos limites na geração 0 (depois dos pesos, que não mudam) e mutam como os pesos (5% de chance por gene, ruído gaussiano σ = 0,10, limitado a [−1, 1]); os demais ficam neutros e não sorteiam nada. Os cérebros de referência sempre usam o corpo de referência.

### No laboratório

Com o modo **Evolução (avaliação isolada)**, o laboratório roda o experimento episódio por episódio sem travar a tela, e a arena mostra o episódio que está sendo avaliado (uma criatura sozinha). O painel indica a geração, a fase (treino, referências na validação ou validação), o indivíduo e a seed do episódio, e deixa explícito que a seleção é artificial (torneio pela fitness de treino), não seleção natural. A cada 5 gerações, a começar pela 0, a geração treinada é validada nas 5 seeds separadas; antes da primeira validação o aleatório e o heurístico são avaliados nessas mesmas seeds.

O gráfico abaixo do painel mostra, por geração, a mediana do treino com a faixa entre quartis, o melhor do treino, a mediana da validação e o campeão (o melhor do treino) na validação, com linhas tracejadas para o aleatório e o heurístico; o painel diz em texto se o campeão supera ou fica abaixo de cada referência. Selecionar a criatura mostra a linhagem do indivíduo: id, geração de nascimento, pai e pesos mutados, se é elite e os ancestrais até o fundador.

Para ver a população no mundo compartilhado, escolha *Mundo compartilhado* com o cérebro *População evoluída*: a evolução fica parada, cada criatura é um indivíduo da última geração avaliada (com a sua linhagem no inspetor) e o botão **Voltar à evolução** continua de onde parou. O experimento do laboratório é exportado em `EvolutionRuns\<data>-lab-seed<n>-<preset>\`, no mesmo formato descrito abaixo. Com a mesma seed e o mesmo preset, as gerações e os números são os mesmos de uma execução sem a cena, em qualquer velocidade.

### Sem a cena

Para rodar: menu **EDEN → Evolution → Run 20 generations (seed 1)** no Editor (o Editor fica ocupado durante a execução, cerca de 20 s) ou, com o Editor fechado:

```powershell
& "C:\Program Files\Unity\Hub\Editor\6000.3.27f1\Editor\Unity.exe" -batchmode -projectPath . `
  -executeMethod Eden.Editor.EvolutionBatch.RunFromCommandLine -eden-seed 1 -eden-generations 20 -logFile evolution.log
```

Opções: `-eden-seed N`, `-eden-generations N`, `-eden-food N` (comida do episódio, padrão 32), `-eden-body none|all|size,speed,sensor,efficiency` (genes de corpo expressos, padrão `none`) e `-eden-output DIR`. Sem `-eden-output`, o resultado vai para `%USERPROFILE%\AppData\LocalLow\NinguemDev\EDEN\EvolutionRuns\<data>-seed<n>\`:

- `experiment.txt` e `experiment.json`: todos os parâmetros do experimento, o hash da configuração (o mesmo texto canônico que os checkpoints usam: hash igual, experimento igual), o modo de avaliação e a unidade de cada coluna dos CSVs; numa execução retomada, também a origem descrita em [checkpoints](#checkpoints);
- `summary.json`: reescrito a cada geração com os totais de treino e validação, os últimos resumos, o melhor resultado de treino e o campeão na validação;
- `generations.csv`: uma linha por geração com melhor, quartis, mediana, pior e média do fitness, sobrevivência, diversidade dos pesos (desvio padrão médio por peso e distância média ao centróide), indivíduos novos, elites e pais distintos;
- `individuals.csv`: cada indivíduo avaliado, com id, pai, geração de nascimento e resultados;
- `episodes.csv`: cada indivíduo × seed, antes de qualquer média.

Os três CSVs se ligam por geração, modo de avaliação (`training` ou `validation`) e candidato; números sempre com ponto decimal. O fitness é medido em energia de comida por segundo simulado. O fitness de treino sozinho não separa progresso real de sobreajuste; para isso servem a validação e a bateria abaixo.

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

Referências nos mesmos cenários: aleatório 0,13 (nunca sobrevive aos 120 s) e heurístico 4,65 (sempre sobrevive). A mediana final superou a da geração 0 e o aleatório nas 5 execuções, mas **não superou o heurístico em nenhuma**: depois de 30 gerações as redes evoluídas ainda ficam bem abaixo de regras simples escritas à mão. A variação entre execuções é grande e a diversidade dos pesos cai depressa nas primeiras gerações. É um resultado com esta configuração, não uma garantia.

### Comparar presets

A comparação roda a mesma bateria (mesmas seeds de experimento e de validação, mesmas gerações e o mesmo episódio) uma vez para cada preset, então os presets diferem só no número de genomas por geração:

```powershell
& "C:\Program Files\Unity\Hub\Editor\6000.3.27f1\Editor\Unity.exe" -batchmode -projectPath . `
  -executeMethod Eden.Editor.EvolutionBatch.ComparePresetsFromCommandLine -eden-seed 1 -logFile presets.log
```

Aceita as opções da bateria e `-eden-presets minimal,standard,stress` (padrão: os três). O diretório contém `presets.csv` (uma linha por preset), `comparison.json` (definição comum, hash da configuração de cada preset, resultados e custo), `comparison.txt` e, por preset, os arquivos da bateria e de cada execução. Um preset maior avalia mais episódios por geração, por isso o relatório mostra quantos episódios cada um usou e só declara um vencedor quando a diferença passa da variação entre execuções.

Primeira comparação (bateria de aceitação, 5 execuções × 30 gerações, seeds 1–5, seed de validação 1):

| Preset | Genomas | Mediana na validação, geração final | Campeão do treino | Episódios de treino | Tempo de relógio |
| --- | --- | --- | --- | --- | --- |
| Mínimo | 32 | 2,30 ± 1,08 | 2,54 ± 1,15 | 14 400 | 100 s |
| Padrão | 64 | 2,43 ± 0,93 | 2,53 ± 0,94 | 28 800 | 195 s |
| Estresse | 128 | 2,80 ± 1,08 | 2,90 ± 1,33 | 57 600 | 395 s |

O estresse teve a maior média, com 4× os episódios de treino do mínimo, mas a diferença fica dentro da variação entre execuções: com este orçamento os presets não se separam. Nos três, a mediana final superou o aleatório e a própria geração 0 em todas as execuções e não superou o heurístico (4,65) em nenhuma. O padrão repete exatamente a bateria acima.

**Economia do episódio isolado.** Os valores (32 comidas, energia inicial 50, metabolismo 1/s, movimento 2/s, episódio de 120 s) foram medidos contra os cérebros de referência numa grade de combinações (comida 32/64/128, energia inicial 50/100, movimento 2/1, metabolismo 1/0,5, duração 120/60 s) em 20 seeds de calibração próprias, que não são de treino nem de validação. Com eles o heurístico sobrevive em todos os episódios (fitness 4,37 ± 0,36) e o aleatório em nenhum (0,09 ± 0,11), então foram mantidos. Para repetir a medição:

```powershell
& "C:\Program Files\Unity\Hub\Editor\6000.3.27f1\Editor\Unity.exe" -batchmode -projectPath . `
  -executeMethod Eden.Editor.EpisodeCalibration.RunFromCommandLine -eden-seed 1 -eden-seeds 20 -logFile calibration.log
```

### Ablação dos genes de corpo

A ablação roda a mesma bateria (mesmas seeds de experimento e de validação, mesmas gerações, população e episódio) uma vez por variante de corpo: `fixed` (corpo fixo, controle), `all` (os quatro genes) e `no-<gene>` (todos menos um). Com `-eden-variants extended` entram também `only-<gene>` (um gene só); a lista pode ser dada explicitamente, por exemplo `-eden-variants fixed,all,only-sensor`. As variantes mudam só os genes expressos, então cada uma avalia exatamente os mesmos episódios:

```powershell
& "C:\Program Files\Unity\Hub\Editor\6000.3.27f1\Editor\Unity.exe" -batchmode -projectPath . `
  -executeMethod Eden.Editor.EvolutionBatch.AblateBodyFromCommandLine -eden-seed 1 -logFile ablation.log
```

Aceita as opções da bateria. O diretório contém `variants.csv` (uma linha por variante, com a diferença para o corpo fixo e para todos os genes e os genes médios finais), `phenotypes.csv` (média e desvio de cada gene em cada geração de treino de cada execução, e a **diversidade de corpo**: o desvio médio dos genes expressos, o equivalente da diversidade dos pesos), `ablation.json`, `ablation.txt` e, por variante, os arquivos da bateria e de cada execução. Uma diferença só é tratada como efeito quando os intervalos média ± desvio entre execuções não se sobrepõem.

Primeira ablação (bateria de aceitação, 5 execuções × 30 gerações × 64 genomas, seeds 1–5, seed de validação 1, custos padrão; 28 800 episódios de treino e 11 200 de validação por variante):

| Variante | Genes | Mediana na validação, geração final | Campeão do treino | Sobrevivência | Diversidade de corpo final |
| --- | --- | --- | --- | --- | --- |
| fixed | — | 2,43 ± 0,93 | 2,53 ± 0,94 | 61% | 0 |
| all | tamanho, velocidade, sensor, eficiência | 3,18 ± 0,83 | 3,43 ± 0,77 | 86% | 0,050 ± 0,014 |
| no-size | velocidade, sensor, eficiência | 2,97 ± 1,19 | 3,09 ± 0,87 | 66% | 0,047 ± 0,013 |
| no-speed | tamanho, sensor, eficiência | 3,00 ± 1,71 | 3,13 ± 1,93 | 60% | 0,056 ± 0,020 |
| no-sensor | tamanho, velocidade, eficiência | 3,02 ± 0,67 | 3,61 ± 0,36 | 74% | 0,057 ± 0,016 |
| no-efficiency | tamanho, velocidade, sensor | 3,84 ± 0,59 | 3,93 ± 0,67 | 84% | 0,049 ± 0,007 |

O controle reproduz exatamente a bateria do M3. Todas as variantes com genes de corpo tiveram média acima do corpo fixo (de +0,54 a +1,41), mas nenhuma diferença passa da variação entre execuções, nem para o corpo fixo nem para todos os genes: com este orçamento nenhum gene isolado mostra efeito, e o relatório diz isso. A diversidade de corpo cai de cerca de 0,56 na geração 0 para cerca de 0,05 já na geração 5, o mesmo colapso precoce observado nos pesos ([#44](https://github.com/ninguemdev/EDEN/issues/44)); os genes médios finais variam muito entre execuções (por exemplo, tamanho +0,24 ± 0,53 com todos os genes), então a direção que cada linhagem toma depende mais da execução do que de uma pressão consistente. Nenhuma variante superou o heurístico (4,65) na mediana, exceto uma execução de `no-speed`.

## Testes

Pelo Editor: **Window → General → Test Runner**, abas EditMode e PlayMode.

- **EditMode:** gerador aleatório (com os vetores oficiais do PCG32 e do SplitMix64), configuração, relógio de ticks, agenda de eventos, sensores, regras do mundo, cérebros, rede neural, genoma, genes de corpo (limites, herança e mutação, expressão no mundo, corpo fixo idêntico ao anterior e regras de custo: nenhum corpo melhor que a referência em tudo), avaliação isolada, seleção, mutação, linhagem, validação separada do treino, bateria, métricas e CSV, checkpoints (retomada idêntica à execução ininterrupta, floats exatos em outra cultura, arquivos corrompidos ou de versão futura recusados, arquivos de antes dos genes de corpo ainda lidos), ticks sem alocação de memória após o aquecimento (gravador de alocações da Unity), exportação (JSON com unidades; CSVs de antes de salvar e depois de carregar, no meio de um episódio, iguais aos da execução sem interrupção, sem episódio repetido), comparação de presets, reprodução local (maturidade, limiar, intervalo, teto sem custo, energia conservada, herança com fluxo próprio do filho, retomada idêntica com nascimentos), ablação dos genes de corpo (mesmo orçamento por variante, controle idêntico à bateria comum, diversidade de corpo, relatório), além de execuções longas com verificação de invariantes e independência de FPS.
- **PlayMode:** a cena Bootstrap real: renderização, pausa, avanço de um tick, inspeção por id, gravação do CSV e os controles do laboratório, inclusive salvar, carregar, a medição de desempenho, o corpo evolutivo (tamanho desenhado, alcance do sensor e inspetor) e a reprodução no mundo compartilhado (filhos com genoma e cérebro próprios, salvar e carregar com nascimentos).

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

O executável sai em `Builds/Windows/EDEN.exe` e roda sem o Editor. Para um build **Development** (com os contadores do profiler, usados para contar alocações na medição de desempenho), use o menu **EDEN → Build → Windows x64 (Development)** ou o método `BuildDevelopmentFromCommandLine`; ele sai em `Builds/WindowsDevelopment/EDEN.exe`. Opções de linha de comando do executável:

| Opção | Efeito |
| --- | --- |
| `-eden-mode shared\|evolution` | modo inicial (mundo compartilhado ou evolução) |
| `-eden-preset minimal\|standard\|stress` | preset inicial |
| `-eden-brain heuristic\|random\|neural` | cérebro inicial do mundo compartilhado |
| `-eden-body fixed\|evolving` | corpo da evolução (padrão `fixed`) |
| `-eden-reproduction on\|off` | reprodução local no mundo compartilhado (padrão `off`) |
| `-eden-seed N` | seed do run (ou do experimento de evolução) |
| `-eden-select ID` | já começa com a criatura `ID` selecionada |
| `-eden-max` | já começa na velocidade máxima |

| `-eden-benchmark S` | mede o desempenho por `S` segundos e fecha (veja abaixo) |
| `-eden-benchmark-warmup S` | aquecimento antes da medição (padrão 5 s) |
| `-eden-benchmark-output DIR` | onde gravar o relatório |

Exemplos: `EDEN.exe -eden-brain neural -eden-select 5` e `EDEN.exe -eden-mode evolution -eden-preset minimal -eden-max -eden-select 1`.

## Desempenho

Para medir, rode o executável com o cenário e `-eden-benchmark`, por exemplo:

```powershell
.\Builds\Windows\EDEN.exe -eden-mode shared -eden-preset minimal -eden-brain heuristic -eden-benchmark 30 `
  -screen-fullscreen 0 -screen-width 1280 -screen-height 720
```

Depois do aquecimento, o laboratório mede pelo tempo pedido, ignorando teclado e mouse, grava `performance.json` e `performance.txt` em `%USERPROFILE%\AppData\LocalLow\NinguemDev\EDEN\Benchmarks\<data>-<modo>-<preset>-<velocidade>-<build>\` e fecha. Os logs de métricas ficam desligados durante a medição. O relatório registra:

- o hardware e a configuração: CPU, núcleos, RAM, GPU, API gráfica, sistema, janela e monitor, qualidade, vsync, tipo de build;
- FPS e tempo de quadro (mediana, p95 e máximo), tempo de simulação por quadro, ticks por segundo efetivos e pedidos;
- no mundo compartilhado, o custo de cada tick e das fases de sensores e de decisão (inferência);
- coletas de lixo, heap gerenciado e memória da engine;
- no build Development, as alocações feitas durante os ticks: por criatura-tick, ou por episódio na evolução.

O que o runtime não fornece aparece como "indisponível", nunca como zero.

**Sem alocação por tick.** Depois do aquecimento, um tick do mundo compartilhado ou de um episódio isolado não aloca memória gerenciada: a comida consumida é reaproveitada na reposição, as listas são dimensionadas para o máximo possível e nenhum laço cria enumeradores. Os testes medem isso com o gravador de alocações da Unity, com os três cérebros, incluindo comida comida, reposições e mortes. O que ainda aloca:

- a criação de cada episódio isolado: cerca de 78 alocações por episódio no build Development;
- a primeira execução de um caminho de código no processo, que no Mono pode alocar uma vez (JIT). Por exemplo, a primeira morte gerou 1 alocação numa medição de 30 s; depois disso, zero.

**Primeira medição** (build release Mono, i5-13400, 16 GB, GPU Intel UHD Graphics 730, Windows 11, janela de 1280 × 720, qualidade Ultra com vsync a 60 Hz, 30 s após 5 s de aquecimento, seed 1, velocidade 1×):

| Cenário | Criaturas (média) | FPS | Ticks/s | Tick mediana / p95 | Sensores (mediana) | Decisão (mediana) |
| --- | --- | --- | --- | --- | --- | --- |
| Mínimo, heurístico | 31,7 | 60,0 | 20,0 | 0,10 / 0,53 ms | 0,06 ms | 0,006 ms |
| Padrão, heurístico | 61,9 | 60,0 | 20,0 | 0,90 / 1,60 ms | 0,56 ms | 0,014 ms |
| Estresse, heurístico | 127,2 | 59,9 | 20,0 | 1,53 / 2,85 ms | 0,94 ms | 0,020 ms |
| Mínimo, rede neural | 17,2 | 60,0 | 20,0 | 0,31 / 0,94 ms | 0,13 ms | 0,024 ms |
| Padrão, rede neural | 45,6 | 60,0 | 20,0 | 1,27 / 2,66 ms | 0,67 ms | 0,050 ms |
| Estresse, rede neural | 98,1 | 60,0 | 20,0 | 3,27 / 5,69 ms | 1,76 ms | 0,052 ms |

Na evolução do preset mínimo em velocidade máxima, o laboratório manteve 59,7 FPS e cerca de 187 mil ticks/s, concluindo 2.458 episódios em 30 s.

O que esses números dizem:

- **A meta do preset mínimo (30 FPS e tick de 20 Hz em tempo real) foi atingida com folga em todos os presets.** O tick mais caro (estresse, p95 de 5,7 ms) usa cerca de 11% do intervalo de 50 ms entre ticks.
- **Os sensores dominam o custo.** Cada criatura procura a comida mais próxima entre todas, então o custo cresce com criaturas × comida. A inferência da rede custa pouco.
- **Com redes neurais aleatórias a população morre de fome** (de 32 para 7 no mínimo em 35 s), por isso a média de criaturas é menor nessas linhas.
- O build Development dá números da mesma ordem.
- **Os valores são de uma máquina em uso, não de um benchmark controlado.** Com CPU híbrida (núcleos P e E) e a janela em segundo plano, o mesmo cenário variou até ~4× entre sessões: 0,22 a 0,99 ms de mediana no padrão heurístico. Repetições na mesma sessão variaram até ~12%.
- **Memória:** working set de ~340 MB no release (~650 MB no Development), com heap gerenciado de 10 a 16 MB. No mundo compartilhado houve 0 ou 1 coleta de lixo em 30 s; na evolução em velocidade máxima, ~15 coletas em 30 s, vindas da criação dos episódios, sem efeito visível no FPS.
- **A GPU usada foi a integrada.** Uma RTX 2060 não seria prova de capacidade de simulação, que é limitada pela CPU.

**Rede.** O código do EDEN não usa rede. A engine, porém, abre conexões HTTPS de telemetria da Unity quando há internet. Com todas as rotas HTTP fechadas (proxy inválido), o player não abriu nenhuma conexão e funcionou igual: 60 FPS, 20 ticks/s, mesma população.

## Estrutura

O código fica em `Assets/_Project` e é dividido em assemblies com responsabilidades separadas. As regras não dependem da Unity: a cena apenas mostra o estado calculado pela simulação.

| Assembly | Responsabilidade |
| --- | --- |
| `Eden.Domain` | ids, configuração, estado do mundo, observação, ação, gerador aleatório e seeds (sem Unity) |
| `Eden.Simulation` | tick fixo, relógio, agenda de eventos, sensores, movimento, alimentação, energia e morte (sem Unity) |
| `Eden.Brains` | contrato de cérebro e cérebros de referência (sem Unity) |
| `Eden.Evolution` | plano de avaliação, fitness, seleção, mutação, linhagem e resumo por geração (sem Unity) |
| `Eden.Application` | liga criaturas a cérebros, avaliação isolada (inteira ou em partes), laço de gerações, sessão de evolução, presets e métricas (sem Unity) |
| `Eden.Persistence` | gravação de métricas e experimentos em CSV, descrição do run e checkpoints JSON versionados (sem Unity) |
| `Eden.Presentation` | cena do laboratório: execução em tempo real, visualização, HUD, controles e gráfico por geração |
| `Eden.Editor` | build, experimentos de evolução e calibração do episódio |
| `Eden.Tests.EditMode`, `Eden.Tests.PlayMode` | testes |

Outras pastas: `Scenes`, `UI` (UXML e tema do UI Toolkit), `Art` e `Settings` (assets do URP 2D).

## Roadmap

| Marco | Conteúdo | Estado |
| --- | --- | --- |
| M0 | projeto Unity, cena Bootstrap e build Windows | concluído |
| M1 | simulação determinística, regras do mundo, cérebros de referência e laboratório visual | concluído |
| M2 | rede neural fixa 12 → 16 → 3 na CPU, genoma por criatura e visualização da rede | concluído |
| M3 | evolução por gerações: avaliação isolada, elites, torneio, mutação, seeds de validação | concluído |
| M4 | laboratório completo e checkpoints (MVP) | concluído |
| M5 | biologia parametrizada: genes de corpo com custos e trade-offs, ablação por gene | concluído |
| M6 | ecossistema contínuo com reprodução local | em andamento |
| M7 | adaptação individual (plasticidade) como experimento | planejado |
| M8 | escala orientada por perfilamento | planejado |

As tarefas de cada marco estão nas [issues](https://github.com/ninguemdev/EDEN/issues) e [milestones](https://github.com/ninguemdev/EDEN/milestones). Nenhum marco promete melhoria garantida a cada geração: estagnar ou extinguir também é um resultado válido de experimento, e os dados existem para investigar isso.

## Licença

Distribuído sob a licença MIT. Veja [LICENSE](LICENSE).
