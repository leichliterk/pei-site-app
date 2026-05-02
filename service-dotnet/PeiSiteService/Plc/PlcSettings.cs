using System.Text.Json.Serialization;

namespace PeiSiteService.Plc;

/// <summary>
/// Selects the PLC protocol used at this site.
/// Stored in config.json as a string ("ControlLogix", "ModbusTcp").
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PlcConnectionType
{
    ControlLogix,
    ModbusTcp
}

public class PlcSettings
{
    public bool Enabled { get; set; } = false;
    public PlcConnectionType ConnectionType { get; set; } = PlcConnectionType.ControlLogix;
    public string IpAddress { get; set; } = "192.168.1.10";

    // CompactLogix / ControlLogix only
    public int Slot { get; set; } = 0;

    // Modbus TCP only
    public int ModbusPort { get; set; } = 502;
    public int ModbusUnitId { get; set; } = 1;

    public int PollingIntervalMs { get; set; } = 500;
    public List<TagDefinition> Tags { get; set; } = new();
}

public class TagDefinition
{
    public string Name { get; set; } = "";

    /// <summary>
    /// ControlLogix: REAL, BOOL, DINT, STRING
    /// Modbus TCP:   FLOAT32, UINT16, INT16, UINT32, INT32, BOOL, BCD_INT_16
    ///
    /// BCD_INT_16: 16-bit register where each nibble is a decimal digit (0–9).
    /// High nibble = 0xF indicates negative (e.g. 0xF015 = -15). Use this for
    /// Host Engineering PLCs whose native V-memory format is BCD.
    /// </summary>
    public string DataType { get; set; } = "REAL";
    public string? DisplayName { get; set; }
    public string? Unit { get; set; }

    /// <summary>
    /// Scaling factor applied after decoding. Primarily used with BCD_INT_16
    /// to handle implied decimal places (e.g. 0.1 turns raw 150 → 15.0).
    /// Defaults to 1.0 (no scaling).
    /// </summary>
    public double Multiplier { get; set; } = 1.0;
}
