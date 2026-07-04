using Blabla.Models;
using Blabla.Services;

namespace Blabla.Core;

public enum DictationState
{
    Idle,
    Recording,
    Transcribing,
}

/// <summary>View abstraction over the recording pop-up, so the state machine is unit-testable.</summary>
public interface IDictationView
{
    void ShowRecording(TimeSpan elapsed);
    void ShowPreset(string presetName);
    void ShowTranscribing();
    void ShowConverting();
    void ShowError(string message);
    void ShowInfo(string message);
    void HideView();
}

/// <summary>
/// The dictation state machine (research R10): Idle → Recording → Transcribing → Idle.
/// All entry points run on the UI thread (hotkeys arrive via the window message loop);
/// recorder events are marshalled through the injected dispatcher.
/// </summary>
public sealed class DictationController
{
    private readonly IAudioRecorder _recorder;
    private readonly ITranscriptionClient _client;
    private readonly ILlmClient _llm;
    private readonly ITextInserter _inserter;
    private readonly IDictationView _view;
    private readonly HistoryStore _history;
    private readonly Func<AppSettings> _getSettings;
    private readonly Func<string> _getApiKey;
    private readonly Action<Action> _dispatch;

    public DictationController(
        IAudioRecorder recorder,
        ITranscriptionClient client,
        ILlmClient llm,
        ITextInserter inserter,
        IDictationView view,
        HistoryStore history,
        Func<AppSettings> getSettings,
        Func<string> getApiKey,
        Action<Action>? dispatch = null)
    {
        _recorder = recorder;
        _client = client;
        _llm = llm;
        _inserter = inserter;
        _view = view;
        _history = history;
        _getSettings = getSettings;
        _getApiKey = getApiKey;
        _dispatch = dispatch ?? (action => action());

        _recorder.Progress += elapsed => _dispatch(() =>
        {
            if (State == DictationState.Recording) _view.ShowRecording(elapsed);
        });
        _recorder.MaxDurationReached += () => _dispatch(() =>
        {
            if (State == DictationState.Recording) _ = StopAndTranscribeAsync();
        });
    }

    public DictationState State { get; private set; } = DictationState.Idle;

    /// <summary>Most recent successful transcription — the internal clipboard behind the paste-last hotkey (FR-004a).</summary>
    public string? LastTranscription { get; set; }

    public event Action<DictationState>? StateChanged;

    /// <summary>Raised when recording is attempted without a configured API key (Story 2 sc. 3).</summary>
    public event Action? ApiKeyMissing;

    /// <summary>Raised when Ctrl+↑/↓ switched the active preset during recording; the app persists it.</summary>
    public event Action<Preset>? ActivePresetChanged;

    /// <summary>
    /// Ctrl+↑/↓ during recording: cycle the active preset without interrupting the capture.
    /// The preset is only consumed at submit time, so the running recording simply continues
    /// and is transcribed with the newly chosen preset.
    /// </summary>
    public void CyclePreset(int direction)
    {
        if (State != DictationState.Recording) return;

        var settings = _getSettings();
        var presets = settings.Presets;
        if (presets.Count == 0) return;

        var index = presets.FindIndex(p => p.Id == settings.ActivePresetId);
        if (index < 0) index = 0;
        var next = presets[(index + direction + presets.Count) % presets.Count];

        settings.ActivePresetId = next.Id;
        _view.ShowPreset(next.Name);
        ActivePresetChanged?.Invoke(next);
    }

    /// <summary>The record hotkey: toggles per state; never leaves the app in a limbo state.</summary>
    public void ToggleRecording()
    {
        switch (State)
        {
            case DictationState.Idle:
                StartRecording();
                break;
            case DictationState.Recording:
                _ = StopAndTranscribeAsync();
                break;
            case DictationState.Transcribing:
                // No queuing (research R10): block with feedback, results can never mix.
                _view.ShowInfo("Nog bezig met de vorige transcriptie…");
                break;
        }
    }

    /// <summary>Esc or the pop-up's ✕: discard the recording, send nothing (FR-005).</summary>
    public void CancelRecording()
    {
        if (State != DictationState.Recording) return;
        _recorder.Cancel();
        SetState(DictationState.Idle);
        _view.HideView();
    }

    /// <summary>The paste-last hotkey: re-insert the newest transcription at the cursor (FR-004a).</summary>
    public async Task PasteLastAsync()
    {
        if (LastTranscription is null)
        {
            _view.ShowInfo("Nog geen transcriptie om te plakken.");
            return;
        }
        await TryInsertAsync(LastTranscription);
    }

    private void StartRecording()
    {
        if (_getApiKey().Length == 0)
        {
            _view.ShowError("Geen API-key ingesteld. Vul je OpenAI API-key in bij de instellingen.");
            ApiKeyMissing?.Invoke();
            return;
        }

        try
        {
            _recorder.Start();
        }
        catch (InvalidOperationException e)
        {
            _view.ShowError(e.Message);
            return;
        }

        SetState(DictationState.Recording);
        _view.ShowRecording(TimeSpan.Zero);
        _view.ShowPreset(_getSettings().ActivePreset.Name);
    }

    private async Task StopAndTranscribeAsync()
    {
        if (State != DictationState.Recording) return;
        SetState(DictationState.Transcribing);
        _view.ShowTranscribing();

        byte[] wav;
        TimeSpan duration;
        try
        {
            (wav, duration) = _recorder.Stop();
        }
        catch (InvalidOperationException e)
        {
            SetState(DictationState.Idle);
            _view.ShowError(e.Message);
            return;
        }

        var preset = _getSettings().ActivePreset;
        var result = await _client.TranscribeAsync(wav, preset, _getApiKey());

        // Optional post-processing: the transcription becomes the LLM's input (per preset).
        // The raw transcription is kept as the entry's intermediate result — also when the
        // LLM step fails, so the text is never lost.
        string? rawText = null;
        if (result.Outcome == TranscriptionOutcome.Success && preset.UsesLlm)
        {
            rawText = result.Text;
            _view.ShowConverting();
            result = await _llm.TransformAsync(result.Text!, preset, _getApiKey());
        }

        var entry = new TranscriptionEntry
        {
            PresetName = preset.Name,
            DurationSeconds = (int)Math.Round(duration.TotalSeconds),
            RawText = rawText,
        };

        if (result.Outcome == TranscriptionOutcome.Success)
        {
            entry.Status = TranscriptionStatus.Succeeded;
            entry.Text = result.Text;
            _history.Append(entry); // success: audio is discarded (data-model)
            LastTranscription = result.Text;
            SetState(DictationState.Idle);
            if (await TryInsertAsync(result.Text!))
                _view.HideView();
        }
        else
        {
            // Silence and failure both keep the audio for retry (FR-006, FR-020).
            entry.Status = TranscriptionStatus.Failed;
            entry.ErrorMessage = result.ErrorMessage;
            _history.Append(entry, wav);
            SetState(DictationState.Idle);
            _view.ShowError(result.ErrorMessage!);
        }
    }

    /// <summary>
    /// Re-submits a failed entry's stored audio with the current active preset (FR-020).
    /// On success the entry becomes Succeeded and its audio is deleted by the history store.
    /// </summary>
    public async Task<bool> RetryAsync(TranscriptionEntry entry, byte[] audio)
    {
        var preset = _getSettings().ActivePreset;
        var result = await _client.TranscribeAsync(audio, preset, _getApiKey());

        // Retry runs the same pipeline as a fresh dictation, including LLM post-processing.
        string? rawText = null;
        if (result.Outcome == TranscriptionOutcome.Success && preset.UsesLlm)
        {
            rawText = result.Text;
            result = await _llm.TransformAsync(result.Text!, preset, _getApiKey());
        }
        entry.RawText = rawText;

        if (result.Outcome == TranscriptionOutcome.Success)
        {
            entry.Status = TranscriptionStatus.Succeeded;
            entry.Text = result.Text;
            entry.ErrorMessage = null;
            _history.Update(entry);
            LastTranscription = result.Text;
            return true;
        }

        entry.ErrorMessage = result.ErrorMessage;
        _history.Update(entry);
        return false;
    }

    /// <summary>
    /// Inserts off the UI thread: the inserter first waits for the hotkey's modifier keys
    /// to be physically released (otherwise held Ctrl+Alt acts as AltGr and apps receive
    /// shortcuts/alternate glyphs instead of the literal text), and that wait must never
    /// block the UI.
    /// </summary>
    private async Task<bool> TryInsertAsync(string text)
    {
        try
        {
            await Task.Run(() => _inserter.Insert(text));
            return true;
        }
        catch (InvalidOperationException e)
        {
            _view.ShowError(e.Message);
            return false;
        }
    }

    private void SetState(DictationState state)
    {
        if (State == state) return;
        State = state;
        StateChanged?.Invoke(state);
    }
}
