using Microsoft.Win32;

namespace NitroPC.Services;

public sealed class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "NitroPC";

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
    }

    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (!enabled)
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
            return;
        }

        var executablePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("The NitroPC process path is unavailable.");
        key.SetValue(ValueName, $"\"{executablePath}\"", RegistryValueKind.String);
    }

    public void EnsureCurrentExecutablePathIfEnabled()
    {
        if (Environment.ProcessPath is not { Length: > 0 } executablePath)
        {
            return;
        }

        using var readKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        if (readKey?.GetValue(ValueName) is not string currentValue ||
            string.IsNullOrWhiteSpace(currentValue))
        {
            return;
        }

        var expected = $"\"{executablePath}\"";
        if (!string.Equals(currentValue, expected, StringComparison.OrdinalIgnoreCase))
        {
            using var writeKey = Registry.CurrentUser.CreateSubKey(RunKeyPath);
            writeKey.SetValue(ValueName, expected, RegistryValueKind.String);
        }
    }
}
