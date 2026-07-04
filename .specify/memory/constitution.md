<!--
Sync Impact Report
- Version change: (template) → 1.0.0
- Modified principles: n/a (initial ratification — all placeholders filled)
- Added sections:
  - Core Principles (I–V)
  - Constraints & Security
  - Development Workflow
  - Governance
- Removed sections: none
- Templates:
  - ✅ .specify/templates/plan-template.md — Constitution Check gate is generic; derives
    gates from this file at plan time. No edits required.
  - ✅ .specify/templates/spec-template.md — no constitution-specific sections; aligned.
  - ✅ .specify/templates/tasks-template.md — task categories compatible; aligned.
- Follow-up TODOs: none
-->

# Blabla Constitution

Blabla is a Windows-only speech-to-text utility: press a global hotkey anywhere, a small
pop-up shows a recording timer, press the hotkey again to stop, the audio is sent to an
OpenAI endpoint for transcription, and the resulting text is pasted at the current cursor
position. A minimal UI shows recent transcriptions and lets the user configure the prompt
sent to OpenAI.

## Core Principles

### I. Simplicity First (KISS)

The app MUST stay a single, small, self-contained Windows application. Prefer simple,
explicit solutions over clever abstractions. No unnecessary dependencies, services,
databases, or infrastructure MAY be added; every dependency MUST be justified by a concrete
need the standard platform cannot meet. If a design feels too complex, it is — simplify it
before implementing.

**Rationale**: This is a personal productivity tool, not a platform. Complexity is the main
threat to it ever being finished and maintained.

### II. Frictionless Capture (NON-NEGOTIABLE)

The core loop — hotkey → record → hotkey → transcribe → paste — MUST work from any
application, without switching focus away from where the user is typing. The global hotkey
MUST be registered system-wide. While recording, a small always-on-top pop-up MUST show
that recording is active, including an elapsed-time timer. On stop, the transcribed text
MUST be inserted at the current cursor position in whatever application is active. Any
feature that adds a step to this loop MUST be rejected or made optional.

**Rationale**: The entire value of the app is speed and zero friction; the core loop is the
product.

### III. Responsive & Honest Feedback

The pop-up MUST appear near-instantly (target < 200 ms) after the hotkey is pressed. The
app MUST always make its state visible: idle, recording (with timer), transcribing, done,
or failed. Failures (no microphone, network error, OpenAI error) MUST be shown to the user
plainly — never a silent no-op, and never fake success. A failed transcription MUST NOT
lose the recorded audio silently.

**Rationale**: A background hotkey tool the user cannot trust or see the state of will be
abandoned immediately.

### IV. Transparent & Configurable OpenAI Integration

Transcription is delegated to an OpenAI endpoint; the app MUST NOT attempt local
speech-recognition models. The prompt/instructions sent along with the audio MUST be
user-configurable through the UI. The API key MUST be stored locally on the user's machine
and MUST never be committed to the repository or logged. Nothing other than the recorded
audio and the configured prompt is sent off the machine.

**Rationale**: Delegating to OpenAI keeps the app simple (Principle I); configurability of
the prompt is a first-class feature, and key handling is the one hard security requirement.

### V. Minimal, Purposeful UI

The UI consists of exactly two surfaces: the recording pop-up, and a small main window
showing the most recent transcriptions (history) and the OpenAI prompt configuration.
History MUST allow re-copying past transcriptions. New UI surfaces, settings pages, or
options MUST NOT be added unless they directly serve the core loop.

**Rationale**: Scope creep in the UI is where "very easy" apps go to die.

## Constraints & Security

- **Platform**: Windows only (Windows 10/11). No cross-platform abstractions or
  portability layers.
- **Distribution**: Runs as a normal user-level desktop app; no admin rights, no drivers,
  no installer complexity beyond what is strictly needed.
- **Privacy**: Recorded audio is kept locally only as needed for the transcription flow
  and history; it MUST never be uploaded anywhere except the configured OpenAI endpoint.
- **Secrets**: The OpenAI API key lives in local user configuration, excluded from source
  control. No secrets in code, logs, or error messages.
- **Offline behavior**: Without network access the app MUST fail gracefully with a clear
  message; it MUST NOT crash or hang.

## Development Workflow

- Features follow the spec-kit flow: constitution → specify → plan → tasks → implement.
- Plans MUST pass the Constitution Check gate; violations require an entry in the plan's
  Complexity Tracking table with a justification.
- Testing effort is proportional to risk: the transcription/paste pipeline and error
  handling deserve tests; trivial UI wiring does not require test-first ceremony.
- Keep project documentation (README, CLAUDE.md) short and limited to what is non-obvious.

## Governance

This constitution supersedes ad-hoc practices for this project. Amendments are made by
editing this file (normally via `/speckit.constitution`), stating the rationale, and
bumping the version:

- **MAJOR**: Removing or redefining a principle in a backward-incompatible way.
- **MINOR**: Adding a principle or materially expanding guidance.
- **PATCH**: Clarifications and wording fixes.

Every plan and PR SHOULD be checked against Principles I, II, and V in particular — is it
simple, does it protect the core loop, and does it avoid UI creep. Complexity MUST be
justified or removed.

**Version**: 1.0.0 | **Ratified**: 2026-07-02 | **Last Amended**: 2026-07-02
