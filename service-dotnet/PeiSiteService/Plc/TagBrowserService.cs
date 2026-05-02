using Microsoft.Extensions.Hosting;
using PeiSiteService.Services;

namespace PeiSiteService.Plc;

/// <summary>
/// BackgroundService that browses PLC tags:
///   - once on startup
///   - every 5 minutes thereafter
///   - on demand via RefreshAsync()
/// </summary>
public class TagBrowserService : BackgroundService
{
    private readonly ConfigManager _configManager;
    private readonly TagBrowserState _state;
    private readonly FileLogger _logger;
    private readonly SemaphoreSlim _refreshSignal = new(0, 1);

    public TagBrowserService(ConfigManager configManager, TagBrowserState state, FileLogger logger)
    {
        _configManager = configManager;
        _state = state;
        _logger = logger;
    }

    /// <summary>Triggers an immediate browse outside the 5-minute cycle.</summary>
    public void RequestRefresh()
    {
        // Non-blocking: ignore if already signaled
        _refreshSignal.Release();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Initial browse on startup
        await BrowseAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            // Wait up to 5 minutes OR until explicitly signaled
            await WaitWithTimeoutAsync(TimeSpan.FromMinutes(5), stoppingToken);

            if (stoppingToken.IsCancellationRequested) break;

            await BrowseAsync(stoppingToken);
        }
    }

    private async Task WaitWithTimeoutAsync(TimeSpan timeout, CancellationToken ct)
    {
        try
        {
            await _refreshSignal.WaitAsync(timeout, ct);
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
    }

    private async Task BrowseAsync(CancellationToken ct)
    {
        var settings = _configManager.GetPlcSettings();
        if (!settings.Enabled || string.IsNullOrWhiteSpace(settings.IpAddress))
            return;

        // Modbus TCP has no network-discoverable tag database.
        // Tags are configured manually in Settings; nothing to browse.
        if (settings.ConnectionType == PlcConnectionType.ModbusTcp)
        {
            _logger.Log("[TagBrowser] Skipping browse — Modbus TCP does not support auto-discovery");
            return;
        }

        int slot = settings.Slot >= 0 ? settings.Slot : 0;

        try
        {
            _logger.Log($"[TagBrowser] Starting browse: {settings.IpAddress} slot {slot}");
            var browser = new CompactLogixTagBrowser(settings.IpAddress, slot, _logger);
            var tags = await browser.BrowseAsync(ct);
            _state.Update(tags);
            _logger.Log($"[TagBrowser] Browse complete: {tags.Count} total tags");
        }
        catch (OperationCanceledException)
        {
            // Shutdown
        }
        catch (Exception ex)
        {
            _logger.Log($"[TagBrowser] Browse failed: {ex.GetType().Name}: {ex.Message}");
            // Browse failure is non-fatal — keep the old tag list
        }
    }
}
