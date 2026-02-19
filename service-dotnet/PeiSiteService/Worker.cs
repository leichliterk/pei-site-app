using System.Net;
using PeiSiteService.Services;

namespace PeiSiteService;

public class Worker : BackgroundService
{
    private readonly FileLogger _logger;
    private readonly WebSocketClient _wsClient;
    private readonly FtpWatcherManager _ftpManager;

    public Worker(FileLogger logger, WebSocketClient wsClient, FtpWatcherManager ftpManager)
    {
        _logger = logger;
        _wsClient = wsClient;
        _ftpManager = ftpManager;
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

        try
        {
            // Wait for network
            await WaitForNetworkAsync(stoppingToken);

            // Connect WebSocket
            _logger.Log("[PEI Site Service] Initiating WebSocket connection...");
            _wsClient.Connect();

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
        await _wsClient.DisconnectAsync();

        _logger.Log("[PEI Site Service] Shutdown complete");
        await base.StopAsync(cancellationToken);
    }

    private async Task WaitForNetworkAsync(CancellationToken ct, int maxAttempts = 30, int delayMs = 2000)
    {
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                _logger.Log($"[PEI Site Service] Checking network availability (attempt {attempt}/{maxAttempts})...");
                var addresses = await Dns.GetHostAddressesAsync("pei-web-server.onrender.com", ct);
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
