namespace PeiSiteService.Plc;

public record TagSnapshot(
    string Name,
    string DataType,
    object? Value,
    string? DisplayName,
    string? Unit,
    bool Error,
    string? ErrorMessage
);

public record PlcSnapshot(
    string IpAddress,
    int Slot,
    DateTimeOffset Timestamp,
    bool Connected,
    IReadOnlyList<TagSnapshot> Tags,
    int SnapshotIntervalMs = 0
);

public record DiscoveredTag(
    string Name,
    string DataType,
    string? Program, // null = controller-scope, "Program:Main" etc for program-scope
    bool IsUdtContainer = false // true = UDT instance that was expanded into members below it
);
