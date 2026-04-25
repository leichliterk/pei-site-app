namespace PeiSiteService.Plc;

public class PlcSettings
{
    public bool Enabled { get; set; } = false;
    public string IpAddress { get; set; } = "192.168.1.10";
    public int Slot { get; set; } = 0;
    public int PollingIntervalMs { get; set; } = 500;
    public List<TagDefinition> Tags { get; set; } = new();
}

public class TagDefinition
{
    public string Name { get; set; } = "";
    public string DataType { get; set; } = "REAL"; // REAL, BOOL, DINT, STRING
    public string? DisplayName { get; set; }
    public string? Unit { get; set; }
}
