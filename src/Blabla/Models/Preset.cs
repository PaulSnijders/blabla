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
    };
}
