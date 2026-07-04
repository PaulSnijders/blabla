using System.IO;
using Blabla.Core;
using Blabla.Models;
using Blabla.Services;

namespace Blabla.Tests;

public sealed class DictationControllerTests : IDisposable
{
    private sealed class FakeRecorder : IAudioRecorder
    {
        public event Action<TimeSpan>? Progress;
        public event Action? MaxDurationReached;
        public bool IsRecording { get; private set; }
        public int StartCalls, StopCalls, CancelCalls;
        public bool ThrowOnStart;

        public void Start()
        {
            if (ThrowOnStart) throw new InvalidOperationException("Geen microfoon gevonden.");
            StartCalls++;
            IsRecording = true;
        }

        public (byte[] Wav, TimeSpan Duration) Stop()
        {
            StopCalls++;
            IsRecording = false;
            return ([1, 2, 3], TimeSpan.FromSeconds(7));
        }

        public void Cancel()
        {
            CancelCalls++;
            IsRecording = false;
        }

        public void RaiseProgress(TimeSpan t) => Progress?.Invoke(t);
        public void RaiseMaxDuration() => MaxDurationReached?.Invoke();
    }

    private sealed class FakeClient : ITranscriptionClient
    {
        public TranscriptionResult Result = TranscriptionResult.Success("hallo wereld");
        public int Calls;
        public TaskCompletionSource<TranscriptionResult>? Pending;

        public async Task<TranscriptionResult> TranscribeAsync(byte[] wav, Preset preset, string apiKey, CancellationToken ct = default)
        {
            Calls++;
            return Pending is null ? Result : await Pending.Task;
        }
    }

    private sealed class FakeInserter : ITextInserter
    {
        public List<string> Inserted = [];
        public bool Throw;

        public void Insert(string text)
        {
            if (Throw) throw new InvalidOperationException("invoegen mislukt");
            Inserted.Add(text);
        }
    }

    private sealed class FakeView : IDictationView
    {
        public List<string> Log = [];
        public string? LastError;
        public void ShowRecording(TimeSpan elapsed) => Log.Add($"recording:{elapsed.TotalSeconds}");
        public void ShowPreset(string presetName) => Log.Add($"preset:{presetName}");
        public void ShowTranscribing() => Log.Add("transcribing");
        public void ShowConverting() => Log.Add("converting");
        public void ShowError(string message) { Log.Add("error"); LastError = message; }
        public void ShowInfo(string message) { Log.Add("info"); LastError = message; }
        public void HideView() => Log.Add("hide");
    }

    private sealed class FakeLlm : ILlmClient
    {
        public TranscriptionResult Result = TranscriptionResult.Success("herschreven tekst");
        public int Calls;
        public string? LastInput;

        public Task<TranscriptionResult> TransformAsync(string text, Preset preset, string apiKey, CancellationToken ct = default)
        {
            Calls++;
            LastInput = text;
            return Task.FromResult(Result);
        }
    }

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "BlablaTests", Guid.NewGuid().ToString("N"));
    private readonly FakeRecorder _recorder = new();
    private readonly FakeClient _client = new();
    private readonly FakeLlm _llm = new();
    private readonly FakeInserter _inserter = new();
    private readonly FakeView _view = new();
    private readonly HistoryStore _history;
    private readonly AppSettings _settings = AppSettings.CreateDefault();
    private string _apiKey = "sk-test";

    public DictationControllerTests()
    {
        _history = new HistoryStore(_dir);
    }

    private DictationController CreateController() => new(
        _recorder, _client, _llm, _inserter, _view, _history, () => _settings, () => _apiKey);

    private void EnableLlmOnActivePreset(string systemPrompt = "Maak er een nette e-mail van.")
    {
        var preset = _settings.ActivePreset;
        preset.LlmEnabled = true;
        preset.LlmSystemPrompt = systemPrompt;
    }

    [Fact]
    public async Task FullLoop_Success_InsertsText_AppendsHistory_UpdatesBuffer()
    {
        var controller = CreateController();

        controller.ToggleRecording();
        Assert.Equal(DictationState.Recording, controller.State);
        Assert.Equal(1, _recorder.StartCalls);

        controller.ToggleRecording();
        await Task.Delay(50); // let the async completion run

        Assert.Equal(DictationState.Idle, controller.State);
        Assert.Equal(["hallo wereld"], _inserter.Inserted);
        Assert.Equal("hallo wereld", controller.LastTranscription);
        Assert.Contains("hide", _view.Log);

        var entries = _history.Load();
        Assert.Single(entries);
        Assert.Equal(TranscriptionStatus.Succeeded, entries[0].Status);
        Assert.Equal("hallo wereld", entries[0].Text);
        Assert.Null(entries[0].AudioFile);
        Assert.Equal(7, entries[0].DurationSeconds);
    }

    [Fact]
    public void NoApiKey_ShowsErrorAndNeverStarts()
    {
        _apiKey = "";
        var controller = CreateController();

        controller.ToggleRecording();

        Assert.Equal(DictationState.Idle, controller.State);
        Assert.Equal(0, _recorder.StartCalls);
        Assert.Contains("API-key", _view.LastError);
    }

    [Fact]
    public void MicrophoneError_ShowsMessage_StaysIdle()
    {
        _recorder.ThrowOnStart = true;
        var controller = CreateController();

        controller.ToggleRecording();

        Assert.Equal(DictationState.Idle, controller.State);
        Assert.Contains("microfoon", _view.LastError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Cancel_DiscardsRecording_NothingSentOrStored()
    {
        var controller = CreateController();
        controller.ToggleRecording();

        controller.CancelRecording();

        Assert.Equal(DictationState.Idle, controller.State);
        Assert.Equal(1, _recorder.CancelCalls);
        Assert.Equal(0, _client.Calls);
        Assert.Empty(_history.Load());
        Assert.Contains("hide", _view.Log);
    }

    [Fact]
    public async Task TogglingWhileTranscribing_IsIgnoredWithFeedback()
    {
        _client.Pending = new TaskCompletionSource<TranscriptionResult>();
        var controller = CreateController();

        controller.ToggleRecording();          // start
        controller.ToggleRecording();          // stop → transcribing (pending)
        Assert.Equal(DictationState.Transcribing, controller.State);

        controller.ToggleRecording();          // pressed during transcription
        Assert.Equal(1, _recorder.StartCalls); // no second recording
        Assert.Contains("info", _view.Log);

        _client.Pending.SetResult(TranscriptionResult.Success("klaar"));
        await Task.Delay(50);
        Assert.Equal(DictationState.Idle, controller.State);
        Assert.Equal(1, _client.Calls);
    }

    [Fact]
    public async Task RapidDoublePress_EndsInCoherentState()
    {
        var controller = CreateController();

        controller.ToggleRecording();
        controller.ToggleRecording();
        controller.ToggleRecording(); // may land during Transcribing or a fresh Idle — never stuck
        await Task.Delay(80);

        Assert.True(controller.State is DictationState.Idle or DictationState.Recording);
        Assert.True(_recorder.StopCalls <= _recorder.StartCalls);
    }

    [Fact]
    public async Task Failure_KeepsAudioInHistory_ShowsError()
    {
        _client.Result = TranscriptionResult.Failure("OpenAI-storing — de opname is bewaard.");
        var controller = CreateController();

        controller.ToggleRecording();
        controller.ToggleRecording();
        await Task.Delay(50);

        Assert.Equal(DictationState.Idle, controller.State);
        Assert.Empty(_inserter.Inserted);
        Assert.Contains("storing", _view.LastError);

        var entries = _history.Load();
        Assert.Single(entries);
        Assert.Equal(TranscriptionStatus.Failed, entries[0].Status);
        Assert.NotNull(entries[0].AudioFile);
        Assert.Equal(new byte[] { 1, 2, 3 }, _history.ReadAudio(entries[0]));
    }

    [Fact]
    public async Task Silence_InformsUser_InsertsNothing()
    {
        _client.Result = TranscriptionResult.Silence();
        var controller = CreateController();

        controller.ToggleRecording();
        controller.ToggleRecording();
        await Task.Delay(50);

        Assert.Empty(_inserter.Inserted);
        Assert.Null(controller.LastTranscription);
        Assert.Contains("Geen spraak", _view.LastError);
    }

    [Fact]
    public async Task MaxDuration_AutoStopsAndTranscribes()
    {
        var controller = CreateController();
        controller.ToggleRecording();

        _recorder.RaiseMaxDuration();
        await Task.Delay(50);

        Assert.Equal(DictationState.Idle, controller.State);
        Assert.Equal(1, _recorder.StopCalls);
        Assert.Equal(1, _client.Calls);
        Assert.Equal(["hallo wereld"], _inserter.Inserted);
    }

    [Fact]
    public void Progress_UpdatesTimer_OnlyWhileRecording()
    {
        var controller = CreateController();
        _recorder.RaiseProgress(TimeSpan.FromSeconds(3));
        Assert.DoesNotContain("recording:3", _view.Log);

        controller.ToggleRecording();
        _recorder.RaiseProgress(TimeSpan.FromSeconds(3));
        Assert.Contains("recording:3", _view.Log);
    }

    [Fact]
    public async Task PasteLast_WithoutTranscription_InformsUser()
    {
        var controller = CreateController();
        await controller.PasteLastAsync();
        Assert.Empty(_inserter.Inserted);
        Assert.Contains("info", _view.Log);
    }

    [Fact]
    public async Task PasteLast_InsertsBufferedText()
    {
        var controller = CreateController();
        controller.LastTranscription = "eerder gedicteerd";
        await controller.PasteLastAsync();
        Assert.Equal(["eerder gedicteerd"], _inserter.Inserted);
    }

    [Fact]
    public async Task LlmDisabled_TranscriptionInsertedUnchanged_NoLlmCall()
    {
        var controller = CreateController();
        controller.ToggleRecording();
        controller.ToggleRecording();
        await Task.Delay(50);

        Assert.Equal(0, _llm.Calls);
        Assert.Equal(["hallo wereld"], _inserter.Inserted);
        Assert.Null(_history.Load()[0].RawText); // no LLM ran → no intermediate result
    }

    [Fact]
    public async Task LlmEnabled_TransformedTextIsInsertedAndStored()
    {
        EnableLlmOnActivePreset();
        var controller = CreateController();

        controller.ToggleRecording();
        controller.ToggleRecording();
        await Task.Delay(50);

        Assert.Equal(1, _llm.Calls);
        Assert.Equal("hallo wereld", _llm.LastInput);          // raw transcription is the LLM input
        Assert.Equal(["herschreven tekst"], _inserter.Inserted); // LLM output is what gets pasted
        Assert.Equal("herschreven tekst", controller.LastTranscription);
        var entry = _history.Load()[0];
        Assert.Equal("herschreven tekst", entry.Text);
        Assert.Equal("hallo wereld", entry.RawText); // intermediate result kept in history
        Assert.Contains("converting", _view.Log);
    }

    [Fact]
    public async Task LlmEnabled_ButEmptySystemPrompt_SkipsLlm()
    {
        EnableLlmOnActivePreset(systemPrompt: "   ");
        var controller = CreateController();

        controller.ToggleRecording();
        controller.ToggleRecording();
        await Task.Delay(50);

        Assert.Equal(0, _llm.Calls);
        Assert.Equal(["hallo wereld"], _inserter.Inserted);
    }

    [Fact]
    public async Task LlmFailure_KeepsAudioForRetry_InsertsNothing()
    {
        EnableLlmOnActivePreset();
        _llm.Result = TranscriptionResult.Failure("OpenAI-storing — de opname is bewaard.");
        var controller = CreateController();

        controller.ToggleRecording();
        controller.ToggleRecording();
        await Task.Delay(50);

        Assert.Empty(_inserter.Inserted);
        var entries = _history.Load();
        Assert.Equal(TranscriptionStatus.Failed, entries[0].Status);
        Assert.NotNull(entries[0].AudioFile);
        Assert.Equal("hallo wereld", entries[0].RawText); // raw transcription survives LLM failure
    }

    [Fact]
    public async Task TranscriptionFailure_NeverCallsLlm()
    {
        EnableLlmOnActivePreset();
        _client.Result = TranscriptionResult.Failure("Geen verbinding.");
        var controller = CreateController();

        controller.ToggleRecording();
        controller.ToggleRecording();
        await Task.Delay(50);

        Assert.Equal(0, _llm.Calls);
    }

    [Fact]
    public async Task Retry_RunsFullPipelineIncludingLlm()
    {
        EnableLlmOnActivePreset();
        var controller = CreateController();
        var failed = new TranscriptionEntry { Status = TranscriptionStatus.Failed, ErrorMessage = "x", PresetName = "Algemeen" };
        _history.Append(failed, [9, 9]);

        var ok = await controller.RetryAsync(_history.Load()[0], [9, 9]);

        Assert.True(ok);
        Assert.Equal(1, _llm.Calls);
        var retried = _history.Load()[0];
        Assert.Equal("herschreven tekst", retried.Text);
        Assert.Equal("hallo wereld", retried.RawText);
    }

    [Fact]
    public void CyclePreset_OnlyWorksWhileRecording()
    {
        var controller = CreateController();
        var before = _settings.ActivePresetId;

        controller.CyclePreset(+1); // idle: ignored
        Assert.Equal(before, _settings.ActivePresetId);

        controller.ToggleRecording();
        controller.CyclePreset(+1);
        Assert.NotEqual(before, _settings.ActivePresetId);
    }

    [Fact]
    public void CyclePreset_CyclesForwardBackwardAndWrapsAround()
    {
        var controller = CreateController();
        controller.ToggleRecording();
        Assert.Equal("Algemeen", _settings.ActivePreset.Name);

        controller.CyclePreset(+1);
        Assert.Equal("Programmeren", _settings.ActivePreset.Name);
        controller.CyclePreset(+1);
        Assert.Equal("Wozzol", _settings.ActivePreset.Name);
        controller.CyclePreset(+1); // wraps
        Assert.Equal("Algemeen", _settings.ActivePreset.Name);
        controller.CyclePreset(-1); // wraps backwards
        Assert.Equal("Wozzol", _settings.ActivePreset.Name);

        Assert.Contains("preset:Wozzol", _view.Log);
    }

    [Fact]
    public async Task CyclePreset_MidRecording_SubmitUsesNewPreset()
    {
        var controller = CreateController();
        Preset? changed = null;
        controller.ActivePresetChanged += preset => changed = preset;

        controller.ToggleRecording();
        controller.CyclePreset(+1); // → Programmeren, recording keeps running
        Assert.Equal(DictationState.Recording, controller.State);
        Assert.Equal("Programmeren", changed!.Name);

        controller.ToggleRecording();
        await Task.Delay(50);

        Assert.Equal("Programmeren", _history.Load()[0].PresetName);
        Assert.Equal(["hallo wereld"], _inserter.Inserted);
    }

    [Fact]
    public void RecordingStart_ShowsActivePresetName()
    {
        var controller = CreateController();
        controller.ToggleRecording();
        Assert.Contains("preset:Algemeen", _view.Log);
    }

    [Fact]
    public async Task InsertFailure_ShowsError_TextStillInBufferAndHistory()
    {
        _inserter.Throw = true;
        var controller = CreateController();

        controller.ToggleRecording();
        controller.ToggleRecording();
        await Task.Delay(50);

        Assert.Contains("invoegen", _view.LastError);
        Assert.Equal("hallo wereld", controller.LastTranscription);
        Assert.Equal(TranscriptionStatus.Succeeded, _history.Load()[0].Status);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }
}
