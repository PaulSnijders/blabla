namespace Blabla.Models;

public enum TranscriptionStatus
{
    Succeeded,
    Failed,
}

/// <summary>A single dictation result (spec: Key Entities / Transcription entry).</summary>
public sealed class TranscriptionEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;

    /// <summary>Final text (after optional LLM rewrite); null while the entry is Failed.</summary>
    public string? Text { get; set; }

    /// <summary>The intermediate raw transcription when LLM post-processing ran; null otherwise.</summary>
    public string? RawText { get; set; }

    /// <summary>Denormalized preset name, so deleting a preset never breaks history.</summary>
    public string PresetName { get; set; } = "";

    public TranscriptionStatus Status { get; set; }

    /// <summary>User-facing message when Failed; never contains the API key.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>WAV file name under the audio directory; non-null iff Status == Failed.</summary>
    public string? AudioFile { get; set; }

    public int DurationSeconds { get; set; }
}
