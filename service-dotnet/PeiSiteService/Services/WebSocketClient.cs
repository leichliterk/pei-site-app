using System.Text.Json;
using PeiSiteService.Models;
using SocketIOClient;
using SocketIOClient.Transport;

namespace PeiSiteService.Services;

public class WebSocketClient : IDisposable
{
    private SocketIOClient.SocketIO? _socket;
    private ServiceConfig _config;
    private readonly FileLogger _logger;
    private readonly object _lock = new();

    private ConnectionStatus _status = ConnectionStatus.disconnected;
    private DateTime? _connectedAt;
    private int[] _connectionHistory = new int[300];
    private Timer? _historyTimer;
    private volatile bool _stopping;

    public event Action<ConnectionStatus>? StatusChanged;
    public event Action<FtpFileAck>? FtpFileAckReceived;

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

    public WebSocketClient(ServiceConfig config, FileLogger logger)
    {
        _config = config;
        _logger = logger;
    }

    public void Connect()
    {
        if (_socket?.Connected == true) return;

        _stopping = false;
        StartHistoryTracking();
        SetStatus(ConnectionStatus.connecting);

        var url = $"{_config.ApiUrl}/desktop";
        _logger.Log($"[WebSocketClient] Connecting to: {url}");

        _socket = new SocketIOClient.SocketIO(new Uri(url), new SocketIOClient.SocketIOOptions
        {
            Auth = new
            {
                api_key = _config.ApiKey,
                site_id = _config.SiteId,
                tenant_id = _config.TenantId,
                connection_source = "service"
            },
            Reconnection = false,
            Transport = TransportProtocol.WebSocket
        });

        _socket.OnConnected += (s, e) =>
        {
            _logger.Log("[WebSocketClient] Connected");
            lock (_lock) { _connectedAt = DateTime.UtcNow; }
            SetStatus(ConnectionStatus.connected);
        };

        _socket.OnDisconnected += (s, reason) =>
        {
            _logger.Log($"[WebSocketClient] Disconnected: {reason}");
            lock (_lock) { _connectedAt = null; }
            SetStatus(ConnectionStatus.disconnected);

            // Auto-reconnect on unexpected disconnects (not during intentional shutdown)
            if (!_stopping)
            {
                _logger.Log("[WebSocketClient] Unexpected disconnect, will reconnect in 5 seconds...");
                _ = Task.Run(async () =>
                {
                    await Task.Delay(5000);
                    if (!_stopping)
                    {
                        _logger.Log("[WebSocketClient] Attempting reconnect...");
                        SetStatus(ConnectionStatus.connecting);
                        try
                        {
                            await _socket.ConnectAsync();
                            _logger.Log("[WebSocketClient] Reconnect succeeded");
                        }
                        catch (Exception ex)
                        {
                            _logger.Log($"[WebSocketClient] Reconnect failed: {ex.Message}, retrying in 10s...");
                            await Task.Delay(10000);
                            if (!_stopping) Connect();
                        }
                    }
                });
            }
        };

        _socket.OnError += (s, error) =>
        {
            _logger.Log($"[WebSocketClient] Connection error: {error}");
            SetStatus(ConnectionStatus.error);
        };

        _socket.OnAny(async (name, ctx) =>
        {
            _logger.Log($"[WebSocketClient] Event: {name}");
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
                _logger.Log($"[WebSocketClient] Error parsing ftp:file_ack: {ex.Message}");
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
                    _logger.Log("[WebSocketClient] ConnectAsync completed");
                    break; // Connected successfully
                }
                catch (Exception ex)
                {
                    _logger.Log($"[WebSocketClient] ConnectAsync exception: {ex.Message}");
                    SetStatus(ConnectionStatus.error);
                    _logger.Log("[WebSocketClient] Will retry connection in 10 seconds...");
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
            _logger.Log("[WebSocketClient] Config changed, reconnecting...");
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

        _logger.Log($"[WebSocketClient] History prepopulated from {sessions.Count} sessions");
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
