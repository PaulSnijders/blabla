using System.Net;
using System.Net.Http;
using Blabla.Core;
using Blabla.Models;

namespace Blabla.Tests;

public sealed class LlmClientTests
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

    private static StubHandler Ok(string answer)
    {
        var json = """{"choices":[{"message":{"role":"assistant","content":"""
                   + System.Text.Json.JsonSerializer.Serialize(answer) + "}}]}";
        return new StubHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json),
        }));
    }

    private static Preset LlmPreset(decimal? temperature = null) => new()
    {
        Name = "E-mail",
        LlmEnabled = true,
        LlmSystemPrompt = "Maak er een nette e-mail van en corrigeer spelfouten.",
        LlmModel = "gpt-5-mini",
        LlmTemperature = temperature,
    };

    [Fact]
    public async Task Request_ContainsModelSystemPromptAndUserText()
    {
        var handler = Ok("Beste collega, ...");
        var client = new LlmClient(handler);

        await client.TransformAsync("ruwe transcriptie", LlmPreset(), "sk-key");

        Assert.Equal("sk-key", handler.LastRequest!.Headers.Authorization!.Parameter);
        var body = handler.LastBody!;
        Assert.Contains("gpt-5-mini", body);
        Assert.Contains("Maak er een nette e-mail van", body);
        Assert.Contains("ruwe transcriptie", body);
        Assert.Contains("\"system\"", body);
        Assert.Contains("\"user\"", body);
        Assert.DoesNotContain("temperature", body);
    }

    [Fact]
    public async Task Request_IncludesTemperatureWhenSet()
    {
        var handler = Ok("x");
        var client = new LlmClient(handler);

        await client.TransformAsync("tekst", LlmPreset(temperature: 0.4m), "sk-key");

        Assert.Contains("\"temperature\":0.4", handler.LastBody);
    }

    [Fact]
    public async Task Success_ReturnsTrimmedAnswer()
    {
        var client = new LlmClient(Ok("  Herschreven tekst.  "));
        var result = await client.TransformAsync("input", LlmPreset(), "sk-key");

        Assert.Equal(TranscriptionOutcome.Success, result.Outcome);
        Assert.Equal("Herschreven tekst.", result.Text);
    }

    [Fact]
    public async Task EmptyAnswer_IsFailure()
    {
        var client = new LlmClient(Ok("   "));
        var result = await client.TransformAsync("input", LlmPreset(), "sk-key");

        Assert.Equal(TranscriptionOutcome.Failure, result.Outcome);
        Assert.Contains("leeg antwoord", result.ErrorMessage);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "API-key")]
    [InlineData(HttpStatusCode.TooManyRequests, "limiet")]
    [InlineData(HttpStatusCode.BadRequest, "gpt-5-mini")]
    [InlineData(HttpStatusCode.InternalServerError, "geschiedenis")]
    public async Task ErrorStatus_MapsToUserMessage(HttpStatusCode status, string expectedFragment)
    {
        var handler = new StubHandler(_ => Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent("{}"),
        }));
        var client = new LlmClient(handler);

        var result = await client.TransformAsync("input", LlmPreset(), "sk-geheim");

        Assert.Equal(TranscriptionOutcome.Failure, result.Outcome);
        Assert.Contains(expectedFragment, result.ErrorMessage);
        Assert.DoesNotContain("sk-geheim", result.ErrorMessage);
    }

    [Fact]
    public async Task NetworkFailure_MapsToConnectionMessage()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("offline"));
        var client = new LlmClient(handler);

        var result = await client.TransformAsync("input", LlmPreset(), "sk-key");

        Assert.Equal(TranscriptionOutcome.Failure, result.Outcome);
        Assert.Contains("Geen verbinding", result.ErrorMessage);
    }
}
