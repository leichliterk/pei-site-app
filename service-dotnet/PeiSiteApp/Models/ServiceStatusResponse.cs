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

public class FtpOverallStatusResponse
{
    public bool FtpEnabled { get; set; }
    public List<FtpServerStatusResponse> Servers { get; set; } = new();
}

public class FtpServerStatusResponse
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Host { get; set; } = "";
    public string Path { get; set; } = "";
    public string Username { get; set; } = "";
    public int PollInterval { get; set; }
    public string? LastPoll { get; set; }
    public string LastResult { get; set; } = "";
    public int FilesForwarded { get; set; }
    public bool IsPolling { get; set; }
}

public class FtpServerResponse
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string FtpHost { get; set; } = "";
    public string FtpPath { get; set; } = "/";
    public int FtpPollInterval { get; set; }
    public string Username { get; set; } = "";
}

public class FtpBrowseResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public List<FtpDirectoryEntryResponse> Directories { get; set; } = new();
}

public class FtpDirectoryEntryResponse
{
    public string Name { get; set; } = "";
    public string FullPath { get; set; } = "";
}
