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
    public bool ForceFullUploadOnNextPoll { get; set; }
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

public class NotificationDataResponse
{
    public bool Ota { get; set; }
    public string? ReleaseId { get; set; }
    public string? Version { get; set; }
    public string? Notes { get; set; }
    public long? Size { get; set; }
    public string? Sha256 { get; set; }
    public string? DownloadToken { get; set; }
}

public class NotificationResponse
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public string Type { get; set; } = "info";
    public NotificationDataResponse? Data { get; set; }
    public bool Read { get; set; }
    public string ReceivedAt { get; set; } = "";

    public bool IsOta => Data?.Ota == true;
}

public class NotificationsResponse
{
    public List<NotificationResponse> Notifications { get; set; } = new();
    public int UnreadCount { get; set; }
}

public class LogEntry
{
    public string Timestamp { get; set; } = "";
    public string Level { get; set; } = "";
    public string Message { get; set; } = "";

    public string ShortTimestamp =>
        Timestamp.Length >= 23 ? Timestamp[11..23] : Timestamp;
}

public class LogsResponse
{
    public List<LogEntry> Entries { get; set; } = new();
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
