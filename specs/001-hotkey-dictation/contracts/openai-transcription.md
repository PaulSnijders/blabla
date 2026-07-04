# Contract: OpenAI Audio Transcription API

**Direction**: outbound (the only network call the app makes)
**Endpoint**: `POST https://api.openai.com/v1/audio/transcriptions`
**Auth**: `Authorization: Bearer <api key>` (decrypted in memory only)

## Request (`multipart/form-data`)

| Part | Value | Source |
|------|-------|--------|
| `file` | `recording.wav` — 16 kHz, mono, 16-bit PCM WAV, ≤ 5 min (~9.6 MB) | AudioRecorder |
| `model` | e.g. `gpt-4o-transcribe` | active Preset `model` |
| `prompt` | context prompt text; part omitted when empty | active Preset `prompt` |
| `language` | ISO-639-1 e.g. `nl`; part omitted when `null` (auto-detect) | active Preset `language` |
| `temperature` | `0`–`1`; part omitted when `null` | active Preset `temperature` |
| `response_format` | `json` (fixed) | app |

Timeout: 60 s per request (covers 5-min uploads on slow links); no automatic retries —
failures go to history for manual retry (FR-020).

## Response

**200 OK**:

```json
{ "text": "the transcribed text" }
```

- `text` trimmed; if empty/whitespace → treated as the "silence" case: user is informed,
  nothing inserted, entry recorded as Failed with message "No speech detected" (spec
  Story 1, scenario 5).

**Errors → user-facing mapping** (FR-006; message must never include the key):

| Condition | HTTP | User message (Dutch UI text decided at implementation) |
|-----------|------|--------------------------------------------------------|
| Invalid/revoked key | 401 | "API key not accepted — check Settings." |
| Rate limit / quota | 429 | "OpenAI rate limit or quota reached — try again shortly." |
| Model name invalid | 400/404 | "Model '<model>' was rejected — check the preset." |
| Service down | 5xx | "OpenAI service error — recording saved, retry from history." |
| No network / DNS / timeout | — | "No connection — recording saved, retry from history." |

All error cases: audio WAV is persisted and the history entry is `Failed` with retry
available. All success cases: audio deleted.

## Versioning assumptions

- Endpoint shape is OpenAI's stable v1 audio API; model ids are pass-through strings so
  new models need no app change (FR-008).
- If OpenAI adds request options the app doesn't model, they are out of scope until a
  preset field is added for them.
