# ImageCaptionSearch Specification

## 0. App Name

Blowfish

## 1. Overview

**Working name:** `ImageCaptionSearch`  
Use this as the default solution/namespace/app name unless the repository already has a chosen name.

## 2. Product Summary

Build a **native Windows desktop app** using **C# + Avalonia UI** that:

- indexes images from one or more user-selected local folders
- captions each image using **LM Studio** via its local REST API
- stores the caption and a semantic embedding locally
- supports:
  - direct caption text search
  - semantic vector search using cosine similarity
- optionally runs a local face-recognition pipeline using **ONNX Runtime + MobileFaceNet**, but only when caption output indicates a human is present
- stores each library’s local databases **inside that library folder**
- is developed and validated through **GitHub Actions CI/CD**
- has a **clean, intuitive, light-mode, modern, friendly UI**
- uses **no emojis anywhere**

This is a **Windows MVP**. The architecture must still remain clean and extensible.

---

## 3. Mandatory Directives

These are hard requirements.

### 3.1 UI and platform
- Use **Avalonia UI** only.
- Use **MVVM Community Toolkit** (`CommunityToolkit.Mvvm`) for MVVM.
- Do **not** use WPF.
- Do **not** use WinForms.
- Do **not** use HTML/CSS/JavaScript.
- Do **not** use WebView, Electron, Tauri, Blazor Hybrid, or any browser-based shell.
- Target **Windows** for the MVP.

### 3.2 Architecture
- All non-UI logic must live in a **C# class library** that is fully decoupled from the UI layer.
- The UI project must only orchestrate views, view models, bindings, and UI composition.
- The core library must contain:
  - data/storage access
  - LM Studio API client
  - image indexing logic
  - queue/pipeline logic
  - embedding logic
  - search logic
  - face pipeline
  - settings/domain models
- The core library must have **no Avalonia references** and no UI-specific types.

### 3.3 AI response formatting
- All captioning prompts must force a **strict JSON response**.
- Parsing must be strict and deterministic.
- Do not rely on loose regex parsing as the normal path.
- Prefer structured output / JSON schema when supported by LM Studio; otherwise enforce JSON-only prompt responses.

### 3.4 CI/CD
- Repository must be set up so that all validation is run via **GitHub Actions**.
- Build, test, packaging, and policy checks must all run in CI.
- CI must include checks that block accidental introduction of:
  - WPF
  - web tech
  - Avalonia references inside the core library

### 3.5 Style
- No emojis in:
  - UI text
  - docs
  - logs
  - test names
  - comments
  - workflow messages

---

## 4. Goals

## 4.1 MVP goals
1. Let the user register multiple image folders as separate libraries.
2. Each library has its own local databases stored inside that folder.
3. Scan images recursively and index new/changed files.
4. Caption images using LM Studio.
5. Generate text embeddings from captions using LM Studio.
6. Store captions and embeddings locally.
7. Support:
   - direct caption search
   - semantic search
8. Run face detection/face embedding only when `has_human = true`.
9. Keep the UI responsive during indexing.
10. Package a Windows artifact through GitHub Actions.

## 4.2 Non-goals for MVP
- No cloud AI services.
- No browser-based UI.
- No OCR pipeline.
- No video indexing.
- No person naming/labeling workflow.
- No cross-library unified search in MVP.
- No Linux/macOS packaging in MVP.
- No auto-update system in MVP.
- No RAW photo support unless it is trivial and stable.

---

## 5. Target Users and Core Use Cases

### 5.1 Users
- A user with a local image collection on Windows
- A user running LM Studio locally
- A user who wants fast local search over images using natural language

### 5.2 Primary use cases
1. Add a local folder as a library.
2. Index all supported images in that folder.
3. Search by exact or fuzzy caption text.
4. Search semantically using natural-language queries.
5. Open a result in the default image viewer or reveal it in Explorer.
6. See whether an image contains a human.
7. Find similar faces from an image that contains people.

---

## 6. High-Level Product Behavior

Each selected folder becomes an independent **library**.

For each library:

1. Create a hidden internal app folder inside the selected root.
2. Create two local databases inside that internal folder:
   - `catalog.db`
   - `vectors.db`
3. Scan the folder recursively for supported images.
4. For each new or changed image:
   - generate/load thumbnail
   - send image to LM Studio vision model for strict JSON captioning
   - extract `caption` and `has_human`
   - send `caption` to LM Studio embedding model
   - store metadata + caption + vector locally
   - if `has_human == true`, run local face pipeline and store face embeddings
5. Provide search over that library.

---

## 7. Library Model

## 7.1 Library definition
A library is a single user-selected root folder.

### Rules
- Each library is independent.
- Each library has its own DB files.
- Nested libraries are **not allowed** in MVP.
  - If a user adds `C:\Photos`, they cannot also add `C:\Photos\Trips`.
- A library root must be writable, because DBs are stored inside it.
- If the folder already contains a valid app internal folder and DBs, the app should attach to the existing library instead of creating a new one.

## 7.2 Internal storage layout

Inside each selected library root, create:

```text
<LibraryRoot>\
  .imagecaptionsearch\
    catalog.db
    vectors.db
    thumbnails\
```

### Requirements
- Set the `.imagecaptionsearch` folder to hidden on Windows.
- Exclude this folder from scans.
- Do not ever index app-generated DB or thumbnail files.

---

## 8. Supported Files

## 8.1 Supported image types
MVP supported extensions:

- `.jpg`
- `.jpeg`
- `.png`
- `.webp`
- `.bmp`
- `.gif` (first frame only)
- `.tif`
- `.tiff`

## 8.2 Not required in MVP
- RAW formats
- SVG
- HEIC/HEIF unless support is stable and trivial

## 8.3 File handling rules
- Scan recursively.
- Ignore hidden/system files by default.
- Do not follow reparse points/symlink loops by default.
- Normalize paths consistently.
- Store image paths as **root-relative paths** in the DB.
- Use case-insensitive path comparisons on Windows.

---

## 9. Core Functional Requirements

## 9.1 Library management
The app must allow the user to:

- add a folder as a library
- view all registered libraries
- open a library
- remove a library from the app
- optionally delete that library’s internal app data after explicit confirmation
- relink a moved library if the folder path changes
- rescan a library
- retry failed items
- rebuild captions/embeddings for selected items or whole library

### App-level library registry
Maintain an app-level registry under `%LocalAppData%\ImageCaptionSearch\` to track:
- known library paths
- display name
- last opened library
- window state/preferences
- global settings

Per-library indexed data must still live inside the library folder itself.

## 9.2 Indexing
For each library:

- enumerate supported files
- compare against DB records
- mark new/changed files for processing
- mark missing files as missing
- keep missing items hidden from search by default
- allow purge cleanup later

### Change detection
Use at minimum:
- relative path
- file size
- last modified UTC

Optional:
- content hash for stronger identity tracking

Recommended behavior:
- use size + modified time for dirty detection
- compute a content hash only when useful, not as a requirement for every scan

## 9.3 Caption generation
For each image:

- load image safely
- correct orientation
- resize for API submission if needed
- send to LM Studio vision endpoint
- require strict JSON with exactly:
  - `caption`
  - `has_human`

Store:
- normalized caption string
- raw JSON response
- model id/name used
- prompt version used

## 9.4 Embedding generation
For each completed caption:

- send only the caption text to the LM Studio embedding endpoint
- store the returned vector in the local vector DB
- store dimension and model metadata
- precompute and store vector norm or store normalized vectors for fast cosine similarity

## 9.5 Search
Two search modes are required:

### A. Caption search
- Search the caption database directly.
- Use SQLite FTS.
- Case-insensitive.
- Support normal plain-text input.
- Quoted phrase behavior is a nice-to-have but not required if it complicates MVP.

### B. Semantic search
- Convert the user query into an embedding using the library’s configured embedding model.
- Compute cosine similarity against the library’s stored image embeddings.
- Return top matches sorted by similarity descending.

### Important
- Semantic search only works when the query embedding model matches the library embedding model space.
- If the embedding model changes, the library must be re-embedded before semantic search is considered valid.

## 9.6 Face recognition
Face processing is gated.

- Only run the face pipeline if the caption JSON has `has_human = true`.
- Use `Microsoft.ML.OnnxRuntime`.
- Use MobileFaceNet for face embeddings.
- Because MobileFaceNet is an embedding model, also use a lightweight ONNX face detector/alignment stage.
- Store one or more face embeddings per image.

MVP face feature:
- image detail view should show face count if available
- allow “Find similar faces” from an image that has detected faces

Not required in MVP:
- person naming
- identity training
- manual face tagging

## 9.7 Indexing controls
The user must be able to:

- start indexing
- pause indexing
- resume indexing
- cancel current indexing
- rescan library
- retry failed items

Only one library needs active indexing at a time in MVP.

---

## 10. Data Pipeline Specification

## 10.1 Pipeline stages
For each image:

1. Discover file
2. Validate/load image
3. Generate thumbnail
4. Caption via LM Studio vision model
5. Parse strict JSON
6. Embed caption text
7. Persist caption + embedding
8. If `has_human = true`, run face detection + face embeddings
9. Mark item complete

## 10.2 Queueing and concurrency
Use **TPL Dataflow**.

Requirements:
- asynchronous processing pipeline
- bounded capacity
- cancellation support
- pause/resume support
- user-adjustable concurrency
- default concurrency: **2**
- recommended range shown in UI: **2–4**
- allowed configurable range: **1–8**

Important:
- enforce a **shared LM Studio concurrency limit**
- do not flood the local GPU queue
- interactive semantic search requests should have higher priority than background indexing requests

Recommended implementation shape:
- one or more `TransformBlock`/`ActionBlock` stages
- `EnsureOrdered = false` for throughput
- bounded capacities to avoid memory growth
- background jobs must not block the UI thread

## 10.3 Retry behavior
For transient failures:
- retry a small number of times with backoff

Examples of retryable failures:
- LM Studio timeout
- local HTTP connection failure
- malformed transient model output

Examples of non-retryable failures:
- unsupported/corrupt image
- permanently missing file

On final failure:
- mark item failed
- store error text
- allow manual retry later

---

## 11. LM Studio Integration

## 11.1 API style
Use LM Studio’s **OpenAI-compatible local REST API** where available.

Expected endpoints:
- `GET /v1/models`
- `POST /v1/chat/completions`
- `POST /v1/embeddings`

The implementation must abstract LM Studio behind a service interface so it can be mocked in tests.

## 11.2 Connection settings
Global settings must include:
- base URL, default `http://127.0.0.1:1234`
- vision model id
- embedding model id
- caption timeout
- embedding timeout
- max LM Studio concurrency
- retry count

The app should provide:
- “Test Connection”
- “Refresh Models”
- manual model id entry if model listing is incomplete/unavailable

## 11.3 Caption prompt contract
The model must be instructed to output only strict JSON.

### Required JSON shape
```json
{
  "caption": "A man walking a dog in a park",
  "has_human": true
}
```

### Strict schema
```json
{
  "type": "object",
  "additionalProperties": false,
  "required": ["caption", "has_human"],
  "properties": {
    "caption": { "type": "string" },
    "has_human": { "type": "boolean" }
  }
}
```

### Prompt rules
The prompt must enforce:
- response must be JSON only
- no markdown
- no code fences
- no commentary
- no extra keys
- caption must be English
- caption must be concise, literal, and searchable
- avoid speculation
- `has_human` is true only if a real human or visible human body part is present

Recommended caption style:
- 8–30 words
- concrete nouns/actions/settings
- no “this image shows”
- no artistic analysis unless directly useful for search

Use the lowest reasonable temperature supported by the model.

## 11.4 JSON parsing
Use strict `System.Text.Json` parsing.

Requirements:
- reject extra properties
- reject wrong types
- reject empty caption
- trim whitespace
- store raw JSON for traceability
- if parsing fails, retry the request; do not silently guess

---

## 12. Face Pipeline Specification

## 12.1 Gating
Run face detection/embedding only if:
- captioning completed successfully
- `has_human = true`

## 12.2 Pipeline
1. Detect faces
2. Align/crop faces
3. Run MobileFaceNet embedding
4. Normalize embedding
5. Store embedding and bounding-box metadata

## 12.3 Storage
Store:
- face id
- image id
- face index within image
- bounding box
- detector model metadata
- recognizer model metadata
- embedding vector

## 12.4 Search behavior
“Find similar faces” should:
- use all face embeddings in the selected image as query seeds
- compare against other stored face embeddings in that library
- return best matching images ordered by highest face similarity

A conservative threshold may be configurable later, but MVP may simply show top-ranked matches.

## 12.5 Model distribution
Preferred:
- bundle required ONNX face models with the app if licensing permits

Fallback:
- allow user-configured model paths in Settings
- disable face recognition gracefully if models are missing

The app must not depend on Python or external runtime scripts.

---

## 13. Storage Design

## 13.1 Database choice
Use **SQLite** for local embedded storage.

There will be **two SQLite DB files per library**:

1. `catalog.db`
   - image metadata
   - captions
   - processing state
   - FTS index
   - face metadata

2. `vectors.db`
   - image embeddings
   - face embeddings

This satisfies the “both DBs stored at the folder” requirement.

## 13.2 SQLite requirements
- enable WAL mode where appropriate
- foreign keys on
- use explicit schema migrations
- maintain schema versioning
- create DBs automatically when a library is added

Recommended:
- use a lightweight data access approach with explicit SQL
- avoid unnecessary ORM complexity around FTS/vector blobs

## 13.3 Atomic updates
When writing caption + embedding data across both DBs:
- prefer coordinated transactions
- using `ATTACH DATABASE` for cross-DB transactional updates is acceptable and recommended

If a partial failure still occurs:
- mark the image state as partial/failed
- queue repair on retry

---

## 14. Database Schema

## 14.1 `catalog.db`

### `schema_info`
- `key` TEXT PRIMARY KEY
- `value` TEXT NOT NULL

### `library_settings`
Single-row or key-value metadata storing:
- library id
- root path
- display name
- vision model id
- embedding model id
- caption prompt version
- created UTC
- updated UTC

### `images`
- `id` TEXT PRIMARY KEY
- `relative_path` TEXT NOT NULL UNIQUE
- `file_name` TEXT NOT NULL
- `extension` TEXT NOT NULL
- `size_bytes` INTEGER NOT NULL
- `modified_utc` TEXT NOT NULL
- `created_utc` TEXT NULL
- `width` INTEGER NULL
- `height` INTEGER NULL
- `content_hash` TEXT NULL
- `status` TEXT NOT NULL
- `last_error` TEXT NULL
- `is_missing` INTEGER NOT NULL DEFAULT 0
- `discovered_utc` TEXT NOT NULL
- `last_processed_utc` TEXT NULL
- `thumbnail_rel_path` TEXT NULL

### `captions`
- `image_id` TEXT PRIMARY KEY
- `caption` TEXT NOT NULL
- `raw_json` TEXT NOT NULL
- `has_human` INTEGER NOT NULL
- `vision_model` TEXT NOT NULL
- `prompt_version` TEXT NOT NULL
- `captioned_utc` TEXT NOT NULL

### `faces`
- `id` TEXT PRIMARY KEY
- `image_id` TEXT NOT NULL
- `face_index` INTEGER NOT NULL
- `bbox_x` REAL NOT NULL
- `bbox_y` REAL NOT NULL
- `bbox_width` REAL NOT NULL
- `bbox_height` REAL NOT NULL
- `detector_model` TEXT NOT NULL
- `recognizer_model` TEXT NOT NULL
- `created_utc` TEXT NOT NULL

### `processing_jobs`
- `image_id` TEXT PRIMARY KEY
- `retry_count` INTEGER NOT NULL
- `pipeline_state` TEXT NOT NULL
- `updated_utc` TEXT NOT NULL

### `captions_fts`
Use SQLite FTS virtual table over `captions.caption`.

Search should operate over completed, non-missing items only.

## 14.2 `vectors.db`

### `image_embeddings`
- `image_id` TEXT PRIMARY KEY
- `model_name` TEXT NOT NULL
- `dimension` INTEGER NOT NULL
- `vector_blob` BLOB NOT NULL
- `vector_norm` REAL NOT NULL
- `embedded_utc` TEXT NOT NULL

### `face_embeddings`
- `face_id` TEXT PRIMARY KEY
- `model_name` TEXT NOT NULL
- `dimension` INTEGER NOT NULL
- `vector_blob` BLOB NOT NULL
- `vector_norm` REAL NOT NULL
- `embedded_utc` TEXT NOT NULL

## 14.3 Vector format
Store embeddings as:
- float32 vectors in BLOB form
- known dimension
- pre-normalized and/or with stored norm

This allows fast cosine similarity.

---

## 15. Search Engine Specification

## 15.1 Caption search
- Query local FTS index
- Return matching images with relevance order
- Default result limit: 100
- Support pagination or incremental loading in UI

## 15.2 Semantic search
- Embed query text with current library embedding model
- Compare against all image embeddings for that library
- Rank by cosine similarity descending
- Default result limit: 100

## 15.3 Retrieval implementation
For MVP:
- load vectors into an in-memory cache per opened library
- compute exact cosine similarity locally
- use SIMD-friendly math where practical
- use partial top-k selection instead of full sorting for large libraries if convenient

This is acceptable for MVP and keeps the solution fully local and embedded.

## 15.4 Model consistency rule
If a library’s embedding model changes:
- existing embeddings are no longer comparable to new query embeddings
- the app must require re-embedding that library

## 15.5 Search UX rules
- blank query should show all indexed items or recent items
- semantic search should execute on explicit submit, not every keystroke
- caption search may be live or submit-based
- if LM Studio is unavailable:
  - caption search must still work for indexed items
  - semantic search must show a clear unavailable state

---

## 16. UI/UX Specification

## 16.1 General
The app must feel:
- native
- modern
- clean
- calm
- friendly
- light mode first

Use Avalonia Fluent theme in **light mode**.

### UI principles
- simple hierarchy
- generous spacing
- rounded controls/cards
- subtle accents
- no clutter
- no developer-tool appearance
- no emoji usage
- no dark mode requirement for MVP

## 16.2 Main screens

### A. Library Home
Shows:
- list/grid of libraries
- library name
- root path
- indexed item count
- pending count
- failed count
- last scan time
- open / rescan / remove actions

### B. Library Detail / Search
Contains:
- top search bar
- search mode selector:
  - Caption
  - Semantic
- optional filters:
  - All
  - Human
  - No human
- rescan button
- start/pause/resume indexing controls
- status/progress summary
- main result grid

### C. Result Grid
Each card shows:
- thumbnail
- file name
- caption snippet
- optional status indicator
- optional human badge/text
- click to open detail

The result grid must support incremental loading or virtualization to avoid UI slowdown.

### D. Image Detail View
Shows:
- larger preview
- full caption
- full file path
- metadata
- processing status
- human flag
- face count if available
- actions:
  - Open image
  - Show in Explorer
  - Copy caption
  - Re-caption
  - Re-embed
  - Find similar images
  - Find similar faces (if faces exist)

### E. Settings
Sections:
- LM Studio
- Indexing
- Face Recognition
- General
- Advanced

## 16.3 Light mode visual direction
- white/off-white background
- subtle card surfaces
- accessible contrast
- readable typography
- modern spacing system
- soft dividers
- accent color used sparingly

## 16.4 Empty states
Provide clear empty states:
- no libraries yet
- LM Studio not connected
- no results
- indexing in progress
- face models not configured

Use plain, helpful text. No emojis.

## 16.5 Accessibility
Minimum requirements:
- keyboard navigable
- visible focus states
- readable font sizes
- sufficient contrast
- no tiny hit targets

---

## 17. Project Structure

Recommended solution structure:

```text
/src
  /ImageCaptionSearch.UI
  /ImageCaptionSearch.Core
/tests
  /ImageCaptionSearch.Core.Tests
  /ImageCaptionSearch.UI.Tests
/.github
  /workflows
```

## 17.1 `ImageCaptionSearch.UI`
Contains:
- Avalonia app bootstrap
- views
- view models
- DI composition root
- UI resources/styles
- app settings shell integration

## 17.2 `ImageCaptionSearch.Core`
Contains:
- domain models
- repositories
- SQLite access
- migrations
- LM Studio client
- indexing/scanning services
- TPL Dataflow pipeline
- search services
- face services
- thumbnail services
- settings models
- logging abstractions

Must not reference Avalonia.

## 17.3 Tests
- unit tests for core logic
- integration tests for DB + fake LM Studio server
- optional headless UI tests for view model flows

---

## 18. Core Service Boundaries

The core library should expose interfaces/services similar to:

- `ILibraryService`
- `ILibraryRegistryService`
- `IScanService`
- `IIndexingPipelineService`
- `ILmStudioClient`
- `ICaptionService`
- `IEmbeddingService`
- `ISearchService`
- `IFaceRecognitionService`
- `IThumbnailService`
- `ISettingsService`
- `ILogService` or `ILogger` abstractions

The exact names may vary, but the separation must remain.

---

## 19. Non-Functional Requirements

## 19.1 Performance
Targets for MVP:
- UI remains responsive during indexing
- opening a library should not freeze the UI
- vector cache may warm in background
- search results should appear quickly after query embedding returns
- thumbnail generation should be cached

## 19.2 Reliability
- app must survive LM Studio outages gracefully
- app must survive corrupt images gracefully
- incomplete jobs should resume or return to queued state on restart
- no user data loss in source images
- app never modifies original image files

## 19.3 Privacy
- all AI processing is local
- no cloud API dependency
- no telemetry by default
- only local HTTP calls to LM Studio

## 19.4 Logging
Store app logs under `%LocalAppData%\ImageCaptionSearch\logs`.

Log:
- startup/shutdown
- library add/remove
- scan summaries
- indexing failures
- LM Studio request failures
- DB migration issues

Do not log entire image contents. Logging captions is acceptable in debug logs if needed, but be conservative.

---

## 20. Settings Specification

## 20.1 Global settings
- LM Studio base URL
- max LM Studio concurrency
- caption timeout
- embedding timeout
- retry count
- default result limit
- app window state
- last opened library

## 20.2 Per-library settings
- vision model id
- embedding model id
- caption prompt version
- thumbnail size
- scan filters
- face recognition enabled/disabled
- detector model path
- recognizer model path

## 20.3 Settings behavior
- changing the embedding model should require re-embedding
- changing caption prompt version or vision model should offer re-caption/re-index
- invalid model paths should disable face recognition with a clear warning

---

## 21. Error Handling and Edge Cases

The app must handle these cases gracefully:

1. LM Studio not running
2. LM Studio reachable but model missing
3. caption response invalid JSON
4. embedding response invalid shape
5. file deleted after discovery
6. corrupt image
7. read-only library folder
8. removable drive disconnected
9. DB schema migration needed
10. partial indexing interrupted by app shutdown

Expected behavior:
- clear user-facing status
- actionable retry options
- no crash
- preserve indexed data where possible

---

## 22. GitHub Actions CI/CD Specification

This project must be buildable and verifiable through GitHub Actions.

## 22.1 Required workflows

### A. `ci.yml`
On push and pull request:
- checkout
- setup .NET SDK
- restore
- build
- run tests
- run format/analyzer checks
- run policy checks
- publish Windows artifact as a smoke build

### B. `release.yml`
On tag or manual dispatch:
- restore/build/test
- publish Windows release artifact
- package app as zip
- upload artifact
- optionally create GitHub Release

### C. `policy.yml` or equivalent
Fail the build if:
- WPF references are present
- WindowsDesktop SDK / `UseWPF` is present
- forbidden web files are introduced (`.html`, `.css`, `.js`, `.tsx`, `.jsx`) unless explicitly justified outside app code
- `WebView`, `Electron`, `Tauri`, or similar forbidden tech appears
- core library references Avalonia packages

## 22.2 CI quality gates
Required:
- build success
- test success
- policy checks success
- no formatting violations
- no analyzer warnings if configured as errors

## 22.3 Packaging
Release artifact should be:
- Windows `win-x64`
- self-contained preferred
- packaged as a zip for MVP

Avoid packaging choices that complicate content assets or native model files unless justified.

## 22.4 Dependency management
- prefer central package management
- keep versions centralized
- use the latest stable compatible versions
- if a newest package is incompatible, use the newest compatible version and document why

---

## 23. Testing Requirements

## 23.1 Unit tests
Must cover:
- path normalization
- dirty file detection
- JSON parsing/validation
- cosine similarity math
- search ranking
- queue state transitions
- library overlap validation
- model mismatch behavior

## 23.2 Integration tests
Must cover:
- SQLite schema creation/migration
- caption ingestion path with fake LM Studio server
- embedding persistence and retrieval
- caption search
- semantic search
- restart/resume behavior for queued items

## 23.3 Face pipeline tests
At minimum:
- gate is only triggered when `has_human = true`
- face records persist correctly
- similar-face search logic works on synthetic/fake embeddings

## 23.4 UI/view model tests
At minimum:
- library add/remove flow
- settings validation
- indexing status state changes
- search mode switching

---

## 24. Acceptance Criteria

The MVP is complete when all of the following are true.

### 24.1 Library and storage
- User can add multiple non-overlapping folders as libraries.
- Each library creates/uses its own `.imagecaptionsearch` folder.
- `catalog.db` and `vectors.db` exist inside the library folder.
- App can reopen an existing indexed library.

### 24.2 Indexing
- App scans supported image files recursively.
- New/changed files are queued.
- Missing files are marked missing.
- Indexing is asynchronous and UI remains responsive.
- User can pause/resume/cancel indexing.

### 24.3 Captioning and embeddings
- Each processed image produces a strict JSON caption result.
- Caption JSON is parsed deterministically.
- Caption and `has_human` are stored.
- Caption embeddings are generated and stored.
- Invalid JSON causes retries and then a visible failed state.

### 24.4 Search
- Caption search returns text matches from local DB.
- Semantic search returns vector matches using cosine similarity.
- Search results show thumbnails and captions.
- Semantic search works only when LM Studio embedding endpoint is available.
- Caption search still works when LM Studio is offline.

### 24.5 Face pipeline
- Face pipeline only runs when `has_human = true`.
- Face embeddings are stored locally.
- User can trigger “Find similar faces” from an image with detected faces.

### 24.6 UI
- UI is Avalonia-based.
- UI is light mode and modern/friendly.
- No web technologies are used.
- No emojis appear anywhere.

### 24.7 Architecture
- Core logic is isolated in a class library.
- Core library has no Avalonia dependency.
- UI does not contain direct DB or AI logic.

### 24.8 CI/CD
- GitHub Actions builds, tests, and packages the app.
- Policy checks prevent WPF/web-tech drift.
- A Windows artifact is published by CI.

---

## 25. Implementation Priorities

Recommended build order:

### Phase 1
- solution skeleton
- core/UI separation
- library registry
- folder add/remove
- internal folder creation
- DB creation + migrations

### Phase 2
- folder scan
- image discovery
- thumbnail generation
- status tracking

### Phase 3
- LM Studio client
- caption prompt contract
- strict JSON parsing
- embedding generation
- DB persistence

### Phase 4
- caption search
- semantic search
- result grid + detail view

### Phase 5
- TPL Dataflow queue
- pause/resume/cancel
- retries and resilience

### Phase 6
- ONNX face pipeline
- similar-face search

### Phase 7
- CI/CD hardening
- policy checks
- packaging
- final polish

---

## 26. Important Implementation Notes

1. Do not allow the core library to depend on Avalonia types such as `Bitmap`, UI dispatchers, or visual controls.
2. Do not let semantic query embedding requests get starved behind long background indexing.
3. Store relative paths, not only absolute paths.
4. Exclude the internal app folder from scans.
5. Prevent nested library roots.
6. If LM Studio supports structured response schema, use it.
7. If not, still force strict JSON and reject invalid output.
8. Face detection is separate from face embedding; MobileFaceNet alone is not enough.
9. Keep the MVP local, deterministic, and simple.
10. Prefer correctness and maintainability over premature complexity.

---

## 27. Explicit Forbidden Choices

The coding agent must not do any of the following:

- replace Avalonia with WPF
- introduce HTML/CSS/JS UI
- use Electron/Tauri/WebView
- put DB logic inside views or code-behind
- put LM Studio HTTP logic inside the UI project
- use loose AI response parsing as the main approach
- store library DBs outside the library folder for primary indexed data
- skip CI policy enforcement
- use emojis anywhere

---

## 28. Future Extensions After MVP

Not for initial implementation, but architecture should leave room for:

- cross-library federated search
- saved searches
- person naming and identity grouping
- OCR
- duplicate detection via hash/perceptual hash
- auto file watching
- dark mode
- additional metadata filters
- ARM64 packaging
- approximate nearest neighbor indexing for very large libraries

---

## 29. Final Definition of Done

The project is done when a Windows user can:

1. launch the Avalonia desktop app
2. add a local image folder
3. let it build local DBs inside that folder
4. caption/index images via LM Studio
5. search those images by caption text
6. search those images semantically by natural-language query
7. see a clean light-mode UI
8. optionally find similar faces for human-containing images
9. receive all build/test/package validation through GitHub Actions
10. inspect the codebase and see a strict UI/core separation with no WPF or web tech