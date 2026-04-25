using System.Text.Json.Serialization;

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

// OTA

/// <summary>
/// Payload of the server→service  ota:install_command  socket event.
/// The server constructs the signed download URL and passes it directly
/// so the service never needs to know the URL scheme.
/// </summary>
public class OtaInstallCommand
{
    [JsonPropertyName("release_id")]
    public string ReleaseId { get; set; } = "";

    [JsonPropertyName("download_url")]
    public string DownloadUrl { get; set; } = "";

    /// <summary>Hex-encoded SHA-256 of the installer exe (optional but recommended).</summary>
    [JsonPropertyName("sha256")]
    public string? Sha256 { get; set; }

    /// <summary>Human-readable version string, e.g. "1.5.0".</summary>
    [JsonPropertyName("version")]
    public string? Version { get; set; }
}

/// <summary>
/// Written to ota-pending.json before the installer launches.
/// Read back on the next startup to emit ota:installed after the MSI
/// restarts the service.
/// </summary>
public class OtaPendingState
{
    public string ReleaseId { get; set; } = "";
    public string Version   { get; set; } = "";
    public string StartedAt { get; set; } = "";
}

public class OtaRespondRequest
{
    public string ReleaseId { get; set; } = "";
    public bool Accepted { get; set; }
}

public class OtaInstalledRequest
{
    public string ReleaseId { get; set; } = "";
    public string Version { get; set; } = "";
}

public class OtaResponseAck
{
    public bool Success { get; set; }
    public string? Error { get; set; }
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
    public bool? ForceFullUploadOnNextPoll { get; set; }
}

public class FtpEnabledRequest
{
    public bool Enabled { get; set; }
}

public class LogLevelRequest
{
    public ServiceLogLevel Level { get; set; } = ServiceLogLevel.Info;
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

// Log entries

public class LogEntry
{
    public string Timestamp { get; set; } = "";
    public string Level { get; set; } = "";
    public string Message { get; set; } = "";
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
    public bool ForceFullUploadOnNextPoll { get; set; }
}

public class FtpOverallStatus
{
    public bool FtpEnabled { get; set; }
    public List<FtpWatcherStatus> Servers { get; set; } = new();
}
