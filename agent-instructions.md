# agent-instructions.md

# Instructions for the AI Coding Agent

You are implementing the `ImageCaptionSearch` application.

Read these files in this order before making changes:

1. `spec.md`
2. `task-breakdown.md`
3. `agent-instructions.md`

If any conflict exists:
- `spec.md` is the source of truth
- `task-breakdown.md` is the implementation plan
- `agent-instructions.md` defines execution rules and guardrails

---

## 1. Mission

Build a **native Windows desktop app** using **C# + Avalonia** that:

- indexes local image folders
- captions images through **LM Studio**
- generates and stores text embeddings
- supports caption search and semantic search
- optionally runs a gated local face recognition pipeline
- stores each library’s DBs inside that library folder
- is developed and validated through **GitHub Actions**
- has a **clean, modern, light-mode, friendly UI**

This project must remain:
- local-first
- deterministic
- maintainable
- strictly separated between UI and Core

---

## 2. Non-Negotiable Rules

These rules are mandatory.

### 2.1 Platform and UI rules
- Use **Avalonia** only for the desktop UI.
- Use **MVVM Community Toolkit** for MVVM patterns.
- Do not use WPF.
- Do not use WinForms.
- Do not use HTML, CSS, JavaScript, React, Vue, Svelte, or any web stack.
- Do not use WebView, Electron, Tauri, Blazor Hybrid, or browser wrappers.
- Target **Windows** for MVP.

### 2.2 Architecture rules
- All non-UI logic must live in `ImageCaptionSearch.Core`.
- `ImageCaptionSearch.Core` must be a plain C# class library.
- `ImageCaptionSearch.Core` must not reference Avalonia.
- The UI project must not contain:
  - direct SQLite logic
  - direct LM Studio HTTP logic
  - business rules
  - vector math
  - face pipeline logic
- Views should be thin.
- ViewModels should orchestrate services, not implement storage or AI logic.
- Code-behind should be minimal and UI-only.

### 2.3 Data/storage rules
- Each user-selected library folder must contain:
  - `.imagecaptionsearch\catalog.db`
  - `.imagecaptionsearch\vectors.db`
  - `.imagecaptionsearch\thumbnails\`
- Do not store primary per-library indexed data elsewhere.
- Exclude `.imagecaptionsearch` from scanning.
- Store indexed file paths as **relative to library root**.

### 2.4 AI integration rules
- LM Studio captions must be requested with a **strict JSON-only contract**.
- Required JSON shape:
  - `caption: string`
  - `has_human: boolean`
- Parsing must be strict with `System.Text.Json`.
- Reject extra keys, missing keys, invalid types, and empty captions.
- Do not use fragile regex parsing as the normal path.
- Use LM Studio’s OpenAI-compatible API where possible.
- Semantic search must use embeddings from LM Studio.

### 2.5 Pipeline rules
- Use **TPL Dataflow** for indexing pipeline orchestration.
- Limit LM Studio concurrency to avoid GPU queue overload.
- Default concurrency should be 2.
- User-adjustable concurrency must exist.
- Semantic search requests must not be starved behind background indexing.
- Face recognition must run only when caption JSON says `has_human = true`.

### 2.6 Style rules
- No emojis anywhere.
- Keep the UI light-mode and calm.
- Use clear naming.
- Prefer explicitness over cleverness.
- Do not add speculative features not required by the spec.

---

## 3. Required Working Style

### 3.1 Build in small vertical slices
Do not try to build the entire system at once.

Preferred sequence:
1. project structure and guardrails
2. DB creation and library registration
3. scanning and thumbnails
4. LM Studio captioning and parsing
5. embeddings and persistence
6. search
7. indexing pipeline
8. face pipeline
9. polish and CI/release

Each slice should produce a working, testable increment.

### 3.2 Do not leave broad incomplete scaffolding
Avoid:
- empty service classes with no behavior
- fake architecture with no integration path
- giant TODO-only layers
- unconnected screens with placeholder buttons

Temporary stubs are acceptable only when:
- clearly marked
- isolated
- not blocking later replacement
- not pretending to be complete

### 3.3 Preserve repo health
At every step:
- build must pass
- tests must pass
- architecture boundaries must remain intact
- CI should be expected to pass

Do not knowingly land a broken intermediate state.

---

## 4. Project Structure Requirements

Use or preserve this structure:

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

### `ImageCaptionSearch.UI`
Allowed responsibilities:
- Avalonia app bootstrap
- views
- view models
- styling/resources
- DI composition root
- UI interaction with services from Core

### `ImageCaptionSearch.Core`
Allowed responsibilities:
- domain models
- repositories
- SQLite access
- migrations
- LM Studio client
- indexing/scanning logic
- queueing/pipeline logic
- embeddings/vector logic
- face pipeline
- search logic
- settings models/services

### Tests
Must cover:
- pure logic
- DB behavior
- pipeline behavior
- fake LM Studio integration
- key ViewModel flows

---

## 5. Mandatory Technical Choices

Use these choices unless there is a strong incompatibility.

### 5.1 Language/runtime
- C#
- latest stable compatible .NET SDK

### 5.2 UI
- Avalonia
- MVVM Community Toolkit

### 5.3 Storage
- SQLite for local DBs
- FTS for caption search

### 5.4 AI integration
- LM Studio local REST API
- OpenAI-compatible endpoints if available

### 5.5 Face pipeline
- `Microsoft.ML.OnnxRuntime`
- face detector + alignment stage
- MobileFaceNet embeddings

### 5.6 Queueing
- `System.Threading.Tasks.Dataflow`

If a package/version issue exists:
- use the newest compatible stable version
- document the reason

---

## 6. Core Design Principles

### 6.1 Keep the Core UI-agnostic
The Core project must not know:
- Avalonia controls
- Avalonia bitmaps
- UI thread dispatchers
- windows/dialogs
- message boxes

If a feature requires image decoding or thumbnails:
- use non-Avalonia-compatible abstractions in Core
- convert to UI-specific objects only in the UI layer if needed

### 6.2 Prefer explicit interfaces
Services should be interface-driven and testable.

Typical service boundaries:
- library registry
- library management
- scan service
- indexing pipeline
- LM Studio client
- caption service
- embedding service
- search service
- face recognition service
- thumbnail service
- settings service

### 6.3 Use deterministic persistence
DB writes must be deliberate and traceable:
- schema migrations
- foreign keys
- version tracking
- clear repository methods
- transactional or coordinated writes when updating both DBs

### 6.4 Keep source images untouched
Never modify original user image files.

---

## 7. Feature Implementation Rules

### 7.1 Library management
Must support:
- multiple libraries
- one root folder per library
- no nested library roots
- reopening existing library data
- app-level registry in LocalAppData
- per-library DBs inside library root

Must reject:
- duplicate root
- nested root
- inaccessible root
- unwritable root

### 7.2 Scanning
Must:
- scan recursively
- support extensions listed in `spec.md`
- exclude `.imagecaptionsearch`
- mark missing files
- detect changed files using size and modified time at minimum
- store root-relative paths

### 7.3 Captioning
Caption requests must:
- force JSON-only output
- use a versioned prompt
- avoid speculation
- produce concise searchable captions
- include `has_human`

Store:
- parsed caption
- raw JSON
- model used
- prompt version
- timestamps

### 7.4 Embeddings
Embedding requests must:
- use caption text only
- validate returned vectors
- store dimension and model metadata
- support cosine similarity efficiently

### 7.5 Search
Implement both:
- caption search using local FTS
- semantic search using embeddings and cosine similarity

Important:
- caption search must work when LM Studio is offline
- semantic search requires LM Studio query embeddings
- semantic search must respect embedding-model consistency

### 7.6 Face pipeline
Only run when:
- captioning succeeded
- `has_human == true`

Store:
- face metadata
- face embeddings
- model metadata

Do not:
- implement person naming in MVP
- add identity training workflows
- require Python or external scripts

---

## 8. UI/UX Rules

### 8.1 Visual direction
UI must feel:
- native
- light
- modern
- calm
- uncluttered
- friendly

Use:
- light mode
- readable spacing
- clean cards/lists
- visible focus states
- sensible typography

### 8.2 UX priorities
The user should be able to:
1. add a library
2. index images
3. search images
4. inspect results
5. manage indexing state
6. configure LM Studio

### 8.3 Avoid common UI mistakes
Do not:
- cram too much on one screen
- expose raw DB concepts in UI
- show stack traces in normal user flows
- create debug-looking UI
- hide critical actions in obscure menus
- make the app feel like a web app in a shell

### 8.4 Accessibility minimums
Ensure:
- keyboard navigation
- visible focus
- readable text
- adequate contrast
- no tiny click targets

---

## 9. Testing Requirements

### 9.1 Write tests as you go
Do not postpone all tests until the end.

### 9.2 Minimum required test areas
Add tests for:
- path normalization
- nested library rejection
- scan filtering
- dirty detection
- JSON parsing strictness
- cosine similarity
- search ranking
- LM Studio client request/response handling
- DB schema and migrations
- pause/resume/cancel state transitions
- face gating logic

### 9.3 Integration testing approach
Use:
- temporary directories
- temporary SQLite files
- fake HTTP handlers or fake LM Studio test server

Avoid:
- dependence on a real external LM Studio instance in CI

### 9.4 UI tests
Keep UI tests focused on:
- ViewModel behavior
- state transitions
- validation
- command flows

Do not overinvest in fragile visual UI automation unless necessary.

---

## 10. CI/CD Rules

### 10.1 GitHub Actions is mandatory
The repository must include workflows for:
- CI on push and pull request
- release packaging on tag or manual dispatch
- policy enforcement

### 10.2 CI must validate
- restore
- build
- test
- formatting/analyzers
- policy checks
- publish smoke artifact

### 10.3 Policy checks must fail if forbidden patterns appear
Examples:
- `UseWPF`
- WindowsDesktop SDK
- Avalonia reference in Core
- `.html`, `.css`, `.js`, `.tsx`, `.jsx` added to app code
- WebView/Electron/Tauri references

### 10.4 Packaging
Publish:
- Windows `win-x64`
- zipped artifact
- self-contained if practical

---

## 11. How to Make Decisions

When a detail is unspecified, decide using this order:

1. preserve strict separation of concerns
2. preserve local-first behavior
3. preserve deterministic behavior
4. favor maintainability over cleverness
5. favor explicit code over abstraction-heavy code
6. avoid unnecessary dependencies
7. keep the MVP focused

When in doubt, choose the simpler implementation that still fully satisfies the spec.

---

## 12. Anti-Patterns to Avoid

Do not do any of the following:

- put business logic in Avalonia code-behind
- reference Avalonia from Core
- put HTTP client logic in ViewModels
- put SQL statements in Views or ViewModels
- use regex as primary JSON extraction
- silently ignore malformed model output
- hardwire everything to one global folder when per-library storage is required
- index the app’s own `.imagecaptionsearch` folder
- block the UI thread with long-running work
- let background indexing consume all LM Studio capacity
- add broad generic repository abstractions that hide simple needed SQL
- introduce speculative architecture for future cloud features
- build a plugin system
- build a browser UI
- use emojis

---

## 13. Error Handling Expectations

Handle failures gracefully and explicitly.

### Must handle
- LM Studio offline
- LM Studio model missing
- malformed caption JSON
- invalid embedding response
- corrupt image
- deleted image after discovery
- read-only library root
- interrupted indexing
- DB migration errors

### Required behavior
- no crash if avoidable
- clear user-visible status
- retry transient failures
- mark permanent failures
- preserve already indexed data
- allow retry later

---

## 14. Logging Rules

Write logs under:
`%LocalAppData%\ImageCaptionSearch\logs`

Log:
- app startup/shutdown
- library add/remove
- scan summaries
- indexing failures
- LM Studio connection/request failures
- migration issues

Do not:
- log raw image binary data
- spam logs with unnecessary noise
- expose sensitive local file details more than needed

---

## 15. Definition of Complete Work

A task is complete only if:
- code builds
- tests pass
- architecture boundaries are respected
- no forbidden tech is introduced
- user-facing behavior for the task actually works
- related documentation/config is updated if needed

A milestone is complete only if:
- all dependent tasks are finished
- the app remains usable
- CI would pass

The project is complete only when it satisfies all acceptance criteria in `spec.md`.

---

## 16. If You Need to Deviate

If a required implementation detail cannot be followed exactly:
1. choose the smallest safe deviation
2. document it clearly in code comments and/or repo docs
3. keep the public behavior aligned with the spec as much as possible
4. do not silently substitute a forbidden technology or architecture

Examples:
- If a package version is incompatible, use the newest compatible version and note why.
- If structured schema output from LM Studio is unavailable, still enforce JSON-only output via prompt and strict parser.

---

## 17. Suggested Execution Loop

For each task:
1. read the relevant spec section
2. implement the smallest complete slice
3. add or update tests
4. run build/tests mentally or through code if available
5. verify architecture boundaries
6. move to the next slice

Prefer finishing one useful slice over touching many files without completing a workflow.

---

## 18. Final Reminder

This is not a web app.

This is not a WPF app.

This is a native Windows desktop app built with:
- C#
- Avalonia
- MVVM Community Toolkit
- SQLite
- LM Studio
- ONNX Runtime
- TPL Dataflow

Keep the implementation clean, local, strict, testable, and aligned with the spec.