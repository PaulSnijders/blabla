using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Blabla.Core;

namespace Blabla.UI;

/// <summary>
/// The recording pop-up (constitution: one of the two allowed UI surfaces). Always on top,
/// never takes focus (WS_EX_NOACTIVATE), pre-created hidden so showing is instant.
/// </summary>
public partial class RecordingPopup : Window, IDictationView
{
    private static readonly Brush RecordingBrush = new SolidColorBrush(Color.FromRgb(0xEB, 0x50, 0x46));
    private static readonly Brush BusyBrush = new SolidColorBrush(Color.FromRgb(0x4E, 0x9C, 0xF5));
    private static readonly Brush ErrorBrush = new SolidColorBrush(Color.FromRgb(0xF5, 0xA6, 0x23));
    private static readonly Brush ConvertingBrush = new SolidColorBrush(Color.FromRgb(0xA0, 0x6A, 0xF5));

    private readonly Storyboard _pulse;
    private readonly DispatcherTimer _autoHide = new();

    /// <summary>Raised when the user clicks the ✕ cancel button.</summary>
    public event Action? CancelRequested;

    public RecordingPopup()
    {
        InitializeComponent();

        var animation = new DoubleAnimation(1.0, 0.25, TimeSpan.FromMilliseconds(650))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
        };
        _pulse = new Storyboard();
        _pulse.Children.Add(animation);
        Storyboard.SetTarget(animation, StateDot);
        Storyboard.SetTargetProperty(animation, new PropertyPath(OpacityProperty));

        _autoHide.Tick += (_, _) => { _autoHide.Stop(); Hide(); };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW: clicks work, but focus never leaves the target app.
        var handle = new WindowInteropHelper(this).Handle;
        var exStyle = GetWindowLong(handle, GwlExstyle);
        SetWindowLong(handle, GwlExstyle, exStyle | WsExNoActivate | WsExToolWindow);
    }

    public void ShowRecording(TimeSpan elapsed)
    {
        _autoHide.Stop();
        StateDot.Fill = RecordingBrush;
        StatusText.Text = "Opnemen";
        TimerText.Text = elapsed.ToString(@"m\:ss");
        PresetText.Visibility = Visibility.Visible;
        CancelButton.Visibility = Visibility.Visible;
        if (!IsVisible) StartPulse();
        ShowNearBottomCenter();
    }

    public void ShowPreset(string presetName)
    {
        PresetText.Text = $"·  {presetName}";
        PresetText.Visibility = Visibility.Visible;
        if (IsVisible) ShowNearBottomCenter(); // re-center: the width changes with the name
    }

    public void ShowTranscribing()
    {
        _autoHide.Stop();
        StopPulse();
        StartPulse();
        StateDot.Fill = BusyBrush;
        StatusText.Text = "Bezig met transcriberen…";
        TimerText.Text = "";
        PresetText.Visibility = Visibility.Collapsed;
        CancelButton.Visibility = Visibility.Collapsed;
        ShowNearBottomCenter();
    }

    public void ShowConverting()
    {
        _autoHide.Stop();
        StopPulse();
        StartPulse();
        StateDot.Fill = ConvertingBrush;
        StatusText.Text = "Bezig met omzetten…";
        TimerText.Text = "";
        PresetText.Visibility = Visibility.Collapsed;
        CancelButton.Visibility = Visibility.Collapsed;
        ShowNearBottomCenter();
    }

    public void ShowError(string message) => ShowMessage(message, TimeSpan.FromSeconds(6));

    public void ShowInfo(string message) => ShowMessage(message, TimeSpan.FromSeconds(3));

    public void HideView()
    {
        _autoHide.Stop();
        StopPulse();
        Hide();
    }

    private void ShowMessage(string message, TimeSpan visibleFor)
    {
        StopPulse();
        StateDot.Fill = ErrorBrush;
        StateDot.Opacity = 1.0;
        StatusText.Text = message;
        TimerText.Text = "";
        PresetText.Visibility = Visibility.Collapsed;
        CancelButton.Visibility = Visibility.Collapsed;
        ShowNearBottomCenter();
        _autoHide.Interval = visibleFor;
        _autoHide.Stop();
        _autoHide.Start();
    }

    private void ShowNearBottomCenter()
    {
        if (!IsVisible) Show();
        UpdateLayout();
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Left + (workArea.Width - ActualWidth) / 2;
        Top = workArea.Bottom - ActualHeight - 48;
    }

    private void StartPulse() => _pulse.Begin();

    private void StopPulse()
    {
        _pulse.Stop();
        StateDot.Opacity = 1.0;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => CancelRequested?.Invoke();

    private const int GwlExstyle = -20;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExToolWindow = 0x00000080;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
