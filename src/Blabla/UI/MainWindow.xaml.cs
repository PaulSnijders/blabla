using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Blabla.Core;
using Blabla.Models;
using Blabla.Services;

namespace Blabla.UI;

/// <summary>
/// The main window (constitution: the second of the two allowed UI surfaces):
/// history + presets + settings. Closing hides it; the app lives in the tray.
/// </summary>
public partial class MainWindow : Window
{
    private readonly App _app;
    private readonly SettingsStore _settingsStore;
    private readonly AppSettings _settings;
    private readonly HistoryStore _historyStore;
    private readonly DictationController _controller;

    // Working copies; applied to _settings only on Opslaan.
    private List<Preset> _presets = [];
    private Guid _activePresetId;
    private Hotkey _recordHotkey = null!;
    private Hotkey _pasteHotkey = null!;
    private Preset? _editingPreset;

    public MainWindow(App app, SettingsStore settingsStore, AppSettings settings,
        HistoryStore historyStore, DictationController controller)
    {
        _app = app;
        _settingsStore = settingsStore;
        _settings = settings;
        _historyStore = historyStore;
        _controller = controller;

        InitializeComponent();
        LoadFromSettings();
        RefreshHistory();
    }

    public void ShowSettingsTab() => Tabs.SelectedIndex = 2;

    protected override void OnClosing(CancelEventArgs e)
    {
        // Tray app: closing the window hides it, the app keeps running (quit via tray menu).
        e.Cancel = true;
        Hide();
        base.OnClosing(e);
    }

    private void LoadFromSettings()
    {
        _presets = _settings.Presets.Select(p => p.Clone()).ToList();
        _activePresetId = _settings.ActivePresetId;
        _recordHotkey = _settings.RecordHotkey;
        _pasteHotkey = _settings.PasteLastHotkey;

        RecordHotkeyBox.Text = _recordHotkey.ToString();
        PasteHotkeyBox.Text = _pasteHotkey.ToString();
        AutoStartBox.IsChecked = _settings.StartWithWindows;
        ApiKeyBox.Clear();
        ApiKeyStatus.Text = _settings.HasApiKey ? "✓ ingesteld" : "nog niet ingesteld";

        _editingPreset = null;
        PresetList.ItemsSource = null;
        PresetList.ItemsSource = _presets;
        PresetList.SelectedItem = _presets.FirstOrDefault(p => p.Id == _activePresetId) ?? _presets.FirstOrDefault();
    }

    // ═══════════ Geschiedenis (US4) ═══════════

    public void RefreshHistory()
    {
        var entries = _historyStore.Load();
        HistoryEmptyText.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        HistoryList.Items.Clear();
        foreach (var entry in entries)
            HistoryList.Items.Add(CreateHistoryCard(entry));
    }

    private UIElement CreateHistoryCard(TranscriptionEntry entry)
    {
        var failed = entry.Status == TranscriptionStatus.Failed;

        var header = new TextBlock
        {
            Text = $"{entry.Timestamp.LocalDateTime:d MMM yyyy HH:mm}  ·  {entry.PresetName}  ·  {entry.DurationSeconds}s",
            Foreground = System.Windows.Media.Brushes.Gray,
            FontSize = 11.5,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        if (entry.RawText is not null)
        {
            var copyRaw = new Button { Content = "Kopieer transcriptie", Margin = new Thickness(0, 0, 8, 0) };
            copyRaw.Click += (_, _) =>
            {
                Clipboard.SetText(entry.RawText);
                ShowStatus("Transcriptie (tussenresultaat) gekopieerd naar het klembord.");
            };
            buttons.Children.Add(copyRaw);
        }
        if (failed)
        {
            var retry = new Button { Content = "Opnieuw proberen", Margin = new Thickness(0, 0, 8, 0) };
            retry.Click += async (_, _) => await RetryEntryAsync(entry, retry);
            buttons.Children.Add(retry);
        }
        else
        {
            var copy = new Button { Content = "Kopieer" };
            copy.Click += (_, _) =>
            {
                // Explicit user action: clipboard use is allowed here (FR-016).
                Clipboard.SetText(entry.Text ?? "");
                ShowStatus("Gekopieerd naar het klembord.");
            };
            buttons.Children.Add(copy);
        }

        var headerRow = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Right);
        headerRow.Children.Add(buttons);
        headerRow.Children.Add(header);

        var body = new TextBlock
        {
            Text = failed ? $"⚠ {entry.ErrorMessage}" : entry.Text,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0),
            Foreground = failed
                ? System.Windows.Media.Brushes.Firebrick
                : System.Windows.Media.Brushes.Black,
        };

        var content = new StackPanel();
        content.Children.Add(headerRow);
        if (entry.RawText is not null)
        {
            // The intermediate result: the raw transcription before the LLM rewrite.
            content.Children.Add(new TextBlock
            {
                Text = $"Transcriptie: {entry.RawText}",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0),
                FontSize = 12,
                Foreground = System.Windows.Media.Brushes.Gray,
                FontStyle = FontStyles.Italic,
            });
        }
        content.Children.Add(body);

        return new Border
        {
            Background = System.Windows.Media.Brushes.White,
            CornerRadius = new CornerRadius(8),
            BorderBrush = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0xE2, 0xE2, 0xE5)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 9, 12, 9),
            Margin = new Thickness(0, 0, 0, 8),
            Child = content,
        };
    }

    private async Task RetryEntryAsync(TranscriptionEntry entry, Button retryButton)
    {
        var audio = _historyStore.ReadAudio(entry);
        if (audio is null)
        {
            ShowStatus("De opname van deze mislukte transcriptie is niet meer beschikbaar.");
            return;
        }

        retryButton.IsEnabled = false;
        ShowStatus("Opnieuw aan het transcriberen…");
        var succeeded = await _controller.RetryAsync(entry, audio);
        ShowStatus(succeeded ? "Transcriptie gelukt." : $"Opnieuw mislukt: {entry.ErrorMessage}");
        RefreshHistory();
    }

    // ═══════════ Presets (US3) ═══════════

    private void PresetList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        CommitPresetEditor();
        _editingPreset = PresetList.SelectedItem as Preset;
        LoadPresetEditor();
    }

    private void LoadPresetEditor()
    {
        var preset = _editingPreset;
        PresetEditor.IsEnabled = preset is not null;
        PresetNameBox.Text = preset?.Name ?? "";
        PresetPromptBox.Text = preset?.Prompt ?? "";
        PresetLanguageBox.Text = preset?.Language ?? "";
        PresetModelBox.Text = preset?.Model ?? "";
        PresetTemperatureBox.Text = preset?.Temperature?.ToString(CultureInfo.InvariantCulture) ?? "";
        PresetActiveBox.IsChecked = preset is not null && preset.Id == _activePresetId;
        PresetLlmEnabledBox.IsChecked = preset?.LlmEnabled == true;
        PresetLlmPromptBox.Text = preset?.LlmSystemPrompt ?? "";
        PresetLlmModelBox.Text = preset?.LlmModel ?? "";
        PresetLlmTemperatureBox.Text = preset?.LlmTemperature?.ToString(CultureInfo.InvariantCulture) ?? "";
    }

    private void CommitPresetEditor()
    {
        if (_editingPreset is null) return;
        _editingPreset.Name = PresetNameBox.Text.Trim();
        _editingPreset.Prompt = PresetPromptBox.Text.Trim();
        _editingPreset.Language = string.IsNullOrWhiteSpace(PresetLanguageBox.Text)
            ? null : PresetLanguageBox.Text.Trim().ToLowerInvariant();
        _editingPreset.Model = PresetModelBox.Text.Trim();
        _editingPreset.Temperature = ParseTemperature(PresetTemperatureBox.Text);
        _editingPreset.LlmEnabled = PresetLlmEnabledBox.IsChecked == true;
        _editingPreset.LlmSystemPrompt = PresetLlmPromptBox.Text.Trim();
        _editingPreset.LlmModel = PresetLlmModelBox.Text.Trim().Length > 0
            ? PresetLlmModelBox.Text.Trim() : "gpt-5-mini";
        _editingPreset.LlmTemperature = ParseTemperature(PresetLlmTemperatureBox.Text);
        if (PresetActiveBox.IsChecked == true)
            _activePresetId = _editingPreset.Id;
        RefreshPresetListDisplay();
    }

    private static decimal? ParseTemperature(string text)
    {
        var normalized = text.Trim().Replace(',', '.');
        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? Math.Clamp(value, 0m, 1m) : null;
    }

    private void RefreshPresetListDisplay()
    {
        var selected = PresetList.SelectedItem;
        PresetList.ItemsSource = null;
        PresetList.ItemsSource = _presets;
        PresetList.SelectedItem = selected;
    }

    private void NewPresetButton_Click(object sender, RoutedEventArgs e)
    {
        CommitPresetEditor();
        var preset = new Preset { Name = "Nieuwe preset", Model = "gpt-4o-transcribe", Language = "nl" };
        _presets.Add(preset);
        RefreshPresetListDisplay();
        PresetList.SelectedItem = preset;
        PresetNameBox.Focus();
        PresetNameBox.SelectAll();
    }

    private void DeletePresetButton_Click(object sender, RoutedEventArgs e)
    {
        if (_editingPreset is null) return;
        if (_presets.Count == 1)
        {
            // FR-014: at least one preset must always exist.
            ShowStatus("De laatste preset kan niet worden verwijderd.");
            return;
        }

        var removed = _editingPreset;
        _editingPreset = null;
        _presets.Remove(removed);
        if (_activePresetId == removed.Id)
            _activePresetId = _presets[0].Id; // FR-013 fallback
        RefreshPresetListDisplay();
        PresetList.SelectedItem = _presets[0];
    }

    // ═══════════ Instellingen (US2) ═══════════

    private void RecordHotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
        => CaptureHotkey(e, RecordHotkeyBox, hotkey => _recordHotkey = hotkey);

    private void PasteHotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
        => CaptureHotkey(e, PasteHotkeyBox, hotkey => _pasteHotkey = hotkey);

    private static void CaptureHotkey(KeyEventArgs e, TextBox box, Action<Hotkey> assign)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.None)
        {
            box.Text = "…";
            return;
        }

        var hotkey = new Hotkey(Keyboard.Modifiers, key);
        assign(hotkey);
        box.Text = hotkey.ToString();
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        CommitPresetEditor();

        // Validate (FR-009, FR-012, FR-014)
        if (_recordHotkey == _pasteHotkey)
        {
            ShowStatus("De twee sneltoetsen moeten verschillen.");
            return;
        }
        if (_presets.Any(p => p.Name.Length == 0))
        {
            ShowStatus("Elke preset heeft een naam nodig.");
            return;
        }
        if (_presets.GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
        {
            ShowStatus("Presetnamen moeten uniek zijn.");
            return;
        }
        foreach (var preset in _presets.Where(p => p.Model.Length == 0))
            preset.Model = "gpt-4o-transcribe";

        // Apply working copies to the live settings object
        _settings.Presets = _presets.Select(p => p.Clone()).ToList();
        _settings.ActivePresetId = _settings.Presets.Any(p => p.Id == _activePresetId)
            ? _activePresetId : _settings.Presets[0].Id;
        _settings.RecordHotkey = _recordHotkey;
        _settings.PasteLastHotkey = _pasteHotkey;
        _settings.StartWithWindows = AutoStartBox.IsChecked == true;

        if (ApiKeyBox.Password.Trim().Length > 0)
            _settingsStore.SetApiKey(_settings, ApiKeyBox.Password.Trim());

        _settingsStore.Save(_settings);
        _app.ApplySettingsChanged(); // re-register hotkeys (with conflict warning), autostart, tray menu

        LoadFromSettings();
        ShowStatus("Opgeslagen.");
    }

    private void ShowStatus(string message) => StatusMessage.Text = message;
}
