namespace PeiSiteApp.Models;

public class ServiceStatusResponse
{
    public string Status { get; set; } = "disconnected";
    public string? ConnectedAt { get; set; }
    public double CurrentUptime { get; set; }
    public int[] ConnectionHistory { get; set; } = Array.Empty<int>();
}

public class HealthResponse
{
    public string Status { get; set; } = "";
    public string Timestamp { get; set; } = "";
}

public class FtpTestResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public int? FileCount { get; set; }
}

public class FtpStatusResponse
{
    public string Status { get; set; } = "disabled";
    public string? LastChecked { get; set; }
    public string? Error { get; set; }
}
