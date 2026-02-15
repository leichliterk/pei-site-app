namespace PeiSiteService.Models;

public class ServiceConfig
{
    public string ApiUrl { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public int SiteId { get; set; }
    public int TenantId { get; set; }
}
