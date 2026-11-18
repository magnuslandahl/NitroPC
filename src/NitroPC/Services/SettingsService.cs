using System.IO;
using System.Text.Json;
using NitroPC.Models;

namespace NitroPC.Services;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _filePath;

    public SettingsService(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NitroPC",
            "settings.json");
    }

    public NitroSettings Load()
    {
        if (!File.Exists(_filePath))
        {
            return new NitroSettings();
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<NitroSettings>(json, JsonOptions) ?? new NitroSettings();
        }
        catch (JsonException)
        {
            return new NitroSettings();
        }
        catch (IOException)
        {
            return new NitroSettings();
        }
        catch (UnauthorizedAccessException)
        {
            return new NitroSettings();
        }
    }

    public void Save(NitroSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(_filePath, json);
    }
}
