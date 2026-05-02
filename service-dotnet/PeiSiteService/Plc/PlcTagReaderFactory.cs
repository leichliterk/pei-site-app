using PeiSiteService.Services;

namespace PeiSiteService.Plc;

/// <summary>
/// Builds the correct IPlcTagReader for the current PlcSettings:
///   ControlLogix → CompactLogixTagReader (uses libplctag, slot auto-discover supported)
///   ModbusTcp    → ModbusTcpReader       (uses FluentModbus)
/// </summary>
public class PlcTagReaderFactory
{
    private readonly ConfigManager _configManager;

    public PlcTagReaderFactory(ConfigManager configManager)
    {
        _configManager = configManager;
    }

    /// <summary>Returns null when PLC is disabled or IP is empty.</summary>
    public async Task<IPlcTagReader?> CreateAsync(CancellationToken ct)
    {
        var settings = _configManager.GetPlcSettings();

        if (!settings.Enabled || string.IsNullOrWhiteSpace(settings.IpAddress))
            return null;

        return settings.ConnectionType switch
        {
            PlcConnectionType.ModbusTcp    => CreateModbusReader(settings),
            _                              => await CreateControlLogixReaderAsync(settings, ct)
        };
    }

    private static IPlcTagReader CreateModbusReader(PlcSettings settings)
    {
        return new ModbusTcpReader(
            settings.IpAddress,
            settings.ModbusPort,
            (byte)Math.Clamp(settings.ModbusUnitId, 0, 255),
            settings.Tags);
    }

    private static async Task<IPlcTagReader?> CreateControlLogixReaderAsync(
        PlcSettings settings, CancellationToken ct)
    {
        int slot = settings.Slot;

        if (slot == -1)
        {
            slot = await CompactLogixSlotScanner.FindFirstSlotAsync(settings.IpAddress, ct);
            if (slot < 0) return null;
        }

        return new CompactLogixTagReader(settings.IpAddress, slot, settings.Tags);
    }
}
