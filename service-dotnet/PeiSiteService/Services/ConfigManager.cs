using System.Text.Json;
using System.Text.Json.Nodes;
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
        ApiKey = "",
        SiteId = 1000,
        TenantId = 1001,
        FtpEnabled = false,
        FtpServers = new()
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
                    // Decrypt API key if stored as DPAPI-protected blob; migrate plaintext on next save
                    var raw = JsonSerializer.Deserialize<JsonObject>(data, JsonOptions);
                    if (raw != null && raw["apiKeyProtected"] is JsonNode protectedNode)
                    {
                        fileConfig.ApiKey = CredentialProtection.TryUnprotect(protectedNode.GetValue<string>()) ?? fileConfig.ApiKey;
                    }

                    _logger.Log($"[ConfigManager] Loaded config from file: {_configPath}");
                    var merged = MergeWithDefaults(fileConfig);
                    return MigrateIfNeeded(merged);
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
            var migrated = MigrateIfNeeded(registryConfig);
            SaveConfig(migrated);
            return migrated;
        }

        _logger.Log("[ConfigManager] Using default configuration");
        return Clone(DefaultConfig);
    }

    /// <summary>
    /// Migrates legacy single-server FTP config (flat fields) to the new
    /// multi-server list format. Preserves the existing ftp-state.json by
    /// renaming it to match the migrated server's ID.
    /// </summary>
    private FullConfig MigrateIfNeeded(FullConfig config)
    {
        if (!string.IsNullOrEmpty(config.FtpHost) &&
            (config.FtpServers == null || config.FtpServers.Count == 0))
        {
            _logger.Log("[ConfigManager] Migrating legacy single-FTP config to multi-server format");
            config.FtpServers = new List<FtpServerConfig>
            {
                new FtpServerConfig
                {
                    Id = "legacy",
                    FtpHost = config.FtpHost,
                    FtpPath = config.FtpPath,
                    FtpPollInterval = config.FtpPollInterval
                }
            };

            // Clear legacy fields
            config.FtpHost = "";
            config.FtpPath = "/";
            config.FtpPollInterval = 60;

            // Rename state file to match the migrated server's ID
            var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            var stateDir = Path.Combine(programData, "PEI Site Service");
            var oldState = Path.Combine(stateDir, "ftp-state.json");
            var newState = Path.Combine(stateDir, "ftp-state-legacy.json");
            try
            {
                if (File.Exists(oldState) && !File.Exists(newState))
                {
                    File.Move(oldState, newState);
                    _logger.Log("[ConfigManager] Migrated ftp-state.json -> ftp-state-legacy.json");
                }
            }
            catch (Exception ex)
            {
                _logger.Log($"[ConfigManager] Could not migrate state file: {ex.Message}");
            }

            SaveConfig(config);
        }

        config.FtpServers ??= new List<FtpServerConfig>();
        return config;
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

                var config = new FullConfig
                {
                    ApiUrl = DefaultConfig.ApiUrl,
                    ApiKey = DefaultConfig.ApiKey,
                    SiteId = siteId != null && int.TryParse(siteId, out var sid) ? sid : DefaultConfig.SiteId,
                    TenantId = tenantId != null && int.TryParse(tenantId, out var tid) ? tid : DefaultConfig.TenantId,
                    FtpEnabled = !string.IsNullOrEmpty(ftpHost),
                    FtpServers = new()
                };

                // If registry has FTP host, create a server entry directly
                if (!string.IsNullOrEmpty(ftpHost))
                {
                    config.FtpServers.Add(new FtpServerConfig
                    {
                        Id = "legacy",
                        FtpHost = ftpHost,
                        FtpPath = ftpPath ?? "/",
                        FtpPollInterval = 60
                    });
                }

                return config;
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

    // Generic config update (for non-FTP fields like apiUrl, siteId, tenantId)
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
                }
            }

            SaveConfig(_config);
        }
    }

    // FTP global toggle
    public void SetFtpEnabled(bool enabled)
    {
        lock (_lock)
        {
            _config.FtpEnabled = enabled;
            SaveConfig(_config);
        }
    }

    // FTP server CRUD
    public FtpServerConfig AddFtpServer(FtpServerCreateRequest req)
    {
        lock (_lock)
        {
            var server = new FtpServerConfig
            {
                Id = Guid.NewGuid().ToString("N")[..8],
                Name = req.Name,
                FtpHost = req.FtpHost,
                FtpPath = req.FtpPath,
                FtpPollInterval = req.FtpPollInterval,
                Username = req.Username,
                Password = req.Password
            };
            _config.FtpServers.Add(server);
            SaveConfig(_config);
            _logger.Log($"[ConfigManager] Added FTP server {server.Id}: {server.FtpHost}");
            return server;
        }
    }

    public FtpServerConfig? UpdateFtpServer(string id, FtpServerUpdateRequest req)
    {
        lock (_lock)
        {
            var server = _config.FtpServers.FirstOrDefault(s => s.Id == id);
            if (server == null) return null;

            if (req.Name != null) server.Name = req.Name;
            if (req.FtpHost != null) server.FtpHost = req.FtpHost;
            if (req.FtpPath != null) server.FtpPath = req.FtpPath;
            if (req.FtpPollInterval.HasValue) server.FtpPollInterval = req.FtpPollInterval.Value;
            if (req.Username != null) server.Username = req.Username;
            if (req.Password != null) server.Password = req.Password;

            SaveConfig(_config);
            _logger.Log($"[ConfigManager] Updated FTP server {id}: {server.FtpHost}");
            return server;
        }
    }

    public bool RemoveFtpServer(string id)
    {
        lock (_lock)
        {
            var removed = _config.FtpServers.RemoveAll(s => s.Id == id);
            if (removed > 0)
            {
                SaveConfig(_config);
                _logger.Log($"[ConfigManager] Removed FTP server {id}");
                return true;
            }
            return false;
        }
    }

    private void SaveConfig(FullConfig config)
    {
        try
        {
            // Serialize the full config, then replace the plaintext apiKey with an encrypted blob
            var node = JsonNode.Parse(JsonSerializer.Serialize(config, JsonOptions))!.AsObject();
            if (!string.IsNullOrEmpty(config.ApiKey))
            {
                node["apiKeyProtected"] = CredentialProtection.Protect(config.ApiKey);
            }
            node.Remove("apiKey"); // never persist plaintext
            File.WriteAllText(_configPath, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
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
            FtpServers = config.FtpServers ?? new(),
            // Keep legacy fields for migration detection
            FtpHost = config.FtpHost ?? "",
            FtpPath = string.IsNullOrEmpty(config.FtpPath) ? "/" : config.FtpPath,
            FtpPollInterval = config.FtpPollInterval
        };
    }

    private static FullConfig Clone(FullConfig c) => new()
    {
        ApiUrl = c.ApiUrl,
        ApiKey = c.ApiKey,
        SiteId = c.SiteId,
        TenantId = c.TenantId,
        FtpEnabled = c.FtpEnabled,
        FtpServers = c.FtpServers.Select(s => new FtpServerConfig
        {
            Id = s.Id,
            Name = s.Name,
            FtpHost = s.FtpHost,
            FtpPath = s.FtpPath,
            FtpPollInterval = s.FtpPollInterval
        }).ToList()
    };
}
