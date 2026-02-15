namespace PeiSiteService.Models;

public class FullConfig
{
    public string ApiUrl { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public int SiteId { get; set; }
    public int TenantId { get; set; }
    public bool FtpEnabled { get; set; }
    public string FtpHost { get; set; } = "";
    public string FtpPath { get; set; } = "/";
    public int FtpPollInterval { get; set; } = 60;

    public ServiceConfig ToServiceConfig() => new()
    {
        ApiUrl = ApiUrl,
        ApiKey = ApiKey,
        SiteId = SiteId,
        TenantId = TenantId
    };

    public FtpConfig ToFtpConfig() => new()
    {
        FtpEnabled = FtpEnabled,
        FtpHost = FtpHost,
        FtpPath = FtpPath,
        FtpPollInterval = FtpPollInterval
    };
}
