using System.Text.Json;
using System.Text.Json.Serialization;
using PeiSiteService.Models;
using PeiSiteService.Plc;
using SocketIOClient;
using SocketIOClient.Transport;

namespace PeiSiteService.Services;

public class WebSocketClient : IDisposable
{
    private SocketIOClient.SocketIO? _socket;
    private ServiceConfig _config;
    private readonly FileLogger _logger;
    private readonly FtpWatcherManager _ftpManager;
    private readonly FileQueue _fileQueue;
    private readonly object _lock = new();

    private ConnectionStatus _status = ConnectionStatus.disconnected;
    private DateTime? _connectedAt;
    private int[] _connectionHistory = new int[300];
    private Timer? _historyTimer;
    private volatile bool _stopping;

    public event Action<ConnectionStatus>? StatusChanged;
    public event Action<FtpFileAck>? FtpFileAckReceived;
    public event Action<ServiceNotification>? NotificationReceived;

    private TaskCompletionSource<bool>? _otaAckSource;
    private TagBrowserState? _tagBrowserState;
    private PlcSnapshotState? _snapshotState;

    public ConnectionStatus Status
    {
        get { lock (_lock) { return _status; } }
    }

    public DateTime? ConnectedAt
    {
        get { lock (_lock) { return _connectedAt; } }
    }

    public int[] ConnectionHistory
    {
        get { lock (_lock) { return (int[])_connectionHistory.Clone(); } }
    }

    public long CurrentUptime
    {
        get
        {
            lock (_lock)
            {
                if (_connectedAt == null || _status != ConnectionStatus.connected) return 0;
                return (long)(DateTime.UtcNow - _connectedAt.Value).TotalMilliseconds;
            }
        }
    }

    public WebSocketClient(ServiceConfig config, FileLogger logger, FtpWatcherManager ftpManager, FileQueue fileQueue)
    {
        _config = config;
        _logger = logger;
        _ftpManager = ftpManager;
        _fileQueue = fileQueue;

        // Emit ftp:status whenever queue depth changes (enqueue or terminal transition)
        _fileQueue.QueueDepthChanged += () => EmitFtpStatus();
    }

    public void Connect()
    {
        if (_socket?.Connected == true) return;

        // Dispose any stale disconnected socket before creating a new one so the
        // server sees a clean connection rather than a lingering session.
        var stale = _socket;
        _socket = null;
        try { stale?.Dispose(); } catch { }

        _stopping = false;
        StartHistoryTracking();
        SetStatus(ConnectionStatus.connecting);

        var url = $"{_config.ApiUrl}/desktop";
        _logger.Log(ServiceLogLevel.Info, $"[WebSocketClient] Connecting to: {url}");

        _socket = new SocketIOClient.SocketIO(new Uri(url), new SocketIOClient.SocketIOOptions
        {
            Auth = new
            {
                api_key = _config.ApiKey,
                site_id = _config.SiteId,
                tenant_id = _config.TenantId,
                connection_source = "service",
                app_version = AppVersion.Current
            },
            Reconnection = false,
            Transport = TransportProtocol.WebSocket
        });

        _socket.OnConnected += (s, e) =>
        {
            _logger.Log(ServiceLogLevel.Info, "[WebSocketClient] Connected");
            lock (_lock) { _connectedAt = DateTime.UtcNow; }
            SetStatus(ConnectionStatus.connected);
            EmitServiceStatus();
            EmitFtpStatus();
            EmitPlcTags();
        };

        _socket.OnDisconnected += (s, reason) =>
        {
            _logger.Log(ServiceLogLevel.Info, $"[WebSocketClient] Disconnected: {reason}");
            lock (_lock) { _connectedAt = null; }
            SetStatus(ConnectionStatus.disconnected);

            // Auto-reconnect on unexpected disconnects (not during intentional shutdown)
            if (!_stopping)
            {
                _logger.Log(ServiceLogLevel.Warning, "[WebSocketClient] Unexpected disconnect, will reconnect in 5 seconds...");
                // Capture a local reference so the retry targets the specific socket
                // that disconnected, not whatever _socket points to after a race.
                var disconnectedSocket = _socket;
                _ = Task.Run(async () =>
                {
                    await Task.Delay(5000);
                    if (!_stopping)
                    {
                        _logger.Log(ServiceLogLevel.Info, "[WebSocketClient] Attempting reconnect...");
                        SetStatus(ConnectionStatus.connecting);
                        try
                        {
                            await disconnectedSocket.ConnectAsync();
                            _logger.Log(ServiceLogLevel.Info, "[WebSocketClient] Reconnect succeeded");
                        }
                        catch (Exception ex)
                        {
                            _logger.Log(ServiceLogLevel.Warning, $"[WebSocketClient] Reconnect failed: {ex.Message}, retrying in 10s...");
                            // Discard the old socket so Connect() starts completely fresh.
                            lock (_lock) { if (_socket == disconnectedSocket) _socket = null; }
                            try { disconnectedSocket.Dispose(); } catch { }
                            await Task.Delay(10000);
                            if (!_stopping) Connect();
                        }
                    }
                });
            }
        };

        _socket.OnError += (s, error) =>
        {
            _logger.Log(ServiceLogLevel.Error, $"[WebSocketClient] Connection error: {error}");
            SetStatus(ConnectionStatus.error);
        };

        _socket.OnAny(async (name, ctx) =>
        {
            _logger.Log(ServiceLogLevel.Debug, $"[WebSocketClient] Event: {name}");
            await Task.CompletedTask;
        });

        _socket.On("ftp:file_ack", response =>
        {
            try
            {
                var ack = response.GetValue<FtpFileAck>();
                FtpFileAckReceived?.Invoke(ack);
            }
            catch (Exception ex)
            {
                _logger.Log(ServiceLogLevel.Error, $"[WebSocketClient] Error parsing ftp:file_ack: {ex.Message}");
            }
        });

        _socket.On("ota:response_ack", response =>
        {
            try
            {
                var ack = response.GetValue<OtaResponseAck>();
                _otaAckSource?.TrySetResult(ack.Success);
            }
            catch
            {
                _otaAckSource?.TrySetResult(false);
            }
        });

        _socket.On("notification", response =>
        {
            try
            {
                var notification = response.GetValue<ServiceNotification>();
                notification.ReceivedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
                _logger.Log(ServiceLogLevel.Info, $"[WebSocketClient] Notification received: {notification.Id} — {notification.Title}");
                NotificationReceived?.Invoke(notification);
            }
            catch (Exception ex)
            {
                _logger.Log(ServiceLogLevel.Error, $"[WebSocketClient] Error parsing notification: {ex.Message}");
            }
        });

        _socket.On("service:command", response =>
        {
            try
            {
                var cmd = response.GetValue<ServiceCommand>();
                _logger.Log(ServiceLogLevel.Info, $"[WebSocketClient] service:command received: {cmd.Action}");
                EmitToServer("service:command_ack", new { action = cmd.Action, success = true });

                if (cmd.Action == "stop" || cmd.Action == "restart")
                {
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(300); // let ack flush
                        Environment.Exit(0);
                    });
                }
                // "start" is a no-op — we are already running
            }
            catch (Exception ex)
            {
                _logger.Log(ServiceLogLevel.Error, $"[WebSocketClient] Error handling service:command: {ex.Message}");
                try { EmitToServer("service:command_ack", new { action = "unknown", success = false, error = ex.Message }); } catch { }
            }
        });

        _socket.On("ftp:command", response =>
        {
            try
            {
                var cmd = response.GetValue<FtpCommand>();
                _logger.Log(ServiceLogLevel.Info, $"[WebSocketClient] ftp:command received: {cmd.Action}");

                switch (cmd.Action)
                {
                    case "pause":       _ftpManager.PauseAll(); break;
                    case "resume":      _ftpManager.ResumeAll(); break;
                    case "full-upload": _ftpManager.TriggerFullUploadAll(); break;
                    default:
                        throw new InvalidOperationException($"Unknown ftp:command action: {cmd.Action}");
                }

                EmitToServer("ftp:command_ack", new { action = cmd.Action, success = true });
                EmitFtpStatus();
            }
            catch (Exception ex)
            {
                _logger.Log(ServiceLogLevel.Error, $"[WebSocketClient] Error handling ftp:command: {ex.Message}");
                try { EmitToServer("ftp:command_ack", new { action = "unknown", success = false, error = ex.Message }); } catch { }
            }
        });

        // We handle reconnection ourselves in OnDisconnected, so Reconnection = false.
        // Outer retry loop handles initial connection failures.
        _ = Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    await _socket.ConnectAsync();
                    _logger.Log(ServiceLogLevel.Info, "[WebSocketClient] ConnectAsync completed");
                    break; // Connected successfully
                }
                catch (Exception ex)
                {
                    _logger.Log(ServiceLogLevel.Warning, $"[WebSocketClient] ConnectAsync exception: {ex.Message}");
                    SetStatus(ConnectionStatus.error);
                    _logger.Log(ServiceLogLevel.Warning, "[WebSocketClient] Will retry connection in 10 seconds...");
                    await Task.Delay(10000);
                    SetStatus(ConnectionStatus.connecting);
                }
            }
        });
    }

    public async Task DisconnectAsync()
    {
        _stopping = true;
        StopHistoryTracking();
        if (_socket != null)
        {
            await _socket.DisconnectAsync();
            _socket.Dispose();
            _socket = null;
        }
        SetStatus(ConnectionStatus.disconnected);
    }

    public void Disconnect()
    {
        _stopping = true;
        StopHistoryTracking();
        if (_socket != null)
        {
            _socket.DisconnectAsync().GetAwaiter().GetResult();
            _socket.Dispose();
            _socket = null;
        }
        SetStatus(ConnectionStatus.disconnected);
    }

    private static readonly JsonSerializerOptions _emitJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public void EmitToServer(string eventName, object data)
    {
        if (_socket?.Connected == true)
        {
            // Serialize explicitly with camelCase so the library sends exactly
            // what we expect — no ambiguity from params-array wrapping.
            var json = JsonSerializer.Serialize(data, data.GetType(), _emitJsonOptions);
            using var doc = JsonDocument.Parse(json);
            _ = _socket.EmitAsync(eventName, doc.RootElement);
        }
    }

    public async Task<bool> EmitOtaResponseAsync(string releaseId, bool accepted)
    {
        _otaAckSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        EmitToServer("ota:response", new { release_id = releaseId, accepted });
        var completed = await Task.WhenAny(_otaAckSource.Task, Task.Delay(TimeSpan.FromSeconds(30)));
        return completed == _otaAckSource.Task && await _otaAckSource.Task;
    }

    public void EmitOtaInstalled(string releaseId, string version)
    {
        EmitToServer("ota:installed", new { release_id = releaseId, version });
    }

    public void UpdateConfig(ConfigUpdateRequest updates)
    {
        bool needsReconnect = false;
        lock (_lock)
        {
            if (_socket?.Connected == true)
            {
                needsReconnect = updates.ApiUrl != null || updates.ApiKey != null
                              || updates.SiteId != null || updates.TenantId != null;
            }
            if (updates.ApiUrl != null) _config.ApiUrl = updates.ApiUrl;
            if (updates.ApiKey != null) _config.ApiKey = updates.ApiKey;
            if (updates.SiteId.HasValue) _config.SiteId = updates.SiteId.Value;
            if (updates.TenantId.HasValue) _config.TenantId = updates.TenantId.Value;
        }

        if (needsReconnect)
        {
            _logger.Log(ServiceLogLevel.Info, "[WebSocketClient] Config changed, reconnecting...");
            Disconnect();
            Connect();
        }
    }

    public void PrepopulateHistory(List<SessionInfo> sessions)
    {
        var now = DateTime.UtcNow;
        var fiveMinutesAgo = now.AddSeconds(-300);

        lock (_lock)
        {
            _connectionHistory = new int[300];

            foreach (var session in sessions)
            {
                if (!DateTime.TryParse(session.ConnectedAt, out var connectedAt)) continue;
                connectedAt = connectedAt.ToUniversalTime();

                var disconnectedAt = session.DisconnectedAt != null
                    && DateTime.TryParse(session.DisconnectedAt, out var da)
                    ? da.ToUniversalTime() : now;

                if (disconnectedAt < fiveMinutesAgo) continue;

                var sessionStart = connectedAt > fiveMinutesAgo ? connectedAt : fiveMinutesAgo;
                var sessionEnd = disconnectedAt < now ? disconnectedAt : now;

                var startIndex = (int)(sessionStart - fiveMinutesAgo).TotalSeconds;
                var endIndex = (int)(sessionEnd - fiveMinutesAgo).TotalSeconds;

                for (int i = Math.Max(0, startIndex); i <= Math.Min(299, endIndex); i++)
                    _connectionHistory[i] = 1;
            }
        }

        _logger.Log(ServiceLogLevel.Debug, $"[WebSocketClient] History prepopulated from {sessions.Count} sessions");
    }

    /// <summary>
    /// Wire up PLC services after the DI container is built.
    /// Starts a background consumer for plc:snapshot and subscribes to TagsUpdated.
    /// </summary>
    public void AttachPlcServices(PlcPollingService pollingService, TagBrowserState tagBrowserState, PlcSnapshotState snapshotState)
    {
        _tagBrowserState = tagBrowserState;
        _snapshotState = snapshotState;

        _tagBrowserState.TagsUpdated += EmitPlcTags;

        // Background consumer: read snapshots from the channel, cache them, and emit to server
        _ = Task.Run(async () =>
        {
            await foreach (var snapshot in pollingService.Snapshots.ReadAllAsync())
            {
                _snapshotState.Update(snapshot);
                EmitPlcSnapshot(snapshot);
            }
        });
    }

    private void EmitServiceStatus()
    {
        EmitToServer("service:status", new { state = "running" });
    }

    private void EmitFtpStatus()
    {
        EmitToServer("ftp:status", new { paused = _ftpManager.IsPaused, queueDepth = _fileQueue.PendingCount });
    }

    private void EmitPlcSnapshot(PlcSnapshot snapshot)
    {
        EmitToServer("plc:snapshot", new
        {
            ipAddress = snapshot.IpAddress,
            slot = snapshot.Slot,
            timestamp = snapshot.Timestamp,
            connected = snapshot.Connected,
            tags = snapshot.Tags.Select(t => new
            {
                name = t.Name,
                dataType = t.DataType,
                value = t.Value,
                displayName = t.DisplayName,
                unit = t.Unit,
                error = t.Error,
                errorMessage = t.ErrorMessage
            })
        });
    }

    private void EmitPlcTags()
    {
        if (_tagBrowserState == null) return;
        EmitToServer("plc:tags", new
        {
            tags = _tagBrowserState.Tags.Select(t => new
            {
                name = t.Name,
                dataType = t.DataType,
                program = t.Program
            })
        });
    }

    private void SetStatus(ConnectionStatus status)
    {
        bool changed;
        lock (_lock)
        {
            changed = _status != status;
            _status = status;
        }
        if (changed) StatusChanged?.Invoke(status);
    }

    private void StartHistoryTracking()
    {
        if (_historyTimer != null) return;
        _historyTimer = new Timer(_ =>
        {
            lock (_lock)
            {
                var isConnected = _status == ConnectionStatus.connected ? 1 : 0;
                Array.Copy(_connectionHistory, 1, _connectionHistory, 0, 299);
                _connectionHistory[299] = isConnected;
            }
        }, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    private void StopHistoryTracking()
    {
        _historyTimer?.Dispose();
        _historyTimer = null;
    }

    public void Dispose()
    {
        StopHistoryTracking();
        _socket?.Dispose();
    }
}
