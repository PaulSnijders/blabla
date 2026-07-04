# Contract: Local Storage Formats

**Location**: `%APPDATA%\Blabla\`
**Writes**: atomic (write `*.tmp`, then `File.Replace`) to survive crashes mid-write.
**Reads**: unknown JSON properties ignored (forward compatible); corrupt file → renamed to
`*.corrupt-<timestamp>` and recreated with defaults, user informed (never a crash loop).

## `settings.json`

```json
{
  "version": 1,
  "apiKeyEncrypted": "<base64 DPAPI ciphertext, CurrentUser scope>",
  "recordHotkey": "Ctrl+Alt+Space",
  "pasteLastHotkey": "Ctrl+Alt+V",
  "startWithWindows": false,
  "activePresetId": "5f3a…",
  "historyLimit": 100,
  "presets": [
    {
      "id": "5f3a…",
      "name": "Algemeen",
      "prompt": "Dit is Nederlands dicteren…",
      "language": "nl",
      "model": "gpt-4o-transcribe",
      "temperature": null
    }
  ]
}
```

Rules: `presets` non-empty; `activePresetId` must resolve (fallback: first preset);
hotkey strings parse to modifiers+key and must differ from each other. The API key never
appears in plaintext anywhere in this file.

## `history.json`

```json
{
  "version": 1,
  "entries": [
    {
      "id": "9c1e…",
      "timestamp": "2026-07-02T14:31:05+02:00",
      "text": "De definitieve tekst (na eventuele LLM-bewerking).",
      "rawText": null,
      "presetName": "Programmeren",
      "status": "Succeeded",
      "errorMessage": null,
      "audioFile": null,
      "durationSeconds": 24
    }
  ]
}
```

Rules: max `historyLimit` entries, newest first; `audioFile` non-null iff
`status == "Failed"`; referenced WAVs live in `audio\` and are deleted when the entry is
removed or successfully retried. Orphaned WAVs (no referencing entry) are cleaned at
startup.

## `audio\*.wav`

16 kHz mono 16-bit PCM, named `<entry-id>.wav`. Exists only for `Failed` entries pending
retry. This directory is the only place recorded audio ever touches disk.

## Registry (autostart)

`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, value name `Blabla`, data
`"<path-to-exe>"`. Present iff `startWithWindows == true`; reconciled on every settings
save.
