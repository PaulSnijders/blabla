using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Blabla.Models;

namespace Blabla.Core;

public interface ILlmClient
{
    /// <summary>Rewrites the transcription via the preset's chat model; the answer replaces the text.</summary>
    Task<TranscriptionResult> TransformAsync(string text, Preset preset, string apiKey, CancellationToken cancellationToken = default);
}

/// <summary>
/// Optional LLM post-processing step (research R11): one call to OpenAI chat completions
/// with the preset's system prompt and the raw transcription as user message.
/// </summary>
public sealed class LlmClient : ILlmClient
{
    public const string DefaultEndpoint = "https://api.openai.com/v1/chat/completions";

    private readonly HttpClient _http;
    private readonly string _endpoint;

    public LlmClient(HttpMessageHandler? handler = null, string endpoint = DefaultEndpoint)
    {
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.Timeout = TimeSpan.FromSeconds(60);
        _endpoint = endpoint;
    }

    public async Task<TranscriptionResult> TransformAsync(string text, Preset preset, string apiKey, CancellationToken cancellationToken = default)
    {
        try
        {
            var payload = new Dictionary<string, object>
            {
                ["model"] = preset.LlmModel,
                ["messages"] = new object[]
                {
                    new { role = "system", content = preset.LlmSystemPrompt },
                    new { role = "user", content = text },
                },
            };
            if (preset.LlmTemperature is not null)
                payload["temperature"] = preset.LlmTemperature.Value;

            using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                return TranscriptionResult.Failure(TranscriptionClient.MapOpenAiError(response.StatusCode, preset.LlmModel));

            var answer = ParseAnswer(body);
            return string.IsNullOrWhiteSpace(answer)
                ? TranscriptionResult.Failure("Het taalmodel gaf een leeg antwoord — de opname is bewaard, probeer het opnieuw vanuit de geschiedenis.")
                : TranscriptionResult.Success(answer.Trim());
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            return TranscriptionResult.Failure(
                "Geen verbinding met OpenAI — de opname is bewaard, probeer het opnieuw vanuit de geschiedenis.");
        }
    }

    private static string? ParseAnswer(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or IndexOutOfRangeException or InvalidOperationException)
        {
            return null;
        }
    }
}
