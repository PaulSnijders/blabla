# Tasks: Hotkey Dictation

**Input**: Design documents from `/specs/001-hotkey-dictation/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: Included selectively per the constitution ("testing effort proportional to risk"):
unit tests cover the transcription pipeline, state machine, and stores; UI wiring is
validated via [quickstart.md](quickstart.md) manual scenarios instead of test-first ceremony.

**Organization**: Tasks are grouped by user story (spec.md priorities P1–P4) so each story
is an independently testable increment.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: US1 = dictation loop, US2 = configuration, US3 = presets, US4 = history

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Solution skeleton matching plan.md's project structure

- [X] T001 Create `Blabla.sln`, WPF app project `src/Blabla/Blabla.csproj` (`net10.0-windows`, `<UseWPF>true</UseWPF>`, nullable enabled), xUnit test project `tests/Blabla.Tests/Blabla.Tests.csproj` referencing the app project, and a .NET `.gitignore` at repo root
- [X] T002 Add NuGet packages to `src/Blabla/Blabla.csproj`: NAudio, H.NotifyIcon.Wpf (ProtectedData turned out to be in-framework on net10.0-windows — 2 packages suffice); publish properties live in the publish profile (T035) to keep `dotnet test` friction-free
- [X] T003 [P] Add application/tray icon assets under `src/Blabla/Assets/` (app icon .ico with recording-state variant) and reference them in `src/Blabla/Blabla.csproj`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Models, stores, hotkey plumbing, and the tray-app shell every story builds on

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [X] T004 [P] Create `Hotkey` value object (modifier flags + virtual key; parse/format strings like `"Ctrl+Alt+Space"`) in `src/Blabla/Models/Hotkey.cs`
- [X] T005 [P] Create `Preset` model (id, name, prompt, language?, model, temperature?) per data-model.md in `src/Blabla/Models/Preset.cs`
- [X] T006 [P] Create `TranscriptionEntry` model + `TranscriptionStatus` enum (Succeeded/Failed) per data-model.md in `src/Blabla/Models/TranscriptionEntry.cs`
- [X] T007 Create `AppSettings` model with defaults (hotkeys `Ctrl+Alt+Space` / `Ctrl+Alt+V`, historyLimit 100) and seed presets "Algemeen", "Programmeren", "Wozzol" per data-model.md in `src/Blabla/Models/AppSettings.cs`
- [X] T008 Implement `SettingsStore` in `src/Blabla/Services/SettingsStore.cs`: load/save `%APPDATA%\Blabla\settings.json` per contracts/local-storage.md — atomic write (tmp + replace), corrupt-file rename-and-recreate, DPAPI CurrentUser encrypt/decrypt for the API key, first-run seeding, activePresetId fallback to first preset
- [X] T009 Implement `HistoryStore` in `src/Blabla/Services/HistoryStore.cs`: load/append `%APPDATA%\Blabla\history.json` per contracts/local-storage.md — newest-first, cap at historyLimit with oldest-removal, failed-audio WAV save/delete under `%APPDATA%\Blabla\audio\`, orphaned-WAV cleanup at startup
- [X] T010 [P] Implement `HotkeyManager` in `src/Blabla/Core/HotkeyManager.cs`: `RegisterHotKey`/`UnregisterHotKey` P/Invoke on a hidden `HwndSource` window, per-hotkey callbacks, registration failure surfaced as a result (for FR-009 conflict warnings)
- [X] T011 Implement app shell in `src/Blabla/App.xaml` + `src/Blabla/App.xaml.cs`: single-instance named mutex, no window at startup, H.NotifyIcon tray icon with menu (Open, Quit), instantiate SettingsStore/HistoryStore/HotkeyManager
- [X] T012 [P] Unit tests for SettingsStore (round-trip, DPAPI key encrypt/decrypt, corrupt-file recovery, first-run seeding, dangling activePresetId fallback) in `tests/Blabla.Tests/SettingsStoreTests.cs`
- [X] T013 [P] Unit tests for HistoryStore (bounding at limit, newest-first order, failed-audio lifecycle, orphan cleanup) in `tests/Blabla.Tests/HistoryStoreTests.cs`

**Checkpoint**: Foundation ready — user story implementation can now begin

---

## Phase 3: User Story 1 - Dictate anywhere with a hotkey (Priority: P1) 🎯 MVP

**Goal**: Full core loop — hotkey → recording pop-up with timer → hotkey → OpenAI
transcription → text typed at cursor without touching the clipboard; paste-last hotkey as
recovery; failures kept for retry.

**Independent Test**: quickstart.md scenarios V2 + V3 (with API key hand-edited into
settings.json until US2 exists): dictate into Notepad, verify clipboard untouched, Esc
cancel, silence handling, paste-last hotkey, offline failure kept in history file.

### Implementation for User Story 1

- [X] T014 [P] [US1] Implement `AudioRecorder` in `src/Blabla/Core/AudioRecorder.cs`: NAudio `WaveInEvent` at 16 kHz mono 16-bit into an in-memory WAV, elapsed-time event for the timer, hard auto-stop event at 5:00 (FR-005a), missing/disabled microphone detected and reported as a typed error
- [X] T015 [P] [US1] Implement `TranscriptionClient` in `src/Blabla/Core/TranscriptionClient.cs`: `HttpClient` multipart POST per contracts/openai-transcription.md (file/model/prompt/language/temperature parts, omit-when-null), 60 s timeout, `response_format=json` parsing, empty text → "no speech detected" result, error mapping table (401/429/400/5xx/network) to user-facing messages that never contain the key
- [X] T016 [P] [US1] Implement `TextInserter` in `src/Blabla/Core/TextInserter.cs`: `SendInput` with `KEYEVENTF_UNICODE` down/up pairs (surrogate-pair safe for emoji/accents), never touching the system clipboard (FR-004)
- [X] T017 [P] [US1] Implement `RecordingPopup` in `src/Blabla/UI/RecordingPopup.xaml` + `.xaml.cs`: small always-on-top window with `WS_EX_NOACTIVATE` (never steals focus), pre-created hidden at startup (< 200 ms show), states: recording (pulsing indicator + mm:ss timer), transcribing, error message; Esc triggers cancel callback
- [X] T018 [US1] Implement `DictationController` in `src/Blabla/Core/DictationController.cs`: state machine Idle → Recording → Transcribing → Idle per research.md R10 — hotkey toggles record/stop, Esc cancels and discards (FR-005), record press during Transcribing ignored with pop-up feedback, auto-stop submits like a manual stop, success → insert text + update internal last-transcription buffer + append Succeeded entry (audio discarded), failure → persist WAV + append Failed entry + show error (FR-006), silence → inform user, no insert
- [X] T019 [US1] Wire hotkeys in `src/Blabla/App.xaml.cs`: record hotkey → DictationController, paste-last hotkey → TextInserter with internal buffer (FR-004a); when no API key is configured, record hotkey shows a message pointing to Settings instead of recording
- [X] T020 [P] [US1] Unit tests for TranscriptionClient (multipart request contains/omits correct parts per preset, each error status maps to the contract's message, empty-text handling) with a mock `HttpMessageHandler` in `tests/Blabla.Tests/TranscriptionClientTests.cs`
- [X] T021 [P] [US1] Unit tests for DictationController (toggle semantics, rapid double-press never sticks, cancel discards, block-while-transcribing, auto-stop path, failure persists audio and entry, success updates internal buffer) with faked recorder/client/inserter in `tests/Blabla.Tests/DictationControllerTests.cs`

**Checkpoint**: MVP — dictation works end-to-end with a hand-configured API key

---

## Phase 4: User Story 2 - Configure once, then go (Priority: P2)

**Goal**: Settings UI in the main window: API key, both hotkeys, start-with-Windows, and
the active preset's transcription options; all persisted and applied live.

**Independent Test**: quickstart.md scenario V1 + V3 step 4: fresh `%APPDATA%\Blabla`,
configure key via UI, dictate; verify no plaintext key on disk, settings survive restart,
invalid key gives the friendly 401 message.

### Implementation for User Story 2

- [X] T022 [US2] Build `MainWindow` settings section in `src/Blabla/UI/MainWindow.xaml` + `.xaml.cs`: API key entry (masked, load/save via SettingsStore DPAPI), hotkey capture controls for both hotkeys, start-with-Windows checkbox, and transcription options for the active preset (prompt textbox, language, model, temperature); opened from tray "Open"
- [X] T023 [P] [US2] Implement `StartupRegistration` in `src/Blabla/Services/StartupRegistration.cs`: add/remove `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` value `Blabla` per contracts/local-storage.md, reconciled on every settings save (FR-011)
- [X] T024 [US2] Apply settings live in `src/Blabla/App.xaml.cs` + `MainWindow`: on save, re-register changed hotkeys via HotkeyManager and show a conflict warning when registration fails (FR-009), reject identical hotkeys, reconcile autostart via StartupRegistration
- [X] T025 [US2] Missing/invalid key UX polish: no-key guard message opens Settings directly; 401 error from a dictation surfaces the "check Settings" message per contracts/openai-transcription.md (Story 2 scenarios 3–4)

**Checkpoint**: A fresh user can go install → key → dictate entirely via UI (SC-003)

---

## Phase 5: User Story 3 - Switch context presets from the tray (Priority: P3)

**Goal**: Full preset CRUD in the main window and one-click active-preset switching from
the tray menu; active preset drives every transcription and survives restarts.

**Independent Test**: quickstart.md scenario V4: create "Programmeren" preset, switch via
tray in ≤ 2 clicks, dictate domain terms, restart and verify persistence.

### Implementation for User Story 3

- [X] T026 [US3] Add preset management to `src/Blabla/UI/MainWindow.xaml` + `.xaml.cs`: preset list with create/edit/rename/delete (FR-012), unique non-empty names, deleting the active preset falls back to first, deleting the last preset blocked (FR-014), per-preset editing of prompt/language/model/temperature (generalizes T022's active-preset editor)
- [X] T027 [US3] Add preset submenu to the tray menu in `src/Blabla/App.xaml.cs`: all presets listed with the active one check-marked, click activates immediately and persists via SettingsStore (FR-013), menu rebuilt on preset CRUD without restart (Story 3 scenario 3)
- [X] T028 [P] [US3] Unit tests for preset rules (name uniqueness validation, active fallback on delete, last-preset delete guard, active-preset options reach TranscriptionClient request) in `tests/Blabla.Tests/PresetTests.cs`

**Checkpoint**: Preset switching works end-to-end from the tray

---

## Phase 6: User Story 4 - Review and reuse past transcriptions (Priority: P4)

**Goal**: History list in the main window with copy-to-clipboard and retry-of-failed
(reusing stored audio, no re-recording).

**Independent Test**: quickstart.md scenario V3 steps 2–3 + V5 step 7: dictate several
times, view newest-first list, copy an entry, retry a failed one offline→online, overflow
past the limit trims oldest and deletes orphan audio.

### Implementation for User Story 4

- [X] T029 [US4] Add history list to `src/Blabla/UI/MainWindow.xaml` + `.xaml.cs`: newest-first entries with timestamp, preset name, status and text preview, explicit copy-to-clipboard button per entry (FR-016 — clipboard use here is an explicit user action, allowed)
- [X] T030 [US4] Implement retry for Failed entries in `src/Blabla/UI/MainWindow.xaml.cs` + `src/Blabla/Core/DictationController.cs`: re-submit the stored WAV through TranscriptionClient with the current active preset, on success update entry to Succeeded + delete audio + update internal paste-last buffer, on failure update the error message (FR-020)
- [X] T031 [P] [US4] Rehydrate the internal paste-last buffer at startup from the newest Succeeded history entry in `src/Blabla/App.xaml.cs` (data-model.md "in-memory only" section)

**Checkpoint**: All four user stories independently functional

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Lifecycle edge cases, size budget, final validation

- [X] T032 Re-register both hotkeys on resume-from-sleep / session unlock via `SystemEvents.PowerModeChanged` + `SessionSwitch` in `src/Blabla/App.xaml.cs` (spec edge case; research.md R9)
- [X] T033 Second-instance launch activates the existing instance's MainWindow (extend the mutex handling in `src/Blabla/App.xaml.cs`, e.g. named event/pipe signal)
- [X] T034 [P] Visual polish pass on `src/Blabla/UI/RecordingPopup.xaml` and `src/Blabla/UI/MainWindow.xaml`: consistent spacing/typography, pleasant recording pulse animation, "it should look okay" (FR-017) — no new surfaces
- [X] T035 [P] Add publish profile `src/Blabla/Properties/PublishProfiles/win-x64.pubxml` (framework-dependent, single-file) and verify published output ≤ 25 MB per quickstart.md size check (SC-004)
- [ ] T036 Execute quickstart.md validation scenarios V1–V6 end-to-end and fix all findings (final gate before ship) — *automated parts done (build, 43 unit tests, publish 1.38 MB ≤ 25 MB, launch smoke test, settings seeding, single-instance); the spoken-dictation scenarios need a human with microphone + API key*
- [X] T037 [P] Write `README.md` at repo root: what it is, build/publish commands, .NET Desktop Runtime prerequisite, settings/history file locations — short, per user's CLAUDE.md guidance

---

## Phase 8: User Story 5 - Optional LLM rewrite per preset (added 2026-07-02)

**Goal**: Per-preset optional chat-model post-processing: transcription → LLM with system
prompt → answer is what gets pasted (see spec User Story 5, FR-021–FR-024).

**Independent Test**: Enable LLM on one preset with a rewrite prompt; dictate → rewritten
text inserted; other presets unchanged; LLM failure keeps audio for retry.

- [X] T038 [US5] Extend `Preset` with `LlmEnabled`, `LlmSystemPrompt`, `LlmModel` (default gpt-5-mini), `LlmTemperature` + `UsesLlm` helper in `src/Blabla/Models/Preset.cs`
- [X] T039 [US5] Implement `LlmClient` (chat completions per contracts/openai-chat.md, shared error mapping via `TranscriptionClient.MapOpenAiError`) in `src/Blabla/Core/LlmClient.cs`
- [X] T040 [US5] Pipeline step in `src/Blabla/Core/DictationController.cs`: after successful transcription run LLM when `preset.UsesLlm` (also in RetryAsync); new `ShowConverting` view state in `src/Blabla/UI/RecordingPopup.xaml.cs`
- [X] T041 [US5] Tabbed preset editor in `src/Blabla/UI/MainWindow.xaml(.cs)`: tab 1 "Spraak naar tekst" (existing fields), tab 2 "Nabewerking (LLM)" with toggle, system prompt, model, temperature
- [X] T042 [P] [US5] Unit tests: `tests/Blabla.Tests/LlmClientTests.cs` (request shape, temperature omission, error mapping) + pipeline tests in `tests/Blabla.Tests/DictationControllerTests.cs` (enabled/disabled/empty-prompt/failure/retry paths)
- [X] T043 [US5] Intermediate result in history (FR-022 amendment): `RawText` on `TranscriptionEntry`, shown + separately copyable in the history card, preserved on LLM failure (`src/Blabla/Models/TranscriptionEntry.cs`, `src/Blabla/Core/DictationController.cs`, `src/Blabla/UI/MainWindow.xaml.cs`)
- [X] T044 [US3] Preset cycling during recording (FR-025): `CyclePreset` + `ActivePresetChanged` in `src/Blabla/Core/DictationController.cs`, temporary Ctrl+↑/↓ hotkeys + persistence in `src/Blabla/App.xaml.cs`, preset name in the pop-up (`src/Blabla/UI/RecordingPopup.xaml(.cs)`), unit tests in `tests/Blabla.Tests/DictationControllerTests.cs`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies
- **Foundational (Phase 2)**: needs Phase 1 — BLOCKS all stories
- **US1 (Phase 3)**: needs Phase 2 only
- **US2 (Phase 4)**: needs Phase 2; T025 references US1's error flow (T015/T019) — do after US1 (priority order anyway)
- **US3 (Phase 5)**: needs Phase 2; tray wiring extends T011; preset editor generalizes T022 (US2)
- **US4 (Phase 6)**: needs Phase 2; retry (T030) reuses US1's TranscriptionClient/controller
- **Polish (Phase 7)**: needs all desired stories complete

### Within-story ordering

- US1: T014–T017 in parallel → T018 → T019; tests T020/T021 parallel once their subjects exist
- US2: T022 ∥ T023 → T024 → T025
- US3: T026 → T027; T028 parallel with T027
- US4: T029 → T030; T031 parallel

### Parallel Opportunities

- Phase 2: T004/T005/T006 together; then T010/T012/T013 alongside T011
- Phase 3 (biggest win): T014, T015, T016, T017 are four independent files — build simultaneously
- Test tasks (T012, T013, T020, T021, T028) never conflict with each other

## Parallel Example: User Story 1

```text
# Launch the four independent core components together:
Task: "Implement AudioRecorder in src/Blabla/Core/AudioRecorder.cs"
Task: "Implement TranscriptionClient in src/Blabla/Core/TranscriptionClient.cs"
Task: "Implement TextInserter in src/Blabla/Core/TextInserter.cs"
Task: "Implement RecordingPopup in src/Blabla/UI/RecordingPopup.xaml"

# Then sequentially: DictationController (T018) → hotkey wiring (T019)
# Then in parallel: tests T020 + T021
```

## Implementation Strategy

### MVP First (recommended)

1. Phases 1–2 (setup + foundation)
2. Phase 3 (US1) — **stop and validate**: hand-edit the API key into settings.json and run
   quickstart V2/V3. This is already a daily-usable dictation tool.
3. Phase 4 (US2) → no more hand-editing; demo-able to anyone (SC-003)
4. Phase 5 (US3) → the differentiator (context presets)
5. Phase 6 (US4) → safety net (history/retry)
6. Phase 7 → edge cases, size check, final quickstart gate

### Notes

- Commit after each task or logical group
- Every story checkpoint maps to quickstart.md scenarios — validate before moving on
- Constitution Principle II: if any task tempts you to add a step to the core loop, don't
