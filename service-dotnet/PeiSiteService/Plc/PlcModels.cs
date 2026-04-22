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
    IReadOnlyList<TagSnapshot> Tags
);

public record DiscoveredTag(
    string Name,
    string DataType,
    string? Program // null = controller-scope, "Program:Main" etc for program-scope
);
