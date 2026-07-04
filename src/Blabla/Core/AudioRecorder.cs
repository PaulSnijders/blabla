using System.Diagnostics;
using System.IO;
using NAudio.Utils;
using NAudio.Wave;

namespace Blabla.Core;

public interface IAudioRecorder
{
    /// <summary>Raised (on the capture thread) as audio arrives, with the elapsed time.</summary>
    event Action<TimeSpan>? Progress;

    /// <summary>Raised (on the capture thread) once when the 5-minute maximum is hit (FR-005a).</summary>
    event Action? MaxDurationReached;

    bool IsRecording { get; }
    void Start();
    (byte[] Wav, TimeSpan Duration) Stop();
    void Cancel();
}

/// <summary>
/// Microphone capture per research R2: NAudio WaveInEvent, 16 kHz mono 16-bit PCM WAV in
/// memory (~1.9 MB/min; a 5-minute maximum stays well under the API's upload limit).
/// </summary>
public sealed class AudioRecorder : IAudioRecorder, IDisposable
{
    public static readonly TimeSpan MaxDuration = TimeSpan.FromMinutes(5);

    private static readonly WaveFormat Format = new(rate: 16000, bits: 16, channels: 1);

    private WaveInEvent? _waveIn;
    private MemoryStream? _buffer;
    private WaveFileWriter? _writer;
    private readonly Stopwatch _elapsed = new();
    private bool _maxReachedRaised;

    public event Action<TimeSpan>? Progress;
    public event Action? MaxDurationReached;

    public bool IsRecording { get; private set; }

    public void Start()
    {
        if (IsRecording) return;

        if (WaveInEvent.DeviceCount == 0)
            throw new InvalidOperationException(
                "Geen microfoon gevonden. Sluit een microfoon aan of controleer de Windows-microfooninstellingen.");

        _buffer = new MemoryStream();
        _writer = new WaveFileWriter(new IgnoreDisposeStream(_buffer), Format);
        _waveIn = new WaveInEvent { WaveFormat = Format, BufferMilliseconds = 100 };
        _waveIn.DataAvailable += OnDataAvailable;
        _maxReachedRaised = false;

        try
        {
            _waveIn.StartRecording();
        }
        catch (Exception e) when (e is NAudio.MmException or InvalidOperationException)
        {
            CleanUp();
            throw new InvalidOperationException(
                "De microfoon kon niet worden gestart. Controleer of een andere app hem niet blokkeert " +
                "en of Blabla microfoontoegang heeft in de Windows-privacyinstellingen.", e);
        }

        _elapsed.Restart();
        IsRecording = true;
    }

    public (byte[] Wav, TimeSpan Duration) Stop()
    {
        if (!IsRecording) throw new InvalidOperationException("Not recording.");

        var duration = _elapsed.Elapsed;
        _waveIn!.StopRecording();
        _writer!.Dispose(); // finalizes the WAV header in the seekable MemoryStream
        var wav = _buffer!.ToArray();
        CleanUp();
        return (wav, duration);
    }

    public void Cancel()
    {
        if (!IsRecording) return;
        _waveIn!.StopRecording();
        CleanUp();
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        var writer = _writer;
        if (writer is null || !IsRecording) return;

        writer.Write(e.Buffer, 0, e.BytesRecorded);
        var elapsed = _elapsed.Elapsed;
        Progress?.Invoke(elapsed);

        if (!_maxReachedRaised && elapsed >= MaxDuration)
        {
            _maxReachedRaised = true;
            MaxDurationReached?.Invoke();
        }
    }

    private void CleanUp()
    {
        IsRecording = false;
        _elapsed.Reset();
        if (_waveIn is not null)
        {
            _waveIn.DataAvailable -= OnDataAvailable;
            _waveIn.Dispose();
            _waveIn = null;
        }
        _writer?.Dispose();
        _writer = null;
        _buffer?.Dispose();
        _buffer = null;
    }

    public void Dispose() => CleanUp();
}
