using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;
using Blabla.Models;

namespace Blabla.Core;

/// <summary>
/// System-wide hotkeys via Win32 RegisterHotKey on a hidden message-only window (research R4).
/// Registration failure is surfaced so callers can warn about conflicts (FR-009).
/// </summary>
public sealed class HotkeyManager : IDisposable
{
    private const int WmHotkey = 0x0312;
    private static readonly IntPtr HwndMessage = new(-3);

    private readonly HwndSource _source;
    private readonly Dictionary<int, (Hotkey Hotkey, Action Callback)> _registrations = [];
    private int _nextId = 1;

    public HotkeyManager()
    {
        var parameters = new HwndSourceParameters("BlablaHotkeys")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0,
            ParentWindow = HwndMessage,
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
    }

    /// <summary>Registers a hotkey; returns false when the combination is taken by another app.</summary>
    public bool TryRegister(Hotkey hotkey, Action callback, out int id)
    {
        id = _nextId++;
        if (!RegisterHotKey(_source.Handle, id, ToNativeModifiers(hotkey.Modifiers),
                (uint)KeyInterop.VirtualKeyFromKey(hotkey.Key)))
        {
            return false;
        }
        _registrations[id] = (hotkey, callback);
        return true;
    }

    public void Unregister(int id)
    {
        if (_registrations.Remove(id))
            UnregisterHotKey(_source.Handle, id);
    }

    /// <summary>Re-registers everything after resume-from-sleep / session unlock (research R9).</summary>
    public void ReRegisterAll()
    {
        foreach (var (id, registration) in _registrations)
        {
            UnregisterHotKey(_source.Handle, id);
            RegisterHotKey(_source.Handle, id, ToNativeModifiers(registration.Hotkey.Modifiers),
                (uint)KeyInterop.VirtualKeyFromKey(registration.Hotkey.Key));
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && _registrations.TryGetValue(wParam.ToInt32(), out var registration))
        {
            registration.Callback();
            handled = true;
        }
        return IntPtr.Zero;
    }

    private static uint ToNativeModifiers(ModifierKeys modifiers)
    {
        uint native = 0x4000; // MOD_NOREPEAT: holding the key down doesn't re-fire
        if (modifiers.HasFlag(ModifierKeys.Alt)) native |= 0x0001;
        if (modifiers.HasFlag(ModifierKeys.Control)) native |= 0x0002;
        if (modifiers.HasFlag(ModifierKeys.Shift)) native |= 0x0004;
        if (modifiers.HasFlag(ModifierKeys.Windows)) native |= 0x0008;
        return native;
    }

    public void Dispose()
    {
        foreach (var id in _registrations.Keys)
            UnregisterHotKey(_source.Handle, id);
        _registrations.Clear();
        _source.Dispose();
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
