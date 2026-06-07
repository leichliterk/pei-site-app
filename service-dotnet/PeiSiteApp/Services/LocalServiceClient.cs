using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PeiSiteApp.Models;

namespace PeiSiteApp.Services;

public class LocalServiceClient : IDisposable
{
    private const string BaseUrl = "http://127.0.0.1:47836";
    private readonly HttpClient _http;
    private readonly ILogger<LocalServiceClient> _logger;
    private Timer? _pollTimer;
    private bool _isPolling;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public ServiceStatusResponse? CurrentStatus { get; private set; }
    public bool IsServiceAvailable { get; private set; }

    public event Action<ServiceStatusResponse?>? StatusChanged;

    public LocalServiceClient(ILogger<LocalServiceClient> logger)
    {
        _logger = logger;
        _http = new HttpClient
        {
            BaseAddress = new Uri(BaseUrl),
            Timeout = TimeSpan.FromSeconds(30)
        };
    }

    // -------------------------------------------------------------------------
    // Private helpers — eliminate repetitive try/catch boilerplate
    // -------------------------------------------------------------------------

    /// <summary>
    /// GET endpoint, deserialize JSON response. Logs at Warning — use for
    /// user-triggered calls. Background polls use FetchStatusAsync directly.
    /// </summary>
    private async Task<T?> GetJsonAsync<T>(string url)
    {
        try
        {
            var response = await _http.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("GET {Url} returned {Status}", url, (int)response.StatusCode);
                return default;
            }
            var json = await response.Content.ReadAsStringAsync();
            try
            {
                return JsonSerializer.Deserialize<T>(json, JsonOptions);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to deserialize response from GET {Url}", url);
                return default;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET {Url} failed", url);
            return default;
        }
    }

    /// <summary>POST with optional JSON body, returns success flag.</summary>
    private async Task<bool> PostAsync(string url, object? body = null)
    {
        try
        {
            var response = body == null
                ? await _http.PostAsync(url, null)
                : await _http.PostAsJsonAsync(url, body);
            if (!response.IsSuccessStatusCode)
                _logger.LogWarning("POST {Url} returned {Status}", url, (int)response.StatusCode);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "POST {Url} failed", url);
            return false;
        }
    }

    /// <summary>PUT with JSON body, returns success flag.</summary>
    private async Task<bool> PutJsonAsync(string url, object body)
    {
        try
        {
            var response = await _http.PutAsJsonAsync(url, body);
            if (!response.IsSuccessStatusCode)
                _logger.LogWarning("PUT {Url} returned {Status}", url, (int)response.StatusCode);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PUT {Url} failed", url);
            return false;
        }
    }

    /// <summary>PUT with typed JSON body (uses custom JsonOptions).</summary>
    private async Task<bool> PutJsonAsync<T>(string url, T body)
    {
        try
        {
            var response = await _http.PutAsJsonAsync(url, body, JsonOptions);
            if (!response.IsSuccessStatusCode)
                _logger.LogWarning("PUT {Url} returned {Status}", url, (int)response.StatusCode);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PUT {Url} failed", url);
            return false;
        }
    }

    /// <summary>DELETE, returns success flag.</summary>
    private async Task<bool> DeleteAsync(string url)
    {
        try
        {
            var response = await _http.DeleteAsync(url);
            if (!response.IsSuccessStatusCode)
                _logger.LogWarning("DELETE {Url} returned {Status}", url, (int)response.StatusCode);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DELETE {Url} failed", url);
            return false;
        }
    }

    /// <summary>POST with JSON body, deserialize response.</summary>
    private async Task<T?> PostJsonAsync<T>(string url, object body)
    {
        try
        {
            var response = await _http.PostAsJsonAsync(url, body);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("POST {Url} returned {Status}", url, (int)response.StatusCode);
                return default;
            }
            var json = await response.Content.ReadAsStringAsync();
            try
            {
                return JsonSerializer.Deserialize<T>(json, JsonOptions);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to deserialize response from POST {Url}", url);
                return default;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "POST {Url} failed", url);
            return default;
        }
    }

    // -------------------------------------------------------------------------
    // Health / status polling (Debug level — these run every second)
    // -------------------------------------------------------------------------

    public async Task<bool> CheckHealthAsync()
    {
        try
        {
            var response = await _http.GetAsync("/health");
            IsServiceAvailable = response.IsSuccessStatusCode;
            return IsServiceAvailable;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Health check failed");
            IsServiceAvailable = false;
            return false;
        }
    }

    public void StartPolling()
    {
        if (_isPolling) return;
        _isPolling = true;

        _ = FetchStatusAsync();

        _pollTimer = new Timer(async _ =>
        {
            await FetchStatusAsync();
        }, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    public void StopPolling()
    {
        _isPolling = false;
        _pollTimer?.Dispose();
        _pollTimer = null;
    }

    private async Task FetchStatusAsync()
    {
        try
        {
            var response = await _http.GetAsync("/status");
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                try
                {
                    var status = JsonSerializer.Deserialize<ServiceStatusResponse>(json, JsonOptions);
                    IsServiceAvailable = true;
                    CurrentStatus = status;
                    StatusChanged?.Invoke(status);
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Failed to deserialize /status response");
                    IsServiceAvailable = false;
                    CurrentStatus = null;
                    StatusChanged?.Invoke(null);
                }
            }
            else
            {
                _logger.LogDebug("/status returned {Status}", (int)response.StatusCode);
                IsServiceAvailable = false;
                CurrentStatus = null;
                StatusChanged?.Invoke(null);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "/status poll failed");
            IsServiceAvailable = false;
            CurrentStatus = null;
            StatusChanged?.Invoke(null);
        }
    }

    // -------------------------------------------------------------------------
    // Config / misc
    // -------------------------------------------------------------------------

    public Task<bool> ForceReconnectAsync()
        => PostAsync("/reconnect", new { });

    public Task<bool> UpdateConfigAsync(object config)
        => PostAsync("/config", config);

    public async Task<bool> PrepopulateHistoryAsync(List<UptimeSession> sessions)
    {
        var payload = new { sessions = sessions.Select(s => new { connected_at = s.ConnectedAt, disconnected_at = s.DisconnectedAt }).ToList() };
        return await PostAsync("/prepopulate-history", payload);
    }

    public Task<bool> SetLogLevelAsync(string level)
        => PutJsonAsync("/log-level", new { level });

    // -------------------------------------------------------------------------
    // Logs
    // -------------------------------------------------------------------------

    public async Task<List<LogEntry>?> GetLogsAsync(string? since = null)
    {
        var url = since != null ? $"/logs?since={Uri.EscapeDataString(since)}" : "/logs";
        var result = await GetJsonAsync<LogsResponse>(url);
        return result?.Entries;
    }

    // -------------------------------------------------------------------------
    // FTP
    // -------------------------------------------------------------------------

    public Task<FtpOverallStatusResponse?> FtpGetStatusAsync()
        => GetJsonAsync<FtpOverallStatusResponse>("/ftp/status");

    public Task<bool> FtpSetEnabledAsync(bool enabled)
        => PutJsonAsync("/ftp/enabled", new { enabled });

    public Task<FtpServerResponse?> FtpAddServerAsync(string name, string host, string path, int pollInterval, string username = "", string password = "")
        => PostJsonAsync<FtpServerResponse>("/ftp/servers", new { name, ftpHost = host, ftpPath = path, ftpPollInterval = pollInterval, username, password });

    public Task<bool> FtpUpdateServerAsync(string id, string name, string host, string path, int pollInterval, string username = "", string password = "", bool forceFullUploadOnNextPoll = false)
        => PutJsonAsync($"/ftp/servers/{id}", new { name, ftpHost = host, ftpPath = path, ftpPollInterval = pollInterval, username, password, forceFullUploadOnNextPoll });

    public Task<bool> FtpDeleteServerAsync(string id)
        => DeleteAsync($"/ftp/servers/{id}");

    public async Task<FtpTestResult> FtpTestConnectionAsync(string host, string path, string username = "", string password = "")
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var response = await _http.PostAsJsonAsync("/ftp/test", new { host, path, username, password }, cts.Token);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(cts.Token);
                try
                {
                    return JsonSerializer.Deserialize<FtpTestResult>(json, JsonOptions)
                        ?? new FtpTestResult { Success = false, Message = "Empty response" };
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Failed to deserialize /ftp/test response");
                    return new FtpTestResult { Success = false, Message = "Invalid response from service" };
                }
            }
            _logger.LogWarning("POST /ftp/test returned {Status}", (int)response.StatusCode);
            return new FtpTestResult { Success = false, Message = $"Service returned {(int)response.StatusCode}" };
        }
        catch (OperationCanceledException)
        {
            return new FtpTestResult { Success = false, Message = "Connection test timed out after 30 seconds" };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "POST /ftp/test failed");
            return new FtpTestResult { Success = false, Message = ex.Message };
        }
    }

    public async Task<FtpBrowseResponse> FtpBrowseAsync(string host, string path, string username = "", string password = "")
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var response = await _http.PostAsJsonAsync("/ftp/browse", new { host, path, username, password }, cts.Token);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(cts.Token);
                try
                {
                    return JsonSerializer.Deserialize<FtpBrowseResponse>(json, JsonOptions)
                        ?? new FtpBrowseResponse { Success = false, Message = "Empty response" };
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Failed to deserialize /ftp/browse response");
                    return new FtpBrowseResponse { Success = false, Message = "Invalid response from service" };
                }
            }
            _logger.LogWarning("POST /ftp/browse returned {Status}", (int)response.StatusCode);
            return new FtpBrowseResponse { Success = false, Message = $"Service returned {(int)response.StatusCode}" };
        }
        catch (OperationCanceledException)
        {
            return new FtpBrowseResponse { Success = false, Message = "Browse timed out after 30 seconds" };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "POST /ftp/browse failed");
            return new FtpBrowseResponse { Success = false, Message = ex.Message };
        }
    }

    // -------------------------------------------------------------------------
    // OTA
    // -------------------------------------------------------------------------

    public Task<bool> OtaRespondAsync(string releaseId, bool accepted)
        => PostAsync("/ota/respond", new { releaseId, accepted });

    public Task<bool> OtaInstalledAsync(string releaseId, string version)
        => PostAsync("/ota/installed", new { releaseId, version });

    // -------------------------------------------------------------------------
    // Notifications
    // -------------------------------------------------------------------------

    public Task<NotificationsResponse?> GetNotificationsAsync()
        => GetJsonAsync<NotificationsResponse>("/notifications");

    public Task<bool> MarkNotificationReadAsync(string id)
        => PostAsync($"/notifications/{id}/read");

    public Task<bool> MarkAllNotificationsReadAsync()
        => PostAsync("/notifications/read-all");

    // -------------------------------------------------------------------------
    // Queue
    // -------------------------------------------------------------------------

    public Task<QueueResponse?> GetQueueAsync()
        => GetJsonAsync<QueueResponse>("/queue");

    public Task<QueueStatusResponse?> GetQueueStatusAsync()
        => GetJsonAsync<QueueStatusResponse>("/queue/status");

    public Task<bool> QueueRetryAsync(string id)
        => PostAsync($"/queue/{id}/retry");

    public Task<bool> QueueClearHistoryAsync()
        => DeleteAsync("/queue/history");

    public Task<bool> QueuePauseAsync()
        => PostAsync("/queue/pause");

    public Task<bool> QueueResumeAsync()
        => PostAsync("/queue/resume");

    // -------------------------------------------------------------------------
    // PLC
    // -------------------------------------------------------------------------

    public Task<PlcSettingsModel?> PlcGetSettingsAsync()
        => GetJsonAsync<PlcSettingsModel>("/plc/settings");

    public Task<PlcTagsResponse?> PlcGetTagsAsync()
        => GetJsonAsync<PlcTagsResponse>("/plc/tags");

    public Task<bool> PlcRefreshTagsAsync()
        => PostAsync("/plc/tags/refresh");

    public Task<PlcSnapshotResponse?> PlcGetSnapshotAsync()
        => GetJsonAsync<PlcSnapshotResponse>("/plc/snapshot");

    public Task<bool> PlcUpdateSettingsAsync(PlcSettingsModel settings)
        => PutJsonAsync("/plc/settings", settings);

    // -------------------------------------------------------------------------

    public void Dispose()
    {
        StopPolling();
        _http.Dispose();
    }
}
