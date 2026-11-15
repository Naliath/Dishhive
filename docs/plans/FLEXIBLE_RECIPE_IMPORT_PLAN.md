# Flexible recipe import: current behaviour and implementation plan

**Status:** Steps 1-6 implemented. Shared completion, AI text/photo extraction, endpoints,
capability checks, remembered source selection, translated errors and reporting are complete.
Physical phone capture and broader handwriting/photo-quality evaluation remain manual checks.
**Reviewed:** 2026-11-15.
**Related:** [Recipe import](../features/recipe-import.md), [library import/export](../features/recipe-import-export.md), [recipe store](../features/recipe-store.md).

**Agreed scope:** Add text import, photo selection/capture import, and a remembered source selector, reusing the existing import pipeline. URL importing retains its existing behaviour. Reddit retrieval, authentication, forum-specific extraction, scraper ordering, and URL page-preparation improvements are deferred.

## 1. Current behaviour

This section records the baseline at the initial review. Step 1's completed shared
completion/provenance changes are described under its implementation status below.

### User interface and API

- The recipe library has an **Import from URL** button. It opens an inline card with one URL field and an Import button. There is no import-source selector or remembered source preference.
- The frontend posts `{ url }` to `POST /api/recipes/import`. The URL is required and limited to 1,000 characters.
- Import saves immediately. On success, the UI opens the saved recipe and asks the user to check its values. There is no extraction-preview or approval stage.
- Settings separately supports ZIP backups and schema.org Recipe JSON files. That structured library-exchange feature does not accept arbitrary recipe text or extract recipes from photos.
- The recipe editor already supports choosing an image and camera capture. These actions set the recipe's display image; they do not read its recipe content.

Sources: [recipe library template](../../src/dishhive-web/src/app/pages/recipes/recipes.page.html), [library component](../../src/dishhive-web/src/app/pages/recipes/recipes.page.ts), [controller](../../src/Dishhive.Api/Controllers/RecipesController.cs), [request DTO](../../src/Dishhive.Api/Models/DTOs/RecipeDtos.cs), [editor](../../src/dishhive-web/src/app/pages/recipe-form/recipe-form.page.html).

### Fetching and deterministic extraction

1. `RecipeImportService` accepts public HTTP(S) URLs and fetches through `ISafeHttpFetcher`. The network boundary validates addresses and redirects, limits the page to 4 MiB, and uses a 15-second HTTP timeout. Only HTML/XHTML responses are accepted when a content type is supplied.
2. Fetching retrieves the HTTP response. It does not render JavaScript, locate a requested forum comment, or fetch a site's comment data separately.
3. The service selects the **first matching provider**:
   - `DagelijkseKostProvider` handles its own hosts. It reads Recipe JSON-LD, repairs truncated instructions using the page's Next.js data, and prefers the page's clean title.
   - `RecipeScrapersFallbackProvider` matches other HTTP(S) sites when the scraper sidecar is configured. The sidecar uses `recipe-scrapers` with generic schema.org extraction enabled.
4. This is not a chain that tries every registered provider. If the dedicated provider fails, the service moves directly to AI when configured; it does not then try the sidecar.
5. Although a reusable C# `SchemaOrgRecipeExtractor` exists, it is not registered as a generic URL provider. Generic deterministic extraction currently depends on the sidecar outside dedicated sites.
6. A provider result is treated as success without a common completeness check at this boundary. In particular, the sidecar requires a title but can return empty ingredient/instruction arrays. Such a result can prevent AI fallback.

Sources: [import service](../../src/Dishhive.Api/Services/Import/RecipeImportService.cs), [registrations](../../src/Dishhive.Api/Program.cs), [fallback provider](../../src/Dishhive.Api/Services/Import/RecipeScrapersFallbackProvider.cs), [sidecar](../../src/dishhive-scraper/app.py).

### AI fallback already exists

- When a provider throws `RecipeExtractionFailedException` and AI is configured, the service invokes `ILlmRecipeExtractor`. It also invokes AI directly when no provider matches but AI is configured.
- Production uses `LlmRecipeExtractor` when AI configuration is valid. Unconfigured AI and the deterministic Testing environment use a no-op extractor. `IsAvailable` describes that registration; it is not a live reachability or image-capability check.
- The extractor removes scripts, styles, navigation, and other HTML noise, flattens the remaining page, and **keeps only its first 12,000 characters**. A subsequent model-budget reduction cannot recover text already discarded here. There is no special handling for a comment permalink or unrelated forum replies.
- AI runs through `IAiExecutionGate`, with a generated structured contract, temperature zero, bounded retries, and durable outcome metrics. JSON schema is applied when verified as supported; otherwise the same contract is requested in the prompt. Recipe extraction does not depend on passing week-planning quality checks.
- A usable AI result must have a title, nonempty ingredients and instructions, positive servings when supplied, and nonnegative times. The prompt tells the model to use source information only.
- A genuine no-recipe response, exhausted JSON/validation retries, and caught AI service errors/timeouts all become `null`. Very short prepared text also returns `null` without calling AI. The import service maps that to the same extraction exception.
- The controller presents extraction exceptions as **422 / No recipe found on page**. The frontend normally displays only the problem title, hiding the distinction between no recipe and failed extraction. Page-fetch HTTP errors have a separate message.

Sources: [LLM extractor](../../src/Dishhive.Api/Services/Import/LlmRecipeExtractor.cs), [HTML reduction](../../src/Dishhive.Api/Services/Import/HtmlText.cs), [execution gate](../../src/Dishhive.Api/Services/Ai/AiExecutionGate.cs), [task profiles](../../src/Dishhive.Api/Services/Ai/AiTaskProfiles.cs).

### Processing after extraction

- Providers and AI produce `ImportedRecipe`. `ApplyImportedRecipe` removes list decorations, parses ingredient quantities/units/names, preserves original measurements, creates ordered steps, and maps metadata. Missing servings currently become four at this mapping stage.
- URL identity determines reimport: a matching source URL updates the existing recipe, replacing ingredients and steps, incrementing its revision, and invalidating stale facts/localization state.
- Remote recipe images are downloaded and normalized on a best-effort basis.
- The recipe and durable enrichment jobs are saved together. Background processing assesses dietary facts and optionally translates imported content according to settings, preserving revision checks and original content.
- **Translation currently requires `recipe.SourceUrl != null`.** Reusing the save pipeline with a null URL for text/photo imports would therefore silently skip translation unless that eligibility rule changes.
- The existing planner-only `PreviewAsync` is read-only and returns page text when deterministic extraction fails. It does not itself perform AI recipe extraction or provide a user import preview.

Sources: [import mapping and persistence](../../src/Dishhive.Api/Services/Import/RecipeImportService.cs), [enrichment staging](../../src/Dishhive.Api/Services/Facts/RecipeFactsAssessmentService.cs).

### What is established about the Reddit example

The supplied URL identifies comment `l9x2f06` inside thread `1dml7sk`. The Reddit page and alternate representations could not be retrieved during this review. Bot blocking or authentication requirements are plausible explanations, but the response received by the running application and the exact failing stage have not been verified.

The user has chosen to focus on the new input features rather than further investigating or improving this URL path. No Reddit reproduction, authenticated retrieval, comment resolver, or URL extraction enhancement is part of this implementation plan. The URL limitations recorded above remain background findings.

## 2. Intended behaviour

| Source | Extraction | Identity and follow-up |
| --- | --- | --- |
| URL | Existing deterministic extraction and configured AI fallback, unchanged | Preserve existing source URL and reimport behaviour |
| Text | AI structures the supplied recipe text | Create a recipe without inventing a source URL; continue normal mapping and enrichment |
| Photo | AI reads an uploaded or newly captured recipe image using verified image support | Create a recipe without inventing a source URL; continue normal mapping and enrichment |

The first use defaults to URL. Selecting another source immediately remembers that source in this browser, including after a failed import, closing the form, or reopening the app. Only the source choice is persisted in browser preferences. Invalid/missing stored values fall back to URL; storage failures do not prevent import.

All modes retain the existing save-then-review flow. Text and photo imports require usable AI; photo additionally requires image understanding. Unavailable modes explain the reason and let the user select another source. A remembered mode must not silently change because AI is temporarily unavailable.

Initial scope is one recipe per import and one photo per photo import. Multi-page scans, PDFs, URL extraction improvements, authenticated site access, bulk forum import, and a new review-before-save workflow are separate extensions. Text import provides a practical way to import recipe content copied from a page the application cannot fetch.

## 3. Step-by-step implementation

### Step 1 — Reuse shared import completion for new input sources

**Implementation:** Complete. `ImportExtractedAsync` accepts typed text/photo extraction
results, validates detached mapped content and JSON metadata, and reuses the URL import's
completion logic. URL-less imports create distinct recipes with `llm-text`/`llm-image`
provenance and can receive configured durable translation/facts enrichment. Existing URL
extraction behaviour is preserved. Raw source-text capture and photo ingestion belong to
the extraction steps below; completion accepts only recipe data and optional JSON metadata.

- Add explicit text/image entry points alongside the existing URL entry point. Keep source acquisition, recipe extraction, validation, and persistence as separate responsibilities.
- Extract reusable completion logic from `ImportAsync` as needed, retaining `ImportedRecipe` and the existing ingredient parser, line cleaner, image handling, reimport revision handling, and enrichment jobs. Preserve URL provider selection, HTML preparation, AI fallback, and error behaviour during this refactor.
- Validate usable ingredients and instructions **after cleaning**, plus domain field lengths/ranges, before accepting new text/photo extraction results or saving data. Changes to existing deterministic URL validation are deferred.
- Make source URL optional throughout the new paths. Run existing-recipe URL lookup only when a source URL exists: null-URL imports must never update an arbitrary existing null-URL recipe. Text/photo imports create new recipes; do not introduce implicit title-based overwrite or synthetic URLs.
- Preserve existing URL provider keys; distinguish text/image provenance with stable provider keys. Store any retained source metadata as valid JSON because `SourceRawData` is a PostgreSQL `jsonb` column. Retain bounded source text for traceability where appropriate; keep photo bytes transient by default and record only bounded metadata/hashes.
- Replace translation's URL-only eligibility check with explicit import-provenance eligibility for the new modes. Preserve manual-recipe behaviour, settings, original-language storage, and durable revision/confirmation checks.

**Completion criterion:** equivalent extracted recipes from all sources receive the same mapping and enrichment, and failed extraction cannot partially replace an existing recipe.

**Verification:** Backend suite with the local PostgreSQL service: 791 passed, one
browser workflow skipped. Coverage includes detached content validation, distinct
URL-less imports, JSONB metadata, facts/translation staging and settings, and existing
URL import/reimport and enrichment-concurrency regressions. No live model calls were
needed for this shared-completion step.

### Step 2 — Add AI text extraction and text import

**Implementation:** Complete; see the living recipe-import feature document and verification notes below.

- Add plain-text extraction that reuses the structured recipe mapping and validation where appropriate. A pasted text block is already text: do not strip it as HTML or require a fabricated `Uri`. Keep the existing URL extractor's preparation and prompt behaviour intact.
- Preserve source facts, order, quantities, units, and language. Keep extraction separate from the existing optional translation stage. Permit a concise descriptive title grounded in the recipe when informal content has no explicit heading; do not invent ingredients, amounts, times, or missing instructions. Unknown optional fields remain null; retain the existing servings default explicitly at mapping time.
- Reuse a typed recipe payload where feasible and define a versioned `AiJsonContract` schema and semantic validation for the new tasks. Keep supplied content separate from system instructions. Return typed extraction outcomes for text/photo so no-result, incomplete data, invalid model output, and service failure remain distinguishable.
- Retain the URL extraction task and add a distinct text task/profile for attribution. All calls and corrective attempts use `IAiExecutionGate`, one operation budget/ID, zero temperature, configured limits, and exactly one terminal semantic outcome per returned completion.
- Reject oversized text with a clear limit before calling AI instead of silently discarding part of the user's recipe.
- Add `POST /api/recipes/import/text` with a bounded `{ text }` request. Route its successful result to shared import completion and return the same `RecipeDto` as URL import.

**Completion criterion:** pasted prose or ingredient/step lists produce a normal saved recipe, while insufficient content and unavailable AI produce specific recoverable errors.

### Step 3 — Add image understanding and photo import

**Implementation:** Complete; see the living recipe-import feature document and verification notes below.

- Add an independent image-understanding capability verdict for the configured provider/model, cached by the relevant configuration fingerprint. Verify it using a small known image; neither AI configuration, model naming, nor passing planning/schema checks proves image support. Keep text extraction available when image support fails.
- Add a separate image extraction task/profile and any capability-probe task to the central gate, including its permitted probe types. Expose lightweight URL/text/photo availability and reasons through `GET /api/recipes/import/capabilities` so the form need not run all integration health probes.
- Send image bytes as multimodal message content through the existing client abstraction and execution gate. Extend admission budgeting, fingerprints/hashes, and telemetry to include image input; the current budget/hash logic counts message text only. Use conservative bounded image accounting and provider-reported usage where available.
- Add `POST /api/recipes/import/image` as a bounded multipart upload. Validate actual decodability, bytes and pixel dimensions; apply orientation and strip metadata. Reuse existing image-processing primitives with an extraction-appropriate resolution rather than blindly applying display-thumbnail settings. Verify supported formats, including the formats produced by target phones.
- Extract into the same recipe contract without guessing unreadable text. Report unreadable/incomplete content with guidance to retake the photo or paste text. A successful image import enters shared completion.
- Treat the uploaded recipe document as source material. Do not automatically set a photographed recipe page as the dish's display image.

**Completion criterion:** choosing a readable recipe photo or taking one on a supported phone yields a recipe; unsupported models, invalid files, and unreadable photos fail clearly without saving partial data.

### Step 4 — Add the remembered source selector

**Implementation:** Complete; see the living recipe-import feature document and verification notes below.

- Extend the library's import action with an accessible Material source menu for **URL**, **Text**, and **Photo**. The main action label and opened form reflect the selected source. Keep the existing library backup/import controls in settings.
- Add a small preference service with a validated local-storage value, defaulting to URL. Remember selection immediately, independently of request success. Restore it after navigation/reload and tolerate unavailable storage.
- URL displays the URL field; Text displays a multiline recipe field; Photo offers **Choose photo** and **Take photo**, a preview, replace/remove actions, and Import. Reuse the editor's capture/file patterns and verify camera availability on the supported devices.
- Show relevant capability explanations, input validation, progress, cancellation/error recovery, and a check-the-values success message. Do not clear source content after a failed request; prevent duplicate submissions and stale completions when switching sources.
- Add English/Dutch UI and server messages. Preserve keyboard focus/labels and check loading, long-text, unavailable-AI, invalid-file, and failed-import states at 360px and 1280px.

**Completion criterion:** URL is initially selected, every source is reachable, and the last selected source persists across app sessions in the same browser.

### Step 5 — Add new-source errors and operational reporting

**Implementation:** Complete; see the living recipe-import feature document and verification notes below.

- Add stable problem codes to the text/photo routes for no recipe, incomplete/ambiguous recipe, AI unavailable, image support unavailable, invalid/oversized input, unreadable photo, and AI timeout/service/contract failure. Choose HTTP statuses consistent with repository API conventions; reserve no-recipe for a genuine extraction verdict. Existing URL errors remain unchanged.
- Surface useful localized details in the form rather than only the generic problem title. Keep retry, source switching, and text/photo recovery available without losing input.
- Register the new task names and labels in AI performance reporting; include per-task successes/failures, attempts, usage, duration, and P95. Correlate extraction and later enrichment using the existing operation ID. Keep recipe text, raw model responses, and image bytes out of durable metrics.
- Include new operational limits in `AiOptions` fingerprints and keep `appsettings.json`, Compose, and `.env.example` consistent. Preserve no-op registrations for unconfigured AI and deterministic tests.
- Use existing provenance/settings fields where sufficient. Add a migration only if the agreed implementation requires new durable domain or telemetry fields; browser source preference alone needs none.

**Completion criterion:** users can distinguish changing the input from retrying a failed service, and operators can identify which stage/source is failing.

### Step 6 — Complete integration and verification

**Implementation:** Complete; see the living recipe-import feature document and verification notes below.

- Add focused tests with each step. Exercise text/photo extraction with AI off, successful responses, malformed/incomplete AI output, cancellation, and timeouts. Run existing URL success, provider/fallback, reimport, and planner-preview regressions when shared code is touched; add no new forum retrieval requirements.
- Cover text and photo success, missing title, unknown quantities, oversized input, invalid images/orientation, unsupported image capability, unreadable content, untrusted instructions, and preservation of source wording/order.
- Verify null-URL imports create distinct recipes and still stage configured translation/facts. Verify reimports invalidate old enrichment and failed imports leave existing data intact. Include relational tests where JSONB, unique URLs, and enrichment concurrency matter.
- Test source preference on first use, selection, failed import, close/reopen, reload, corrupt/missing storage, and unavailable AI. Exercise file selection and supported phone capture; browser fixture tests alone do not prove real camera behaviour.
- Regenerate OpenAPI and the Angular client using `npm run generate:api-client`. Run backend tests, Angular tests/build, browser workflow regressions, and the 360px/1280px rendered audit. Use `scripts/verify-local.ps1` for final repository verification, plus `git diff --check`.
- Run a small live extraction check using representative informal recipe text and representative phone photos against the configured model. Report live/model/device checks separately from deterministic fixture tests.
- Update the living recipe-import documentation and checklists for text/photo input, capability requirements, remembered source selection, and shared enrichment. Keep the existing description of URL provider ordering accurate.

**Completion criterion:** text and photo inputs produce normal recipes through the shared pipeline, source selection is remembered, existing URL/library/planner behaviour is preserved, and any untested model/device limitations are explicit.

## 4. Suggested delivery order

1. **Shared completion:** prepare the reuse boundary and provenance/translation eligibility for inputs without a URL.
2. **Text import and source selection:** add text extraction/input and introduce the selector with URL as the initial default and the last selection remembered.
3. **Photo import:** verify image capability, extend multimodal gate accounting, add upload/capture UI, and validate representative phones/photos.
4. **Complete verification:** finish capability/error reporting, cross-source regression checks, mobile/desktop validation, and documentation.

The shared completion boundary should be established before adding the new input routes so ingredient parsing, persistence, and enrichment remain one pipeline. The new text/photo features do not depend on diagnosing Reddit access or improving URL extraction.

## 5. Implementation evidence and limits

- Backend unit/HTTP/relational coverage includes new-source validation, persistence,
  model outcomes, bounded retries, image-aware hashes/admission, capability caching,
  and enrichment settings. No schema migration was required.
- Frontend unit tests cover preference restoration/storage failure, retained drafts,
  cancellation and unavailable AI. Browser fixtures cover URL/text/photo states at
  360px and 1280px, successful import navigation and native capture-input wiring.
- Existing URL/provider ordering, page preparation, errors and reimport behaviour
  remain unchanged. No Reddit retrieval/authentication work was added.
- Live smoke checks against the configured model successfully extracted the synthetic
  pancake recipe from text and a rendered image, and passed the image capability probe.
  These checks do not establish handwriting or difficult real-photo accuracy.
- The UI uses existing Material controls and theme styles. Local CSS is limited to
  wrapping the form, bounded photo-preview geometry and the established mobile boundary.
- Physical phone capture and camera/gallery formats still need target-device validation.
  Only JPEG/PNG/WebP are advertised; unsupported input receives an actionable error.
- Final verification output is retained in `artifacts/flexible-import-verification`;
  inspected source-form screenshots are in `artifacts/flexible-import-ui`.

Final checks passed: 806 backend tests with PostgreSQL (the opt-in live check was
run separately), 97 frontend tests, 42 browser fixtures, the production PWA test,
13 Linux scraper tests, production build, localization and generated-contract checks.
The final capability-check close/reopen fix additionally passed its frontend regression,
production rebuild and all six import browser tests. `git diff --check` passed.
