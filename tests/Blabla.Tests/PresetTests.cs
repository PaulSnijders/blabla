using System.IO;
using Blabla.Core;
using Blabla.Models;
using Blabla.Services;

namespace Blabla.Tests;

public sealed class PresetTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "BlablaTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void DefaultSettings_SeedThreePresets_AlgemeenActive()
    {
        var settings = AppSettings.CreateDefault();

        Assert.Equal(3, settings.Presets.Count);
        Assert.Equal(["Algemeen", "Programmeren", "Wozzol"], settings.Presets.Select(p => p.Name).ToArray());
        Assert.Equal("Algemeen", settings.ActivePreset.Name);
        Assert.All(settings.Presets, p => Assert.Equal("gpt-4o-transcribe", p.Model));
        Assert.Null(settings.Presets.Single(p => p.Name == "Wozzol").Language); // multilingual: auto-detect
    }

    [Fact]
    public void ActivePreset_DanglingId_FallsBackToFirst()
    {
        var settings = AppSettings.CreateDefault();
        settings.ActivePresetId = Guid.NewGuid();

        Assert.Equal(settings.Presets[0], settings.ActivePreset);
    }

    [Fact]
    public void Clone_IsIndependentCopy_WithSameId()
    {
        var original = AppSettings.CreateDefault().Presets[0];
        var clone = original.Clone();

        clone.Name = "Anders";
        clone.Prompt = "Andere prompt";

        Assert.Equal(original.Id, clone.Id);
        Assert.NotEqual(original.Name, clone.Name);
        Assert.NotEqual(original.Prompt, clone.Prompt);
    }

    [Fact]
    public async Task Controller_UsesActivePresetForTranscription()
    {
        Preset? usedPreset = null;
        var client = new CapturingClient(preset => usedPreset = preset);
        var settings = AppSettings.CreateDefault();
        var programmeren = settings.Presets.Single(p => p.Name == "Programmeren");
        settings.ActivePresetId = programmeren.Id;

        var controller = new DictationController(
            new StubRecorder(), client, new StubLlm(), new StubInserter(), new StubView(),
            new HistoryStore(_dir), () => settings, () => "sk-test");

        controller.ToggleRecording();
        controller.ToggleRecording();
        await Task.Delay(50);

        Assert.NotNull(usedPreset);
        Assert.Equal("Programmeren", usedPreset.Name);
        Assert.Equal("Programmeren", new HistoryStore(_dir).Load()[0].PresetName);
    }

    [Fact]
    public async Task SwitchingActivePreset_AffectsNextDictation()
    {
        var used = new List<string>();
        var client = new CapturingClient(preset => used.Add(preset.Name));
        var settings = AppSettings.CreateDefault();

        var controller = new DictationController(
            new StubRecorder(), client, new StubLlm(), new StubInserter(), new StubView(),
            new HistoryStore(_dir), () => settings, () => "sk-test");

        controller.ToggleRecording();
        controller.ToggleRecording();
        await Task.Delay(50);

        settings.ActivePresetId = settings.Presets.Single(p => p.Name == "Wozzol").Id;
        controller.ToggleRecording();
        controller.ToggleRecording();
        await Task.Delay(50);

        Assert.Equal(["Algemeen", "Wozzol"], used);
    }

    private sealed class CapturingClient(Action<Preset> capture) : ITranscriptionClient
    {
        public Task<TranscriptionResult> TranscribeAsync(byte[] wav, Preset preset, string apiKey, CancellationToken ct = default)
        {
            capture(preset);
            return Task.FromResult(TranscriptionResult.Success("ok"));
        }
    }

    private sealed class StubRecorder : IAudioRecorder
    {
        public event Action<TimeSpan>? Progress { add { } remove { } }
        public event Action? MaxDurationReached { add { } remove { } }
        public bool IsRecording { get; private set; }
        public void Start() => IsRecording = true;
        public (byte[] Wav, TimeSpan Duration) Stop() { IsRecording = false; return ([1], TimeSpan.FromSeconds(1)); }
        public void Cancel() => IsRecording = false;
    }

    private sealed class StubInserter : ITextInserter
    {
        public void Insert(string text) { }
    }

    private sealed class StubLlm : ILlmClient
    {
        public Task<TranscriptionResult> TransformAsync(string text, Preset preset, string apiKey, CancellationToken ct = default)
            => Task.FromResult(TranscriptionResult.Success(text));
    }

    private sealed class StubView : IDictationView
    {
        public void ShowRecording(TimeSpan elapsed) { }
        public void ShowPreset(string presetName) { }
        public void ShowTranscribing() { }
        public void ShowConverting() { }
        public void ShowError(string message) { }
        public void ShowInfo(string message) { }
        public void HideView() { }
    }

    [Fact]
    public void ApplyReplacements_CaseInsensitive_WholeWordsOnly()
    {
        var preset = new Preset { Replacements = "cloud=Claude\r\nzonder scheidingsteken\n  c sharp = C#  " };

        Assert.Equal("Ik gebruik Claude Code op cloudflare met C#.",
            preset.ApplyReplacements("Ik gebruik Cloud Code op cloudflare met C Sharp."));
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }
}
