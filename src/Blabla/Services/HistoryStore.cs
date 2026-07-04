using System.IO;
using System.Text;
using System.Text.Json;
using Blabla.Models;

namespace Blabla.Services;

/// <summary>
/// Persists transcription history per contracts/local-storage.md: history.json capped at a
/// limit (newest first), failed-recording WAVs under audio\, orphan cleanup at startup.
/// </summary>
public sealed class HistoryStore
{
    private sealed class HistoryFile
    {
        public int Version { get; set; } = 1;
        public List<TranscriptionEntry> Entries { get; set; } = [];
    }

    private readonly string _directory;
    private readonly string _filePath;
    private readonly string _audioDirectory;

    public HistoryStore(string? baseDirectory = null)
    {
        _directory = baseDirectory
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Blabla");
        _filePath = Path.Combine(_directory, "history.json");
        _audioDirectory = Path.Combine(_directory, "audio");
    }

    /// <summary>Maximum number of entries kept; set from AppSettings.HistoryLimit.</summary>
    public int Limit { get; set; } = 100;

    public List<TranscriptionEntry> Load()
    {
        if (!File.Exists(_filePath)) return [];
        try
        {
            var file = JsonSerializer.Deserialize<HistoryFile>(File.ReadAllText(_filePath), SettingsStore.JsonOptions);
            return file?.Entries ?? [];
        }
        catch (JsonException)
        {
            File.Move(_filePath, $"{_filePath}.corrupt-{DateTimeOffset.Now:yyyyMMdd-HHmmss}", overwrite: true);
            return [];
        }
    }

    /// <summary>Prepends an entry (newest first), optionally persisting its audio, and trims to the limit.</summary>
    public void Append(TranscriptionEntry entry, byte[]? audio = null)
    {
        var entries = Load();
        if (audio is not null)
        {
            Directory.CreateDirectory(_audioDirectory);
            entry.AudioFile = $"{entry.Id}.wav";
            File.WriteAllBytes(Path.Combine(_audioDirectory, entry.AudioFile), audio);
        }
        entries.Insert(0, entry);
        Trim(entries);
        Save(entries);
    }

    /// <summary>Replaces the entry with the same id; deletes its audio when no longer Failed (FR-020).</summary>
    public void Update(TranscriptionEntry entry)
    {
        var entries = Load();
        var index = entries.FindIndex(e => e.Id == entry.Id);
        if (index < 0) return;

        if (entry.Status != TranscriptionStatus.Failed && entry.AudioFile is not null)
        {
            DeleteAudio(entry.AudioFile);
            entry.AudioFile = null;
        }
        entries[index] = entry;
        Save(entries);
    }

    public byte[]? ReadAudio(TranscriptionEntry entry)
    {
        if (entry.AudioFile is null) return null;
        var path = Path.Combine(_audioDirectory, entry.AudioFile);
        return File.Exists(path) ? File.ReadAllBytes(path) : null;
    }

    /// <summary>Deletes WAV files not referenced by any Failed entry (run at startup).</summary>
    public void CleanupOrphans()
    {
        if (!Directory.Exists(_audioDirectory)) return;
        var referenced = Load()
            .Where(e => e.AudioFile is not null)
            .Select(e => e.AudioFile!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var wav in Directory.GetFiles(_audioDirectory, "*.wav"))
        {
            if (!referenced.Contains(Path.GetFileName(wav)))
                TryDelete(wav);
        }
    }

    private void Trim(List<TranscriptionEntry> entries)
    {
        while (entries.Count > Limit)
        {
            var oldest = entries[^1];
            if (oldest.AudioFile is not null) DeleteAudio(oldest.AudioFile);
            entries.RemoveAt(entries.Count - 1);
        }
    }

    private void Save(List<TranscriptionEntry> entries)
    {
        Directory.CreateDirectory(_directory);
        var tmp = _filePath + ".tmp";
        var payload = new HistoryFile { Entries = entries };
        File.WriteAllText(tmp, JsonSerializer.Serialize(payload, SettingsStore.JsonOptions), Encoding.UTF8);
        File.Move(tmp, _filePath, overwrite: true);
    }

    private void DeleteAudio(string audioFile) => TryDelete(Path.Combine(_audioDirectory, audioFile));

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { /* best effort; orphan cleanup will retry next start */ }
    }
}
