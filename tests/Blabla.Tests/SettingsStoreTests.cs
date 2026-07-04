using System.IO;
using System.Windows.Input;
using Blabla.Models;
using Blabla.Services;

namespace Blabla.Tests;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "BlablaTests", Guid.NewGuid().ToString("N"));

    private SettingsStore CreateStore() => new(_dir);

    [Fact]
    public void FirstRun_SeedsDefaultPresets()
    {
        var store = CreateStore();
        var settings = store.Load();

        Assert.True(File.Exists(Path.Combine(_dir, "settings.json")));
        Assert.NotEmpty(settings.Presets);
        Assert.Equal("Algemeen", settings.ActivePreset.Name);
        Assert.False(settings.HasApiKey);
        Assert.Equal(100, settings.HistoryLimit);
    }

    [Fact]
    public void SaveAndLoad_RoundTripsAllSettings()
    {
        var store = CreateStore();
        var settings = store.Load();

        settings.RecordHotkey = new Hotkey(ModifierKeys.Control | ModifierKeys.Shift, Key.F9);
        settings.PasteLastHotkey = new Hotkey(ModifierKeys.Windows, Key.B);
        settings.StartWithWindows = true;
        settings.HistoryLimit = 42;
        settings.Presets[0].Prompt = "Aangepaste prompt";
        settings.Presets[0].Model = "whisper-1";
        settings.Presets[0].Temperature = 0.3m;
        settings.Presets[0].Language = null;
        store.Save(settings);

        var reloaded = CreateStore().Load();
        Assert.Equal(settings.RecordHotkey, reloaded.RecordHotkey);
        Assert.Equal(settings.PasteLastHotkey, reloaded.PasteLastHotkey);
        Assert.True(reloaded.StartWithWindows);
        Assert.Equal(42, reloaded.HistoryLimit);
        Assert.Equal("Aangepaste prompt", reloaded.Presets[0].Prompt);
        Assert.Equal("whisper-1", reloaded.Presets[0].Model);
        Assert.Equal(0.3m, reloaded.Presets[0].Temperature);
        Assert.Null(reloaded.Presets[0].Language);
    }

    [Fact]
    public void ApiKey_IsDpapiEncrypted_AndRoundTrips()
    {
        var store = CreateStore();
        var settings = store.Load();

        store.SetApiKey(settings, "sk-test-geheim-123");
        store.Save(settings);

        var json = File.ReadAllText(Path.Combine(_dir, "settings.json"));
        Assert.DoesNotContain("sk-test-geheim-123", json);

        var reloaded = CreateStore().Load();
        Assert.True(reloaded.HasApiKey);
        Assert.Equal("sk-test-geheim-123", CreateStore().GetApiKey(reloaded));
    }

    [Fact]
    public void ClearingApiKey_RemovesCiphertext()
    {
        var store = CreateStore();
        var settings = store.Load();
        store.SetApiKey(settings, "sk-x");
        store.SetApiKey(settings, "");
        Assert.False(settings.HasApiKey);
        Assert.Equal("", store.GetApiKey(settings));
    }

    [Fact]
    public void CorruptFile_IsBackedUpAndRecreated()
    {
        var store = CreateStore();
        store.Load(); // create valid file
        File.WriteAllText(Path.Combine(_dir, "settings.json"), "{ not valid json !!!");

        var recovered = CreateStore();
        var settings = recovered.Load();

        Assert.NotNull(recovered.LoadWarning);
        Assert.NotEmpty(settings.Presets);
        Assert.Single(Directory.GetFiles(_dir, "settings.json.corrupt-*"));
    }

    [Fact]
    public void DanglingActivePresetId_FallsBackToFirstPreset()
    {
        var store = CreateStore();
        var settings = store.Load();
        settings.ActivePresetId = Guid.NewGuid();
        store.Save(settings);

        var reloaded = CreateStore().Load();
        Assert.Equal(reloaded.Presets[0].Id, reloaded.ActivePresetId);
        Assert.Equal(reloaded.Presets[0], reloaded.ActivePreset);
    }

    [Fact]
    public void HotkeyString_ParsesAndFormats()
    {
        Assert.Equal("Ctrl+Alt+Space", Hotkey.Parse("ctrl+alt+space").ToString());
        Assert.Equal(new Hotkey(ModifierKeys.Control | ModifierKeys.Alt, Key.Space), Hotkey.Parse("Ctrl+Alt+Space"));
        Assert.False(Hotkey.TryParse("Ctrl+", out _));
        Assert.False(Hotkey.TryParse("", out _));
        Assert.False(Hotkey.TryParse("Ctrl+Bogus", out _));
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }
}
