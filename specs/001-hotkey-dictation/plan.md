# Implementation Plan: Hotkey Dictation

**Branch**: `001-hotkey-dictation` | **Date**: 2026-07-02 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/001-hotkey-dictation/spec.md`

## Summary

A tiny Windows tray app for hotkey-driven dictation: a global hotkey starts microphone
capture and shows an always-on-top recording pop-up with a timer; pressing it again stops
capture, sends the audio with the active preset's context prompt to OpenAI's transcription
API, and pastes the result at the current cursor position (clipboard-paste with restore of
the previous clipboard content). A second hotkey re-inserts the last transcription. A small WPF main window
provides settings (API key, hotkeys, presets, OpenAI options) and a bounded history list.
Built as a single .NET 10 WPF project, framework-dependent single-file publish (~a few MB
on disk), direct `HttpClient` call to the API — no SDK, no database, no services.

## Technical Context

**Language/Version**: C# 13 / .NET 10 (LTS)

**Primary Dependencies**: WPF (built-in), NAudio (microphone capture),
H.NotifyIcon.Wpf (tray icon + WPF context menu),
System.Security.Cryptography.ProtectedData (DPAPI key encryption).
Win32 via P/Invoke for global hotkeys (`RegisterHotKey`) and text insertion (`SendInput`).

**Storage**: JSON files in `%APPDATA%\Blabla` (settings + presets, history capped at 100
entries); WAV files kept only for failed transcriptions, deleted on successful retry.

**Testing**: xUnit for core logic (request building, settings/history stores, preset
resolution, DPAPI round-trip, history bounding); manual quickstart scenarios for hotkey,
audio, and insertion behavior.

**Target Platform**: Windows 10/11 x64 desktop, per-user install, no admin rights

**Project Type**: Desktop app (single project + test project)

**Performance Goals**: Recording pop-up visible < 200 ms after hotkey (constitution);
transcribed text inserted ≤ 5 s after stop for ≤ 30 s dictation (SC-002); idle footprint
that of a dormant tray app.

**Constraints**: ≤ 25 MB on disk (SC-004) → framework-dependent publish; insertion restores
the previous clipboard content afterwards (FR-004 as amended); API key DPAPI-encrypted at
rest (FR-007); max
recording duration 5 minutes with auto-stop-and-transcribe (FR-005a); offline/API failures
must surface clearly and keep audio for retry (FR-006, FR-020).

**Scale/Scope**: Single user, single machine, ~2 UI surfaces + tray menu, ~30–60 dictations
per day, history bounded at 100 entries.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Check | Status |
|-----------|-------|--------|
| I. Simplicity First (KISS) | Single WPF project, 3 NuGet packages each covering a concrete need the platform lacks (NAudio: audio capture; H.NotifyIcon.Wpf: WPF-styled tray; ProtectedData: DPAPI). No DB, no DI container, no MVVM framework, no OpenAI SDK for one endpoint. | PASS |
| II. Frictionless Capture | Core loop is hotkey → record → hotkey → transcribe → SendInput paste; zero focus change (pop-up never takes focus, `WS_EX_NOACTIVATE`). Cancel (Esc) is optional, not an extra step. | PASS |
| III. Responsive & Honest Feedback | Pop-up window pre-created at startup and only shown on hotkey → < 200 ms. Explicit states: recording (timer), transcribing, error. Failures keep audio and show messages (FR-006). | PASS |
| IV. Transparent & Configurable OpenAI | Direct call to `/v1/audio/transcriptions`; prompt, language, model user-configurable; key DPAPI-encrypted in `%APPDATA%`, never logged. Nothing but audio + prompt leaves the machine. | PASS |
| V. Minimal, Purposeful UI | Exactly two surfaces: recording pop-up + main window (history list + settings/presets). Tray menu only: presets, open window, quit. | PASS |

**Post-Phase-1 re-check**: design artifacts introduce no new projects, dependencies, or UI
surfaces beyond the above. PASS — Complexity Tracking table left empty.

## Project Structure

### Documentation (this feature)

```text
specs/001-hotkey-dictation/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   ├── openai-transcription.md   # External API contract
│   └── local-storage.md          # Settings/history file formats
└── tasks.md             # Phase 2 output (/speckit-tasks — NOT created by /speckit-plan)
```

### Source Code (repository root)

```text
src/
└── Blabla/                      # single WPF app project (net10.0-windows)
    ├── App.xaml / App.xaml.cs   # startup, single-instance mutex, tray icon wiring
    ├── Core/
    │   ├── HotkeyManager.cs     # RegisterHotKey/UnregisterHotKey P/Invoke + message loop hook
    │   ├── AudioRecorder.cs     # NAudio WaveInEvent → 16 kHz mono 16-bit WAV, 5-min auto-stop
    │   ├── TextInserter.cs      # clipboard-paste + restore, injected Ctrl+V (R5 amended)
    │   ├── TranscriptionClient.cs # HttpClient multipart POST to OpenAI, typed errors
    │   └── DictationController.cs # state machine: Idle → Recording → Transcribing → Idle
    ├── Models/
    │   ├── AppSettings.cs       # key (encrypted), hotkeys, autostart, active preset id
    │   ├── Preset.cs            # name, prompt, language, model, extra options
    │   └── TranscriptionEntry.cs # timestamp, text, preset, status, audio path (failed only)
    ├── Services/
    │   ├── SettingsStore.cs     # JSON + DPAPI for key, %APPDATA%\Blabla
    │   ├── HistoryStore.cs      # JSON, capped at 100, failed-audio cleanup
    │   └── StartupRegistration.cs # HKCU Run key
    └── UI/
        ├── RecordingPopup.xaml  # always-on-top, no-activate; timer / transcribing / error states
        └── MainWindow.xaml      # history list + settings + preset editor

tests/
└── Blabla.Tests/                # xUnit: stores, request building, state machine, bounding
```

**Structure Decision**: Single executable project `src/Blabla` plus one test project
`tests/Blabla.Tests`. No layering into separate libraries — the app is small enough that
folders (`Core`, `Models`, `Services`, `UI`) provide all the separation needed (Principle I).

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

*No violations — table intentionally empty.*
