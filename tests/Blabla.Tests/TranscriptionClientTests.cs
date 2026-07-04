using System.Net;
using System.Net.Http;
using Blabla.Core;
using Blabla.Models;

namespace Blabla.Tests;

public sealed class TranscriptionClientTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest;
        public string? LastBody;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return await respond(request);
        }
    }

    private static StubHandler Ok(string json) => new(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = new StringContent(json),
    }));

    private static StubHandler Status(HttpStatusCode status) => new(_ => Task.FromResult(new HttpResponseMessage(status)
    {
        Content = new StringContent("{}"),
    }));

    private static Preset FullPreset() => new()
    {
        Name = "Programmeren",
        Prompt = "Nederlandse zinnen met Engelse programmeertermen.",
        Language = "nl",
        Model = "gpt-4o-transcribe",
        Temperature = 0.2m,
    };

    private static readonly byte[] Wav = [82, 73, 70, 70]; // "RIFF"

    [Fact]
    public async Task Request_ContainsAllPartsForFullPreset()
    {
        var handler = Ok("""{"text":"hallo"}""");
        var client = new TranscriptionClient(handler);

        await client.TranscribeAsync(Wav, FullPreset(), "sk-key");

        Assert.Equal("Bearer", handler.LastRequest!.Headers.Authorization!.Scheme);
        Assert.Equal("sk-key", handler.LastRequest.Headers.Authorization.Parameter);
        var body = handler.LastBody!;
        Assert.Contains("name=file", body);
        Assert.Contains("recording.wav", body);
        Assert.Contains("gpt-4o-transcribe", body);
        Assert.Contains("Nederlandse zinnen met Engelse programmeertermen.", body);
        Assert.Contains("name=language", body);
        Assert.Contains("0.2", body);
        Assert.Contains("name=response_format", body);
    }

    [Fact]
    public async Task Request_OmitsEmptyPromptNullLanguageAndTemperature()
    {
        var handler = Ok("""{"text":"hallo"}""");
        var client = new TranscriptionClient(handler);
        var preset = new Preset { Name = "Kaal", Prompt = "", Language = null, Temperature = null };

        await client.TranscribeAsync(Wav, preset, "sk-key");

        var body = handler.LastBody!;
        Assert.DoesNotContain("name=prompt", body);
        Assert.DoesNotContain("name=language", body);
        Assert.DoesNotContain("name=temperature", body);
    }

    [Fact]
    public async Task Success_ReturnsTrimmedText()
    {
        var client = new TranscriptionClient(Ok("""{"text":"  Dit is een test.  "}"""));
        var result = await client.TranscribeAsync(Wav, FullPreset(), "sk-key");

        Assert.Equal(TranscriptionOutcome.Success, result.Outcome);
        Assert.Equal("Dit is een test.", result.Text);
    }

    [Fact]
    public async Task EmptyText_IsSilence()
    {
        var client = new TranscriptionClient(Ok("""{"text":"   "}"""));
        var result = await client.TranscribeAsync(Wav, FullPreset(), "sk-key");

        Assert.Equal(TranscriptionOutcome.Silence, result.Outcome);
        Assert.NotNull(result.ErrorMessage);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "API-key")]
    [InlineData(HttpStatusCode.TooManyRequests, "limiet")]
    [InlineData(HttpStatusCode.BadRequest, "gpt-4o-transcribe")]
    [InlineData(HttpStatusCode.NotFound, "preset")]
    [InlineData(HttpStatusCode.InternalServerError, "geschiedenis")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "geschiedenis")]
    public async Task ErrorStatus_MapsToContractMessage(HttpStatusCode status, string expectedFragment)
    {
        var client = new TranscriptionClient(Status(status));
        var result = await client.TranscribeAsync(Wav, FullPreset(), "sk-key");

        Assert.Equal(TranscriptionOutcome.Failure, result.Outcome);
        Assert.Contains(expectedFragment, result.ErrorMessage);
    }

    [Fact]
    public async Task ErrorMessage_NeverContainsApiKey()
    {
        foreach (var status in new[] { HttpStatusCode.Unauthorized, HttpStatusCode.BadRequest, HttpStatusCode.InternalServerError })
        {
            var client = new TranscriptionClient(Status(status));
            var result = await client.TranscribeAsync(Wav, FullPreset(), "sk-supergeheim");
            Assert.DoesNotContain("sk-supergeheim", result.ErrorMessage);
        }
    }

    [Fact]
    public async Task NetworkFailure_MapsToConnectionMessage()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("dns failure"));
        var client = new TranscriptionClient(handler);
        var result = await client.TranscribeAsync(Wav, FullPreset(), "sk-key");

        Assert.Equal(TranscriptionOutcome.Failure, result.Outcome);
        Assert.Contains("Geen verbinding", result.ErrorMessage);
    }
}
