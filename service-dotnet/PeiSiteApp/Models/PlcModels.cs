using System.Text.Json.Serialization;

namespace PeiSiteApp.Models;

public class PlcTagSnapshotItem
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("dataType")] public string DataType { get; set; } = "";
    [JsonPropertyName("value")] public object? Value { get; set; }
    [JsonPropertyName("displayName")] public string? DisplayName { get; set; }
    [JsonPropertyName("unit")] public string? Unit { get; set; }
    [JsonPropertyName("error")] public bool Error { get; set; }
    [JsonPropertyName("errorMessage")] public string? ErrorMessage { get; set; }

    public string Label => !string.IsNullOrEmpty(DisplayName) ? DisplayName : Name;
    public string ValueDisplay => Error ? $"Error: {ErrorMessage ?? "?"}" : FormatValue(Value);

    private static string FormatValue(object? v) => v switch
    {
        null => "—",
        bool b => b ? "TRUE" : "FALSE",
        double d => d.ToString("G6"),
        float f => f.ToString("G6"),
        _ => v.ToString() ?? "—"
    };
}

public class PlcSnapshotResponse
{
    [JsonPropertyName("ipAddress")] public string IpAddress { get; set; } = "";
    [JsonPropertyName("slot")] public int Slot { get; set; }
    [JsonPropertyName("timestamp")] public DateTimeOffset Timestamp { get; set; }
    [JsonPropertyName("connected")] public bool Connected { get; set; }
    [JsonPropertyName("tags")] public List<PlcTagSnapshotItem> Tags { get; set; } = new();
}

public class PlcDiscoveredTag
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("dataType")] public string DataType { get; set; } = "";
    [JsonPropertyName("program")] public string? Program { get; set; }
    [JsonPropertyName("isUdtContainer")] public bool IsUdtContainer { get; set; }

    public string Scope => Program != null ? Program : "Controller";
}

public class PlcTagsResponse
{
    [JsonPropertyName("tags")] public List<PlcDiscoveredTag> Tags { get; set; } = new();
}

public class PlcSettingsModel
{
    [JsonPropertyName("enabled")] public bool Enabled { get; set; }
    [JsonPropertyName("connectionType")] public string ConnectionType { get; set; } = "ControlLogix";
    [JsonPropertyName("ipAddress")] public string IpAddress { get; set; } = "";
    [JsonPropertyName("slot")] public int Slot { get; set; }
    [JsonPropertyName("modbusPort")] public int ModbusPort { get; set; } = 502;
    [JsonPropertyName("modbusUnitId")] public int ModbusUnitId { get; set; } = 1;
    [JsonPropertyName("pollingIntervalMs")] public int PollingIntervalMs { get; set; } = 500;
    [JsonPropertyName("tags")] public List<PlcTagDefinitionModel> Tags { get; set; } = new();
}

public class PlcTagDefinitionModel
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("dataType")] public string DataType { get; set; } = "REAL";
    [JsonPropertyName("displayName")] public string? DisplayName { get; set; }
    [JsonPropertyName("unit")] public string? Unit { get; set; }
    [JsonPropertyName("multiplier")] public double Multiplier { get; set; } = 1.0;
}
