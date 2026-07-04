# Quickstart & Validation: Hotkey Dictation

**Feature**: 001-hotkey-dictation

## Prerequisites

- Windows 10/11 x64
- .NET 10 SDK (build) / .NET 10 Desktop Runtime (run)
- An OpenAI API key with access to audio transcription models
- A working microphone

## Build & run

```powershell
dotnet build .\src\Blabla\Blabla.csproj
dotnet run --project .\src\Blabla\Blabla.csproj
```

Run tests:

```powershell
dotnet test
```

Publish (size check — must stay ≤ 25 MB, SC-004):

```powershell
dotnet publish .\src\Blabla\Blabla.csproj -c Release -r win-x64 --self-contained false `
  -p:PublishSingleFile=true -o .\publish
Get-ChildItem .\publish | Measure-Object Length -Sum
```

## Validation scenarios

Run in order; each maps to a user story in [spec.md](spec.md).

### V1 — First run & configuration (Story 2)

1. Start the app → tray icon appears; no window steals focus.
2. Press the record hotkey **before** configuring a key → clear message pointing to
   Settings (FR-006/Story 2 sc. 3).
3. Open Settings from tray → enter API key → save.
4. Verify `%APPDATA%\Blabla\settings.json` exists and contains **no plaintext key**
   (contract: local-storage.md).
5. Restart the app → settings retained.

### V2 — Core dictation loop (Story 1)

1. Focus Notepad. Press `Ctrl+Alt+Space` → pop-up with timer appears (subjectively
   instant; SC-001), Notepad keeps focus.
2. Speak a sentence; press `Ctrl+Alt+Space` again → pop-up shows "transcribing", then the
   sentence appears at the cursor in Notepad within ~5 s (SC-002).
3. Copy something to the clipboard first, dictate again → the paste goes through and your
   earlier clipboard content is restored within ~1 second (FR-004 as amended).
4. Press hotkey, then **Esc** → recording discarded, nothing inserted (FR-005).
5. Press hotkey, stay silent, press again → "no speech detected" feedback, nothing
   inserted (Story 1 sc. 5).

### V3 — Paste-last & failure handling (Stories 1, 4)

1. Dictate into Notepad; focus a different editor; press `Ctrl+Alt+V` → same text inserted
   there (FR-004a).
2. Disconnect network; dictate → clear error, entry appears in history as **Failed**
   (FR-006).
3. Reconnect; open main window → Retry on the failed entry → text produced **without
   re-recording** (FR-020), audio file removed from `%APPDATA%\Blabla\audio\`.
4. Set an invalid API key; dictate → "key not accepted" style message (Story 2 sc. 4).

### V4 — Presets (Story 3)

1. In main window create preset "Programmeren" with a prompt containing e.g. *C#, Angular,
   PostgreSQL, Wozzol*.
2. Right-click tray → both presets listed, active one marked; switch in ≤ 2 clicks
   (SC-005).
3. Dictate "ik gebruik C sharp en PostgreSQL in Wozzol" → domain terms transcribed
   correctly (SC-007, best-effort check).
4. Restart app → active preset unchanged (FR-013).

### V5 — Edge cases (spec Edge Cases)

1. Register the record hotkey while another app owns it (e.g. set both hotkeys equal, or
   use a known-taken combo) → warning shown, app still runs (FR-009).
2. Double-press the hotkey as fast as possible, repeatedly → app always ends in a coherent
   state (recording or idle), never stuck.
3. Press record hotkey during "transcribing" → ignored with feedback, results never mix.
4. Sleep/resume the machine → hotkeys still work without restart.
5. Unplug/disable the microphone; press record → clear message, no crash.
6. Dictate > 5 min → auto-stops at 5:00 and transcribes (FR-005a).
7. History: produce > 100 entries (or temporarily lower `historyLimit` in settings.json) →
   oldest trimmed, orphaned audio deleted (FR-019).

### V6 — Lifecycle

1. Enable "start with Windows" → HKCU Run entry `Blabla` exists; disable → gone (FR-011).
2. Launch a second instance → first instance's main window is activated instead.
3. Quit from tray → process exits, hotkeys released (re-registering them in another tool
   succeeds).

## Expected outcome

All scenarios pass → feature satisfies its success criteria and is ready to ship. Any
failure maps back to a numbered FR/SC in [spec.md](spec.md) for targeted fixing.
