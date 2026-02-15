using System.Text.Json.Serialization;

namespace PeiSiteApp.Models;

public class UptimeResponse
{
    [JsonPropertyName("tenant_id")]
    public int TenantId { get; set; }

    [JsonPropertyName("site_id")]
    public int SiteId { get; set; }

    public int Days { get; set; }

    [JsonPropertyName("start_date")]
    public string StartDate { get; set; } = "";

    [JsonPropertyName("end_date")]
    public string EndDate { get; set; } = "";

    [JsonPropertyName("total_time_ms")]
    public long TotalTimeMs { get; set; }

    [JsonPropertyName("total_uptime_ms")]
    public long TotalUptimeMs { get; set; }

    [JsonPropertyName("uptime_percentage")]
    public double UptimePercentage { get; set; }

    public List<UptimeSession> Sessions { get; set; } = new();
}

public class UptimeSession
{
    [JsonPropertyName("connected_at")]
    public string ConnectedAt { get; set; } = "";

    [JsonPropertyName("disconnected_at")]
    public string? DisconnectedAt { get; set; }

    [JsonPropertyName("duration_ms")]
    public long DurationMs { get; set; }

    [JsonPropertyName("disconnect_reason")]
    public string DisconnectReason { get; set; } = "";
}
