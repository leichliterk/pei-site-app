using System.Net;
using PeiSiteService.Services;

namespace PeiSiteService;

public class Worker : BackgroundService
{
    private readonly FileLogger _logger;
    private readonly WebSocketClient _wsClient;
    private readonly FileBroker _fileBroker;
    private readonly FtpWatcherManager _ftpManager;
    private readonly ConfigManager _configManager;
    private readonly NotificationManager _notificationManager;

    public Worker(FileLogger logger, WebSocketClient wsClient, FileBroker fileBroker, FtpWatcherManager ftpManager, ConfigManager configManager, NotificationManager notificationManager)
    {
        _logger = logger;
        _wsClient = wsClient;
        _fileBroker = fileBroker;
        _ftpManager = ftpManager;
        _configManager = configManager;
        _notificationManager = notificationManager;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.Log("[PEI Site Service] Starting...");
        _logger.Log($"[PEI Site Service] Process ID: {Environment.ProcessId}");
        _logger.Log($"[PEI Site Service] Working directory: {Directory.GetCurrentDirectory()}");

        _wsClient.StatusChanged += status =>
        {
            _logger.Log($"[PEI Site Service] Connection status: {status}");
        };

        _wsClient.NotificationReceived += _notificationManager.Add;

        try
        {
            // Wait for network
            await WaitForNetworkAsync(stoppingToken);

            // Connect WebSocket
            _logger.Log("[PEI Site Service] Initiating WebSocket connection...");
            _wsClient.Connect();

            // Start file broker (sends queued files as WS connects)
            _fileBroker.Start();

            // Start FTP watchers
            _ftpManager.StartAll();

            _logger.Log("[PEI Site Service] Service started successfully");

            // Keep alive until cancellation
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
        catch (Exception ex)
        {
            _logger.Log($"[PEI Site Service] Failed to start: {ex.Message}");
            _logger.Log("[PEI Site Service] Service will continue running and retry connections");

            try { await Task.Delay(Timeout.Infinite, stoppingToken); }
            catch (OperationCanceledException) { }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.Log("[PEI Site Service] Shutting down...");

        _ftpManager.StopAll();
        _fileBroker.Stop();
        await _wsClient.DisconnectAsync();

        _logger.Log("[PEI Site Service] Shutdown complete");
        await base.StopAsync(cancellationToken);
    }

    private async Task WaitForNetworkAsync(CancellationToken ct, int maxAttempts = 30, int delayMs = 2000)
    {
        var apiUrl = _configManager.GetConfig().ApiUrl;
        string host;
        try { host = new Uri(apiUrl).Host; }
#if PRODUCTION
        catch { host = "pei-web-server.onrender.com"; }
#else
        catch { host = "pei-web-server-staging.onrender.com"; }
#endif

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                _logger.Log($"[PEI Site Service] Checking network availability (attempt {attempt}/{maxAttempts}, host={host})...");
                var addresses = await Dns.GetHostAddressesAsync(host, ct);
                if (addresses.Length > 0)
                {
                    _logger.Log("[PEI Site Service] Network is available");
                    return;
                }
            }
            catch (Exception ex)
            {
                _logger.Log($"[PEI Site Service] Network not ready: {ex.Message}");
                if (attempt < maxAttempts)
                    await Task.Delay(delayMs, ct);
            }
        }

        _logger.Log("[PEI Site Service] Network check timed out, will continue anyway (socket.io will retry)");
    }
}
