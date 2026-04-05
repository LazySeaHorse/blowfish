# task-breakdown.md

# ImageCaptionSearch Task Breakdown

This document turns `spec.md` into an implementation plan for an AI coding agent.

## 1. Instructions to the Coding Agent

Read `spec.md` first. It is the source of truth. If this file conflicts with `spec.md`, follow `spec.md`.

### Hard rules
- Use **C# + Avalonia** only for the desktop UI.
- Use **MVVM Community Toolkit**.
- Keep all non-UI logic in `ImageCaptionSearch.Core`.
- Do not use WPF, WinForms, HTML, CSS, JavaScript, WebView, Tauri, Electron, or any browser shell.
- Use strict JSON parsing for LM Studio caption responses.
- Store each library’s data inside that library folder under `.imagecaptionsearch`.
- Keep CI green after every milestone.
- No emojis anywhere.

### Delivery rules
- Build in small vertical slices.
- Prefer working end-to-end increments over broad incomplete scaffolding.
- Add tests with each core feature.
- Avoid placeholder implementations unless explicitly temporary and clearly marked.
- Do not hardcode package versions if central package management can be used.
- Use latest stable compatible packages.

---

## 2. Recommended Delivery Strategy

Implement in this order:

1. Repository skeleton and guardrails
2. Core/UI separation
3. Local library registry and per-library DB creation
4. Folder scanning and change detection
5. LM Studio client and strict caption parsing
6. Embeddings and storage
7. Search
8. TPL Dataflow indexing pipeline
9. Face pipeline
10. UI polish
11. CI/CD hardening and release packaging

The goal is to have a minimally usable app as early as possible:
- add library
- scan images
- caption and embed
- search
- inspect results

---

## 3. Milestone Summary

| Milestone | Title | Depends On | Main Output |
|---|---|---:|---|
| M0 | Repo bootstrap and guardrails | none | Solution, projects, analyzers, policy checks | [x] |
| M1 | Core domain and app shell | M0 | Core abstractions, Avalonia shell | [x] |
| M2 | Library registry and DB creation | M1 | Add/open/remove library, DB initialization | [ ] |
| M3 | Scanning and thumbnails | M2 | File discovery, metadata, thumbnails |
| M4 | LM Studio integration | M2 | Connection, model listing, captioning, embeddings |
| M5 | Persistence and search engine | M3, M4 | Caption DB, vector DB, FTS, cosine search |
| M6 | Indexing pipeline | M3, M4, M5 | TPL Dataflow queue, retries, progress |
| M7 | Search UI and detail views | M5, M6 | Usable MVP search experience |
| M8 | Face pipeline | M4, M5, M6 | Gated face embeddings and similar-face search |
| M9 | Settings, resiliency, logging | M2-M8 | Restart safety, validation, diagnostics |
| M10 | CI/CD and packaging | M0-M9 | GitHub Actions build/test/package/release |
| M11 | Final polish and acceptance pass | all | MVP validation against spec |

---

## 4. Detailed Task List

---

## M0. Repo Bootstrap and Guardrails

### [x] T001. Create solution and projects
**Goal:** Create a clean solution structure with strict separation.

**Deliverables**
- `src/ImageCaptionSearch.UI`
- `src/ImageCaptionSearch.Core`
- `tests/ImageCaptionSearch.Core.Tests`
- `tests/ImageCaptionSearch.UI.Tests` if useful, otherwise create later
- solution file
- central package management setup if used

**Subtasks**
- Create the solution and projects.
- Configure `ImageCaptionSearch.UI` as Avalonia desktop app.
- Configure `ImageCaptionSearch.Core` as plain class library.
- Add project references:
  - UI references Core
  - Tests reference target projects
- Ensure Core has no Avalonia dependency.

**Acceptance**
- Solution builds.
- UI launches to a placeholder window.
- Core has no Avalonia packages or Avalonia namespaces.

---

### [x] T002. Add repository-wide coding standards
**Goal:** Make the repo consistent and CI-friendly from day one.

**Deliverables**
- `.editorconfig`
- optional `Directory.Build.props`
- optional `Directory.Packages.props`
- nullable enabled
- warnings as errors where reasonable
- analyzers configured

**Subtasks**
- Enable nullable reference types.
- Enable implicit usings if desired.
- Configure analyzers and code style.
- Set deterministic builds if convenient.
- Add formatting config.

**Acceptance**
- `dotnet build` passes cleanly.
- Formatting can be checked in CI.
- New code defaults to nullable-safe patterns.

---

### [x] T003. Add policy checks to block forbidden tech
**Goal:** Prevent architecture drift.

**Deliverables**
- CI-friendly script(s) or test(s) that fail on:
  - WPF usage
  - WindowsDesktop SDK / `UseWPF`
  - web UI files in app code
  - Avalonia referenced by Core
  - WebView/Electron/Tauri references

**Subtasks**
- Add a script under `build/` or `scripts/`.
- Search project files and source tree for forbidden patterns.
- Integrate with CI.

**Acceptance**
- Introducing `UseWPF` or `.html` in app code fails CI.
- Introducing Avalonia reference into Core fails CI.

---

## M1. Core Domain and App Shell

### [x] T004. Define core domain models and enums
**Goal:** Establish a stable domain model before implementation expands.

**Deliverables**
Core types for:
- library
- image record
- caption record
- embedding metadata
- face record
- processing states
- search query and results
- settings models
- error/result types

**Subtasks**
- Define immutable or well-scoped models.
- Define enums for:
  - image status
  - pipeline state
  - search mode
  - retry classification
- Keep models serialization-friendly.

**Acceptance**
- Core compiles with no UI references.
- Models support all fields required by `spec.md`.

---

### [x] T005. Define core service interfaces
**Goal:** Keep implementation swappable and testable.

**Deliverables**
Interfaces such as:
- `ILibraryRegistryService`
- `ILibraryService`
- `IScanService`
- `IIndexingPipelineService`
- `ILmStudioClient`
- `ICaptionService`
- `IEmbeddingService`
- `ISearchService`
- `IFaceRecognitionService`
- `IThumbnailService`
- `ISettingsService`

**Subtasks**
- Define method signatures and contracts.
- Keep interfaces UI-agnostic.
- Add cancellation tokens where needed.

**Acceptance**
- Core public API is coherent and testable.
- UI can depend on interfaces only.

---

### [x] T006. Create Avalonia app shell
**Goal:** Establish the basic desktop application structure.

**Deliverables**
- App bootstrap
- main window
- view model wiring
- dependency injection setup
- placeholder navigation or main content regions

**Subtasks**
- Configure Avalonia app entry point.
- Use MVVM Community Toolkit.
- Create a simple shell layout that can later host:
  - library home
  - library detail
  - settings

**Acceptance**
- App launches cleanly.
- ViewModels resolve through DI.
- No business logic in code-behind.

---

## M2. Library Registry and DB Creation

### T007. Implement app-level library registry
**Goal:** Track known libraries under `%LocalAppData%\ImageCaptionSearch\`.

**Deliverables**
- registry storage
- add/remove/open/update library metadata
- last opened library support

**Subtasks**
- Choose simple persistent format:
  - JSON file or SQLite
- Store:
  - library root path
  - display name
  - last opened
  - app-level preferences
- Handle missing/moved library roots.

**Acceptance**
- Libraries persist across app restarts.
- Last opened library can be restored.
- Moved/missing libraries are detected cleanly.

---

### T008. Validate library roots and prevent nesting
**Goal:** Enforce library rules from the spec.

**Deliverables**
- root validation logic
- overlap detection
- write-access checks

**Subtasks**
- Reject duplicate roots.
- Reject nested libraries.
- Check root is writable.
- Exclude invalid folders with useful messages.

**Acceptance**
- `C:\Photos` and `C:\Photos\Trips` cannot both be added.
- Read-only or inaccessible folders are rejected gracefully.

---

### T009. Create per-library internal storage folder
**Goal:** Initialize `.imagecaptionsearch` inside each library root.

**Deliverables**
- hidden internal folder creation
- thumbnails folder creation
- DB file creation hooks

**Subtasks**
- Create:
  - `.imagecaptionsearch`
  - `thumbnails`
- Mark internal folder hidden on Windows.
- Ensure scanner excludes this folder.

**Acceptance**
- New library creates the hidden folder and required substructure.
- Existing valid library data is reused.

---

### T010. Implement DB creation and schema migrations
**Goal:** Create `catalog.db` and `vectors.db` with versioned migrations.

**Deliverables**
- migration runner
- initial schema for both DBs
- schema version tracking

**Subtasks**
- Create SQLite connections and migration mechanism.
- Implement tables from `spec.md`.
- Enable pragmas as needed:
  - foreign keys
  - WAL where appropriate
- Build FTS table for captions.

**Acceptance**
- A fresh library creates both DB files successfully.
- Reopening the library does not recreate or corrupt existing DBs.
- Schema version is tracked.

---

## M3. Scanning and Thumbnails

### T011. Implement recursive image discovery
**Goal:** Find supported image files under a library root.

**Deliverables**
- recursive scanner
- supported extension filter
- internal folder exclusion
- hidden/system handling

**Subtasks**
- Support extensions from `spec.md`.
- Exclude `.imagecaptionsearch`.
- Normalize paths to root-relative paths.
- Avoid symlink/reparse loops by default.

**Acceptance**
- Scanner returns only supported user image files.
- App-generated files are never indexed.

---

### T012. Implement dirty detection
**Goal:** Determine which files are new, changed, unchanged, or missing.

**Deliverables**
- comparison logic using:
  - relative path
  - size
  - modified UTC
- missing-file marking

**Subtasks**
- Compare current scan against DB records.
- Mark files for:
  - queueing
  - no-op
  - missing
- Optionally prepare content hash extension point.

**Acceptance**
- Changed files are reprocessed.
- Deleted files are marked missing.
- Unchanged files are skipped.

---

### T013. Extract image metadata and generate thumbnails
**Goal:** Produce thumbnail cache and basic dimensions safely.

**Deliverables**
- image loading abstraction
- orientation correction
- metadata extraction
- thumbnail generation and storage

**Subtasks**
- Read image dimensions safely.
- Handle corrupt files gracefully.
- Generate thumbnails into `.imagecaptionsearch\thumbnails`.
- Store thumbnail relative path in DB.

**Acceptance**
- Valid images have thumbnails and dimensions stored.
- Corrupt images do not crash indexing.

---

## M4. LM Studio Integration

### T014. Implement LM Studio API client
**Goal:** Communicate with LM Studio through local OpenAI-compatible endpoints.

**Deliverables**
- model listing
- connection test
- chat completions for vision captioning
- embeddings request support

**Subtasks**
- Add `HttpClient`-based client.
- Support configurable base URL.
- Model endpoints:
  - `GET /v1/models`
  - `POST /v1/chat/completions`
  - `POST /v1/embeddings`
- Make it mockable for tests.

**Acceptance**
- Client can test connectivity.
- Client can fetch model list when available.
- Client supports timeouts and cancellation.

---

### T015. Implement caption prompt builder
**Goal:** Enforce strict, deterministic JSON output.

**Deliverables**
- prompt template
- schema/instructions
- prompt version identifier

**Subtasks**
- Build vision prompt to require:
  - JSON only
  - `caption`
  - `has_human`
  - no extra keys
- Keep prompt versioned.
- Prefer structured schema if endpoint supports it.

**Acceptance**
- Prompt output contract matches `spec.md`.
- Prompt version is stored with results.

---

### T016. Implement strict JSON caption parsing
**Goal:** Parse LM Studio responses deterministically.

**Deliverables**
- parser/validator
- error classification
- retryable parse failure handling

**Subtasks**
- Parse with `System.Text.Json`.
- Reject:
  - missing keys
  - extra keys
  - empty caption
  - wrong types
- Normalize/truncate whitespace.
- Preserve raw JSON string.

**Acceptance**
- Valid responses parse into a strong type.
- Invalid responses fail cleanly and can trigger retries.

---

### T017. Implement embedding service
**Goal:** Convert caption text into stored vectors.

**Deliverables**
- text embedding request logic
- vector normalization/metadata
- vector serialization

**Subtasks**
- Send caption text to LM Studio embedding endpoint.
- Validate embedding dimensions.
- Store float32 vectors as blobs.
- Store vector norm or pre-normalize vectors.

**Acceptance**
- Captions can be embedded and persisted.
- Invalid embedding payloads are rejected.

---

## M5. Persistence and Search Engine

### T018. Persist image, caption, and embedding data
**Goal:** Save processed outputs consistently across both DBs.

**Deliverables**
- repository layer
- transactional writes
- processing state updates

**Subtasks**
- Insert/update `images`, `captions`, `image_embeddings`.
- Store:
  - model names
  - dimensions
  - prompt version
  - timestamps
  - raw JSON
- Coordinate updates across both DBs.

**Acceptance**
- Processed items show consistent DB state.
- Partial failure produces a retryable/repairable state, not silent corruption.

---

### T019. Implement caption search with SQLite FTS
**Goal:** Support direct caption search without LM Studio availability.

**Deliverables**
- FTS index maintenance
- caption query service
- ranked results

**Subtasks**
- Build or sync `captions_fts`.
- Return only completed, non-missing items.
- Support result limits and paging/incremental loading.

**Acceptance**
- Indexed captions are searchable while LM Studio is offline.
- Results are relevant and ordered.

---

### T020. Implement semantic search engine
**Goal:** Support natural-language semantic retrieval per library.

**Deliverables**
- query embedding flow
- vector cache
- cosine similarity search
- model compatibility checks

**Subtasks**
- Embed query text using the current library embedding model.
- Load image vectors into an in-memory cache.
- Compute exact cosine similarity.
- Enforce model-space consistency.

**Acceptance**
- Semantic search returns ranked results.
- If the library embedding model changed, user is told re-embedding is required.

---

### T021. Implement result mapping and domain DTOs for UI
**Goal:** Keep UI simple and avoid DB-shaped view models.

**Deliverables**
- search result DTOs
- image detail DTOs
- library summary DTOs

**Subtasks**
- Shape results with:
  - thumbnail
  - caption snippet
  - path
  - score
  - human flag
  - processing status
- Avoid leaking repository internals to UI.

**Acceptance**
- UI can render library lists, result grids, and detail views without custom DB logic.

---

## M6. Indexing Pipeline

### T022. Implement TPL Dataflow-based indexing pipeline
**Goal:** Build a resilient asynchronous processing queue.

**Deliverables**
- pipeline stages
- bounded capacity
- cancellation support
- concurrency settings

**Subtasks**
- Define stages for:
  - discovery intake
  - image load/thumbnail
  - captioning
  - parsing
  - embedding
  - persistence
  - face processing
- Use user-adjustable concurrency.
- Default concurrency = 2.
- Allow 1–8, recommend 2–4 in UI.

**Acceptance**
- Pipeline processes images asynchronously.
- UI remains responsive.
- LM Studio calls do not exceed configured concurrency.

---

### T023. Add pause, resume, cancel, and retry support
**Goal:** Make indexing controllable and recoverable.

**Deliverables**
- pause/resume mechanism
- cancel current run
- retry failed items
- pipeline state tracking

**Subtasks**
- Implement state machine for indexing session.
- Keep state visible to UI.
- Persist enough state to resume cleanly after restart if possible.

**Acceptance**
- User can pause/resume/cancel indexing without app instability.
- Failed items can be retried manually.

---

### T024. Add retry policy and transient error handling
**Goal:** Handle local model and network instability safely.

**Deliverables**
- retry policy with backoff
- transient vs permanent failure classification

**Subtasks**
- Retry:
  - LM Studio timeout
  - local connection failure
  - transient invalid model output
- Do not retry:
  - corrupt image
  - missing file
  - unsupported format

**Acceptance**
- Retryable failures are retried a small number of times.
- Non-retryable failures are marked failed once with a useful error.

---

### T025. Prioritize interactive search over background indexing
**Goal:** Prevent background indexing from making search unusable.

**Deliverables**
- request prioritization strategy
- separate or prioritized LM Studio channels

**Subtasks**
- Ensure user-initiated semantic searches are not starved behind batch jobs.
- Keep caption search fully local and independent.

**Acceptance**
- A semantic search request remains responsive even while indexing is active.

---

## M7. Search UI and Detail Views

### T026. Implement Library Home screen
**Goal:** Let the user manage libraries clearly.

**Deliverables**
- library list/grid
- add/open/remove actions
- status summaries

**Subtasks**
- Show:
  - display name
  - path
  - indexed count
  - pending count
  - failed count
  - last scan time
- Add “Add Library” flow.
- Add confirmation for removal and optional local data deletion.

**Acceptance**
- User can create and open libraries from the UI.
- Nested or invalid libraries show clear validation errors.

---

### T027. Implement Library Detail screen
**Goal:** Provide the main search and indexing workspace.

**Deliverables**
- search bar
- search mode selector
- indexing controls
- status summary
- result grid host

**Subtasks**
- Add search mode toggle:
  - Caption
  - Semantic
- Add filters:
  - All
  - Human
  - No human
- Show indexing progress and counts.
- Add rescan/start/pause/resume/cancel actions.

**Acceptance**
- Library detail is the main usable workspace for MVP.
- Search and indexing controls are discoverable and clean.

---

### T028. Implement result grid with virtualization/incremental loading
**Goal:** Display many items efficiently.

**Deliverables**
- result cards
- thumbnail rendering
- lazy/incremental loading strategy

**Subtasks**
- Each card shows:
  - thumbnail
  - file name
  - caption snippet
  - human indicator
  - status indicator if needed
- Clicking opens detail.

**Acceptance**
- Large result sets remain responsive.
- Thumbnails load without freezing the UI.

---

### T029. Implement Image Detail view
**Goal:** Give users a focused inspection screen for one image.

**Deliverables**
- larger preview
- metadata panel
- caption display
- actions

**Subtasks**
- Show:
  - preview
  - full caption
  - path
  - dimensions
  - status
  - human flag
  - face count
- Add actions:
  - Open image
  - Show in Explorer
  - Copy caption
  - Re-caption
  - Re-embed
  - Find similar images
  - Find similar faces

**Acceptance**
- Detail view supports core per-image workflows.
- File system actions work on Windows.

---

## M8. Face Pipeline

### T030. Add face pipeline configuration model
**Goal:** Prepare the face subsystem without polluting other services.

**Deliverables**
- detector model settings
- recognizer model settings
- feature enabled/disabled controls

**Subtasks**
- Define settings for:
  - face enabled
  - detector model path
  - recognizer model path
- Add capability checks.

**Acceptance**
- Face pipeline can be disabled cleanly when models are unavailable.

---

### T031. Implement gated face detection and embedding
**Goal:** Run face processing only when `has_human = true`.

**Deliverables**
- ONNX Runtime integration
- face detector stage
- MobileFaceNet embedding stage

**Subtasks**
- Load detector and recognizer models.
- Detect and align/crop faces.
- Generate one or more normalized face embeddings per image.
- Persist metadata and vectors.

**Acceptance**
- Face processing is skipped when `has_human = false`.
- Images with detected faces store face records and face embeddings.

---

### T032. Implement similar-face search
**Goal:** Let the user find visually similar faces within the same library.

**Deliverables**
- face similarity service
- ranked face match results

**Subtasks**
- Use all faces from the source image as query seeds.
- Compare against stored face embeddings in the same library.
- Return best-ranked images and/or face matches.

**Acceptance**
- “Find similar faces” works from an image that has stored face embeddings.
- Results are scoped to the current library.

---

### T033. Surface face status in UI
**Goal:** Make face functionality visible but not intrusive.

**Deliverables**
- face count on detail view
- similar-face action visibility
- model-missing warning state

**Subtasks**
- Show whether face models are unavailable.
- Hide or disable similar-face actions when no faces exist.

**Acceptance**
- Face-related UI is clear and gracefully degraded when disabled.

---

## M9. Settings, Resiliency, and Logging

### T034. Implement Settings UI and persistence
**Goal:** Let users configure LM Studio, indexing, and face options.

**Deliverables**
- settings screen
- validation
- persistence

**Subtasks**
- Add sections:
  - LM Studio
  - Indexing
  - Face Recognition
  - General
  - Advanced
- Include:
  - base URL
  - vision model
  - embedding model
  - timeouts
  - concurrency
  - retries
  - face model paths
- Add:
  - Test Connection
  - Refresh Models

**Acceptance**
- Settings survive restart.
- Invalid values are blocked or clearly warned.

---

### T035. Implement startup recovery and interrupted-job handling
**Goal:** Survive app restarts and failures gracefully.

**Deliverables**
- pipeline recovery behavior
- cleanup or requeue logic for partial jobs

**Subtasks**
- On startup, inspect unfinished records.
- Move incomplete work back to queued/retry state.
- Avoid duplicate completed entries.

**Acceptance**
- Abrupt app closure does not leave the library in an unusable state.
- Incomplete jobs can continue or be retried.

---

### T036. Implement structured logging
**Goal:** Aid debugging without exposing unnecessary data.

**Deliverables**
- log output under `%LocalAppData%\ImageCaptionSearch\logs`
- core and UI logging hooks

**Subtasks**
- Log:
  - startup/shutdown
  - library add/remove
  - scan summaries
  - indexing failures
  - LM Studio failures
  - migration failures
- Avoid overly noisy logs in normal flow.

**Acceptance**
- Logs are written locally and useful for diagnosis.
- No source images are copied into logs.

---

### T037. Improve error surfaces in UI
**Goal:** Show actionable user-facing states instead of raw exceptions.

**Deliverables**
- error banners/dialogs/status messages
- empty states
- offline LM Studio state
- no-results state

**Subtasks**
- Add clear text for:
  - no libraries
  - no results
  - LM Studio unavailable
  - face models missing
  - failed items pending retry
- Do not expose stack traces to normal users.

**Acceptance**
- Common failures do not crash the app.
- Users see clear next steps.

---

## M10. CI/CD and Packaging

### T038. Implement CI workflow
**Goal:** Ensure the repo is always buildable and validated.

**Deliverables**
- `.github/workflows/ci.yml`

**Subtasks**
- Setup .NET SDK.
- Restore, build, test.
- Run formatting/analyzer checks.
- Run policy checks.
- Publish Windows smoke artifact.

**Acceptance**
- CI runs on push and pull request.
- Failures are easy to interpret.

---

### T039. Implement release workflow
**Goal:** Produce a distributable Windows artifact.

**Deliverables**
- `.github/workflows/release.yml`

**Subtasks**
- Trigger on tag or manual dispatch.
- Build/test/publish app.
- Produce `win-x64` artifact.
- Zip output.
- Upload artifact.
- Optionally create GitHub Release.

**Acceptance**
- Release workflow outputs a downloadable Windows package.

---

### T040. Add artifact packaging validation
**Goal:** Catch deployment issues before release.

**Deliverables**
- publish profile or scripted publish step
- packaging verification step

**Subtasks**
- Confirm packaged app includes required assets.
- Confirm app launches in a basic smoke-check if feasible.
- Verify ONNX model handling strategy does not break publish.

**Acceptance**
- Published artifact is structurally complete.
- Packaging does not omit required runtime assets.

---

## M11. Final Polish and Acceptance Pass

### T041. Visual polish pass
**Goal:** Make the UI feel modern, clean, and friendly.

**Deliverables**
- light-theme refinement
- spacing and typography cleanup
- card/list polish

**Subtasks**
- Review:
  - spacing
  - margins
  - empty states
  - focus states
  - contrast
- Ensure UI feels native and not developer-centric.

**Acceptance**
- App clearly meets the “clean, intuitive, light mode, modern and friendly UI” requirement.

---

### T042. Accessibility and keyboard pass
**Goal:** Meet minimum accessibility expectations.

**Deliverables**
- keyboard navigation
- visible focus states
- sensible tab order

**Subtasks**
- Verify all key actions are reachable by keyboard.
- Ensure buttons and fields have visible focus.
- Avoid tiny click targets.

**Acceptance**
- Main flows can be used without a mouse.
- Focus indication is visible and consistent.

---

### T043. Full spec compliance review
**Goal:** Validate that nothing critical was missed.

**Deliverables**
- checklist-based pass against `spec.md`
- fixes for any remaining deviations

**Subtasks**
- Review architecture boundaries.
- Review storage locations.
- Review search behavior.
- Review face gating.
- Review CI policy coverage.
- Review absence of forbidden tech and emojis.

**Acceptance**
- All acceptance criteria in `spec.md` are satisfied or explicitly documented as deferred.

---

## 5. Test Plan by Workstream

---

## Core unit tests
Create tests for:
- path normalization
- library overlap detection
- supported extension filtering
- dirty detection
- JSON parsing strictness
- prompt contract handling
- vector serialization
- cosine similarity math
- model mismatch handling
- queue state transitions
- retry classification

---

## DB and integration tests
Create tests for:
- schema creation
- migration idempotency
- caption persistence
- embedding persistence
- FTS search
- vector retrieval
- missing-file handling
- partial failure handling
- restart recovery

Use:
- temporary folders
- temporary SQLite DBs
- fake LM Studio HTTP server or handler

---

## LM Studio integration tests
Create tests for:
- connection success/failure
- model list parsing
- caption request payload shape
- embedding request payload shape
- timeout handling
- strict invalid JSON response handling

---

## Face pipeline tests
Create tests for:
- `has_human = false` skips face stage
- `has_human = true` enables face stage
- face metadata persistence
- face vector persistence
- similar-face ranking on deterministic synthetic embeddings

If real ONNX models are too heavy for CI:
- keep unit tests synthetic
- gate real-model integration tests separately if needed

---

## UI/ViewModel tests
Create tests for:
- add/remove library flow
- validation error messages
- switching search modes
- indexing state transitions
- empty states
- settings save/load behavior

---

## 6. Suggested PR / Commit Slices

If the coding agent works best in small slices, use this order:

1. Bootstrap solution, projects, analyzers, policy checks
2. Core domain models and service interfaces
3. Avalonia shell and DI
4. Library registry and root validation
5. Per-library folder creation and DB migrations
6. Scanner and dirty detection
7. Thumbnail generation and metadata extraction
8. LM Studio client and connection settings
9. Prompt builder and strict JSON parser
10. Embedding service and vector storage
11. Caption search with FTS
12. Semantic search with cosine similarity
13. TPL Dataflow indexing pipeline
14. Pause/resume/cancel/retry controls
15. Library home UI
16. Library detail/search UI
17. Image detail UI
18. Face pipeline core
19. Similar-face search UI
20. Settings screen and connection testing
21. Recovery, logging, and error UX
22. CI/release workflows and packaging
23. Final polish and acceptance sweep

---

## 7. Definition of Done Per Milestone

A milestone is done only if:
- code builds
- relevant tests pass
- no forbidden tech introduced
- Core remains UI-free
- CI remains green
- user-facing flows for that milestone are usable, not half-wired

---

## 8. Risk List and Mitigations

### Risk: LM Studio returns malformed JSON
**Mitigation**
- strict parser
- retries with backoff
- low temperature
- structured schema if available

### Risk: background indexing overwhelms local GPU queue
**Mitigation**
- bounded TPL Dataflow
- shared LM Studio concurrency limiter
- default concurrency = 2
- prioritize interactive search

### Risk: face pipeline complexity delays MVP
**Mitigation**
- complete image caption/search MVP first
- keep face subsystem isolated
- degrade gracefully if models unavailable

### Risk: large vector sets slow semantic search
**Mitigation**
- in-memory cache
- normalized vectors
- exact cosine for MVP
- incremental optimization only if needed

### Risk: architecture drift into UI code-behind
**Mitigation**
- service interfaces in Core
- policy checks
- ViewModel-driven UI
- code review checklist

---

## 9. Explicit MVP Cut Line

The MVP must include:
- multiple libraries
- per-library DBs in the library folder
- scanning and change detection
- LM Studio captioning with strict JSON
- caption embeddings
- caption search
- semantic search
- TPL Dataflow queue with pause/resume/cancel
- gated face pipeline
- similar-face search
- Avalonia light-mode UI
- GitHub Actions CI/CD and release artifact

Not required before MVP completion:
- cross-library search
- OCR
- RAW support
- dark mode
- auto-updater
- duplicate detection
- person naming

---

## 10. Final Acceptance Checklist

Use this at the end.

- [ ] Solution uses Avalonia, not WPF
- [ ] Core library has no Avalonia dependency
- [ ] No web tech is used
- [ ] Multiple non-overlapping libraries are supported
- [ ] Each library stores `.imagecaptionsearch\catalog.db`
- [ ] Each library stores `.imagecaptionsearch\vectors.db`
- [ ] Scanner excludes internal app folder
- [ ] New/changed/missing file states work correctly
- [ ] LM Studio connection and model settings are configurable
- [ ] Caption prompt requires strict JSON only
- [ ] Caption parser rejects extra/malformed fields
- [ ] Captions and raw JSON are stored
- [ ] Embeddings are stored as vectors with metadata
- [ ] Caption search works locally without LM Studio
- [ ] Semantic search works through LM Studio embeddings
- [ ] Semantic search enforces embedding-model compatibility
- [ ] Indexing is asynchronous and UI remains responsive
- [ ] User can start/pause/resume/cancel indexing
- [ ] Retry failed items is supported
- [ ] Face pipeline only runs when `has_human = true`
- [ ] Face embeddings are stored locally
- [ ] Similar-face search works within a library
- [ ] Library home UI is usable
- [ ] Library detail and result grid are usable
- [ ] Image detail screen is usable
- [ ] Settings screen is usable
- [ ] Logging exists under LocalAppData
- [ ] CI builds, tests, runs policy checks, and publishes Windows artifact
- [ ] No emojis appear anywhere