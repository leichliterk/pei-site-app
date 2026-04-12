namespace PeiSiteService.Models;

public enum QueueEntryStatus
{
    Pending,
    Sending,
    Retrying,
    Success,
    PermanentlyFailed
}

public class QueueEntry
{
    public string Id { get; set; } = "";
    public string ServerId { get; set; } = "";
    public string Filename { get; set; } = "";
    /// <summary>FTP MDTM timestamp (YYYYMMDDHHmmss) or empty string if unknown.</summary>
    public string FileDate { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public long SizeBytes { get; set; }
    public string Source { get; set; } = "";
    public string Category { get; set; } = "ftp";
    public string ContentBase64 { get; set; } = "";
    public string Encoding { get; set; } = "base64";
    public int SiteId { get; set; }
    public int TenantId { get; set; }

    public QueueEntryStatus Status { get; set; } = QueueEntryStatus.Pending;
    public int RetryCount { get; set; }
    public int MaxRetries { get; set; } = 3;
    public int? RecordsInserted { get; set; }

    public string QueuedAt { get; set; } = "";
    public string? SentAt { get; set; }
    public string? AckAt { get; set; }
    public string? FailedAt { get; set; }
    public string? LastError { get; set; }
    /// <summary>ISO timestamp after which a Retrying entry becomes Pending again.</summary>
    public string? RetryAfter { get; set; }
}
