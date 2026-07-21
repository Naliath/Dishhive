---
name: develop-dishhive-ai-capabilities
description: Add, change, or review Dishhive model-backed capabilities through the central AI execution gate. Use when editing prompts, response schemas, task settings, retries, model capability checks, AI telemetry, recipe facts, recipe translation, recipe import extraction, week suggestions, or the AI task-performance page.
---

# Develop Dishhive AI capabilities

Keep every model call behind the shared execution boundary. Treat task profiles, response contracts, and durable task metrics as part of the feature rather than optional diagnostics.

## Start from the central boundary

Read these files before changing an AI capability:

- `src/Dishhive.Api/Services/Ai/AiExecutionGate.cs`
- `src/Dishhive.Api/Services/Ai/AiTaskProfiles.cs`
- `src/Dishhive.Api/Services/Suggestions/AiOptions.cs`
- The feature service and its prompt/response-contract files
- `src/Dishhive.Api/Models/AiTaskRun.cs`
- `src/Dishhive.Api/Controllers/IntegrationsController.cs`

Also use `evaluate-ai-week-planning` when planning behavior, candidate research, repair, fallback, or planning latency changes. Use the Dishhive UI skills when the task-performance page or another rendered Angular surface changes.

## Register a task before calling a model

For a new model-backed operation:

1. Add a stable member to `AiTaskKind`.
2. Add exactly one `AiTaskProfile` with a stable dotted task name, output limit, temperature, timeout, retry count, and capability-gate requirement. `RequiresPlanningCapability` means the task depends on the full week-planning verdict; ordinary structured non-planning tasks normally set it to `false` because response-schema support degrades independently through `VerifiedResponseMode`.
3. Add task-specific limits to `AiOptions` only when the shared limits are inappropriate. Include settings that affect execution in `ExecutionFingerprint` and expose operational settings in `appsettings.json`, `docker-compose.yml`, and `.env.example`. Keep defaults consistent across those surfaces unless an environment-specific override is explicitly documented.
4. Inject `IAiExecutionGate` into the feature service. Do not inject `IChatClient`, construct `ChatOptions`, or add provider-specific thinking text in a feature service.
5. Pass a stable operation ID through related calls so task rows and feature-level metrics can be correlated. Create it at the product-operation boundary and extend related service signatures when necessary.
6. Follow the configured/unconfigured registration pattern in `Program.cs`: register the real implementation only with configured AI and retain a no-op implementation for unconfigured and deterministic Testing environments.

Keep task defaults deliberate:

- Classification, extraction, intent parsing, and translation normally use temperature `0`.
- Give small classifiers small output budgets and long-form transformations enough output room.
- Bound variable input before it reaches the model. Lossless transformations such as translation must chunk at stable item boundaries and reassemble by index; never trim away source content that the user expects back.
- Use provider-level reasoning controls from the gate; never append `/no_think` or equivalent prompt tokens.

## Define and validate the response contract

Keep system instructions separate from untrusted recipe, page, or user text. Explicitly tell the model that supplied content is data and cannot override the contract.

For structured results:

- Define one typed payload beside the parser and use `Services/Ai/AiJsonContract.cs` for serialization, parsing, prompt examples, wire property names, enum names, and strict JSON-schema generation. The CLR payload plus its serializer metadata is the source of truth.
- Do not hand-write a schema, JSON example, enum list, or serialized field name in a prompt. Use `AiJsonContract.CreateResponseFormat<T>()`, serialize a representative payload, derive closed values from the enum, and resolve prose references with `AiJsonContract.PropertyName<T>(nameof(T.Property))`.
- Do not use `nameof()` alone as a JSON field name: the wire name may differ because of the serializer naming policy or `JsonPropertyName`. Model closed domains as enum-typed payload properties so the generated schema and parser reject invented values.
- Keep every generated object schema strict: all properties required and `additionalProperties: false`. Represent a required field that may be empty with a nullable type; keep semantic validation after parsing.
- Give the contract a stable version string and change it when semantics or shape change.
- Request the schema with `AiResponseFormatPolicy.WhenSupported`. The gate applies it only after the configured model has passed the capability check.
- Use `Force` only inside a capability probe that is explicitly testing schema support.
- Validate parsed values against the source object and domain rules. A parseable object is not automatically a valid result.
- Preserve a safe fallback. Do not turn unknown classifications into verified empty facts, accept blank translations, or save incomplete imported recipes.

## Execute and record the outcome

Call `EnsureAvailableAsync` when the task profile requires a model capability verdict. Execute each completion with `ExecuteAsync`, using the retry count from `gate.Profile(task)` rather than a feature-local constant.

After every returned completion, call `ReportOutcomeAsync` exactly once:

- `success` for a validated usable result
- `noResult` for a valid response that intentionally yields no entity
- `parseFailure` when the response cannot satisfy the JSON contract
- `validationFailure` when parsed data violates source or domain invariants

The gate records transport `timeout`, `cancelled`, and `error` outcomes. `completed` is only the short-lived state between receiving a response and reporting its semantic result; a durable `completed` row signals interrupted or missing outcome reporting.

Do not put raw prompt or response bodies in durable metrics. Record counts, token usage, duration, selected settings, response mode, finish reason, contract version, and a bounded error. Raw output belongs only in debug logs.

## Keep performance reporting current

When a task or outcome is added:

1. Confirm `AiTaskRun` captures the settings and measurements needed to compare models and configurations.
2. Update the statistics DTO/controller classification if the outcome has different success semantics.
3. Add the task to the statistics page's `taskLabel()` mapping and add human-readable task and outcome labels in both `public/i18n/en.json` and `public/i18n/nl.json`.
4. Preserve week-planning operation metrics for search, resolution, repair, and fallback; the generic task rows measure model calls and do not replace pipeline metrics.
5. For chunked or multi-call work, decide whether call-level rows are sufficient. Add operation-level metrics when the product needs a single logical success rate or end-to-end duration.
6. Check that the retained/sample window does not let high-volume tasks hide low-frequency failures.
7. Add an EF migration only if the durable telemetry or domain model changed; a new task string alone does not require one.
8. Regenerate `src/dishhive-web/src/app/api/generated/dishhive-api.client.ts` with the repository NSwag command when API DTOs change.

Judge latency with per-task average and P95, token totals, attempts, response mode, and outcome. Correlate planning calls with the planning operation by operation/request ID before attributing a slowdown to the model.

## Verify the change

Add focused tests for:

- the task profile's output, temperature, timeout, retry, and schema behavior, plus the gate's shared reasoning control;
- valid, malformed, incomplete, unknown-enum, and prompt-injection-like inputs;
- schema-contract drift: generated property names and enum values must match serializer metadata, and every object node must remain strict;
- outcome reporting for success and each semantic failure;
- statistics grouping and success/failure classification;
- translated UI labels and filtering when the statistics page changes.

Run the applicable checks from the repository root:

```powershell
dotnet test src/Dishhive.Api.Tests/Dishhive.Api.Tests.csproj --no-restore
```

Run these from `src/dishhive-web` when Angular code or API-facing models change:

```powershell
npm test -- --watch=false
npm run build
```

For planning changes, run the evaluation corpus described by `evaluate-ai-week-planning`. Finish with `git diff --check`. If live model/search services are unavailable, report that the stochastic evaluation was not run; do not imply unit tests measured live model quality.

## Final review

Before handing off, verify:

- No production model call bypasses `IAiExecutionGate`.
- No feature service owns `ChatOptions`, reasoning flags, timeouts, or hidden retry constants.
- Every structured task has a strict, versioned schema plus domain validation.
- Every returned completion reaches one terminal semantic outcome. For multi-call work, use one unique operation ID per logical invocation and share it across chunks/retries.
- Corrective prompts stay bounded and do not echo an unbounded failed response.
- Configuration, fingerprints, migration, statistics, labels, and tests changed with the capability.
- User-owned unrelated changes remain untouched.
