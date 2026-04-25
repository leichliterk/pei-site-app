namespace PeiSiteApp.Models;

public class AppSettings
{
    public string Version { get; set; } = "1.4.9";
    public string AppName { get; set; } = "PEI Site App";
    public int SiteNumber { get; set; } = 1000;
    public string SiteName { get; set; } = "";
#if PRODUCTION
    public string ApiUrl { get; set; } = "https://pei-web-server.onrender.com/api/data";
    public bool Staging { get; set; } = false;
#else
    public string ApiUrl { get; set; } = "https://pei-web-server-staging.onrender.com/api/data";
    public bool Staging { get; set; } = true;
#endif
    public int TenantId { get; set; } = 1001;
    public string ApiKey { get; set; } = "";
    public string LogLevel { get; set; } = "info";
}
