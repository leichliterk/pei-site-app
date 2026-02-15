using System.Text.Json;
using Microsoft.Win32;
using PeiSiteService.Models;

namespace PeiSiteService.Services;

public class ConfigManager
{
    private readonly string _configPath;
    private FullConfig _config;
    private readonly FileLogger _logger;
    private readonly object _lock = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private static readonly FullConfig DefaultConfig = new()
    {
        ApiUrl = "https://pei-web-server-staging.onrender.com/api/data",
        ApiKey = "_6@L<Q*SC?mSdp$a1E4?L{\"M+8QQ0|Cw",
        SiteId = 1000,
        TenantId = 1001,
        FtpEnabled = false,
        FtpHost = "",
        FtpPath = "/",
        FtpPollInterval = 60
    };

    public ConfigManager(FileLogger logger)
    {
        _logger = logger;
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var configDir = Path.Combine(programData, "PEI Site Service");
        try { Directory.CreateDirectory(configDir); } catch { }
        _configPath = Path.Combine(configDir, "config.json");
        _config = LoadConfig();
    }

    private FullConfig LoadConfig()
    {
        // Try config file first
        if (File.Exists(_configPath))
        {
            try
            {
                var data = File.ReadAllText(_configPath);
                var fileConfig = JsonSerializer.Deserialize<FullConfig>(data, JsonOptions);
                if (fileConfig != null)
                {
                    _logger.Log($"[ConfigManager] Loaded config from file: {_configPath}");
                    return MergeWithDefaults(fileConfig);
                }
            }
            catch (Exception ex)
            {
                _logger.Log($"[ConfigManager] Error reading config file: {ex.Message}");
            }
        }

        // Try Windows registry
        var registryConfig = ReadFromRegistry();
        if (registryConfig != null)
        {
            SaveConfig(registryConfig);
            return registryConfig;
        }

        _logger.Log("[ConfigManager] Using default configuration");
        return Clone(DefaultConfig);
    }

    private FullConfig? ReadFromRegistry()
    {
        if (!OperatingSystem.IsWindows()) return null;

        try
        {
            // Read 32-bit registry first (WOW6432Node), then 64-bit
            string? tenantId = ReadRegistryValue(RegistryView.Registry32, "TenantId")
                            ?? ReadRegistryValue(RegistryView.Registry64, "TenantId");
            string? siteId = ReadRegistryValue(RegistryView.Registry32, "SiteId")
                          ?? ReadRegistryValue(RegistryView.Registry64, "SiteId");
            string? ftpHost = ReadRegistryValue(RegistryView.Registry32, "FtpHost")
                           ?? ReadRegistryValue(RegistryView.Registry64, "FtpHost");
            string? ftpPath = ReadRegistryValue(RegistryView.Registry32, "FtpPath")
                           ?? ReadRegistryValue(RegistryView.Registry64, "FtpPath");

            if (tenantId != null || siteId != null)
            {
                _logger.Log($"[ConfigManager] Found registry values: tenantId={tenantId}, siteId={siteId}, ftpHost={ftpHost}, ftpPath={ftpPath}");
                return new FullConfig
                {
                    ApiUrl = DefaultConfig.ApiUrl,
                    ApiKey = DefaultConfig.ApiKey,
                    SiteId = siteId != null && int.TryParse(siteId, out var sid) ? sid : DefaultConfig.SiteId,
                    TenantId = tenantId != null && int.TryParse(tenantId, out var tid) ? tid : DefaultConfig.TenantId,
                    FtpEnabled = !string.IsNullOrEmpty(ftpHost),
                    FtpHost = ftpHost ?? DefaultConfig.FtpHost,
                    FtpPath = ftpPath ?? DefaultConfig.FtpPath,
                    FtpPollInterval = DefaultConfig.FtpPollInterval
                };
            }
        }
        catch (Exception ex)
        {
            _logger.Log($"[ConfigManager] Error reading registry: {ex.Message}");
        }

        return null;
    }

    private static string? ReadRegistryValue(RegistryView view, string valueName)
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using var key = baseKey.OpenSubKey(@"Software\PEI Data Systems\PEI Site App");
            return key?.GetValue(valueName) as string;
        }
        catch { return null; }
    }

    public FullConfig GetConfig()
    {
        lock (_lock) { return Clone(_config); }
    }

    public FtpConfig GetFtpConfig()
    {
        lock (_lock) { return _config.ToFtpConfig(); }
    }

    public void UpdateConfig(object updates)
    {
        lock (_lock)
        {
            var json = JsonSerializer.Serialize(updates, JsonOptions);
            var partial = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, JsonOptions);
            if (partial == null) return;

            foreach (var kvp in partial)
            {
                switch (kvp.Key)
                {
                    case "apiUrl" when kvp.Value.ValueKind == JsonValueKind.String:
                        _config.ApiUrl = kvp.Value.GetString()!; break;
                    case "apiKey" when kvp.Value.ValueKind == JsonValueKind.String:
                        _config.ApiKey = kvp.Value.GetString()!; break;
                    case "siteId" when kvp.Value.ValueKind == JsonValueKind.Number:
                        _config.SiteId = kvp.Value.GetInt32(); break;
                    case "tenantId" when kvp.Value.ValueKind == JsonValueKind.Number:
                        _config.TenantId = kvp.Value.GetInt32(); break;
                    case "ftpEnabled":
                        if (kvp.Value.ValueKind == JsonValueKind.True) _config.FtpEnabled = true;
                        else if (kvp.Value.ValueKind == JsonValueKind.False) _config.FtpEnabled = false;
                        break;
                    case "ftpHost" when kvp.Value.ValueKind == JsonValueKind.String:
                        _config.FtpHost = kvp.Value.GetString()!; break;
                    case "ftpPath" when kvp.Value.ValueKind == JsonValueKind.String:
                        _config.FtpPath = kvp.Value.GetString()!; break;
                    case "ftpPollInterval" when kvp.Value.ValueKind == JsonValueKind.Number:
                        _config.FtpPollInterval = kvp.Value.GetInt32(); break;
                }
            }

            SaveConfig(_config);
        }
    }

    private void SaveConfig(FullConfig config)
    {
        try
        {
            var json = JsonSerializer.Serialize(config, JsonOptions);
            File.WriteAllText(_configPath, json);
            _logger.Log($"[ConfigManager] Saved config to: {_configPath}");
        }
        catch (Exception ex)
        {
            _logger.Log($"[ConfigManager] Error saving config: {ex.Message}");
        }
    }

    private static FullConfig MergeWithDefaults(FullConfig config)
    {
        return new FullConfig
        {
            ApiUrl = string.IsNullOrEmpty(config.ApiUrl) ? DefaultConfig.ApiUrl : config.ApiUrl,
            ApiKey = string.IsNullOrEmpty(config.ApiKey) ? DefaultConfig.ApiKey : config.ApiKey,
            SiteId = config.SiteId == 0 ? DefaultConfig.SiteId : config.SiteId,
            TenantId = config.TenantId == 0 ? DefaultConfig.TenantId : config.TenantId,
            FtpEnabled = config.FtpEnabled,
            FtpHost = config.FtpHost ?? DefaultConfig.FtpHost,
            FtpPath = string.IsNullOrEmpty(config.FtpPath) ? DefaultConfig.FtpPath : config.FtpPath,
            FtpPollInterval = config.FtpPollInterval == 0 ? DefaultConfig.FtpPollInterval : config.FtpPollInterval
        };
    }

    private static FullConfig Clone(FullConfig c) => new()
    {
        ApiUrl = c.ApiUrl,
        ApiKey = c.ApiKey,
        SiteId = c.SiteId,
        TenantId = c.TenantId,
        FtpEnabled = c.FtpEnabled,
        FtpHost = c.FtpHost,
        FtpPath = c.FtpPath,
        FtpPollInterval = c.FtpPollInterval
    };
}
