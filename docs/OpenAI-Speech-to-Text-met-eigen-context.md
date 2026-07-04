# OpenAI Speech-to-Text met eigen context

## Idee

In plaats van een bestaande dicteerapp zoals FineType te gebruiken, kan ik een eenvoudige .NET-applicatie maken die rechtstreeks de OpenAI Speech-to-Text API aanroept.

Voordelen:

- Zelf bepalen welke context wordt meegestuurd.
- Betere herkenning van eigennamen en vaktermen.
- Volledige controle over de workflow.
- Eenvoudig uit te breiden met eigen functies.

## Werking

De applicatie kan de volgende stappen uitvoeren:

1. Audio opnemen.
2. Audio naar de OpenAI Speech-to-Text API sturen.
3. Een vaste of dynamische context (`prompt`) meesturen.
4. De transcriptie ontvangen.
5. De tekst automatisch plakken op de huidige cursorpositie.

## Voorbeeldcontext

```text
Dit is Nederlands dicteren door een softwareontwikkelaar.
Onderwerpen: C#, Angular, TypeScript, PostgreSQL, OpenAI, Claude Code,
Wozzol, MagnaFlow.

Herken Nederlandse zinnen met Engelse programmeertermen.
```

Voor Wozzol kan de context bijvoorbeeld zijn:

```text
Dit is een dictee voor de Wozzol-app.
Gebruikers spreken woorden uit in verschillende talen.
Herken buitenlandse woorden en eigennamen zo nauwkeurig mogelijk.
```

## Mogelijke uitbreidingen

- Verschillende contextprofielen (Programmeren, Wozzol, Algemeen, Vergadering).
- Automatische taalherkenning of vaste taal (`nl`).
- Push-to-talk.
- Tekst direct invoegen op de cursorpositie.
- Lokale sneltoetsen.
- Geschiedenis van transcripties.

## Globale C#-opzet

```csharp
// Audio opnemen
// MultipartFormDataContent maken
// model = gpt-4o-transcribe
// language = "nl"
// prompt = contextPrompt
// file = audio.wav
// POST naar /v1/audio/transcriptions
// Transcriptie ontvangen
// Tekst invoegen
```

## Conclusie

Voor mijn gebruik lijkt een eigen .NET-tool aantrekkelijker dan een generieke dicteerapp. Door zelf context mee te sturen kan de herkenning van programmeertermen, productnamen en meertalige gesprekken waarschijnlijk verbeteren. Daarnaast biedt een eigen tool veel meer flexibiliteit voor toekomstige uitbreidingen.
