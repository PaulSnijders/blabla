using Microsoft.Win32;

namespace Blabla.Services;

/// <summary>
/// Start-with-Windows via the per-user Run key (contracts/local-storage.md), no admin rights.
/// Reconciled on every settings save (FR-011).
/// </summary>
public sealed class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Blabla";

    public void Apply(bool startWithWindows)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (startWithWindows)
            key.SetValue(ValueName, $"\"{Environment.ProcessPath}\"");
        else if (key.GetValue(ValueName) is not null)
            key.DeleteValue(ValueName);
    }

    public bool IsRegistered()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(ValueName) is not null;
    }
}
