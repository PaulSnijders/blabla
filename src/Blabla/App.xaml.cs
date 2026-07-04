using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Blabla.Core;
using Blabla.Models;
using Blabla.Services;
using H.NotifyIcon;
using Microsoft.Win32;

namespace Blabla;

public partial class App : Application
{
    private const string ActivationEventName = @"Local\Blabla.Activate";

    private Mutex? _singleInstanceMutex;
    private EventWaitHandle? _activationEvent;
    private RegisteredWaitHandle? _activationWait;
    private TaskbarIcon? _trayIcon;

    private SettingsStore _settingsStore = null!;
    private HistoryStore _historyStore = null!;
    private HotkeyManager _hotkeyManager = null!;
    private StartupRegistration _startupRegistration = null!;
    private AppSettings _settings = null!;

    private AudioRecorder _recorder = null!;
    private DictationController _controller = null!;
    private UI.RecordingPopup _popup = null!;
    private UI.MainWindow? _mainWindow;

    private int _recordHotkeyId = -1;
    private int _pasteHotkeyId = -1;
    private int _escHotkeyId = -1;
    private int _presetUpHotkeyId = -1;
    private int _presetDownHotkeyId = -1;

    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstanceMutex = new Mutex(initiallyOwned: true, @"Local\Blabla.SingleInstance", out var isFirstInstance);
        if (!isFirstInstance)
        {
            // Second launch: activate the running instance's main window instead (T033).
            try
            {
                using var signal = EventWaitHandle.OpenExisting(ActivationEventName);
                signal.Set();
            }
            catch (WaitHandleCannotBeOpenedException) { }
            Shutdown();
            return;
        }

        _activationEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivationEventName);
        _activationWait = ThreadPool.RegisterWaitForSingleObject(_activationEvent,
            (_, _) => Dispatcher.BeginInvoke(ShowMainWindow),
            state: null, millisecondsTimeOutInterval: -1, executeOnlyOnce: false);

        base.OnStartup(e);

        _settingsStore = new SettingsStore();
        _settings = _settingsStore.Load();
        _historyStore = new HistoryStore { Limit = _settings.HistoryLimit };
        _historyStore.CleanupOrphans();
        _hotkeyManager = new HotkeyManager();
        _startupRegistration = new StartupRegistration();

        // Pre-created hidden so the first hotkey press shows it instantly (constitution III).
        _popup = new UI.RecordingPopup();

        _recorder = new AudioRecorder();
        _controller = new DictationController(
            _recorder,
            new TranscriptionClient(),
            new LlmClient(),
            new TextInserter(),
            _popup,
            _historyStore,
            () => _settings,
            () => _settingsStore.GetApiKey(_settings),
            action => Dispatcher.Invoke(action));

        // Internal paste-last buffer survives restarts via the newest succeeded entry (FR-004a).
        _controller.LastTranscription = _historyStore.Load()
            .FirstOrDefault(entry => entry.Status == TranscriptionStatus.Succeeded)?.Text;

        _popup.CancelRequested += _controller.CancelRecording;
        _controller.StateChanged += OnDictationStateChanged;
        _controller.ActivePresetChanged += _ =>
        {
            _settingsStore.Save(_settings); // survives restarts (FR-013)
            RebuildTrayMenu();
        };
        _controller.ApiKeyMissing += () => Dispatcher.Invoke(() =>
        {
            ShowMainWindow();
            _mainWindow!.ShowSettingsTab();
        });

        CreateTrayIcon();
        RegisterConfiguredHotkeys();

        // Hotkey registrations can be lost across sleep/unlock; re-register defensively (research R9).
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;

        if (_settingsStore.LoadWarning is not null)
        {
            MessageBox.Show(_settingsStore.LoadWarning, "Blabla",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>Called by MainWindow after Opslaan: apply everything that has live side effects.</summary>
    internal void ApplySettingsChanged()
    {
        RegisterConfiguredHotkeys();
        _startupRegistration.Apply(_settings.StartWithWindows);
        _historyStore.Limit = _settings.HistoryLimit;
        RebuildTrayMenu();
    }

    /// <summary>(Re)registers the two configured hotkeys, warning on conflicts (FR-009).</summary>
    private void RegisterConfiguredHotkeys()
    {
        if (_recordHotkeyId >= 0) _hotkeyManager.Unregister(_recordHotkeyId);
        if (_pasteHotkeyId >= 0) _hotkeyManager.Unregister(_pasteHotkeyId);
        _recordHotkeyId = -1;
        _pasteHotkeyId = -1;

        var warnings = new List<string>();
        if (!_hotkeyManager.TryRegister(_settings.RecordHotkey, _controller.ToggleRecording, out _recordHotkeyId))
            warnings.Add($"Opname-sneltoets {_settings.RecordHotkey} is al in gebruik door een andere app.");
        if (!_hotkeyManager.TryRegister(_settings.PasteLastHotkey, () => _ = _controller.PasteLastAsync(), out _pasteHotkeyId))
            warnings.Add($"Plak-sneltoets {_settings.PasteLastHotkey} is al in gebruik door een andere app.");

        if (warnings.Count > 0)
        {
            MessageBox.Show(
                string.Join(Environment.NewLine, warnings) +
                Environment.NewLine + "Kies een andere sneltoets in de instellingen.",
                "Blabla — sneltoetsconflict", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnDictationStateChanged(DictationState state)
    {
        // Esc (cancel) and Ctrl+↑/↓ (preset switching) work only while recording;
        // registered globally just for that window of time so they conflict with nothing.
        if (state == DictationState.Recording && _escHotkeyId < 0)
        {
            _hotkeyManager.TryRegister(new Hotkey(ModifierKeys.None, Key.Escape),
                _controller.CancelRecording, out _escHotkeyId);
            _hotkeyManager.TryRegister(new Hotkey(ModifierKeys.Control, Key.Up),
                () => _controller.CyclePreset(-1), out _presetUpHotkeyId);
            _hotkeyManager.TryRegister(new Hotkey(ModifierKeys.Control, Key.Down),
                () => _controller.CyclePreset(+1), out _presetDownHotkeyId);
        }
        else if (state != DictationState.Recording && _escHotkeyId >= 0)
        {
            _hotkeyManager.Unregister(_escHotkeyId);
            _hotkeyManager.Unregister(_presetUpHotkeyId);
            _hotkeyManager.Unregister(_presetDownHotkeyId);
            _escHotkeyId = -1;
            _presetUpHotkeyId = -1;
            _presetDownHotkeyId = -1;
        }

        if (_trayIcon is not null)
        {
            var icon = state == DictationState.Recording ? "blabla-recording.ico" : "blabla.ico";
            _trayIcon.IconSource = new BitmapImage(new Uri($"pack://application:,,,/Assets/{icon}"));
        }

        // Keep the visible history current while the main window is open.
        if (state == DictationState.Idle && _mainWindow?.IsVisible == true)
            _mainWindow.RefreshHistory();
    }

    private void CreateTrayIcon()
    {
        _trayIcon = new TaskbarIcon
        {
            IconSource = new BitmapImage(new Uri("pack://application:,,,/Assets/blabla.ico")),
            ToolTipText = "Blabla — spraak naar tekst",
        };
        _trayIcon.TrayLeftMouseUp += (_, _) => ShowMainWindow();
        RebuildTrayMenu();
        _trayIcon.ForceCreate();
    }

    /// <summary>Tray menu: preset switching in one click (FR-013), open, quit — nothing more (constitution V).</summary>
    private void RebuildTrayMenu()
    {
        if (_trayIcon is null) return;

        var menu = new ContextMenu();

        foreach (var preset in _settings.Presets)
        {
            var item = new MenuItem
            {
                Header = preset.Name,
                IsCheckable = true,
                IsChecked = preset.Id == _settings.ActivePresetId,
            };
            var presetId = preset.Id;
            item.Click += (_, _) => ActivatePreset(presetId);
            menu.Items.Add(item);
        }

        menu.Items.Add(new Separator());

        var openItem = new MenuItem { Header = "Openen" };
        openItem.Click += (_, _) => ShowMainWindow();
        menu.Items.Add(openItem);

        menu.Items.Add(new Separator());

        var exitItem = new MenuItem { Header = "Afsluiten" };
        exitItem.Click += (_, _) => Shutdown();
        menu.Items.Add(exitItem);

        _trayIcon.ContextMenu = menu;
    }

    private void ActivatePreset(Guid presetId)
    {
        if (_settings.Presets.All(p => p.Id != presetId)) return;
        _settings.ActivePresetId = presetId;
        _settingsStore.Save(_settings); // active preset survives restarts (FR-013)
        RebuildTrayMenu();
    }

    private void ShowMainWindow()
    {
        _mainWindow ??= new UI.MainWindow(this, _settingsStore, _settings, _historyStore, _controller);
        _mainWindow.RefreshHistory();
        _mainWindow.Show();
        if (_mainWindow.WindowState == WindowState.Minimized)
            _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
            Dispatcher.BeginInvoke(_hotkeyManager.ReRegisterAll);
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason == SessionSwitchReason.SessionUnlock)
            Dispatcher.BeginInvoke(_hotkeyManager.ReRegisterAll);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_hotkeyManager is not null)
        {
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            SystemEvents.SessionSwitch -= OnSessionSwitch;
        }
        _activationWait?.Unregister(null);
        _activationEvent?.Dispose();
        _trayIcon?.Dispose();
        _hotkeyManager?.Dispose();
        _recorder?.Dispose();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
