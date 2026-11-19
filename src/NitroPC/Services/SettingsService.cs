using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NitroPC.Models;

namespace NitroPC.Services;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
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
        if (TryLoadFromFile(_filePath, out var primary))
        {
            return primary;
        }

        var tempPath = _filePath + ".tmp";
        if (TryLoadFromFile(tempPath, out var recovered))
        {
            return recovered;
        }

        return new NitroSettings();
    }

    public void Save(NitroSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Normalize();

        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(settings, JsonOptions));
        var tempPath = _filePath + ".tmp";
        using (var stream = new FileStream(
                   tempPath,
                   FileMode.Create,
                   FileAccess.Write,
                   FileShare.None,
                   4096,
                   FileOptions.WriteThrough))
        {
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush(flushToDisk: true);
        }

        File.Move(tempPath, _filePath, overwrite: true);
    }

    private static bool TryLoadFromFile(string path, out NitroSettings settings)
    {
        settings = null!;
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            var json = File.ReadAllText(path, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            var loaded = JsonSerializer.Deserialize<NitroSettings>(json, JsonOptions);
            if (loaded is null)
            {
                return false;
            }

            loaded.Normalize();
            settings = loaded;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
