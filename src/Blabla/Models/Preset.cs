using System.Text.RegularExpressions;

namespace Blabla.Models;

/// <summary>A named bundle of transcription settings (spec: Key Entities / Preset).</summary>
public sealed class Preset
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";

    /// <summary>Context prompt sent with the audio; may be empty.</summary>
    public string Prompt { get; set; } = "";

    /// <summary>ISO-639-1 code (e.g. "nl"), or null for automatic detection.</summary>
    public string? Language { get; set; }

    public string Model { get; set; } = "gpt-4o-transcribe";

    /// <summary>0–1, or null for the service default.</summary>
    public decimal? Temperature { get; set; }

    // Optional LLM post-processing: the transcription is sent as user message to a chat
    // model with this system prompt, and the model's answer is what gets inserted.
    public bool LlmEnabled { get; set; }
    public string LlmSystemPrompt { get; set; } = "";
    public string LlmModel { get; set; } = "gpt-5-mini";

    /// <summary>Chat-model temperature, or null for the service default (some models only accept the default).</summary>
    public decimal? LlmTemperature { get; set; }

    /// <summary>Find/replace fixes, one "find=replace" per line, applied to every transcription.</summary>
    public string Replacements { get; set; } = "";

    [System.Text.Json.Serialization.JsonIgnore]
    public bool UsesLlm => LlmEnabled && !string.IsNullOrWhiteSpace(LlmSystemPrompt);

    public Preset Clone() => new()
    {
        Id = Id,
        Name = Name,
        Prompt = Prompt,
        Language = Language,
        Model = Model,
        Temperature = Temperature,
        LlmEnabled = LlmEnabled,
        LlmSystemPrompt = LlmSystemPrompt,
        LlmModel = LlmModel,
        LlmTemperature = LlmTemperature,
        Replacements = Replacements,
    };

    /// <summary>
    /// Applies the "find=replace" lines: case-insensitive, and whole words only
    /// (so "cloud=Claude" leaves "cloudflare" alone). Lines without '=' are ignored.
    /// </summary>
    public string ApplyReplacements(string text)
    {
        foreach (var line in Replacements.Split('\n'))
        {
            var separator = line.IndexOf('=');
            if (separator < 0) continue;
            var find = line[..separator].Trim();
            var replace = line[(separator + 1)..].Trim();
            if (find.Length == 0) continue;

            var pattern = Regex.Escape(find);
            if (char.IsLetterOrDigit(find[0])) pattern = @"\b" + pattern;
            if (char.IsLetterOrDigit(find[^1])) pattern += @"\b";
            text = Regex.Replace(text, pattern, replace.Replace("$", "$$"), RegexOptions.IgnoreCase);
        }
        return text;
    }
}
