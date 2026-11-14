# Cup conversion during AI translation

Tested on 2026-11-14 with the local application's configured `openai-compatible` provider,
model `deepseek/deepseek-v4.1-flash`. All examples are synthetic and public. No household
recipes or settings were sent or changed by this experiment.

## Result and implementation decision

The model can translate and convert in the same completion when given the allowed conversion
list. However, application-side arithmetic remains simpler: it works without translation,
honors later user edits immediately, preserves original units, and does not require expanding
the production translation contract. The production translator still translates text only.

An unconstrained prompt was a negative control: translate to Dutch and convert cups using
the model's own ingredient knowledge. All three runs supplied guessed weights for unlisted
frozen corn and mixed vegetables. A second prompt supplied Dishhive's small conversion list
and required unlisted ingredients to keep their cups. All three runs passed the six numeric
and unit expectations below.

| Input | Expected under the list policy | Unconstrained result in all three runs |
| --- | --- | --- |
| 0.5 cup all-purpose flour | 60 g | 60 g |
| 0.25 cup granulated sugar | 49.5 g | 50 g |
| 1 cup frozen corn | 1 cup | 165 g |
| 1.5 cups water | approximately 354.88 ml | 360 ml |
| 1/3 cup almond flour | 32 g | 40 g |
| 0.75 cup chopped mixed vegetables | 0.75 cup | 135 g |

Known-ingredient differences here reflect chosen reference values and conventions; the
experiment does not establish that every alternative weight is universally incorrect.
Inventing conversions for unlisted ingredients does violate the requested product behavior.

The evaluation used temperature 0, reasoning disabled, and the existing `recipe.translate`
task profile through `IAiExecutionGate`. A typed payload generates the schema, prompt example
and parser through `AiJsonContract`. Every returned completion receives a terminal metrics
outcome. Model answers are never applied to recipes. This is an evaluation-only contract,
not a new production AI capability or a bypass of the execution gate.

Average latency was approximately 1.06 seconds for the negative control and 1.36 seconds for
the constrained prompt. Three repetitions on one model are limited evidence. The checks
cover ingredient indexes, nonempty translated names/title, step count, and quantities/units
within 0.02 of the expected value; they do not independently assess full Dutch translation
quality or establish reliability on a wider corpus.

## Reproduction

From the repository root, with the local application container available:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/run-ai-facts-evals.ps1 `
  -CupTranslation -Iterations 3 -OutputPath artifacts/cup-translation-eval/latest.json
```

The runner reads AI configuration from the local application into child-process environment
variables. It does not print credentials. The report is in the ignored artifacts directory
and records model/configuration, actual measurements, per-call settings, outcomes and latency.
`-ValidateOnly` checks the synthetic case set without making model calls.
