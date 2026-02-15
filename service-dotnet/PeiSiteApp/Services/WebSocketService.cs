using PeiSiteApp.Models;
using SocketIOClient;
using SocketIOClient.Transport;

namespace PeiSiteApp.Services;

public class WebSocketService : IDisposable
{
    private SocketIOClient.SocketIO? _socket;
    private readonly object _lock = new();
    private Timer? _historyTimer;
    private volatile bool _stopping;

    private ConnectionStatus _status = ConnectionStatus.Disconnected;
    private DateTime? _connectedAt;
    private int[] _connectionHistory = new int[300];

    public event Action<ConnectionStatus>? StatusChanged;

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
                if (_connectedAt == null || _status != ConnectionStatus.Connected) return 0;
                return (long)(DateTime.UtcNow - _connectedAt.Value).TotalMilliseconds;
            }
        }
    }

    public void Connect(string apiUrl, string apiKey, int siteId, int tenantId)
    {
        if (_socket?.Connected == true) return;

        _stopping = false;
        StartHistoryTracking();
        SetStatus(ConnectionStatus.Connecting);

        var url = $"{apiUrl}/desktop";

        _socket = new SocketIOClient.SocketIO(new Uri(url), new SocketIOClient.SocketIOOptions
        {
            Auth = new
            {
                api_key = apiKey,
                site_id = siteId,
                tenant_id = tenantId,
                connection_source = "app"
            },
            Reconnection = true,
            Transport = TransportProtocol.WebSocket
        });

        _socket.OnConnected += (s, e) =>
        {
            lock (_lock) { _connectedAt = DateTime.UtcNow; }
            SetStatus(ConnectionStatus.Connected);
        };

        _socket.OnDisconnected += (s, reason) =>
        {
            lock (_lock) { _connectedAt = null; }
            SetStatus(ConnectionStatus.Disconnected);
        };

        _socket.OnError += (s, error) =>
        {
            SetStatus(ConnectionStatus.Error);
        };

        _ = Task.Run(async () =>
        {
            while (!_stopping)
            {
                try
                {
                    await _socket.ConnectAsync();
                    break;
                }
                catch
                {
                    SetStatus(ConnectionStatus.Error);
                    await Task.Delay(10000);
                    if (!_stopping) SetStatus(ConnectionStatus.Connecting);
                }
            }
        });
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
        SetStatus(ConnectionStatus.Disconnected);
    }

    public void PrepopulateHistory(List<UptimeSession> sessions)
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
                var isConnected = _status == ConnectionStatus.Connected ? 1 : 0;
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
