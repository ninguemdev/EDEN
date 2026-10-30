# EDEN — Evolutionary Digital Ecosystem Network

EDEN é um laboratório visual de vida artificial feito em Unity. Uma população de criaturas simples vive numa arena 2D: cada uma percebe só o que está perto, decide o que fazer com um cérebro próprio, gasta energia para se mover e precisa encontrar comida para sobreviver. O objetivo do projeto é observar e medir como comportamentos surgem, se espalham ou desaparecem quando cérebros de redes neurais e, mais tarde, características do corpo passam de geração em geração com pequenas variações.

O foco está em experimentos reproduzíveis e inspecionáveis: com a mesma seed e a mesma configuração, a simulação produz o mesmo resultado, e qualquer criatura pode ser selecionada para ver o que ela sente e o que decidiu.

> Estado atual: os marcos **M1** (simulação determinística e baseline), **M2** (rede neural fixa), **M3** (evolução por gerações), **M4** (laboratório completo e checkpoints, o MVP), **M5** (biologia parametrizada), **M6** (ecossistema contínuo) e **M7** (adaptação individual, experimental) estão concluídos. Genomas evoluem em episódios isolados, com seeds de validação fora da seleção e comparação com os cérebros de referência; na primeira bateria a evolução superou o aleatório, mas ainda não o heurístico. O laboratório inicia, mostra e inspeciona uma evolução pela interface, salva e retoma o experimento em checkpoints, exporta os dados com unidades, compara presets e mede o próprio desempenho no build. O genoma carrega genes de corpo limitados, cada característica com custo explícito; o laboratório pode evoluir o corpo junto com o cérebro (o corpo fixo continua o padrão e o controle), e uma ablação por gene compara as variantes com a variação entre execuções. No mundo compartilhado as criaturas podem se reproduzir localmente, sem torneio, com energia conservada (auditada), teto de população, genealogia e extinção explícita sem reposição. Redes podem ter plasticidade limitada durante a vida (desligada por padrão), comparada com redes fixas num mundo que muda; até aqui sem benefício mensurável. O próximo marco é o **M8** (escala orientada por perfilamento). Veja o [roadmap](#roadmap).

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
- **rede neural:** perceptron 12 → 16 → 3 com `tanh` (259 parâmetros). Cada criatura tem o seu **genoma** (topologia, pesos-base e [genes de corpo](#genes-de-corpo); imutável e separado de energia, idade e posição) e a sua própria rede. No mundo compartilhado os genomas são aleatórios ou vêm da população evoluída no próprio laboratório (veja [experimentos por gerações](#evolução-por-gerações)). A rede é feed-forward: não guarda memória entre decisões nem aprende durante a vida, salvo no experimento de [plasticidade](#plasticidade-durante-a-vida-experimento-m7), desligado por padrão.

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
- **Preset:** Mínimo (32 criaturas, 64 comidas), Padrão (64, 128), Estresse (128, 256) ou Mil (1000, 2000). Na evolução o preset define quantos genomas há por geração; o mundo de cada episódio isolado não muda. O teto de população é elevado, se necessário, para comportar a população inicial com reprodução ativada.
- **Cérebro** (mundo compartilhado): heurístico, aleatório, rede aleatória ou *população evoluída* (a última geração avaliada da evolução do laboratório, um indivíduo por criatura, cada um com o seu corpo).
- **Corpo** (evolução): *Fixo (controle)*, todos com o corpo de referência, ou *Evolutivo*, em que os quatro [genes de corpo](#genes-de-corpo) são herdados e mutados. O tamanho de cada criatura na tela segue o seu raio; a selecionada mostra o alcance do sensor, e o inspetor mostra os genes, cada característica em relação à referência e o gasto por segundo.
- **Algoritmo** (evolução): *MLP fixa (controle)* ou *NEAT*, em que a estrutura da rede também evolui (veja [NEAT](#neat-em-desenvolvimento)).
- **Treino** (evolução): *Sequencial* ou *Paralelo (CPU)*. No paralelo, os episódios rodam em mundos independentes, distribuídos entre os processadores disponíveis; a arena mostra um desses mundos. Combine com **Máx.** para acelerar o treino.
- **Sentidos** (evolução): *Legados* (círculo de comida e radar de paredes) ou *Olhos fixos* ou *Olhos evolutivos* (só com NEAT; veja [olhos](#olhos-em-desenvolvimento)). A criatura selecionada mostra o cone de cada olho, com um ponto na comida que cada olho vê agora, e o inspetor lista os olhos, o gasto de cada um e o que os olhos entregaram ao cérebro na última decisão.
- **Arena** (os dois modos): *Aberta* (o controle: criaturas são pontos que se atravessam), *Corpos sólidos*, *Sólidos + pilares* ou *Sólidos + corredor* (veja [corpos sólidos](#corpos-sólidos-em-desenvolvimento)). A mesma escolha vale para o mundo compartilhado e para os episódios da evolução. Os obstáculos aparecem em cinza; com corpos sólidos cada criatura é desenhada do tamanho do disco que colide, a selecionada mostra num halo claro até onde a boca alcança, e um ponto laranja marca cada par de corpos encostados. O resumo mostra o cenário, os contatos do último tick, os nascimentos recusados (sem espaço ao lado do pai ou pelo teto) e as reposições de comida adiadas; o inspetor mostra o raio do disco, em quem a criatura está encostada (criaturas, parede, obstáculo), a comida que os obstáculos escondem dos olhos ou da boca (e, para os sentidos legados, quando o que eles apontam está atrás de um obstáculo) e o motivo do último nascimento recusado.
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
  `tick, simulated_seconds, population, food_available, food_eaten_window, starvation_deaths_window, old_age_deaths_window, mean_energy, total_food_eaten, births_window, births_refused_window, max_generation, extinct, dehydration_deaths_window, contacts_per_tick_window, births_blocked_window, respawns_delayed_window` (as quatro últimas: mortes de sede, pares de corpos encostados por tick em média, nascimentos recusados por falta de espaço ao lado do pai e reposições de comida adiadas por falta de lugar). Não há geração global no mundo compartilhado: as janelas de tempo são a unidade. O tick em que a última criatura morre fecha a sua janela na hora, com `extinct = 1`;
- `genealogy.csv` (só com reprodução ligada): os eventos da genealogia em ordem de tick, `tick, event, creature, parent, generation, cause`, com `event` = `founder`, `death`, `birth` ou `extinction`. Um run retomado de checkpoint escreve antes o histórico salvo, então o arquivo sempre tem a linha completa de cada criatura.

**Auditoria de energia.** A cada tick a simulação registra de onde veio e para onde foi a energia das criaturas: comida ingerida, custos de viver efetivamente pagos (ninguém paga mais do que tem), energia de comida que não coube numa criatura cheia, energia passada aos filhos, custo dos nascimentos e energia levada por quem morre, além da comida reposta. A energia total das criaturas muda exatamente pelo saldo desses fluxos. Num teste de 6 000 ticks com 162 nascimentos, 4 628 nascimentos recusados no teto, 90 mortes por fome e 74 por idade, a conta fecha tick a tick e no total (resíduo de 0,02 em 15 830 de energia de comida, só arredondamento de float), e a de comida fecha exatamente (inicial + reposta − ingerida = disponível). Com 30 comidas o mesmo mundo sustenta em média 7,8 criaturas; com 120, 83,6: a competição pelo recurso limita a população.

**Auditoria do ambiente físico.** Os fluxos também separam a comida que surgiu (reposições), a comida ingerida, a absorvida e a desperdiçada (a que não coube), e os custos cobrados por causa — manutenção (metabolismo), sensor legado, esforço (empuxo e deslocamento lateral, inclusive empurrar outros corpos, que não tem custo próprio), olhos e giro — além do que não pôde ser pago por quem ficou sem energia. `WorldAudit` acompanha um mundo desde um ponto de partida e mede o erro de fechamento de cada balanço: energia das criaturas, energia da comida no mundo, hidratação das criaturas e água das fontes limitadas, e se as partes dos custos somam o que foi cobrado. Num teste de 2 400 ticks com corpos sólidos, pilares, dois lagos finitos, metade das criaturas com olhos, nascimentos (36 recusados por espaço) e 19 193 contatos, todas as contas fecham tick a tick e no total, com maior resíduo de 0,06 em cerca de 10 mil de energia (arredondamento de float). No laboratório, o resumo do mundo compartilhado mostra essa auditoria desde o início (ou o carregamento) e o fechamento de cada conta.

**Extinção.** Quando a última criatura do mundo compartilhado morre, o laboratório pausa e o mundo não roda mais: ninguém é reposto. O HUD mostra o tick da extinção e indica **Iniciar** (ou R) para começar um mundo novo, e a extinção fica registrada nos dois CSVs.

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

Numa primeira observação (mínimo, heurístico, seed 3, 7 563 s simulados) houve 2 346 nascimentos, a maior geração viva chegou a 109 e a população se manteve perto de 30, limitada pela comida, bem abaixo do teto. Com reprodução ligada, o inspetor mostra também a **genealogia** da criatura selecionada, mesmo depois da sua morte: a linha até a fundadora (os ancestrais mais próximos e a fundadora, quando a linha é longa) e quantos descendentes ela tem e quantos estão vivos. Salvar e Carregar guardam a genealogia inteira, inclusive os mortos.

## Evolução por gerações

Um experimento evolui uma população de genomas (padrão 64) sem competição entre eles, no laboratório ou sem a cena:

1. **Avaliação isolada.** Cada genoma vive sozinho num episódio de 120 s simulados, uma vez para cada uma das 3 seeds de treino da geração. Todos os genomas da geração enfrentam exatamente as mesmas condições iniciais (comida, posição, direção).
2. **Fitness** = energia de comida consumida ÷ duração configurada do episódio (o denominador não diminui se a criatura morrer antes), em média nas 3 seeds. Sobrevivência, tempo vivo e energia final são medidos à parte, sem somar tudo numa nota só.
3. **Próxima geração.** Os 4 melhores ficam sem alteração (e são avaliados de novo, sem levar a nota antiga); as demais vagas vêm de torneios de 3 com reposição. Cada filho é uma cópia do pai em que cada peso tem 5% de chance de receber ruído gaussiano (σ = 0,10), sempre limitado a [−3, 3]. Não há cruzamento: cada filho tem um único pai.
4. **Linhagem.** Cada indivíduo novo recebe um id único, o id do pai e a geração em que nasceu; elites mantêm os seus. A partir de qualquer id dá para seguir a ascendência até a primeira geração.

Esses valores são propostas iniciais a medir, não garantias de melhoria. Com a mesma seed o experimento repete as mesmas gerações, ids e resultados.

Essa evolução de **topologia fixa** (MLP 12 → 16 → 3, só pesos e genes de corpo mudam) é o padrão e o controle. Não é NEAT.

### NEAT (em desenvolvimento)

Com `-eden-algorithm neat` (opção `Algorithm` do experimento) a rede passa a evoluir também a estrutura, seguindo NEAT (Stanley e Miikkulainen, 2002): cada genoma começa mínimo (bias e as 12 entradas ligadas às 3 saídas, 39 conexões), conexões e nós novos surgem por mutação e recebem **marcações históricas** (a mesma estrutura nova na mesma geração recebe o mesmo número), o **cruzamento** alinha genes por essas marcações e a seleção acontece por **espécies** com fitness compartilhada, campeões copiados e remoção de espécies estagnadas. As entradas e saídas têm identidade estável por um contrato neural versionado (`legacy-12-3-v1`), base para os olhos futuros. Todos os parâmetros são propostas iniciais a calibrar, registrados em `experiment.txt`.

Além dos CSVs comuns, um experimento NEAT grava `neat-generations.csv` (espécies e tamanho das redes por geração) e `neat-individuals.csv` (espécie, nós ocultos e conexões de cada indivíduo); `individuals.csv` lista os dois pais de um filho de cruzamento, e a diversidade de pesos por posição fica vazia porque não se aplica. Checkpoints de um experimento NEAT guardam também o histórico de inovações, as espécies e a espécie de cada indivíduo da última geração treinada, e continuam exatamente como a execução sem interrupção; no treino sequencial esses arquivos usam o schema 6 (MLP usa o 5), e no treino paralelo ambos usam o schema 7. Um pool de genomas NEAT (`-eden-seed-genomes`) só semeia uma evolução NEAT: como marcações de experimentos diferentes podem ter o mesmo número com significados diferentes, cada genoma importado mantém os números fixos da rede mínima e recebe nós e inovações novos, próprios dele, para o resto. No laboratório, o campo **Algoritmo** escolhe NEAT para uma evolução nova; a criatura selecionada mostra a sua rede real (colunas por profundidade, conexões desabilitadas em cinza), os nós e conexões, os dois pais e a espécie. Na arena, a *população evoluída* NEAT usa cérebros NEAT; ao se reproduzir lá, o filho herda a estrutura do pai e só pesos e genes de corpo mutam, porque estrutura nova é numerada pela evolução por gerações.

### Olhos (em desenvolvimento)

Com `-eden-senses eyes` (opção `Senses` do experimento; só com NEAT) as criaturas deixam de ter o sentido circular de comida e o radar das quatro paredes: percebem apenas o que os **olhos** alcançam. Cada olho tem orientação em relação ao corpo, abertura (30°–180°) e alcance (0,5–1,5× o alcance de sensor do corpo), fica na superfície do corpo e vê a comida mais próxima e o ponto mais próximo da borda da arena dentro do seu cone e alcance. Os genomas têm até quatro posições de olho reservadas; a posição é a identidade do olho e tem canais próprios no contrato neural `eyes-v1`, então um olho ausente (tudo 0) é diferente de um olho presente que não vê nada. Os fundadores nascem com um olho frontal de 120°. Cada olho custa, por segundo e mesmo sem nada à vista, o custo de sensor do corpo vezes a fração do círculo que o seu setor cobre (abertura / 360° × alcance²): três olhos de 120° custam o mesmo que o sentido circular antigo, olhos sobrepostos pagam cada um, e enxergar mais longe encarece mais que enxergar mais largo na mesma proporção. Criaturas com olhos também pagam para girar: uma fração (`TurnCostFraction`, proposta 0,25) do custo de movimento × giro², para que varrer o ambiente com um olho estreito não seja grátis. A auditoria de energia separa o gasto dos olhos e do giro. Com `-eden-eyes evolving` (no laboratório, *Olhos evolutivos*) a orientação, a abertura e o alcance de cada olho são herdados e mutam (chance e ruído próprios, propostas a calibrar; a orientação dá a volta em ±180°, abertura e alcance ficam nos limites), sempre na mesma posição de olho; com olhos fixos (o controle) todos mantêm o olho dos fundadores. `neat-generations.csv` traz o número médio de olhos, a abertura e o alcance médios. Com olhos evolutivos um filho também pode ganhar um olho (uma cópia levemente alterada de um dos seus, na primeira posição livre; até quatro) ou perder um (nunca o último), com chances propostas de 0,02 e 0,01. Cada olho mantém a sua posição, então acrescentar, remover ou girar um olho não muda o significado dos canais dos outros; o cruzamento alinha os olhos por posição, nunca pelo ângulo, e cada olho paga o seu custo. As referências aleatória e heurística continuam com os sentidos antigos.

### Corpos sólidos (em desenvolvimento)

Com `-eden-solid on` (opção `SolidBodies` do mundo; desligada por padrão) cada criatura é um disco sólido do raio do seu corpo (`BodyRadius`, proposto 0,5 na referência e escalado pelo gene de tamanho como o raio de alimentação, que continua separado: a comida é um ponto, comida dentro do raio de alimentação). Os discos não se sobrepõem, ficam inteiros dentro da arena e se empurram: depois que todas as criaturas giram e aceleram, o tick é dividido em subpassos (o suficiente para que nenhum corpo ande mais que o menor raio possível num deles, 2 com os valores propostos), e depois de cada subpasso até 16 passadas (param assim que nada se sobrepõe) separam os pares sobrepostos ao longo da linha entre os centros, cada corpo pela parcela da massa do outro (massa ∝ raio²); um corpo preso na parede não cede, e o outro se afasta o resto. Os pares são visitados pela posição (corpos ordenados por x), em sentidos alternados a cada passada, nunca pela ordem dos ids, então nenhuma criatura tem prioridade pelo seu id. Sem engarrafamento os corpos ficam separados até o arredondamento (1e-3); quando uma multidão empurra contra a parede pode sobrar uma sobreposição de até ~2% da soma dos raios, que se desfaz quando a pressão para. Corpos que se tocam perdem a velocidade com que se aproximam, conservando o momento: quem empurra move o outro e paga só o esforço do próprio impulso, como sempre. Nada da física fica guardado entre ticks (posição e velocidade bastam), então checkpoints retomam exatamente, inclusive no meio de contatos; a opção e o raio só são gravados quando diferem do padrão, e os arquivos de antes não mudam. Os olhos ficam na superfície do disco.

**Obstáculos.** Com corpos sólidos, `Obstacles` (lista de retângulos alinhados aos eixos, fixos durante a execução) põe obstáculos na arena; `-eden-obstacles none|pillars|corridor` usa um layout pronto, escalado pela arena: *pillars*, nove pilares em grade 3 × 3 com corredores largos, e *corridor*, uma parede no meio com uma única passagem de oito raios de corpo. Os dois deixam todo o espaço livre conectado para o maior corpo possível. Um obstáculo segura o corpo como a parede (o vizinho que empurra é que se afasta), inclusive nos cantos, em qualquer velocidade; uma passagem mais larga que o corpo deixa passar e uma mais estreita bloqueia. Os obstáculos ficam dentro da arena, cada lado com pelo menos o diâmetro do menor corpo, até 256; vão no checkpoint e em `experiment.txt` só quando existem. Obstáculos são opacos para os olhos: comida atrás de um obstáculo não é vista, e cada obstáculo é uma barreira que o olho vê, como a borda, no ponto mais próximo dentro do cone e do alcance. Também bloqueiam a alimentação: só se come comida com linha livre entre o centro do corpo e ela (se a criatura mais próxima está bloqueada, a próxima com linha livre come). Encostar num lado ou canto não bloqueia. Os sentidos legados (controle MLP) continuam de propósito como eram: o círculo de comida enxerga através dos obstáculos e o radar só conhece a borda; `experiment.txt` registra isso. Criaturas não escondem nada umas das outras.

**Nascer e repor em área livre.** Com corpos sólidos, cada fundador (com o seu próprio tamanho) só aparece onde o disco inteiro cabe, longe das paredes, dos obstáculos e dos outros; a comida não aparece dentro de obstáculo, embaixo de um corpo nem num canto onde nenhum corpo alcança para comer (é preciso haver, a um raio de alimentação, um lugar onde o corpo de referência caiba, com linha livre). Se a população ou a comida pedidas não couberem, a execução não começa: o erro de *cenário inviável* diz quantas couberam, e nada é reduzido em silêncio. Um filho sem espaço perto do pai não nasce: nada é cobrado, ninguém é posto longe e o genoma concebido é esquecido (`LastBirthsBlocked`, separado da recusa pelo teto). Uma reposição de comida que não acha lugar espera o tick seguinte na agenda (`LastFoodRespawnsDelayed`), então nada se perde nem se multiplica, e o checkpoint a guarda. Com obstáculos, a configuração mapeia onde o corpo de referência cabe e conta as regiões separadas (`free_regions` em `experiment.txt`; um cenário dividido aparece ali, e um sem lugar algum é recusado). O caminho sem corpos sólidos não muda. No [laboratório](#usar-o-laboratório) a arena física se escolhe no campo *Arena*; `run-info.txt` (mundo compartilhado), `experiment.txt` e `experiment.json` registram o cenário (`obstacle_layout`, ou `custom` para obstáculos postos à mão). No laboratório, um cenário inviável ou inválido é recusado antes de qualquer mudança: a execução mostrada continua, os controles voltam a ela e o resumo começa com *CENÁRIO INVIÁVEL* e o motivo (por exemplo, quantas criaturas couberam); um início que funciona apaga o aviso. Se isso acontece já na abertura (linha de comando), o laboratório começa na arena de controle e mostra o motivo.

**Água e hidratação.** Com corpos sólidos, `Water` (fontes fixas: um retângulo, a reserva máxima e a recarga por segundo) põe água na arena; `-eden-water none|pond|ponds` usa um layout pronto (*pond*, um lago com um décimo da arena de lado; *ponds*, dois, um de cada lado do meio, fora dos pilares e da parede do corredor), com reserva proposta de 200 e recarga de 2/s, ou inesgotável com `-eden-water-supply inexhaustible` (o controle, identificado como tal em `experiment.txt`). O interior da água segura o corpo como um obstáculo e nada nasce ou aparece dentro, mas a água não é opaca: olhos e bocas veem e alcançam através dela. Com água, cada criatura tem uma **hidratação** separada da energia (cheia em 100 nos fundadores; propostas a calibrar): perde 1/s faça o que fizer, só se recupera bebendo e, em 0, a criatura morre de sede (`dehydration` na genealogia). Beber é automático: quem está com a margem dentro do raio de alimentação, sem obstáculo no caminho, toma até 20/s, nunca além do cheio; água não dá energia e comida não hidrata. A fonte limitada recarrega até a capacidade e esvazia com o que se bebe; se o que todos pedem num tick passa do que ela tem, cada um recebe a mesma fração do que pediu (a divisão não depende de id nem de ordem). O filho recebe da hidratação do pai a mesma parte que recebe da energia, e o pai a perde. A auditoria (`HydrationFlows`) explica exatamente a variação da água das criaturas (bebido − perdido − levado por quem morre) e das fontes (recarga − consumo). A água, as reservas e a hidratação vão no checkpoint e em `experiment.txt`/`run-info.txt` só quando existem; sem água nada muda. O laboratório ainda não oferece água, só a desenha (em azul) e mostra a hidratação no inspetor quando o cenário a tem.

**Perceber sede e ver água.** Com olhos num cenário com água os cérebros usam o contrato neural **`eyes-water-v1`**: as quatro entradas do corpo de `eyes-v1`, a **hidratação** (hidratação / máxima, canal novo de id 12) e, por posição de olho, os nove canais de `eyes-v1` com os mesmos ids seguidos de quatro canais de **água** (vista, seno, cosseno e proximidade do ponto de água mais próximo dentro do cone e do alcance; ids novos dentro da faixa do olho), 57 entradas ao todo. Um obstáculo esconde a água atrás dele (só contam pontos com linha livre); a água não esconde comida nem é barreira para os olhos. Sem água os olhos continuam em `eyes-v1` e o sentido legado em `legacy-12-3-v1`, ambos sem nenhuma mudança; o sentido legado, mesmo com água, não vê água nem sente sede (decisão registrada em `experiment.txt` como `water_senses=none`). As posições das entradas mudam entre `eyes-v1` e `eyes-water-v1`, então os contratos não se misturam: genomas de um não semeiam nem retomam um experimento do outro (recusa explícita), e um checkpoint de olhos com água salvo antes deste contrato não é aceito. O inspetor mostra a hidratação lida e o que cada olho vê de água.

**Composição de genomas na arena.** No mundo compartilhado, o cérebro *Composição de genomas (pasta)* põe na arena quantidades exatas de genomas exportados: a pasta é a de *Pool de genomas* e a composição diz quais e quantos, por nome de arquivo ou pela posição no pool por fitness — `a.json*20; #1*5` são 20 cópias de `a.json` e 5 do melhor do pool (vazio: uma cópia de cada). As fundadoras nascem nessa ordem (ids 1–20 de A, 21–25 de B), em área livre, cada uma com o corpo e os olhos do seu genoma; cópias compartilham a herança e mais nada (posição, energia, estado do cérebro e vida são de cada uma). Dois modos explícitos: **congelados** (confronto: sem reprodução, nenhum genoma muda) ou **com descendência mutante** (reprodução ligada; filhos mutam os genes de corpo que alguma fundadora expressa e, em MLP plástica, a plasticidade; olhos herdados como estão; a genealogia registra tudo). Cada genoma é conferido contra o mundo antes de começar — `eyes-v1` só sem água, `eyes-water-v1` só com água — e um genoma recusado, um nome desconhecido ou uma contagem inválida deixam o cérebro heurístico no lugar e dizem o motivo. O resumo mostra a composição, o total e a densidade; o inspetor diz de qual genoma (e de qual cópia) vem cada criatura, e de qual fundadora descende um filho. A composição vai no checkpoint (genomas, cópias, origem e modo) e a retomada é exata. No player: `-eden-brain composition -eden-genomes PASTA -eden-composition "a.json*20;#1*5" [-eden-composition-mode frozen|descendants]`.

**Cenário: tamanho e recursos.** Cada fator do cenário se ajusta separadamente, do mesmo jeito no laboratório (painel *Cenário: tamanho e recursos*, abaixo de *Arena*) e no batch: tamanho da arena quadrada (`-eden-arena 50|100|200`, ou outro lado; 50 × 50 é o controle), se os recursos **mantêm a densidade** (comida e reservas de água × área / 2500) ou **a quantidade** do controle (`-eden-resources density|quantity`), comida no 50 × 50 antes da escala (`-eden-food`; no lab 0 deixa a do modo: a do preset no mundo compartilhado, 32 nos episódios), energia por comida (`-eden-food-energy`), tempo até a comida voltar (`-eden-food-respawn`), água (`-eden-water`, `-eden-water-supply`, reserva e recarga por lago no 50 × 50 com `-eden-water-capacity`/`-eden-water-recharge`), duração do episódio (`-eden-episode-seconds`), idade máxima (`-eden-max-age`) e energia inicial (`-eden-initial-energy`). Obstáculos e lagos acompanham o tamanho da arena; água implica corpos sólidos; a população não muda com o tamanho. Um campo do lab que o usuário não mexeu não sobrescreve as regras do run. O resumo do lab e os registros mostram as **densidades** (por 100 u²) e a **oferta**: a energia por segundo que a comida pode dar se cada item for comido assim que volta (itens × energia / tempo de volta), no total e por criatura, ao lado do gasto em repouso, e a recarga de água por criatura ao lado da sede — porque muitos itens que voltam devagar podem render menos que poucos que voltam rápido. `experiment.txt` e `run-info.txt` ganham `resources=` e `supply=` (o `experiment.json` traz `supply` em `arena`), e `scenario=` registra tamanho e escala quando não é o controle. Medição (release, i5-13400, densidade mantida): mundo compartilhado com 64 criaturas e sólidos custa 0,17 ms por tick em 50 × 50, 0,77 ms em 100 × 100 e 3,8 ms em 200 × 200; com 128 criaturas e 4096 comidas em 200 × 200, 13,9 ms (70 ticks/s) — metade nos sentidos, que percorrem toda a comida. Populações maiores em arenas grandes esperam um índice espacial. A evolução com olhos, pilares e lagos roda a 60 mil ticks/s em 50 × 50 e 15 mil em 200 × 200: os episódios agora reaproveitam a grade de espaço livre (que só depende das regras, não da seed) em vez de reconstruí-la a cada episódio, o que antes deixava 200 × 200 a 700 ticks/s com quadros de vários segundos (os resultados são idênticos).

### Plasticidade durante a vida (experimento M7)

Por padrão a rede é fixa durante a vida. Com `-eden-plasticity on` (opção `Plasticity` do experimento), cada genoma ganha dois coeficientes de plasticidade, sorteados na geração 0 depois dos genes de corpo: uma **taxa de aprendizado** em [0, 1] e um **decaimento** em [0, 0,2]. Os coeficientes são herdados e mutam como os genes de corpo (mesma chance, ruído proporcional à faixa de cada um, sempre dentro dela), na evolução e nos nascimentos do mundo compartilhado. A rede passa a agir com W_base + ΔW nas 48 conexões da camada oculta para a de saída, e ΔW muda a cada decisão por uma regra Hebbiana modulada pela variação de energia:

ΔW ← limite((1 − decaimento)·ΔW + taxa·m·h·y, ±1), em que m é a variação da entrada de energia desde a decisão anterior e h e y são as ativações oculta e de saída daquela decisão.

É um experimento, não uma promessa de aprendizado por reforço. ΔW pertence ao estado individual da criatura, nunca ao genoma: começa em zero ao nascer e em cada episódio, não é herdado e é salvo no checkpoint para continuar uma vida. O filho herda os pesos-base e os coeficientes do genoma do pai, nunca o que o pai aprendeu; herdar ΔW seria outro experimento, explicitamente identificado, e não existe. O inspetor mostra se a rede é fixa ou plástica, os coeficientes e o maior |ΔW| da vida atual. Com a rede fixa nada muda: a evolução continua idêntica byte a byte à de antes.

**Mundo que muda e comparação.** Com `FoodShiftSeconds` (desligado por padrão) a comida fica na metade esquerda da arena até esse instante e passa para a direita a partir dele: a comida que sobrou muda de lugar e as reposições seguem. A comparação roda a mesma bateria em quatro células, mundo estático e mundo que muda, cada um com rede fixa e plástica, com o mesmo orçamento. Para separar evolução de aprendizado, a população plástica final é avaliada de novo nas mesmas seeds de validação com o aprendizado desligado (mesmos genomas, coeficientes zerados): uma **lesão**.

```powershell
& "C:\Program Files\Unity\Hub\Editor\6000.3.27f1\Editor\Unity.exe" -batchmode -projectPath . `
  -executeMethod Eden.Editor.EvolutionBatch.ComparePlasticityFromCommandLine -eden-seed 1 -eden-food-shift 60 -logFile plasticity.log
```

Aceita as opções da bateria. O diretório contém `cells.csv`, `comparison.json`, `comparison.txt` e, por célula, os arquivos da bateria e de cada execução.

Primeira comparação (bateria de aceitação, 5 execuções × 30 gerações × 64 genomas, seeds 1–5, seed de validação 1, comida mudando aos 60 s do episódio de 120 s; 28 800 episódios de treino e 11 200 de validação por célula):

| Mundo | Rede | Mediana na validação, geração final | Sem aprender (lesão) | Sobrevivência |
| --- | --- | --- | --- | --- |
| estático | fixa | 2,43 ± 0,93 | — | 61% |
| estático | plástica | 2,35 ± 1,20 | 2,20 ± 1,21 | 58% |
| muda | fixa | 2,34 ± 1,31 | — | 51% |
| muda | plástica | 2,04 ± 1,44 | 1,78 ± 1,18 | 51% |

Resultado negativo, apresentado como tal: a plasticidade não melhorou o desempenho em nenhum dos dois mundos (plástica − fixa: −0,08 no estático, −0,30 no que muda), e nenhuma diferença passa da variação entre execuções. A lesão sugere que as populações plásticas passaram a depender um pouco do que aprendem (+0,15 e +0,27 com o aprendizado ligado), mas também dentro da variação. Com esta regra (só a camada de saída, modulada pela variação de energia) e este orçamento, não há evidência de benefício do aprendizado durante a vida. O controle (mundo estático, rede fixa) repete exatamente a bateria do M3.

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

Com o modo **Evolução (avaliação isolada)**, o laboratório avalia cada genoma em seu próprio mundo (uma criatura sozinha), sequencialmente ou em paralelo conforme o campo **Treino**. O painel indica a geração, a fase (treino, referências na validação ou validação), o indivíduo e a seed do episódio mostrado, e deixa explícito que a seleção é artificial (torneio pela fitness de treino), não seleção natural. A cada 5 gerações, a começar pela 0, a geração treinada é validada nas 5 seeds separadas; antes da primeira validação o aleatório e o heurístico são avaliados nessas mesmas seeds.

**Treino paralelo.** Selecione **Treino → Paralelo (CPU)**, **Iniciar** e **Máx.**. A fila inclui todos os genomas e suas seeds; até `min(64, processadores lógicos − 1)` episódios avançam simultaneamente, com mínimo de um, sem criar uma thread por criatura. Cada episódio tem mundo, cérebro e geradores aleatórios próprios. Os resultados são agregados na mesma ordem do sequencial, mantendo a fitness, a seleção e as mutações idênticas no mesmo ambiente. A tela acompanha um episódio; **Pausar** pausa todos, e **+1 tick** avança apenas o episódio mostrado. Em velocidade limitada, o relógio continua controlando o total de ticks; use **Máx.** para aproveitar o paralelismo. Checkpoints paralelos usam schema 7 e guardam todos os mundos em andamento, os resultados já concluídos e o número de workers; arquivos sequenciais antigos continuam aceitos. O ganho de velocidade depende da CPU e da complexidade das redes.

O gráfico abaixo do painel mostra, por geração, a mediana do treino com a faixa entre quartis, o melhor do treino, a mediana da validação e o campeão (o melhor do treino) na validação, com linhas tracejadas para o aleatório e o heurístico; o painel diz em texto se o campeão supera ou fica abaixo de cada referência. Selecionar a criatura mostra a linhagem do indivíduo: id, geração de nascimento, pai e pesos mutados, se é elite e os ancestrais até o fundador.

Para ver a população no mundo compartilhado, escolha *Mundo compartilhado* com o cérebro *População evoluída*: a evolução fica parada, cada criatura é um indivíduo da última geração avaliada (com a sua linhagem no inspetor) e o botão **Voltar à evolução** continua de onde parou. O experimento do laboratório é exportado em `EvolutionRuns\<data>-lab-seed<n>-<preset>\`, no mesmo formato descrito abaixo. Com a mesma seed e o mesmo preset, as gerações e os números são os mesmos de uma execução sem a cena, em qualquer velocidade.

### Busca híbrida com fome e sede

No laboratório, **Perfil → Somente busca (híbrida, fome e sede)** executa apenas a busca, com peso 100%. **Duas etapas (híbrida, fome e sede)** combina a busca com os recursos atuais (30%/70% por padrão). A escolha ativa NEAT e olhos; os campos em **Busca — configuração → Fome, sede e exploração** ajustam a configuração efetiva. O HUD mostra reservas, gastos, ingestão útil/desperdício, E/D/C, multiplicador, nota e tempo restante.

Os perfis são `search-explore-collect-active-v1` e `two-stage-explore-collect-active-v1`. A fórmula adimensional é `R = (0,65E + 0,35C) × (1 + 0,5D + 2C) / 3,5`, limitada a [0,1]. E mede células distintas (lado 2, margem de entrada 0,2, meta 200 limitada à máscara conectada, excluindo a célula inicial); D conta os IDs realmente representados pelos olhos nas decisões; C conta comida ingerida e fontes das quais foram recebidas pelo menos 5 unidades úteis de água. Repetições não duplicam crédito. A máscara usa o corpo de referência, obstáculos e lagos, com células parciais nas bordas; cobertura da máscara e progresso na meta são medidas separadas.

A busca dura 120 s ou até a morte, inclusive depois de coletar tudo. Começa com energia 80/160 e hidratação 100/100; cada alimento fornece 30. Metabolismo 0,5/s, movimento 0,5/s por esforço, sensor de referência 0,1/s e fração de giro 0,25; água é perdida a 1,25/s e bebida a até 20/s. Os custos reais dos olhos e a física permanecem os mesmos. A comida consumida desaparece sem reposição; os dois lagos são inesgotáveis e continuam hidratando após pontuar. Morrer não reduz denominadores nem apaga o consumo concluído no tick fatal.

Na CLI, use `-eden-profile search-active|two-stage-active -eden-algorithm neat -eden-senses eyes -eden-eyes evolving`. Os argumentos `-eden-stage1-*` configuram o cenário da busca. Os novos argumentos `-eden-search-cell-side`, `-eden-search-hysteresis`, `-eden-search-exploration-goal`, `-eden-search-water-threshold`, `-eden-search-initial-energy`, `-eden-search-max-energy`, `-eden-search-food-energy`, `-eden-search-metabolism`, `-eden-search-movement`, `-eden-search-sensor`, `-eden-search-turn`, `-eden-search-max-hydration`, `-eden-search-hydration-loss`, `-eden-search-drink-rate` e `-eden-search-max-age` configuram suas regras. Perfil e mundo são validados juntos; os parâmetros e regras participam da identificação do experimento.

Para verificar as seeds planejadas sem iniciar evolução, execute `-executeMethod Eden.Editor.EvolutionBatch.PreflightSearchFromCommandLine` com os mesmos argumentos e `-eden-generations N`. O diagnóstico conhece o mapa, segue caminhos e gerencia reservas usando os ticks, contatos, olhos e tempos reais de bebida; ele nunca participa da seleção. Verifica olho inicial e quatro olhos máximos, cinco coletas, sobrevivência até o limite e reservas de pelo menos 10% antes dos reabastecimentos. Os batches ativos verificam suas seeds antes de treinar. Uma rota que falha produz erro com seed, mapa, reservas e percurso; não troca seeds nem acrescenta suprimentos. A viabilidade é demonstrada para a política declarada e as seeds verificadas, sem garantia para ações desperdiçadoras ou qualquer mapa futuro.

Os perfis novos usam seeds lógicas (scheme 3): a busca é pareada entre os modos único e composto. `stages.csv` exporta componentes e eventos por alvo com precisão completa, custos e reservas; valores ausentes ficam vazios. `stage-summaries.csv` contém médias por etapa/geração, excluindo ausências da média de cada componente. Checkpoints novos usam schema 9 e preservam grade, visitas, detecções, consumo, água por fonte e workers adiantados. Perfis anteriores continuam com seus schemas e regras originais, inclusive a busca congelada abaixo. Genomas compatíveis podem iniciar um novo experimento, com reavaliação e sem herdar o fitness de outra recompensa. Referências com sensores legados ficam fora das curvas comparáveis da busca ativa. Nenhum treino longo é iniciado ao abrir o Editor.

### Perfil de duas etapas: busca e recursos

O perfil `two-stage-search-v1` pontua cada genoma em duas etapas, em toda geração: **recursos** (o mundo do experimento, com comida e água, nota em [0, 1] por sobrevivência 50%, reposição útil 30% e reservas finais 20%) e **busca** (arena própria, sem gasto de energia nem de hidratação, com alvos fixos de comida e água longe uns dos outros). A nota é 30% busca e 70% recursos. Na busca, cada alvo coletado vale mais que qualquer aproximação; a rapidez só pesa 30% de cada coleta e o crédito de aproximação é limitado a meio alvo. O fitness legado (energia por segundo) continua gravado em todo episódio. O perfil legado `food-energy-per-second-v1` segue como padrão e produz os mesmos arquivos de sempre.

No laboratório, escolha **Perfil → Duas etapas** (o painel *Busca — configuração* ajusta peso, duração, alvos, distância mínima, α e β), **Iniciar**. Use sentidos de olhos com NEAT. O episódio observado mostra a etapa, os alvos coletados em ordem e a aproximação; o resumo mostra a mediana e a dispersão de cada etapa, com aviso quando uma etapa não separa os genomas, e o gráfico ganha as séries S1 e S2.

No batch e no player: `-eden-profile legacy|two-stage|resources`, `-eden-stage1-weight`, `-eden-stage1-seconds` (padrão 120), `-eden-stage1-distance` (30), `-eden-stage1-arena` e `-eden-stage1-max-arena` (a arena cresce até os alvos caberem), `-eden-stage1-obstacles none|pillars|corridor`, `-eden-stage1-food`, `-eden-stage1-water`, `-eden-stage1-alpha`, `-eden-stage1-beta` e `-eden-stage1-seeds`. Valores inválidos são recusados. Fora do perfil legado, o experimento ganha uma linha `stage=` por etapa e o arquivo `stages.csv` (nota, fitness legado e componentes de cada episódio); os checkpoints usam o schema 8, e genomas desse perfil não semeiam execuções de outro perfil.

### Sem a cena

Para rodar: menu **EDEN → Evolution → Run 20 generations (seed 1)** no Editor (o Editor fica ocupado durante a execução, cerca de 20 s) ou, com o Editor fechado:

```powershell
& "C:\Program Files\Unity\Hub\Editor\6000.3.27f1\Editor\Unity.exe" -batchmode -projectPath . `
  -executeMethod Eden.Editor.EvolutionBatch.RunFromCommandLine -eden-seed 1 -eden-generations 20 -logFile evolution.log
```

Opções: `-eden-seed N`, `-eden-generations N`, `-eden-food N` (comida do episódio no 50 × 50, padrão 32; veja o cenário acima para as outras opções de tamanho e recursos), `-eden-body none|all|size,speed,sensor,efficiency` (genes de corpo expressos, padrão `none`), `-eden-plasticity on|off` (plasticidade durante a vida, padrão `off`), `-eden-algorithm mlp|neat` (veja [NEAT](#neat-em-desenvolvimento), padrão `mlp`), `-eden-senses legacy|eyes` e `-eden-eyes fixed|evolving` (veja [olhos](#olhos-em-desenvolvimento), padrões `legacy` e `fixed`), `-eden-solid on|off` e `-eden-obstacles none|pillars|corridor` (veja [corpos sólidos](#corpos-sólidos-em-desenvolvimento), padrões `off` e `none`), `-eden-water none|pond|ponds` e `-eden-water-supply finite|inexhaustible` (água e hidratação; exigem corpos sólidos; padrões `none` e `finite`) e `-eden-output DIR`. Sem `-eden-output`, o resultado vai para `%USERPROFILE%\AppData\LocalLow\NinguemDev\EDEN\EvolutionRuns\<data>-seed<n>\`:

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

Aceita as opções da bateria e `-eden-presets minimal,standard,stress,thousand` (padrão: os quatro). O diretório contém `presets.csv` (uma linha por preset), `comparison.json` (definição comum, hash da configuração de cada preset, resultados e custo), `comparison.txt` e, por preset, os arquivos da bateria e de cada execução. Um preset maior avalia mais episódios por geração, por isso o relatório mostra quantos episódios cada um usou e só declara um vencedor quando a diferença passa da variação entre execuções.

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

- **EditMode:** gerador aleatório (com os vetores oficiais do PCG32 e do SplitMix64), configuração, relógio de ticks, agenda de eventos, sensores, regras do mundo, cérebros, rede neural, genoma, genes de corpo (limites, herança e mutação, expressão no mundo, corpo fixo idêntico ao anterior e regras de custo: nenhum corpo melhor que a referência em tudo), avaliação isolada, seleção, mutação, linhagem, validação separada do treino, bateria, métricas e CSV, checkpoints (retomada idêntica à execução ininterrupta, floats exatos em outra cultura, arquivos corrompidos ou de versão futura recusados, arquivos de antes dos genes de corpo ainda lidos), ticks sem alocação de memória após o aquecimento (gravador de alocações da Unity), exportação (JSON com unidades; CSVs de antes de salvar e depois de carregar, no meio de um episódio, iguais aos da execução sem interrupção, sem episódio repetido), comparação de presets, reprodução local (maturidade, limiar, intervalo, teto sem custo, energia conservada, herança com fluxo próprio do filho, retomada idêntica com nascimentos), genealogia com nascimentos encadeados e mortes, métricas de nascimento por janela e extinção registrada, auditoria de energia e de comida em execução longa com nascimentos e mortes, competição por comida escassa, mundo extinto que continua vazio, plasticidade (genes limitados, regra Hebbiana exata, decaimento e limite, ΔW zerado a cada vida e episódio, sem NaN nem explosão em execução longa, vida retomada com o que aprendeu; herança só do genoma, nunca do aprendizado, na evolução e no mundo compartilhado), mundo que muda (comida passa de metade, retomada exata), comparação de plasticidade (quatro células no mesmo orçamento, lesão), ablação dos genes de corpo (mesmo orçamento por variante, controle idêntico à bateria comum, diversidade de corpo, relatório), além de execuções longas com verificação de invariantes e independência de FPS.
- **PlayMode:** a cena Bootstrap real: renderização, pausa, avanço de um tick, inspeção por id, gravação do CSV e os controles do laboratório, inclusive salvar, carregar, a medição de desempenho, o corpo evolutivo (tamanho desenhado, alcance do sensor e inspetor) a reprodução no mundo compartilhado (filhos com genoma e cérebro próprios, salvar e carregar com nascimentos) e a extinção (o mundo para, o HUD avisa, os CSVs registram, só Iniciar recomeça).

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
| `-eden-preset minimal\|standard\|stress\|thousand` | preset inicial |
| `-eden-brain heuristic\|random\|neural` | cérebro inicial do mundo compartilhado |
| `-eden-body fixed\|evolving` | corpo da evolução (padrão `fixed`) |
| `-eden-solid on\|off` | corpos sólidos na arena dos dois modos (padrão `off`) |
| `-eden-obstacles none\|pillars\|corridor` | obstáculos da arena dos dois modos; um layout liga os corpos sólidos (padrão `none`) |
| `-eden-training sequential\|parallel` | execução dos episódios da evolução (padrão `sequential`; combine `parallel` com `-eden-max`) |
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

## Gravação e acompanhamento da evolução

No laboratório, o painel **Gravação e acompanhamento** configura o checkpoint automático e a observação dos indivíduos avaliados. O intervalo padrão é **50 gerações de treino concluídas** (50, 100, 150…); zero desativa. A geração inicial é g0, portanto o primeiro arquivo contém os resultados até g49 e a população seguinte. Os arquivos ficam em `Checkpoints/Automatic/<execução>/generation-000050.json`, dentro do diretório de dados do aplicativo, separados do checkpoint manual `lab.json`. Salvar/F5 também grava em segundo plano: o estado é capturado entre ticks e o treino pode continuar enquanto esse estado é serializado, conferido e gravado. Fechar a execução aguarda a gravação pendente.

Ative o acompanhamento dos melhores, escolha quantos indivíduos assistir, a posição inicial e a velocidade do replay. A classificação usa a **nota média do treino concluído**, com desempate pelo id do indivíduo. Cada replay mostra seu melhor episódio da etapa de busca (ou da primeira etapa, quando não há busca). O painel identifica geração, posição, indivíduo, nota e seed. Ao terminar o ciclo, passa à classificação mais recente; o botão de atualização antecipa essa troca. O replay tem mundo, cérebro e RNG próprios: não participa da seleção nem modifica o treino. As preferências de observação não fazem parte do experimento salvo. No player: `-eden-auto-checkpoint 50 -eden-follow-ranked 5 -eden-follow-rank 1 -eden-replay-speed 1` (acompanha os cinco melhores).

A evolução interativa usa **métricas v2**, com uma fila limitada e um gravador em segundo plano. Cada geração/avaliação é um segmento em `segments/`, com indivíduos, episódios, fatos por alvo, resumos e o genoma campeão. Cenários compartilhados são gravados uma vez em `scenarios/`. Os episódios guardam os valores usados pela seleção com precisão de ida e volta; campos indisponíveis ficam vazios. Os alvos usam linhas apenas quando há detecção ou interação, evitando centenas de colunas vazias. A exportação antiga também ganhou um formatador linear.

Cada segmento é preparado em uma pasta `.partial`, recebe um manifesto de tamanhos e hashes SHA-256 e só então é publicado por renomeação. Leitores ignoram segmentos parciais e verificam os arquivos publicados. Se a fila encher, o treino aguarda com a interface disponível; falhas de gravação suspendem o treino e aparecem no painel. Uma retomada cria outro diretório e registra sua origem. Checkpoints vinculados às métricas têm um recibo adjacente `.metrics.json`, associado ao hash do estado, indicando o último segmento anterior ao checkpoint. Ao juntar execuções retomadas, considere apenas esse prefixo da execução anterior; resultados posteriores pertencem à linha de tempo abandonada. Checkpoints antigos continuam aceitos; o schema 10 acrescenta as detecções por alvo quando presentes, preservando os formatos anteriores para estados sem esses dados.

Ferramentas que precisam dos CSVs antigos podem converter uma execução v2 **encerrada** para uma pasta nova e vazia, sem simular novamente:

```powershell
& "C:\Program Files\Unity\Hub\Editor\6000.3.27f1\Editor\Unity.exe" -batchmode -projectPath . `
  -executeMethod Eden.Editor.MetricsExport.FromCommandLine `
  -eden-source "C:\dados\execucao-v2" -eden-output "C:\dados\csv-compativel" -logFile export.log
```

Use um projeto de validação separado quando o Editor já estiver aberto. Os comandos de bateria existentes mantêm sua exportação CSV; a mudança de armazenamento padrão se aplica à evolução interativa do laboratório.

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
| M6 | ecossistema contínuo: reprodução local, genealogia, extinção explícita, auditoria de energia | concluído |
| M7 | adaptação individual (plasticidade) como experimento, com mundo que muda e lesão | concluído |
| M8 | escala orientada por perfilamento | planejado |

As tarefas de cada marco estão nas [issues](https://github.com/ninguemdev/EDEN/issues) e [milestones](https://github.com/ninguemdev/EDEN/milestones). Nenhum marco promete melhoria garantida a cada geração: estagnar ou extinguir também é um resultado válido de experimento, e os dados existem para investigar isso.

## Licença

Distribuído sob a licença MIT. Veja [LICENSE](LICENSE).
