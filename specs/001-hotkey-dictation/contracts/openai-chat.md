# Contract: OpenAI Chat Completions (LLM post-processing)

**Added**: 2026-07-02 (User Story 5)
**Direction**: outbound; called only when the active preset has LLM post-processing enabled
**Endpoint**: `POST https://api.openai.com/v1/chat/completions`
**Auth**: `Authorization: Bearer <api key>` (same key as transcription)

## Request (`application/json`)

```json
{
  "model": "<preset.llmModel, default gpt-5-mini>",
  "messages": [
    { "role": "system", "content": "<preset.llmSystemPrompt>" },
    { "role": "user", "content": "<the raw transcription text>" }
  ],
  "temperature": 0.4
}
```

- `temperature` omitted when `preset.llmTemperature` is null (some models only accept the
  default).
- Timeout 60 s; no automatic retries — failures go to history for manual retry (FR-024).

## Response

**200 OK**: `choices[0].message.content` is the final text. Trimmed; empty/whitespace →
treated as failure ("empty answer"), audio kept for retry.

**Errors**: mapped to the same user-facing messages as the transcription contract
(401 / 429 / 400+404 with the LLM model name / 5xx / network), never containing the key.

## Pipeline position

`audio → /v1/audio/transcriptions → text → (if preset.UsesLlm) /v1/chat/completions →
final text → insert + history + paste-last buffer`. A transcription failure short-circuits:
the LLM is never called. Retry from history re-runs this full pipeline with the *current*
active preset.
