# Data Model: Hotkey Dictation

**Feature**: 001-hotkey-dictation | **Date**: 2026-07-02

Three persisted entities (spec → Key Entities), all stored as JSON per
[contracts/local-storage.md](contracts/local-storage.md).

## AppSettings

Singleton — one per installation.

| Field | Type | Rules |
|-------|------|-------|
| `apiKeyEncrypted` | string (base64) | DPAPI CurrentUser ciphertext; empty = not configured (FR-007). Never logged. |
| `recordHotkey` | Hotkey | Default `Ctrl+Alt+Space`. Must register successfully or user is warned (FR-009). |
| `pasteLastHotkey` | Hotkey | Default `Ctrl+Alt+V`. Same registration rule. Must differ from `recordHotkey`. |
| `startWithWindows` | bool | Default `false`. Mirrors presence of HKCU Run entry (FR-011). |
| `activePresetId` | Guid | Must reference an existing preset; falls back to first preset if dangling (FR-013). |
| `historyLimit` | int | Default `100`; not exposed in v1 UI (spec assumption). |
| `presets` | Preset[] | At least 1 (FR-014). Stored inline in settings file. |

**Hotkey** (value object): `modifiers` (flags: Ctrl/Alt/Shift/Win) + `key` (virtual-key
name). Serialized as string, e.g. `"Ctrl+Alt+Space"`.

## Preset

| Field | Type | Rules |
|-------|------|-------|
| `id` | Guid | Stable identity (rename-safe). |
| `name` | string | Non-empty, unique among presets (tray menu label). |
| `prompt` | string | Context prompt sent to the API; may be empty. |
| `language` | string? | ISO-639-1 (e.g. `"nl"`) or `null` = auto-detect (FR-008). |
| `model` | string | Default `"gpt-4o-transcribe"`. Free text — any model id the endpoint accepts. |
| `temperature` | decimal? | `null` = service default; else 0–1 (FR-008 "every option"). |
| `llmEnabled` | bool | Default `false`. LLM post-processing toggle (FR-021, added 2026-07-02). |
| `llmSystemPrompt` | string | System prompt for the rewrite; LLM step is skipped when empty. |
| `llmModel` | string | Default `"gpt-5-mini"`. Chat model id, free text. |
| `llmTemperature` | decimal? | `null` = service default (omitted from the request). |

**Lifecycle**: created/edited/renamed/deleted in main window (FR-012). Deleting the active
preset activates the first remaining one; deleting the last preset is blocked (FR-014).
History entries keep the preset *name* denormalized, so deletion never breaks history.

**Seed data**: fresh install creates "Algemeen" (general-purpose Dutch prompt) as default,
plus optional templates "Programmeren" and "Wozzol" from
[docs/OpenAI-Speech-to-Text-met-eigen-context.md](../../docs/OpenAI-Speech-to-Text-met-eigen-context.md).

## TranscriptionEntry

| Field | Type | Rules |
|-------|------|-------|
| `id` | Guid | |
| `timestamp` | DateTimeOffset | Local time of recording stop; list sorted descending (FR-016). |
| `text` | string? | Final text (after optional LLM rewrite); `null` while failed. |
| `rawText` | string? | Intermediate raw transcription when LLM post-processing ran; `null` otherwise. Also kept on LLM failure so the transcription is never lost (added 2026-07-03). |
| `presetName` | string | Denormalized preset name used (FR-018). |
| `status` | enum | `Succeeded` \| `Failed` (FR-020). |
| `errorMessage` | string? | User-facing message when `Failed`; never contains the API key. |
| `audioFile` | string? | Relative path under `%APPDATA%\Blabla\audio\`; only while `Failed` (retry source, FR-020). Cleared + file deleted on successful retry. |
| `durationSeconds` | int | Recording length (display). |

**State transitions**:

```
(recording stopped) ──submit──▶ Succeeded(text, audio deleted)
                        │
                        └─────▶ Failed(errorMessage, audio kept)
Failed ──retry (reuses audioFile)──▶ Succeeded | Failed
Failed/Succeeded ──history overflow (> historyLimit)──▶ removed (+ audio file deleted)
```

**Bounding**: on every append, entries beyond `historyLimit` are removed oldest-first and
their audio files deleted (FR-019).

## In-memory only (not persisted)

- **DictationController state**: `Idle | Recording | Transcribing` (+ error display).
  See research R10.
- **Internal clipboard**: the most recent successful transcription text, target of the
  paste-last hotkey (FR-004a). Rehydrated at startup from the newest succeeded history
  entry.
- **Recording buffer**: WAV being captured; promoted to `audio\` file only on failure,
  discarded on cancel (FR-005) and after success.
