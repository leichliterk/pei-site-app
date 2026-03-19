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
    public string ModifiedAt { get; set; } = "";  // FTP server's MDTM timestamp (YYYYMMDDHHmmss), or local time if server doesn't support MDTM
    public string QueuedAt { get; set; } = "";
}
