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

// FTP server CRUD

public class FtpServerCreateRequest
{
    public string Name { get; set; } = "";
    public string FtpHost { get; set; } = "";
    public string FtpPath { get; set; } = "/";
    public int FtpPollInterval { get; set; } = 900;
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
}

public class FtpServerUpdateRequest
{
    public string? Name { get; set; }
    public string? FtpHost { get; set; }
    public string? FtpPath { get; set; }
    public int? FtpPollInterval { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }
}

public class FtpEnabledRequest
{
    public bool Enabled { get; set; }
}

public class FtpTestRequest
{
    public string Host { get; set; } = "";
    public string? Path { get; set; }
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
}

public class FtpTestResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public int? FileCount { get; set; }
}

public class FtpBrowseRequest
{
    public string Host { get; set; } = "";
    public string Path { get; set; } = "/";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
}

public class FtpBrowseResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public List<FtpDirectoryEntry> Directories { get; set; } = new();
}

public class FtpDirectoryEntry
{
    public string Name { get; set; } = "";
    public string FullPath { get; set; } = "";
}

// FTP status responses

public class FtpWatcherStatus
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

public class FtpOverallStatus
{
    public bool FtpEnabled { get; set; }
    public List<FtpWatcherStatus> Servers { get; set; } = new();
}
