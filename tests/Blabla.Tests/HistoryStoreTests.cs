using System.IO;
using Blabla.Models;
using Blabla.Services;

namespace Blabla.Tests;

public sealed class HistoryStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "BlablaTests", Guid.NewGuid().ToString("N"));

    private HistoryStore CreateStore(int limit = 100) => new(_dir) { Limit = limit };

    private static TranscriptionEntry Entry(string text, DateTimeOffset? timestamp = null) => new()
    {
        Text = text,
        PresetName = "Algemeen",
        Status = TranscriptionStatus.Succeeded,
        Timestamp = timestamp ?? DateTimeOffset.Now,
        DurationSeconds = 3,
    };

    [Fact]
    public void Append_KeepsNewestFirst()
    {
        var store = CreateStore();
        store.Append(Entry("eerste"));
        store.Append(Entry("tweede"));

        var entries = store.Load();
        Assert.Equal(2, entries.Count);
        Assert.Equal("tweede", entries[0].Text);
        Assert.Equal("eerste", entries[1].Text);
    }

    [Fact]
    public void Append_BeyondLimit_TrimsOldestAndDeletesItsAudio()
    {
        var store = CreateStore(limit: 2);
        var failed = new TranscriptionEntry { Status = TranscriptionStatus.Failed, ErrorMessage = "offline", PresetName = "Algemeen" };
        store.Append(failed, [1, 2, 3]);
        store.Append(Entry("twee"));
        store.Append(Entry("drie"));

        var entries = store.Load();
        Assert.Equal(2, entries.Count);
        Assert.Equal("drie", entries[0].Text);
        Assert.Equal("twee", entries[1].Text);
        Assert.Empty(Directory.GetFiles(Path.Combine(_dir, "audio"), "*.wav"));
    }

    [Fact]
    public void FailedEntry_PersistsAudio_UpdateToSucceeded_DeletesIt()
    {
        var store = CreateStore();
        var failed = new TranscriptionEntry { Status = TranscriptionStatus.Failed, ErrorMessage = "fout", PresetName = "Algemeen" };
        store.Append(failed, [10, 20, 30]);

        var persisted = store.Load()[0];
        Assert.Equal($"{failed.Id}.wav", persisted.AudioFile);
        Assert.Equal(new byte[] { 10, 20, 30 }, store.ReadAudio(persisted));

        persisted.Status = TranscriptionStatus.Succeeded;
        persisted.Text = "gelukt";
        persisted.ErrorMessage = null;
        store.Update(persisted);

        var updated = store.Load()[0];
        Assert.Equal(TranscriptionStatus.Succeeded, updated.Status);
        Assert.Null(updated.AudioFile);
        Assert.Empty(Directory.GetFiles(Path.Combine(_dir, "audio"), "*.wav"));
    }

    [Fact]
    public void CleanupOrphans_DeletesUnreferencedWavs_KeepsReferenced()
    {
        var store = CreateStore();
        var failed = new TranscriptionEntry { Status = TranscriptionStatus.Failed, ErrorMessage = "fout", PresetName = "Algemeen" };
        store.Append(failed, [1]);

        var audioDir = Path.Combine(_dir, "audio");
        File.WriteAllBytes(Path.Combine(audioDir, "orphan.wav"), [9, 9]);

        store.CleanupOrphans();

        var remaining = Directory.GetFiles(audioDir, "*.wav").Select(f => Path.GetFileName(f)!).ToArray();
        Assert.Equal([$"{failed.Id}.wav"], remaining);
    }

    [Fact]
    public void Load_MissingFile_ReturnsEmpty()
    {
        Assert.Empty(CreateStore().Load());
    }

    [Fact]
    public void Load_CorruptFile_RecoversToEmpty()
    {
        var store = CreateStore();
        store.Append(Entry("x"));
        File.WriteAllText(Path.Combine(_dir, "history.json"), "niet geldig");

        Assert.Empty(CreateStore().Load());
        Assert.Single(Directory.GetFiles(_dir, "history.json.corrupt-*"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }
}
