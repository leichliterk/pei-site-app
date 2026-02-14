namespace PeiSiteService.Models;

public class ConfigUpdateRequest
{
    public string? ApiUrl { get; set; }
    public string? ApiKey { get; set; }
    public int? SiteId { get; set; }
    public int? TenantId { get; set; }
}

public class PrepopulateRequest
{
    public List<SessionInfo>? Sessions { get; set; }
}

public class SessionInfo
{
    public string ConnectedAt { get; set; } = "";
    public string? DisconnectedAt { get; set; }
}

public class FtpConfigUpdate
{
    public bool? FtpEnabled { get; set; }
    public string? FtpHost { get; set; }
    public string? FtpPath { get; set; }
    public int? FtpPollInterval { get; set; }
}

public class FtpTestRequest
{
    public string Host { get; set; } = "";
    public string? Path { get; set; }
}

public class FtpTestResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public int? FileCount { get; set; }
}

public class FtpWatcherStatus
{
    public bool Enabled { get; set; }
    public string Host { get; set; } = "";
    public string Path { get; set; } = "";
    public int PollInterval { get; set; }
    public string? LastPoll { get; set; }
    public string LastResult { get; set; } = "";
    public int FilesForwarded { get; set; }
    public bool IsPolling { get; set; }
}
