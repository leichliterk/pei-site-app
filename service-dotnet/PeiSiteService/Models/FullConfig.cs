namespace PeiSiteService.Models;

public class FullConfig
{
    public string ApiUrl { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public int SiteId { get; set; }
    public int TenantId { get; set; }
    public ServiceLogLevel LogLevel { get; set; } = ServiceLogLevel.Info;
    public bool FtpEnabled { get; set; }
    public List<FtpServerConfig> FtpServers { get; set; } = new();

    // Legacy flat fields — kept for deserializing old config.json files.
    // After migration these are cleared and FtpServers is used instead.
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
        Servers = FtpServers.Select(s => new FtpServerConfig
        {
            Id = s.Id,
            FtpHost = s.FtpHost,
            FtpPath = s.FtpPath,
            FtpPollInterval = s.FtpPollInterval
        }).ToList()
    };
}
