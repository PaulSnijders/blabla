# Feature Specification: Hotkey Dictation

**Feature Branch**: `001-hotkey-dictation`

**Created**: 2026-07-02

**Status**: Draft

**Input**: User description: "Windows-only speech-to-text app. Super fast, super KISS, nice UI. Lives in the system tray with settings. Shows a list of what you've spoken. Every setting you can send to OpenAI is configurable. Preset profiles (e.g., emails, friends, programming) selectable from the tray, each with its own context prompt for OpenAI. Hotkey → pop-up shows you're talking → hotkey again → text is pasted where the cursor is. Small footprint (current tool is 100 MB, too much) and much cheaper than an $8/month subscription — just configure the API key and go."

## Clarifications

### Session 2026-07-02

- Q: What happens to the system clipboard when transcribed text is inserted at the cursor?
  → A: Insert without touching the system clipboard (e.g., simulated typing); additionally,
  keep the transcription in an app-internal clipboard with a separate hotkey that pastes the
  last transcription on demand, as recovery when insertion went wrong.
  **Amended (2026-07-02, during validation)**: simulated typing proved unreliable in real
  applications (garbled/dropped characters, even in Notepad). New decision, confirmed by
  the user: insert via clipboard-paste with save/restore — the previous clipboard content
  is snapshotted and restored immediately after the paste, so the net clipboard state is
  preserved. Known side effect: when Windows clipboard history (Win+V) is enabled, the
  pasted text appears there. The app-internal clipboard and paste-last hotkey are unchanged.
- Q: How strongly must the API key be protected at rest? → A: Encrypted at rest, tied to
  the Windows user account (unreadable if the config file is copied to another machine or
  user).
- Q: What is the maximum recording duration? → A: 5 minutes; on reaching it the recording
  auto-stops and is submitted for transcription as if the user had stopped it.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Dictate anywhere with a hotkey (Priority: P1)

The user is typing in any Windows application (email client, IDE, browser, chat). They press
a global hotkey; a small pop-up appears showing that recording is active, with an elapsed-time
timer. They speak, press the same hotkey again, the pop-up shows the audio is being
transcribed, and moments later the transcribed text is inserted at the current cursor
position in the application they were working in. Their configured context prompt is sent
along with the audio to improve recognition of names and jargon.

**Why this priority**: This loop *is* the product. Without it, nothing else has value; with
only this (and a hard-coded configuration), the app is already a usable MVP.

**Independent Test**: Configure an API key, open any text editor, press the hotkey, speak a
sentence, press the hotkey again, and verify the spoken sentence appears at the cursor.

**Acceptance Scenarios**:

1. **Given** the app is running in the background and a text field has focus in any
   application, **When** the user presses the hotkey, **Then** a small always-on-top pop-up
   appears within a fraction of a second showing a recording indicator and a running timer.
2. **Given** recording is in progress, **When** the user presses the hotkey again,
   **Then** recording stops, the pop-up indicates transcription is in progress, and the
   resulting text is inserted at the cursor position of the previously focused application.
3. **Given** recording is in progress, **When** the user cancels (e.g., presses Escape or a
   cancel control on the pop-up), **Then** recording is discarded, nothing is sent, and no
   text is inserted.
4. **Given** the transcription service returns an error or the network is down, **When** a
   recording is submitted, **Then** the user sees a clear error message, no text is inserted,
   and the recording is not silently lost (it remains available for retry from the history).
5. **Given** the user presses the hotkey and says nothing (or only silence is captured),
   **When** they stop the recording, **Then** the app handles the empty result gracefully and
   informs the user instead of pasting nothing without explanation.

---

### User Story 2 - Configure once, then go (Priority: P2)

The user opens the app's settings from the system tray icon, enters their OpenAI API key,
and adjusts transcription options: the context prompt, the spoken language (fixed language
or automatic detection), the transcription model, and the recording hotkey. Every option the
transcription service accepts is available. Settings persist across restarts, and the app
starts with Windows so it is always ready.

**Why this priority**: Required for anyone to use the app with their own account, but the
core loop (Story 1) can be demonstrated with a minimal hard-coded configuration first.

**Independent Test**: Enter an API key and a distinctive context prompt (e.g., with a unique
product name), dictate that term, and verify the term is recognized; restart the app and
verify settings are retained.

**Acceptance Scenarios**:

1. **Given** a fresh installation, **When** the user opens settings from the tray icon and
   enters a valid API key, **Then** dictation works without any other mandatory
   configuration.
2. **Given** the user edits the context prompt, language, model, or hotkey, **When** they
   save and later restart the app, **Then** all settings are preserved and applied.
3. **Given** no API key is configured, **When** the user tries to dictate, **Then** the app
   clearly tells them an API key is required and points them to settings, rather than
   failing silently.
4. **Given** an invalid API key, **When** a transcription is attempted, **Then** the user
   receives a clear, non-technical error message indicating the key is not accepted.

---

### User Story 3 - Switch context presets from the tray (Priority: P3)

The user maintains multiple presets, each bundling transcription settings (at minimum the
context prompt, optionally language and other options): for example "Programming" (Dutch
sentences with English programming terms like C#, Angular, PostgreSQL), "Email" (formal
business writing), "Friends" (casual chat), or "Wozzol" (multilingual vocabulary words).
Before dictating, they right-click the tray icon and pick the active preset in one click.
Subsequent dictations use that preset's settings.

**Why this priority**: This is the differentiating feature over generic dictation tools —
purpose-built context per situation — but dictation works fine with a single prompt first.

**Independent Test**: Create two presets with clearly different context prompts, switch
between them via the tray menu, and verify each dictation uses the active preset's prompt.

**Acceptance Scenarios**:

1. **Given** multiple presets exist, **When** the user opens the tray icon menu,
   **Then** all presets are listed with the active one marked, and selecting another one
   makes it active immediately.
2. **Given** a preset is active, **When** the user dictates, **Then** that preset's context
   prompt and settings are used for the transcription request.
3. **Given** the settings UI is open, **When** the user creates, edits, renames, or deletes
   a preset, **Then** the changes are reflected in the tray menu without restarting the app.
4. **Given** the app restarts, **When** the user dictates, **Then** the preset that was
   active before the restart is still active.

---

### User Story 4 - Review and reuse past transcriptions (Priority: P4)

The user opens the main window and sees a list of their recent transcriptions, newest first,
with timestamps. They can copy any past transcription back to the clipboard — useful when a
paste went to the wrong window, the target application discarded it, or they want to reuse
an earlier dictation.

**Why this priority**: A safety net and convenience feature; valuable but not required for
the core dictation experience.

**Independent Test**: Perform several dictations, open the main window, verify all appear in
order, and copy an older entry back to the clipboard.

**Acceptance Scenarios**:

1. **Given** the user has completed dictations, **When** they open the main window,
   **Then** recent transcriptions are listed newest-first with a timestamp.
2. **Given** the history list is shown, **When** the user activates a copy action on an
   entry, **Then** that transcription's full text is placed on the clipboard.
3. **Given** a transcription failed after recording, **When** the user opens the history,
   **Then** the failed item is visible and can be retried without re-recording.
4. **Given** history grows over time, **When** the retention limit is reached, **Then** the
   oldest entries are removed automatically so storage stays bounded.

---

### User Story 5 - Optional LLM rewrite per preset (Priority: P5) *(added 2026-07-02)*

A preset can optionally send the finished transcription through a chat language model
before insertion. In the preset editor a second tab, "Nabewerking (LLM)", offers an
activation checkbox plus a system prompt, model, and temperature. When enabled, the raw
transcription becomes the user message for the chat model, and the model's answer is what
gets pasted — e.g. an "E-mail" preset whose system prompt says "make this proper e-mail
text, fix spelling, make the sentences logical".

**Why this priority**: Pure addition on top of a complete product; dictation works fully
without it.

**Independent Test**: Enable the LLM tab on a preset with a rewrite system prompt, dictate
a sloppy sentence, and verify the inserted text is the rewritten version while a preset
without LLM still inserts the raw transcription.

**Acceptance Scenarios**:

1. **Given** a preset with LLM post-processing enabled and a system prompt, **When** a
   dictation succeeds, **Then** the pop-up shows a distinct "converting" state and the
   inserted text (and history entry, and paste-last buffer) contain the LLM's answer.
2. **Given** a preset with LLM disabled (or an empty system prompt), **When** a dictation
   succeeds, **Then** the raw transcription is inserted unchanged and no LLM call is made.
3. **Given** the LLM call fails, **When** the dictation completes, **Then** the user sees a
   clear error, nothing is inserted, and the recording stays in history for retry (the
   retry re-runs the full pipeline including the LLM step).

---

### Edge Cases

- Hotkey conflicts: the chosen hotkey is already registered by another application — the app
  must detect this, inform the user, and let them pick a different hotkey.
- No microphone present, microphone disabled, or Windows microphone permission denied — a
  clear message, not a crash or an empty recording.
- Target application loses focus or is closed between recording start and insertion — the
  text must still be recoverable via the paste-last-transcription hotkey and the history.
- The cursor is in a location that does not accept text input (e.g., desktop, read-only
  field) — the text remains available via the paste-last-transcription hotkey and history.
- Very long dictation — recording auto-stops at the 5-minute maximum and is transcribed;
  the user is never surprised by a failed submit due to length.
- Hotkey pressed twice in very rapid succession — no stuck states; the app cleanly ends up
  either idle or recording, never in a limbo state.
- User attempts to start a second recording while a transcription is still in flight — the
  app either queues or blocks with feedback, but never mixes up results.
- System resume from sleep — the hotkey must still work without restarting the app.

## Requirements *(mandatory)*

### Functional Requirements

**Core dictation loop**

- **FR-001**: The app MUST register a system-wide hotkey that works regardless of which
  application currently has focus.
- **FR-002**: On hotkey press while idle, the app MUST start capturing microphone audio and
  display a small always-on-top pop-up showing a recording indicator and elapsed time.
- **FR-003**: On hotkey press while recording, the app MUST stop capture, submit the audio
  with the active preset's settings to the OpenAI transcription service, and show a
  "transcribing" state in the pop-up.
- **FR-004**: On successful transcription, the app MUST insert the resulting text at the
  current cursor position of the application that had focus, and the pop-up MUST close.
  Insertion works via clipboard-paste with restore: the clipboard content from before the
  insertion MUST be restored immediately afterwards (within ~1 second), so the clipboard is
  never left holding the transcription.
- **FR-004a**: The app MUST keep the most recent transcription in an app-internal clipboard
  and provide a second global hotkey that inserts it at the current cursor position on
  demand — the recovery path when the automatic insertion went to the wrong place or was
  lost.
- **FR-005**: The user MUST be able to cancel an in-progress recording, discarding the audio
  without submitting it.
- **FR-005a**: Recording MUST automatically stop at the maximum duration of 5 minutes and be
  submitted for transcription exactly as if the user had stopped it via the hotkey.
- **FR-006**: On any failure (network, service error, invalid key, no microphone), the app
  MUST show a clear user-facing message and keep the recorded audio available for retry; it
  MUST never fail silently or pretend success.

**Configuration**

- **FR-007**: The user MUST be able to configure their OpenAI API key; the key MUST be
  stored locally only, encrypted at rest such that only the current Windows user account
  can decrypt it, and never included in source control, logs, or error messages.
- **FR-008**: The user MUST be able to configure every transcription option the service
  accepts — at minimum: context prompt, language (fixed or automatic), and model.
- **FR-009**: The user MUST be able to change both global hotkeys (record/stop and
  paste-last-transcription); the app MUST warn when a chosen hotkey cannot be registered.
- **FR-010**: All settings MUST persist across app restarts.
- **FR-011**: The app MUST offer an option to start automatically with Windows.

**Presets**

- **FR-012**: The user MUST be able to create, edit, rename, and delete named presets, each
  containing at least a context prompt and optionally overriding other transcription options.
- **FR-013**: Exactly one preset is active at any time; the active preset MUST be selectable
  from the system tray menu and from the main window, and MUST survive restarts.
- **FR-014**: A fresh installation MUST include at least one default preset so dictation
  works immediately after entering an API key.
- **FR-025** *(added 2026-07-03)*: While recording, Ctrl+↑/Ctrl+↓ MUST cycle the active
  preset without interrupting the capture; the pop-up shows the selected preset name next
  to the timer, the choice is persisted immediately, and the recording is transcribed with
  the preset active at submit time. These hotkeys are registered only during recording, so
  they never conflict with other applications. (Design note: the original idea — confirm a
  preset and abort the session — proved unnecessary, because the preset is only consumed
  when the audio is submitted; Esc remains available to discard and start over.)

**LLM post-processing** *(added 2026-07-02)*

- **FR-021**: Each preset MAY enable LLM post-processing, configured on a separate tab of
  the preset editor with: activation toggle, system prompt, chat model, and temperature.
- **FR-022**: When enabled with a non-empty system prompt, the app MUST send the successful
  transcription as the user message to the configured chat model and use the model's answer
  as the final text (insertion, history, paste-last buffer). The intermediate raw
  transcription MUST be stored on the history entry and shown (and copyable) in the history
  list — also when the LLM step fails, so the transcription is never lost.
- **FR-023**: The pop-up MUST show a distinct state while the LLM conversion runs.
- **FR-024**: An LLM failure MUST behave like a transcription failure: clear message,
  nothing inserted, audio kept, retry re-runs the full pipeline.

**Tray & UI**

- **FR-015**: The app MUST run as a background app with a system tray icon; the tray menu
  MUST provide at minimum: preset selection, opening the main window/settings, and quit.
- **FR-016**: The main window MUST show the transcription history (newest first, with
  timestamps) and provide a copy-to-clipboard action per entry.
- **FR-017**: The app MUST have a clean, polished appearance ("it should look okay") — the
  pop-up and main window are the only two UI surfaces.

**History**

- **FR-018**: The app MUST store completed transcriptions locally with their timestamp and
  the preset used.
- **FR-019**: History MUST be bounded (by count or age) with the oldest entries removed
  automatically; history is stored locally only.
- **FR-020**: Failed transcriptions MUST appear in history with a retry option that reuses
  the stored audio without re-recording.

### Key Entities

- **Preset**: A named bundle of transcription settings — name, context prompt, language
  setting, model choice, and any other service options — plus optional LLM post-processing
  settings (enabled flag, system prompt, chat model, temperature). One preset is marked
  active.
- **Transcription entry**: A single dictation result — timestamp, transcribed text, preset
  used, status (succeeded/failed), and (for failed entries, until retried or expired) the
  recorded audio.
- **App settings**: API key, the two hotkeys (record/stop, paste-last-transcription),
  start-with-Windows flag, history retention limit, and the reference to the active preset.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: From hotkey press, the recording pop-up is visible in under half a second in
  ordinary use — the user never wonders whether the app heard them.
- **SC-002**: For a typical dictation of up to 30 seconds, the transcribed text appears at
  the cursor within 5 seconds of stopping the recording (given a normal internet connection).
- **SC-003**: A new user can go from finished installation to their first successful
  dictation in under 2 minutes, with entering the API key as the only required setup step.
- **SC-004**: The installed application is at least 4× smaller than the 100 MB tool it
  replaces (target: 25 MB or less on disk).
- **SC-005**: Switching the active preset from the tray takes no more than 2 clicks.
- **SC-006**: Running cost is pay-per-use API pricing only — no subscription; for typical
  personal use (≈30 minutes of dictation per day) the monthly cost stays well below the $8
  subscription it replaces.
- **SC-007**: With a domain-specific preset (e.g., programming terms), domain terms from the
  context prompt are transcribed correctly in the large majority of dictations — noticeably
  better than dictating without a context prompt.
- **SC-008**: 100% of failed transcriptions result in a visible error message, and none of
  them lose the recorded audio.

## Assumptions

- Single user, single machine; no accounts, sync, or multi-user features.
- The user brings their own OpenAI API key and accepts OpenAI's pay-per-use pricing; cost
  display/tracking inside the app is out of scope for v1.
- Windows 10/11 desktop only; no Linux/macOS support (explicit product decision to keep the
  app small and fast; the user primarily dictates on Windows).
- "Paste at cursor" is best-effort text insertion into the previously focused application,
  performed via clipboard-paste with restore of the previous clipboard content;
  applications that block programmatic input are an accepted limitation, mitigated by the
  paste-last-transcription hotkey and the history's copy action.
- Default hotkeys are sensible, rarely-conflicting combinations chosen at implementation
  time; the user can change both (FR-009).
- Maximum recording duration is 5 minutes (auto-stop and transcribe), comfortably within
  the transcription service's audio limits; typical dictations are well under a minute.
- Default history retention is a bounded recent window (e.g., last 100 entries); exact
  number is an implementation default the user does not need to configure in v1.
- Audio of successful transcriptions is not retained; only failed entries keep audio, and
  only until retried or cleaned up.
- The default preset ships with a general-purpose context prompt; example presets from the
  context document (Programming, Wozzol) may ship as optional templates.
- A working internet connection is required to transcribe; offline dictation is out of
  scope by design.
