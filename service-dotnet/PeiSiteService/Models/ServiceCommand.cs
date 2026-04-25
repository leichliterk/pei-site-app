using System.Text.Json.Serialization;

namespace PeiSiteService.Models;

public class ServiceCommand
{
    [JsonPropertyName("action")]
    public string Action { get; set; } = "";
}

public class FtpCommand
{
    [JsonPropertyName("action")]
    public string Action { get; set; } = "";
}
