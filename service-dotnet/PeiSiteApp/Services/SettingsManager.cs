using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Win32;
using PeiSiteApp.Models;

namespace PeiSiteApp.Services;

public class SettingsManager
{
    private readonly string _configPath;
    private readonly string _userSettingsPath;
    private AppSettings _settings;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public AppSettings Settings => _settings;

    public SettingsManager()
    {
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var configDir = Path.Combine(programData, AppPaths.ServiceDirName);
        try { Directory.CreateDirectory(configDir); } catch { }
        _configPath = Path.Combine(configDir, "config.json");

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var userDir = Path.Combine(appData, AppPaths.AppDirName);
        try { Directory.CreateDirectory(userDir); } catch { }
        _userSettingsPath = Path.Combine(userDir, "settings.json");

        _settings = LoadSettings();
    }

    private AppSettings LoadSettings()
    {
        var settings = new AppSettings();

        // Load from shared config.json (service config)
        if (File.Exists(_configPath))
        {
            try
            {
                var data = File.ReadAllText(_configPath);
                var config = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(data, JsonOptions);
                if (config != null)
                {
                    if (config.TryGetValue("apiUrl", out var apiUrl) && apiUrl.ValueKind == JsonValueKind.String)
                        settings.ApiUrl = apiUrl.GetString()!;
                    // Prefer DPAPI-protected key; fall back to plaintext for migration of existing installs
                    if (config.TryGetValue("apiKeyProtected", out var apiKeyProtected) && apiKeyProtected.ValueKind == JsonValueKind.String)
                        settings.ApiKey = CredentialProtection.TryUnprotect(apiKeyProtected.GetString()!) ?? settings.ApiKey;
                    else if (config.TryGetValue("apiKey", out var apiKey) && apiKey.ValueKind == JsonValueKind.String)
                        settings.ApiKey = apiKey.GetString()!;
                    if (config.TryGetValue("siteId", out var siteId))
                    {
                        if (siteId.ValueKind == JsonValueKind.String)
                            settings.SiteNumber = siteId.GetString()!;
                        else if (siteId.ValueKind == JsonValueKind.Number)
                            settings.SiteNumber = siteId.GetInt32().ToString();
                    }
                    if (config.TryGetValue("tenantId", out var tenantId) && tenantId.ValueKind == JsonValueKind.Number)
                        settings.TenantId = tenantId.GetInt32();
                    if (config.TryGetValue("logLevel", out var logLevel) && logLevel.ValueKind == JsonValueKind.String)
                        settings.LogLevel = logLevel.GetString()!;
                }
            }
            catch { }
        }

        // Try Windows Registry for installer-provided values
        var regSiteId = ReadRegistryValue("SiteId");
        var regTenantId = ReadRegistryValue("TenantId");
        var regSiteName = ReadRegistryValue("SiteName");

        if (regSiteId != null)
            settings.SiteNumber = regSiteId;
        if (regTenantId != null && int.TryParse(regTenantId, out var tid))
            settings.TenantId = tid;
        if (regSiteName != null)
            settings.SiteName = regSiteName;

        // Load user-specific settings (site name, etc.)
        if (File.Exists(_userSettingsPath))
        {
            try
            {
                var data = File.ReadAllText(_userSettingsPath);
                var userSettings = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(data, JsonOptions);
                if (userSettings != null)
                {
                    if (userSettings.TryGetValue("siteName", out var siteName) && siteName.ValueKind == JsonValueKind.String)
                        settings.SiteName = siteName.GetString()!;
                }
            }
            catch { }
        }

        return settings;
    }

    private static string? ReadRegistryValue(string valueName)
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            // Try 32-bit registry first (WOW6432Node)
            using var baseKey32 = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
            using var key32 = baseKey32.OpenSubKey(AppPaths.RegistryKey);
            var value = key32?.GetValue(valueName) as string;
            if (value != null) return value;

            // Try 64-bit registry
            using var baseKey64 = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key64 = baseKey64.OpenSubKey(AppPaths.RegistryKey);
            return key64?.GetValue(valueName) as string;
        }
        catch { return null; }
    }

    public void SaveSiteName(string siteName)
    {
        _settings.SiteName = siteName;
        SaveUserSettings();
    }

    public void UpdateSiteNumber(string siteNumber)
    {
        _settings.SiteNumber = siteNumber;
        SaveServiceConfig();
    }

    public void UpdateTenantId(int tenantId)
    {
        _settings.TenantId = tenantId;
        SaveServiceConfig();
    }

    public void SaveLogLevel(string level)
    {
        _settings.LogLevel = level;
        SaveServiceConfig();
    }

    private void SaveUserSettings()
    {
        try
        {
            var data = new { siteName = _settings.SiteName };
            File.WriteAllText(_userSettingsPath, JsonSerializer.Serialize(data, JsonOptions));
        }
        catch { }
    }

    private void SaveServiceConfig()
    {
        try
        {
            // Read the existing config to preserve fields we don't own (ftpEnabled, ftpServers, etc.)
            JsonObject node = new();
            if (File.Exists(_configPath))
            {
                try
                {
                    var raw = File.ReadAllText(_configPath);
                    node = JsonNode.Parse(raw)?.AsObject() ?? new();
                }
                catch { }
            }

            // Patch only the fields the app is responsible for; encrypt the API key
            node["apiUrl"] = _settings.ApiUrl;
            if (!string.IsNullOrEmpty(_settings.ApiKey))
                node["apiKeyProtected"] = CredentialProtection.Protect(_settings.ApiKey);
            node.Remove("apiKey"); // never persist plaintext
            node["siteId"] = _settings.SiteNumber;
            node["tenantId"] = _settings.TenantId;
            node["logLevel"] = _settings.LogLevel;

            File.WriteAllText(_configPath, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
