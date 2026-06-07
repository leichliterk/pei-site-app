using PeiSiteService.Models;

namespace PeiSiteService.Services;

public class FtpWatcherManager
{
    private readonly Dictionary<string, FtpWatcher> _watchers = new();
    private readonly FileQueue _fileQueue;
    private readonly FileLogger _logger;
    private readonly ConfigManager _configManager;
    private readonly object _lock = new();
    private string _siteId = "";
    private int _tenantId;
    private bool _enabled;
    private bool _globalPaused;

    public bool IsPaused => _globalPaused;

    public FtpWatcherManager(FileQueue fileQueue, FileLogger logger, ConfigManager configManager)
    {
        _fileQueue = fileQueue;
        _logger = logger;
        _configManager = configManager;
    }

    public void Initialize(FtpConfig ftpConfig, string siteId, int tenantId)
    {
        _siteId = siteId;
        _tenantId = tenantId;
        _enabled = ftpConfig.FtpEnabled;

        lock (_lock)
        {
            foreach (var server in ftpConfig.Servers)
            {
                var watcher = new FtpWatcher(server, _fileQueue, _siteId, _tenantId, _logger, _configManager.ClearForceFullUpload);
                _watchers[server.Id] = watcher;
            }
        }

        _logger.Log(ServiceLogLevel.Info, $"[FtpWatcherManager] Initialized with {ftpConfig.Servers.Count} servers, enabled={_enabled}");
    }

    public void StartAll()
    {
        if (!_enabled)
        {
            _logger.Log(ServiceLogLevel.Info, "[FtpWatcherManager] FTP is disabled, not starting watchers");
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
            var watcher = new FtpWatcher(server, _fileQueue, _siteId, _tenantId, _logger, _configManager.ClearForceFullUpload);
            _watchers[server.Id] = watcher;
            if (_enabled) watcher.Start();
            _logger.Log(ServiceLogLevel.Info, $"[FtpWatcherManager] Added server {server.Id}: {server.FtpHost}");
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
            _logger.Log(ServiceLogLevel.Info, $"[FtpWatcherManager] Updated server {updated.Id}: {updated.FtpHost}");
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
            _logger.Log(ServiceLogLevel.Info, $"[FtpWatcherManager] Removed server {id}");
            return true;
        }
    }

    public void PauseAll()
    {
        _globalPaused = true;
        lock (_lock)
        {
            foreach (var watcher in _watchers.Values)
                watcher.Pause();
        }
        _logger.Log(ServiceLogLevel.Info, "[FtpWatcherManager] All watchers paused");
    }

    public void ResumeAll()
    {
        _globalPaused = false;
        lock (_lock)
        {
            foreach (var watcher in _watchers.Values)
                watcher.Resume();
        }
        _logger.Log(ServiceLogLevel.Info, "[FtpWatcherManager] All watchers resumed");
    }

    public void TriggerFullUploadAll()
    {
        lock (_lock)
        {
            foreach (var watcher in _watchers.Values)
                watcher.TriggerFullUpload();
        }
        _logger.Log(ServiceLogLevel.Info, "[FtpWatcherManager] Full upload triggered on all watchers");
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

    public async Task<FtpTestResult> TestConnectionAsync(string host, string path, string username = "", string password = "")
    {
        await PauseWatchersForHostAsync(host);
        try
        {
            var tempConfig = new FtpServerConfig { FtpHost = host, FtpPath = path, Username = username, Password = password };
            var tempWatcher = new FtpWatcher(tempConfig, _fileQueue, _siteId, _tenantId, _logger);
            return await tempWatcher.TestConnectionAsync(host, path);
        }
        finally
        {
            ResumeWatchersForHost(host);
        }
    }

    public async Task<FtpBrowseResult> BrowseDirectoryAsync(string host, string path, string username = "", string password = "")
    {
        await PauseWatchersForHostAsync(host);
        try
        {
            var tempConfig = new FtpServerConfig { FtpHost = host, FtpPath = path, Username = username, Password = password };
            var tempWatcher = new FtpWatcher(tempConfig, _fileQueue, _siteId, _tenantId, _logger);
            return await tempWatcher.ListDirectoriesAsync(host, path);
        }
        finally
        {
            ResumeWatchersForHost(host);
        }
    }

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
            watcher.Stop();
            _logger.Log(ServiceLogLevel.Debug, $"[FtpWatcherManager] Paused watcher {watcher.Id} for host {host}");
        }

        var timeout = DateTime.UtcNow.AddSeconds(60);
        while (matchingWatchers.Any(w => w.IsCurrentlyPolling) && DateTime.UtcNow < timeout)
            await Task.Delay(500);

        if (matchingWatchers.Any(w => w.IsCurrentlyPolling))
            _logger.Log(ServiceLogLevel.Warning, $"[FtpWatcherManager] Watcher for {host} still polling after timeout");
    }

    private void ResumeWatchersForHost(string host)
    {
        if (!_enabled) return;

        lock (_lock)
        {
            foreach (var watcher in _watchers.Values
                .Where(w => w.Host.Equals(host, StringComparison.OrdinalIgnoreCase)))
            {
                watcher.Start();
                _logger.Log(ServiceLogLevel.Debug, $"[FtpWatcherManager] Resumed watcher {watcher.Id} for host {host}");
            }
        }
    }
}
