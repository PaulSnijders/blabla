using System.Text.Json.Serialization;
using System.Windows.Input;

namespace Blabla.Models;

/// <summary>Application settings, persisted to settings.json (spec: Key Entities / App settings).</summary>
public sealed class AppSettings
{
    public int Version { get; set; } = 1;

    /// <summary>Base64 DPAPI (CurrentUser) ciphertext; empty when no key configured.</summary>
    public string ApiKeyEncrypted { get; set; } = "";

    public Hotkey RecordHotkey { get; set; } = new(ModifierKeys.Control | ModifierKeys.Alt, Key.Space);
    public Hotkey PasteLastHotkey { get; set; } = new(ModifierKeys.Control | ModifierKeys.Alt, Key.V);

    public bool StartWithWindows { get; set; }
    public Guid ActivePresetId { get; set; }
    public int HistoryLimit { get; set; } = 100;
    public List<Preset> Presets { get; set; } = [];

    /// <summary>The active preset, falling back to the first preset when the id dangles (FR-013/FR-014).</summary>
    [JsonIgnore]
    public Preset ActivePreset => Presets.FirstOrDefault(p => p.Id == ActivePresetId) ?? Presets[0];

    [JsonIgnore]
    public bool HasApiKey => ApiKeyEncrypted.Length > 0;

    /// <summary>First-run defaults with the seed presets from the context document.</summary>
    public static AppSettings CreateDefault()
    {
        var algemeen = new Preset
        {
            Name = "Algemeen",
            Language = "nl",
            Prompt = "Dit is Nederlands dicteren. Herken volledige Nederlandse zinnen " +
                     "met correcte interpunctie en hoofdletters.",
        };
        var programmeren = new Preset
        {
            Name = "Programmeren",
            Language = "nl",
            Prompt = "Dit is Nederlands dicteren door een softwareontwikkelaar. " +
                     "Onderwerpen: Python, C#, Angular, TypeScript, PostgreSQL, OpenAI, Claude Code. " +
                     "Herken Nederlandse zinnen met Engelse programmeertermen.",
        };
        var general = new Preset
        {
            Name = "General",
            Language = "en",
            Prompt = "This is English dictation. Recognize complete English sentences " +
                     "with correct punctuation and capitalization.",
        };
        var programming = new Preset
        {
            Name = "Programming",
            Language = "en",
            Prompt = "This is English dictation for software developers. " +
                     "Topics: Python, C#, Angular, TypeScript, PostgreSQL, OpenAI, Claude Code. " +
                     "Recognize English sentences with programming terms.",
        };
        var multilingual = new Preset
        {
            Name = "Multilingual",
            Language = null, // multilingual: automatic detection
            Prompt = "This is a dictation option. Users speak words in " +
                     "different languages. Recognize foreign words and proper names as " +
                     "accurately as possible.",
        };

        return new AppSettings
        {
            Presets = [algemeen, programmeren, general, programming, multilingual],
            ActivePresetId = general.Id,
        };
    }
}
