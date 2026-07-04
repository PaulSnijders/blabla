# Research: Hotkey Dictation

**Feature**: 001-hotkey-dictation | **Date**: 2026-07-02

All Technical Context unknowns resolved. Each decision below follows the constitution's
KISS principle and the ≤ 25 MB size budget (SC-004).

## R1. UI framework & runtime

**Decision**: WPF on .NET 10 (LTS), `net10.0-windows`, framework-dependent single-file publish.

**Rationale**: The user is a C#/.NET developer (fastest path to done). WPF gives the "nice
UI" the spec asks for with full styling control, and is Windows-only by nature — matching
the explicit Windows-only product decision. Framework-dependent publish keeps the app to a
few MB on disk; the .NET Desktop Runtime is a one-time shared install. .NET 10 is the
current LTS (support into 2028).

**Alternatives considered**:
- *WinForms*: simpler, but visibly dated styling; "it should look okay" is a requirement.
- *Self-contained / Native AOT*: self-contained WPF is ~70+ MB (breaks SC-004); WPF does
  not support Native AOT.
- *Avalonia*: cross-platform capability we explicitly do not want to pay for.
- *C++/Win32*: smallest possible binary but far slower to build and maintain; conflicts
  with KISS-for-the-maintainer.
- *Electron/Tauri-class stacks*: the 100 MB problem this project exists to escape.

## R2. Audio capture

**Decision**: NAudio (`WaveInEvent`), recording 16 kHz mono 16-bit PCM WAV.

**Rationale**: NAudio is the de-facto standard .NET audio library, tiny, no native
dependencies. 16 kHz mono 16-bit is what speech models are trained on; ~1.9 MB/min means a
5-minute maximum recording is ~9.6 MB — comfortably under the API's 25 MB upload limit,
with zero encoding complexity (no MP3/Opus encoder dependency).

**Alternatives considered**:
- *Raw waveIn / WASAPI P/Invoke*: no dependency but substantial fiddly native-interop code —
  worse KISS trade than one well-known package.
- *Compressed upload (Opus/MP3)*: smaller uploads but adds an encoder dependency; WAV at
  16 kHz is already small enough.

## R3. Transcription API

**Decision**: Direct `HttpClient` multipart POST to `https://api.openai.com/v1/audio/transcriptions`.
Default model `gpt-4o-transcribe`; model, language, prompt, and temperature configurable
per preset. No OpenAI SDK.

**Rationale**: One endpoint, one request shape (see
[contracts/openai-transcription.md](contracts/openai-transcription.md)). An SDK adds a
dependency tree for what is ~40 lines of HttpClient code, and the constitution forbids
unneeded dependencies. Direct control also guarantees nothing but audio + prompt leaves
the machine (Principle IV).

**Alternatives considered**:
- *Official OpenAI .NET SDK*: convenient but a large dependency for a single endpoint.
- *Azure OpenAI*: no benefit for a personal tool with a personal OpenAI key.
- *Local Whisper*: explicitly out of scope by constitution (Principle IV) — big models,
  big binary, exactly what we're avoiding.

## R4. Global hotkeys

**Decision**: Win32 `RegisterHotKey`/`UnregisterHotKey` via P/Invoke, messages received
through an `HwndSource` hook on a hidden window. Two hotkeys: record/stop toggle and
paste-last-transcription. Defaults chosen at implementation: `Ctrl+Alt+Space` (record) and
`Ctrl+Alt+V` (paste last); both user-configurable with conflict detection
(`RegisterHotKey` returns failure when taken — surface it, per FR-009).

**Rationale**: `RegisterHotKey` is the OS-sanctioned mechanism, works regardless of focused
app, requires no dependency, and failure-to-register is detectable for conflict warnings.

**Alternatives considered**:
- *Low-level keyboard hook (`WH_KEYBOARD_LL`)*: needed only for hotkeys that must swallow
  keys system-wide or key-up detection (push-to-talk). Toggle mode doesn't need it; hooks
  are more invasive and fragile.
- *Hotkey NuGet packages*: wrappers around the same API — unnecessary.

## R5. Text insertion (amended 2026-07-02)

**Decision**: Clipboard-paste with save/restore — snapshot the clipboard's formats, set the
transcription text, inject Ctrl+V via `SendInput`, wait ~300 ms for the target app to read
the clipboard, then restore the original content. Before injecting, wait until the physical
modifier keys are released (a Ctrl+V injected while Alt is still held from the hotkey
becomes AltGr+V in the target app). Clipboard operations run on a dedicated STA thread, off
the UI thread.

**Rationale**: The original decision (`SendInput` + `KEYEVENTF_UNICODE` typing, clipboard
untouched) failed validation on the user's machine: receiving applications — including
Notepad — garbled or dropped the synthetic per-character input, both as one burst and when
paced per character. Clipboard-paste is what established dictation tools use and works in
effectively every application; save/restore preserves the spirit of the original decision
(net clipboard state unchanged). Known side effect: Windows clipboard history (Win+V), when
enabled, records the pasted text.

**Alternatives considered**:
- *`SendInput` `KEYEVENTF_UNICODE` typing (original decision)*: implemented first, garbled
  output in practice (repeated/dropped characters) — rejected after validation.
- *UI Automation (`ValuePattern`)*: not universally supported by target apps.

## R6. API key protection

**Decision**: DPAPI, current-user scope (`System.Security.Cryptography.ProtectedData`),
key stored base64 in the settings JSON.

**Rationale**: Matches the clarification decision (encrypted at rest, tied to the Windows
account) with ~5 lines of code and a Microsoft-maintained package. Copied config files are
unreadable elsewhere.

**Alternatives considered**:
- *Plaintext*: rejected in clarification.
- *Windows Credential Manager*: also viable, but DPAPI-in-config keeps all settings in one
  inspectable file and avoids credential-store API interop.

## R7. Settings, presets & history storage

**Decision**: Two JSON files in `%APPDATA%\Blabla`: `settings.json` (app settings +
presets) and `history.json` (last 100 entries). Failed-transcription audio as WAV files in
`%APPDATA%\Blabla\audio\`, deleted after successful retry or when their entry leaves
history. Atomic writes (write temp file, then replace). `System.Text.Json` (built-in).

**Rationale**: Volumes are trivial (≤ 100 small records) — a database is over-engineering
(Principle I). JSON is debuggable and hand-editable. Formats specified in
[contracts/local-storage.md](contracts/local-storage.md).

**Alternatives considered**:
- *SQLite*: adds a native dependency for 100 rows — rejected.
- *Registry for settings*: opaque, hard to inspect/back up.

## R8. Tray icon

**Decision**: H.NotifyIcon.Wpf NuGet package.

**Rationale**: WPF has no built-in tray support. The alternative — WinForms `NotifyIcon`
interop — is dependency-free but yields a WinForms-styled context menu that clashes with
the "nice UI" requirement. H.NotifyIcon.Wpf is small, maintained, and gives WPF-native
styled menus. This is the one "comfort" dependency; justified under Principle V (polished
minimal UI) and recorded here per Principle I.

**Alternatives considered**:
- *WinForms NotifyIcon interop*: works, zero packages, ugly menu — acceptable fallback if
  the package ever becomes a liability.

## R9. App lifecycle

**Decision**: Single-instance via named mutex (second launch activates the main window of
the first). Start-with-Windows via `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`
(per-user, no admin). Hotkeys re-registered on session unlock/resume events
(`SystemEvents.SessionSwitch` / `PowerModeChanged`) to cover the resume-from-sleep edge case.

**Rationale**: All standard, dependency-free mechanisms; HKCU Run is the simplest autostart
that needs no installer or task scheduler.

## R10. Dictation state machine

**Decision**: Explicit `DictationController` with states `Idle → Recording → Transcribing →
Idle` (+ `Error` overlay state). Hotkey presses are handled per-state: in `Transcribing`, a
record press is ignored with visual feedback (no queuing). Rapid double-press resolves
deterministically because state transitions are dispatcher-serialized.

**Rationale**: The spec's edge cases (double-press, second recording while in flight,
stuck-state prohibition) are exactly what a small explicit state machine prevents. This is
the most unit-testable part of the app.

**Alternatives considered**:
- *Queuing recordings during transcription*: more complex, easy to mix up results —
  spec allows blocking with feedback; chosen for simplicity.

## R11. Optional LLM post-processing (added 2026-07-02)

**Decision**: Per-preset optional chat-completions step after transcription
([contracts/openai-chat.md](contracts/openai-chat.md)): system prompt from the preset, raw
transcription as user message, answer becomes the final text. Default model `gpt-5-mini`;
temperature omitted unless set (several chat models only accept the default). Implemented
as a second `HttpClient` class (`LlmClient`) mirroring `TranscriptionClient`, sharing its
error-message mapping. Failure semantics identical to transcription failure (audio kept,
retry re-runs the full pipeline).

**Rationale**: User-requested addition — turns raw dictation into purpose-shaped text
("make this a proper e-mail") without adding a new UI surface: it lives on a tab inside the
existing preset editor, and the core loop gains no mandatory steps (Principle II — the
feature is opt-in per preset).

**Alternatives considered**:
- *Responses API*: newer, but chat completions is stable, universally supported by the
  models involved, and one request shape — KISS.
- *Separate API key / provider for the LLM*: unnecessary; same OpenAI account.
