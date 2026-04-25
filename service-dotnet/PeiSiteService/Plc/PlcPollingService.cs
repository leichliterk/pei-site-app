using Microsoft.Extensions.Hosting;
using PeiSiteService.Services;
using System.Threading.Channels;

namespace PeiSiteService.Plc;

/// <summary>
/// BackgroundService that polls the PLC at the configured interval and pushes
/// PlcSnapshot objects into a bounded channel for consumption by WebSocketClient.
///
/// Hot-reload: ConfigManager fires PlcSettingsChanged which sets an Interlocked flag;
/// the poll loop recreates the reader on the next iteration.
/// </summary>
public class PlcPollingService : BackgroundService
{
    private readonly ConfigManager _configManager;
    private readonly PlcTagReaderFactory _factory;
    private readonly Channel<PlcSnapshot> _channel;
    private int _settingsChanged = 0; // Interlocked flag

    /// <summary>Consumed by WebSocketClient to emit plc:snapshot events.</summary>
    public ChannelReader<PlcSnapshot> Snapshots => _channel.Reader;

    public PlcPollingService(ConfigManager configManager, PlcTagReaderFactory factory)
    {
        _configManager = configManager;
        _factory = factory;

        _channel = Channel.CreateBounded<PlcSnapshot>(new BoundedChannelOptions(256)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = false,
            SingleWriter = true
        });

        _configManager.PlcSettingsChanged += _ => Interlocked.Exchange(ref _settingsChanged, 1);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        CompactLogixTagReader? reader = null;
        int resolvedSlot = -1;

        while (!stoppingToken.IsCancellationRequested)
        {
            var settings = _configManager.GetPlcSettings();

            // Rebuild reader on first run or after settings change
            if (reader == null || Interlocked.Exchange(ref _settingsChanged, 0) == 1)
            {
                (reader, resolvedSlot) = await _factory.CreateAsync(stoppingToken);
            }

            if (!settings.Enabled || reader == null)
            {
                await DelayAsync(settings.PollingIntervalMs, stoppingToken);
                continue;
            }

            try
            {
                using var readCt = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                readCt.CancelAfter(TimeSpan.FromSeconds(10));

                var snapshot = await reader.ReadAllAsync(readCt.Token);
                await _channel.Writer.WriteAsync(snapshot, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                // Read failure — emit a disconnected snapshot so UI reflects the state
                var errorSnapshot = new PlcSnapshot(
                    settings.IpAddress, resolvedSlot,
                    DateTimeOffset.UtcNow, false,
                    Array.Empty<TagSnapshot>());
                await _channel.Writer.WriteAsync(errorSnapshot, stoppingToken);
            }

            await DelayAsync(settings.PollingIntervalMs, stoppingToken);
        }

        _channel.Writer.Complete();
    }

    private static async Task DelayAsync(int ms, CancellationToken ct)
    {
        try { await Task.Delay(Math.Max(ms, 100), ct); }
        catch (OperationCanceledException) { }
    }
}
