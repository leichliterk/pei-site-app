using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using PeiSiteApp.Models;

namespace PeiSiteApp.Services;

public class LocalServiceClient : IDisposable
{
    private const string BaseUrl = "http://127.0.0.1:47836";
    private readonly HttpClient _http;
    private Timer? _pollTimer;
    private bool _isPolling;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public ServiceStatusResponse? CurrentStatus { get; private set; }
    public bool IsServiceAvailable { get; private set; }

    public event Action<ServiceStatusResponse?>? StatusChanged;

    public LocalServiceClient()
    {
        _http = new HttpClient
        {
            BaseAddress = new Uri(BaseUrl),
            Timeout = TimeSpan.FromSeconds(30)
        };
    }

    public async Task<bool> CheckHealthAsync()
    {
        try
        {
            var response = await _http.GetAsync("/health");
            IsServiceAvailable = response.IsSuccessStatusCode;
            return IsServiceAvailable;
        }
        catch
        {
            IsServiceAvailable = false;
            return false;
        }
    }

    public void StartPolling()
    {
        if (_isPolling) return;
        _isPolling = true;

        // Initial fetch
        _ = FetchStatusAsync();

        // Poll every second
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
                var status = JsonSerializer.Deserialize<ServiceStatusResponse>(json, JsonOptions);
                IsServiceAvailable = true;
                CurrentStatus = status;
                StatusChanged?.Invoke(status);
            }
            else
            {
                IsServiceAvailable = false;
                CurrentStatus = null;
                StatusChanged?.Invoke(null);
            }
        }
        catch
        {
            IsServiceAvailable = false;
            CurrentStatus = null;
            StatusChanged?.Invoke(null);
        }
    }

    public async Task<bool> ForceReconnectAsync()
    {
        try
        {
            var response = await _http.PostAsync("/reconnect", new StringContent("{}", Encoding.UTF8, "application/json"));
            return response.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<bool> UpdateConfigAsync(object config)
    {
        try
        {
            var response = await _http.PostAsJsonAsync("/config", config);
            return response.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<bool> PrepopulateHistoryAsync(List<UptimeSession> sessions)
    {
        try
        {
            var payload = new { sessions = sessions.Select(s => new { connected_at = s.ConnectedAt, disconnected_at = s.DisconnectedAt }).ToList() };
            var response = await _http.PostAsJsonAsync("/prepopulate-history", payload);
            return response.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    // --- Log endpoints ---

    public async Task<List<LogEntry>?> GetLogsAsync(string? since = null)
    {
        try
        {
            var url = since != null ? $"/logs?since={Uri.EscapeDataString(since)}" : "/logs";
            var response = await _http.GetAsync(url);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<LogsResponse>(json, JsonOptions);
                return result?.Entries;
            }
            return null;
        }
        catch { return null; }
    }

    // --- FTP endpoints ---

    public async Task<FtpOverallStatusResponse?> FtpGetStatusAsync()
    {
        try
        {
            var response = await _http.GetAsync("/ftp/status");
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<FtpOverallStatusResponse>(json, JsonOptions);
            }
            return null;
        }
        catch { return null; }
    }

    public async Task<bool> FtpSetEnabledAsync(bool enabled)
    {
        try
        {
            var response = await _http.PutAsJsonAsync("/ftp/enabled", new { enabled });
            return response.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<FtpServerResponse?> FtpAddServerAsync(string name, string host, string path, int pollInterval, string username = "", string password = "")
    {
        try
        {
            var response = await _http.PostAsJsonAsync("/ftp/servers", new
            {
                name,
                ftpHost = host,
                ftpPath = path,
                ftpPollInterval = pollInterval,
                username,
                password
            });
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<FtpServerResponse>(json, JsonOptions);
            }
            return null;
        }
        catch { return null; }
    }

    public async Task<bool> FtpUpdateServerAsync(string id, string name, string host, string path, int pollInterval, string username = "", string password = "", bool forceFullUploadOnNextPoll = false)
    {
        try
        {
            var response = await _http.PutAsJsonAsync($"/ftp/servers/{id}", new
            {
                name,
                ftpHost = host,
                ftpPath = path,
                ftpPollInterval = pollInterval,
                username,
                password,
                forceFullUploadOnNextPoll
            });
            return response.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<bool> FtpDeleteServerAsync(string id)
    {
        try
        {
            var response = await _http.DeleteAsync($"/ftp/servers/{id}");
            return response.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<FtpTestResult> FtpTestConnectionAsync(string host, string path, string username = "", string password = "")
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var response = await _http.PostAsJsonAsync("/ftp/test", new { host, path, username, password }, cts.Token);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(cts.Token);
                return JsonSerializer.Deserialize<FtpTestResult>(json, JsonOptions) ?? new FtpTestResult { Success = false, Message = "Invalid response" };
            }
            return new FtpTestResult { Success = false, Message = "Service returned error" };
        }
        catch (OperationCanceledException)
        {
            return new FtpTestResult { Success = false, Message = "Connection test timed out after 30 seconds" };
        }
        catch (Exception ex)
        {
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
                return JsonSerializer.Deserialize<FtpBrowseResponse>(json, JsonOptions)
                    ?? new FtpBrowseResponse { Success = false, Message = "Invalid response" };
            }
            return new FtpBrowseResponse { Success = false, Message = "Service returned error" };
        }
        catch (OperationCanceledException)
        {
            return new FtpBrowseResponse { Success = false, Message = "Browse timed out after 30 seconds" };
        }
        catch (Exception ex)
        {
            return new FtpBrowseResponse { Success = false, Message = ex.Message };
        }
    }

    public async Task<bool> SetLogLevelAsync(string level)
    {
        try
        {
            var response = await _http.PutAsJsonAsync("/log-level", new { level });
            return response.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    // --- Notification endpoints ---

    public async Task<NotificationsResponse?> GetNotificationsAsync()
    {
        try
        {
            var response = await _http.GetAsync("/notifications");
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<NotificationsResponse>(json, JsonOptions);
            }
            return null;
        }
        catch { return null; }
    }

    public async Task<bool> MarkNotificationReadAsync(string id)
    {
        try
        {
            var response = await _http.PostAsync($"/notifications/{id}/read", null);
            return response.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<bool> MarkAllNotificationsReadAsync()
    {
        try
        {
            var response = await _http.PostAsync("/notifications/read-all", null);
            return response.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public void Dispose()
    {
        StopPolling();
        _http.Dispose();
    }
}
