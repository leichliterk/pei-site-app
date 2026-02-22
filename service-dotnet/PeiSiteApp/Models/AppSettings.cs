namespace PeiSiteApp.Models;

public class AppSettings
{
    public string Version { get; set; } = "1.3.9.1";
    public string AppName { get; set; } = "PEI Site App";
    public int SiteNumber { get; set; } = 1000;
    public string SiteName { get; set; } = "";
    public string ApiUrl { get; set; } = "https://pei-web-server-staging.onrender.com/api/data";
    public int TenantId { get; set; } = 1001;
    public string ApiKey { get; set; } = "_6@L<Q*SC?mSdp$a1E4?L{\"M+8QQ0|Cw";
    public bool Staging { get; set; } = true;
}
