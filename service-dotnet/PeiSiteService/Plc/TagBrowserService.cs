using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace PeiSiteService.Plc;

/// <summary>
/// BackgroundService that browses PLC tags:
///   - once on startup
///   - every 5 minutes thereafter
///   - on demand via RefreshAsync()
/// </summary>
public class TagBrowserService : BackgroundService
{
    private readonly IOptionsMonitor<PlcSettings> _options;
    private readonly TagBrowserState _state;
    private readonly SemaphoreSlim _refreshSignal = new(0, 1);

    public TagBrowserService(IOptionsMonitor<PlcSettings> options, TagBrowserState state)
    {
        _options = options;
        _state = state;
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
        var settings = _options.CurrentValue;
        if (!settings.Enabled || string.IsNullOrWhiteSpace(settings.IpAddress))
            return;

        int slot = settings.Slot >= 0 ? settings.Slot : 0;

        try
        {
            var browser = new CompactLogixTagBrowser(settings.IpAddress, slot);
            var tags = await browser.BrowseAsync(ct);
            _state.Update(tags);
        }
        catch (OperationCanceledException)
        {
            // Shutdown
        }
        catch
        {
            // Browse failure is non-fatal — keep the old tag list
        }
    }
}
