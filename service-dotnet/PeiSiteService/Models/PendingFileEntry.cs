namespace PeiSiteService.Models;

public class PendingFileEntry
{
    public string PendingFileId { get; set; } = "";
    public string ServerId { get; set; } = "";
    public string Filename { get; set; } = "";
    public string ContentBase64 { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public string Encoding { get; set; } = "base64";
    public long Size { get; set; }
    public string Source { get; set; } = "";
    public int SiteId { get; set; }
    public int TenantId { get; set; }
    public string Timestamp { get; set; } = "";
    public string QueuedAt { get; set; } = "";
}
