# Rascunho antigo de currículo sequencial — arquivado

Este branch (`archive/curriculum-lexicographic-wip`) guarda um rascunho que estava **sem commit** na cópia de trabalho original do projeto quando o desenvolvimento foi pausado em 03/11/2026. Ele foi commitado aqui, sem alterações de conteúdo, só para não perder o trabalho; **não foi integrado, testado contra o estado atual nem validado**.

## O que é

`Assets/_Project/Scripts/Evolution/Curriculum.cs` e seus testes implementam um currículo **sequencial** de cinco estágios fixos ("Interceptação", "Alinhamento", "Busca", "Sobrevivência", "Arena completa"), com avanço por taxa de sucesso usando `FoodItems`/`Survived` do resultado do episódio.

## Por que está arquivado, não na história principal

Este branch parte do commit `b7fe100` (`feat(evolution): optional eye difference in the NEAT compatibility distance`), que hoje está **75 commits atrás** de `main`. Faltam, em relação ao estado atual do produto:

- Toda a fase P2: corpos sólidos, obstáculos, oclusão, posicionamento livre, água e hidratação, visão da água.
- Toda a fase P3: perfis de recompensa versionados, plano de avaliação em várias etapas, mundo de busca, perfil `two-stage-search-v1`, busca ativa fome/sede (`search-explore-collect-active-v1`), checkpoints/CSV correspondentes, laboratório com as duas etapas, exploração pontuada pelas células vistas (`…-active-v2`).

O código também **não corresponde** ao que os issues [#134](https://github.com/ninguemdev/EDEN/issues/134) e [#135](https://github.com/ninguemdev/EDEN/issues/135) pedem hoje — um currículo **lexicográfico**, com critério de avanço diferente. Tudo indica que este rascunho foi abandonado quando o projeto mudou de abordagem, não que estava quase pronto.

## Antes de reaproveitar

1. Decidir se a ideia sequencial ainda interessa ou se os issues #134/#135 (lexicográfico) já a substituem.
2. Se interessar, rebasear sobre `main` atual — `EvaluationJob`, `EvolutionSession`, `CheckpointCodec`, `LabHud`/`LabRunner`/`LabText`, `WorldSimulation` e `SimulationSnapshot` mudaram substancialmente nos 75 commits que faltam, então isso não é um rebase trivial.
3. Revalidar do zero (EditMode/PlayMode/build) só depois do rebase; os testes deste branch rodam contra um produto que não existe mais.

Os issues #134/#135 permanecem **abertos** — este arquivamento não os resolve.
