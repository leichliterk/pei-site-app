using PeiSiteService.Services;

namespace PeiSiteService.Plc;

/// <summary>
/// Builds a CompactLogixTagReader from the current PlcSettings.
/// Also handles the Slot=-1 auto-discover flow by delegating to CompactLogixSlotScanner.
/// </summary>
public class PlcTagReaderFactory
{
    private readonly ConfigManager _configManager;

    public PlcTagReaderFactory(ConfigManager configManager)
    {
        _configManager = configManager;
    }

    /// <summary>
    /// Creates a reader for the current settings.
    /// If Slot is -1, scans slots 0-7 to find the first responding CPU.
    /// Returns null when PLC is disabled or IP is empty.
    /// </summary>
    public async Task<(CompactLogixTagReader? reader, int resolvedSlot)> CreateAsync(CancellationToken ct)
    {
        var settings = _configManager.GetPlcSettings();

        if (!settings.Enabled || string.IsNullOrWhiteSpace(settings.IpAddress))
            return (null, -1);

        int slot = settings.Slot;

        if (slot == -1)
        {
            slot = await CompactLogixSlotScanner.FindFirstSlotAsync(settings.IpAddress, ct);
            if (slot < 0)
                return (null, -1);
        }

        var reader = new CompactLogixTagReader(settings.IpAddress, slot, settings.Tags);
        return (reader, slot);
    }
}
