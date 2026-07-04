# Blabla

Windows speech-to-text in de systeemtray. Sneltoets → inspreken → sneltoets → de tekst
wordt getypt waar je cursor staat. Transcriptie via de OpenAI API met je eigen contextprompt
per preset (Algemeen / Programmeren / Wozzol / eigen presets).

- **Opnemen/stoppen**: `Ctrl+Alt+Space` (aanpasbaar) — `Esc` annuleert
- **Laatste transcriptie plakken**: `Ctrl+Alt+V` (aanpasbaar)
- Preset wisselen: rechtsklik op het tray-icoon
- ~1,4 MB op schijf; alleen een OpenAI API-key nodig (pay-per-use, geen abonnement)

## Bouwen & draaien

Vereist de [.NET 10 SDK](https://dotnet.microsoft.com/download) (voor alleen draaien: de
.NET 10 Desktop Runtime).

```powershell
dotnet run --project src\Blabla\Blabla.csproj    # ontwikkelen
dotnet test                                       # tests
dotnet publish src\Blabla\Blabla.csproj -c Release /p:PublishProfile=win-x64
# → src\Blabla\bin\publish\Blabla.exe (single file)
```

## Eerste gebruik

1. Start de app; het icoon verschijnt in de systeemtray.
2. Open het venster (klik op het icoon) → tab **Instellingen** → vul je OpenAI API-key in
   → **Opslaan**. De key wordt versleuteld opgeslagen (DPAPI, alleen jouw Windows-account).
3. Zet je cursor ergens, druk `Ctrl+Alt+Space`, spreek, en druk nogmaals.

## Bestanden

- Instellingen & presets: `%APPDATA%\Blabla\settings.json`
- Geschiedenis (max 100): `%APPDATA%\Blabla\history.json`
- Audio van mislukte transcripties (tot retry): `%APPDATA%\Blabla\audio\`

Specificatie en ontwerp: [specs/001-hotkey-dictation/](specs/001-hotkey-dictation/).
