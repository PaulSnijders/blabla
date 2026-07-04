using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;

namespace Blabla.Core;

public interface ITextInserter
{
    void Insert(string text);
}

/// <summary>
/// Inserts text into the focused application via clipboard-paste with restore
/// (research R5, amended after clarification 2026-07-02): the previous clipboard content
/// is snapshotted, the text is pasted with an injected Ctrl+V, and the original clipboard
/// is restored ~300 ms later. Synthetic per-character typing (SendInput KEYEVENTF_UNICODE)
/// proved unreliable — receiving apps garbled or dropped fast VK_PACKET input.
///
/// Before injecting, waits for the physical modifier keys (Ctrl/Alt/Shift/Win) to be
/// released: both hotkeys are modifier combinations, and a Ctrl+V injected while Alt is
/// still held becomes Ctrl+Alt+V (AltGr+V) in the target app.
/// </summary>
public sealed class TextInserter : ITextInserter
{
    private static readonly TimeSpan ModifierReleaseTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan PasteSettleTime = TimeSpan.FromMilliseconds(300);

    public void Insert(string text)
    {
        if (text.Length == 0) return;

        WaitForModifierRelease();

        RunSta(() =>
        {
            var backup = SnapshotClipboard();
            SetClipboardText(text);
            SendCtrlV();
            Thread.Sleep(PasteSettleTime); // let the target app read the clipboard first
            RestoreClipboard(backup);
        });
    }

    /// <summary>Runs clipboard work on a dedicated STA thread (required by OLE clipboard APIs).</summary>
    private static void RunSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception e) { error = e; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        thread.Join();
        if (error is not null) throw error;
    }

    private static Dictionary<string, object>? SnapshotClipboard()
    {
        try
        {
            var data = Clipboard.GetDataObject();
            if (data is null) return null;

            var snapshot = new Dictionary<string, object>();
            foreach (var format in data.GetFormats(autoConvert: false))
            {
                try
                {
                    if (data.GetData(format, autoConvert: false) is { } value)
                        snapshot[format] = value;
                }
                catch
                {
                    // Formats that refuse to materialize (delayed rendering, dead owners) are skipped.
                }
            }
            return snapshot;
        }
        catch
        {
            return null;
        }
    }

    private static void RestoreClipboard(Dictionary<string, object>? snapshot)
    {
        try
        {
            if (snapshot is null || snapshot.Count == 0)
            {
                Clipboard.Clear(); // the clipboard was empty (or unreadable): leave it empty
                return;
            }

            var data = new DataObject();
            foreach (var (format, value) in snapshot)
            {
                try { data.SetData(format, value, autoConvert: false); }
                catch { }
            }
            Clipboard.SetDataObject(data, copy: true);
        }
        catch
        {
            // Restoring is best effort; the paste itself already succeeded.
        }
    }

    private static void SetClipboardText(string text)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Clipboard.SetDataObject(text, copy: true);
                return;
            }
            catch (COMException)
            {
                Thread.Sleep(50); // clipboard briefly locked by another app
            }
        }
        throw new InvalidOperationException(
            "Het klembord wordt vastgehouden door een andere app — de tekst staat in de geschiedenis.");
    }

    private static void SendCtrlV()
    {
        Input[] inputs =
        [
            Key(VkControl, up: false),
            Key(VkV, up: false),
            Key(VkV, up: true),
            Key(VkControl, up: true),
        ];
        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
        if (sent != inputs.Length)
            throw new InvalidOperationException(
                "De plak-toetsaanslag kon niet worden verstuurd — kopieer de tekst vanuit de geschiedenis.");
    }

    /// <summary>Blocks until Ctrl, Alt, Shift and Win are all physically up (or the timeout passes).</summary>
    private static void WaitForModifierRelease()
    {
        ReadOnlySpan<int> modifierVks = [0x10 /*Shift*/, 0x11 /*Ctrl*/, 0x12 /*Alt*/, 0x5B /*LWin*/, 0x5C /*RWin*/];
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < ModifierReleaseTimeout)
        {
            var anyDown = false;
            foreach (var vk in modifierVks)
            {
                if ((GetAsyncKeyState(vk) & 0x8000) != 0)
                {
                    anyDown = true;
                    break;
                }
            }
            if (!anyDown) return;
            Thread.Sleep(15);
        }
        // Timeout: proceed anyway — inserting something beats silently dropping the text.
    }

    private const ushort VkControl = 0x11;
    private const ushort VkV = 0x56;
    private const int InputKeyboard = 1;
    private const uint KeyEventFKeyUp = 0x0002;

    private static Input Key(ushort vk, bool up) => new()
    {
        Type = InputKeyboard,
        Data = new KeyboardInput { Vk = vk, Flags = up ? KeyEventFKeyUp : 0 },
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public int Type;
        public KeyboardInput Data;
        // Pad to the size of the largest union member (MOUSEINPUT); KEYBDINPUT is smaller.
        private readonly long _padding;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort Vk;
        public ushort Scan;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, Input[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
}
