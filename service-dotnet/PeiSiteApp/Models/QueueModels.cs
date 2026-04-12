namespace PeiSiteApp.Models;

public class QueueEntryResponse
{
    public string Id { get; set; } = "";
    public string ServerId { get; set; } = "";
    public string Filename { get; set; } = "";
    public string FileDate { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public long SizeBytes { get; set; }
    public string Source { get; set; } = "";
    public string Status { get; set; } = "";
    public int RetryCount { get; set; }
    public int MaxRetries { get; set; }
    public int? RecordsInserted { get; set; }
    public string QueuedAt { get; set; } = "";
    public string? SentAt { get; set; }
    public string? AckAt { get; set; }
    public string? FailedAt { get; set; }
    public string? LastError { get; set; }
}

public class QueueResponse
{
    public List<QueueEntryResponse> Entries { get; set; } = new();
}

public class QueueStatusResponse
{
    public bool Paused { get; set; }
    public bool InFlight { get; set; }
    public int PendingCount { get; set; }
}
