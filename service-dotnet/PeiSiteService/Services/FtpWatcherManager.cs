using PeiSiteService.Models;

namespace PeiSiteService.Services;

public class FtpWatcherManager
{
    private readonly Dictionary<string, FtpWatcher> _watchers = new();
    private readonly WebSocketClient _wsClient;
    private readonly PendingFileQueue _pendingQueue;
    private readonly FileLogger _logger;
    private readonly object _lock = new();
    private int _siteId;
    private int _tenantId;
    private bool _enabled;

    public FtpWatcherManager(WebSocketClient wsClient, PendingFileQueue pendingQueue, FileLogger logger)
    {
        _wsClient = wsClient;
        _pendingQueue = pendingQueue;
        _logger = logger;

    }

    public void Initialize(FtpConfig ftpConfig, int siteId, int tenantId)
    {
        _siteId = siteId;
        _tenantId = tenantId;
        _enabled = ftpConfig.FtpEnabled;

        lock (_lock)
        {
            foreach (var server in ftpConfig.Servers)
            {
                var watcher = new FtpWatcher(server, _wsClient, _pendingQueue, _siteId, _tenantId, _logger);
                _watchers[server.Id] = watcher;
            }
        }

        _logger.Log($"[FtpWatcherManager] Initialized with {ftpConfig.Servers.Count} servers, enabled={_enabled}");
    }

    public void StartAll()
    {
        if (!_enabled)
        {
            _logger.Log("[FtpWatcherManager] FTP is disabled, not starting watchers");
            return;
        }

        lock (_lock)
        {
            foreach (var watcher in _watchers.Values)
                watcher.Start();
        }
    }

    public void StopAll()
    {
        lock (_lock)
        {
            foreach (var watcher in _watchers.Values)
                watcher.Stop();
        }
    }

    public void SetEnabled(bool enabled)
    {
        _enabled = enabled;
        if (enabled)
            StartAll();
        else
            StopAll();
    }

    public FtpServerConfig AddServer(FtpServerConfig server)
    {
        lock (_lock)
        {
            var watcher = new FtpWatcher(server, _wsClient, _pendingQueue, _siteId, _tenantId, _logger);
            _watchers[server.Id] = watcher;
            if (_enabled) watcher.Start();
            _logger.Log($"[FtpWatcherManager] Added server {server.Id}: {server.FtpHost}");
            return server;
        }
    }

    public bool UpdateServer(FtpServerConfig updated)
    {
        lock (_lock)
        {
            if (!_watchers.TryGetValue(updated.Id, out var watcher))
                return false;

            watcher.Stop();
            watcher.UpdateConfig(updated);
            if (_enabled) watcher.Start();
            _logger.Log($"[FtpWatcherManager] Updated server {updated.Id}: {updated.FtpHost}");
            return true;
        }
    }

    public bool RemoveServer(string id)
    {
        lock (_lock)
        {
            if (!_watchers.TryGetValue(id, out var watcher))
                return false;

            watcher.Stop();
            _watchers.Remove(id);
            _logger.Log($"[FtpWatcherManager] Removed server {id}");
            return true;
        }
    }

    public FtpOverallStatus GetOverallStatus()
    {
        lock (_lock)
        {
            return new FtpOverallStatus
            {
                FtpEnabled = _enabled,
                Servers = _watchers.Values.Select(w => w.GetStatus()).ToList()
            };
        }
    }

    public async Task<FtpTestResult> TestConnectionAsync(string host, string path)
    {
        await PauseWatchersForHostAsync(host);
        try
        {
            var tempConfig = new FtpServerConfig { FtpHost = host, FtpPath = path };
            var tempWatcher = new FtpWatcher(tempConfig, _wsClient, _pendingQueue, _siteId, _tenantId, _logger);
            return await tempWatcher.TestConnectionAsync(host, path);
        }
        finally
        {
            ResumeWatchersForHost(host);
        }
    }

    public async Task<FtpBrowseResult> BrowseDirectoryAsync(string host, string path)
    {
        await PauseWatchersForHostAsync(host);
        try
        {
            var tempConfig = new FtpServerConfig { FtpHost = host, FtpPath = path };
            var tempWatcher = new FtpWatcher(tempConfig, _wsClient, _pendingQueue, _siteId, _tenantId, _logger);
            return await tempWatcher.ListDirectoriesAsync(host, path);
        }
        finally
        {
            ResumeWatchersForHost(host);
        }
    }

    /// <summary>
    /// Stops watchers targeting the given host and waits for any active poll to finish.
    /// This prevents concurrent connections to FTP servers that only support one connection.
    /// </summary>
    private async Task PauseWatchersForHostAsync(string host)
    {
        List<FtpWatcher> matchingWatchers;
        lock (_lock)
        {
            matchingWatchers = _watchers.Values
                .Where(w => w.Host.Equals(host, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        foreach (var watcher in matchingWatchers)
        {
            watcher.Stop(); // Stop the timer so no new polls start
            _logger.Log($"[FtpWatcherManager] Paused watcher {watcher.Id} for host {host}");
        }

        // Wait for any in-progress polls to finish (up to 60 seconds)
        var timeout = DateTime.UtcNow.AddSeconds(60);
        while (matchingWatchers.Any(w => w.IsCurrentlyPolling) && DateTime.UtcNow < timeout)
        {
            await Task.Delay(500);
        }

        if (matchingWatchers.Any(w => w.IsCurrentlyPolling))
        {
            _logger.Log($"[FtpWatcherManager] Warning: watcher for {host} still polling after timeout");
        }
    }

    /// <summary>
    /// Restarts watchers for the given host after a test/browse operation.
    /// </summary>
    private void ResumeWatchersForHost(string host)
    {
        if (!_enabled) return;

        lock (_lock)
        {
            foreach (var watcher in _watchers.Values
                .Where(w => w.Host.Equals(host, StringComparison.OrdinalIgnoreCase)))
            {
                watcher.Start();
                _logger.Log($"[FtpWatcherManager] Resumed watcher {watcher.Id} for host {host}");
            }
        }
    }
}
