using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using Blabla.Models;

namespace Blabla.Core;

public enum TranscriptionOutcome
{
    Success,
    Silence,
    Failure,
}

public sealed record TranscriptionResult(TranscriptionOutcome Outcome, string? Text, string? ErrorMessage)
{
    public static TranscriptionResult Success(string text) => new(TranscriptionOutcome.Success, text, null);
    public static TranscriptionResult Silence() => new(TranscriptionOutcome.Silence, null, "Geen spraak gedetecteerd.");
    public static TranscriptionResult Failure(string message) => new(TranscriptionOutcome.Failure, null, message);
}

public interface ITranscriptionClient
{
    Task<TranscriptionResult> TranscribeAsync(byte[] wav, Preset preset, string apiKey, CancellationToken cancellationToken = default);
}

/// <summary>
/// Direct call to OpenAI's transcription endpoint per contracts/openai-transcription.md.
/// No SDK (research R3); error messages are user-facing and never contain the API key.
/// </summary>
public sealed class TranscriptionClient : ITranscriptionClient
{
    public const string DefaultEndpoint = "https://api.openai.com/v1/audio/transcriptions";

    private readonly HttpClient _http;
    private readonly string _endpoint;

    public TranscriptionClient(HttpMessageHandler? handler = null, string endpoint = DefaultEndpoint)
    {
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.Timeout = TimeSpan.FromSeconds(60);
        _endpoint = endpoint;
    }

    public async Task<TranscriptionResult> TranscribeAsync(byte[] wav, Preset preset, string apiKey, CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
            request.Content = BuildContent(wav, preset);

            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                return TranscriptionResult.Failure(MapOpenAiError(response.StatusCode, preset.Model));

            var text = ParseText(body);
            return string.IsNullOrWhiteSpace(text)
                ? TranscriptionResult.Silence()
                : TranscriptionResult.Success(text.Trim());
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            return TranscriptionResult.Failure(
                "Geen verbinding met OpenAI — de opname is bewaard, probeer het opnieuw vanuit de geschiedenis.");
        }
    }

    private static MultipartFormDataContent BuildContent(byte[] wav, Preset preset)
    {
        var content = new MultipartFormDataContent();

        var file = new ByteArrayContent(wav);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("audio/wav");
        content.Add(file, "file", "recording.wav");

        content.Add(new StringContent(preset.Model), "model");
        content.Add(new StringContent("json"), "response_format");

        if (!string.IsNullOrWhiteSpace(preset.Prompt))
            content.Add(new StringContent(preset.Prompt), "prompt");
        if (!string.IsNullOrWhiteSpace(preset.Language))
            content.Add(new StringContent(preset.Language), "language");
        if (preset.Temperature is not null)
            content.Add(new StringContent(preset.Temperature.Value.ToString(CultureInfo.InvariantCulture)), "temperature");

        return content;
    }

    private static string? ParseText(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("text", out var text) ? text.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal static string MapOpenAiError(HttpStatusCode status, string model) => status switch
    {
        HttpStatusCode.Unauthorized =>
            "API-key niet geaccepteerd — controleer de key in de instellingen.",
        HttpStatusCode.TooManyRequests =>
            "OpenAI-limiet of quotum bereikt — probeer het zo dadelijk opnieuw.",
        HttpStatusCode.BadRequest or HttpStatusCode.NotFound =>
            $"Het verzoek is geweigerd (model '{model}') — controleer de preset-instellingen.",
        >= HttpStatusCode.InternalServerError =>
            "OpenAI-storing — de opname is bewaard, probeer het opnieuw vanuit de geschiedenis.",
        _ =>
            $"Onverwachte fout van OpenAI ({(int)status}) — de opname is bewaard, probeer het opnieuw vanuit de geschiedenis.",
    };
}
