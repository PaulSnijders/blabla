using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Blabla.Models;

namespace Blabla.Services;

/// <summary>
/// Loads/saves settings.json per contracts/local-storage.md: atomic writes,
/// corrupt-file recovery, DPAPI (CurrentUser) encryption for the API key.
/// </summary>
public sealed class SettingsStore
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    private readonly string _directory;
    private readonly string _filePath;

    public SettingsStore(string? baseDirectory = null)
    {
        _directory = baseDirectory
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Blabla");
        _filePath = Path.Combine(_directory, "settings.json");
    }

    public string Directory => _directory;

    /// <summary>Set when the last Load had to recover from a corrupt file; user should be informed.</summary>
    public string? LoadWarning { get; private set; }

    public AppSettings Load()
    {
        LoadWarning = null;
        System.IO.Directory.CreateDirectory(_directory);

        if (!File.Exists(_filePath))
        {
            var fresh = AppSettings.CreateDefault();
            Save(fresh);
            return fresh;
        }

        AppSettings? settings = null;
        try
        {
            settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_filePath), JsonOptions);
        }
        catch (JsonException)
        {
        }

        if (settings is null || settings.Presets.Count == 0 && settings.ApiKeyEncrypted.Length == 0)
        {
            // Corrupt or empty: keep the evidence, start over with defaults (contract: never a crash loop).
            var backup = $"{_filePath}.corrupt-{DateTimeOffset.Now:yyyyMMdd-HHmmss}";
            File.Move(_filePath, backup, overwrite: true);
            LoadWarning = $"Instellingen waren beschadigd en zijn opnieuw aangemaakt. Backup: {backup}";
            var fresh = AppSettings.CreateDefault();
            Save(fresh);
            return fresh;
        }

        // Repair invariants rather than failing (contract: forward compatible, self-healing).
        if (settings.Presets.Count == 0)
            settings.Presets = AppSettings.CreateDefault().Presets;
        if (settings.Presets.All(p => p.Id != settings.ActivePresetId))
            settings.ActivePresetId = settings.Presets[0].Id;
        if (settings.HistoryLimit <= 0)
            settings.HistoryLimit = 100;

        return settings;
    }

    public void Save(AppSettings settings)
    {
        System.IO.Directory.CreateDirectory(_directory);
        var tmp = _filePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(settings, JsonOptions), Encoding.UTF8);
        File.Move(tmp, _filePath, overwrite: true);
    }

    /// <summary>Decrypts the stored API key; empty string when unset or undecryptable on this account.</summary>
    public string GetApiKey(AppSettings settings)
    {
        if (settings.ApiKeyEncrypted.Length == 0) return "";
        try
        {
            var cipher = Convert.FromBase64String(settings.ApiKeyEncrypted);
            var plain = ProtectedData.Unprotect(cipher, optionalEntropy: null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception e) when (e is CryptographicException or FormatException)
        {
            return "";
        }
    }

    /// <summary>Encrypts and stores the API key on the settings object (call Save afterwards).</summary>
    public void SetApiKey(AppSettings settings, string apiKey)
    {
        settings.ApiKeyEncrypted = apiKey.Length == 0
            ? ""
            : Convert.ToBase64String(ProtectedData.Protect(
                Encoding.UTF8.GetBytes(apiKey), optionalEntropy: null, DataProtectionScope.CurrentUser));
    }
}
