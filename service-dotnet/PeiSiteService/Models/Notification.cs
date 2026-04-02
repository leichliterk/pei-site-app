using System.Text.Json.Serialization;

namespace PeiSiteService.Models;

public class NotificationData
{
    [JsonPropertyName("ota")]
    public bool Ota { get; set; }

    [JsonPropertyName("release_id")]
    public string? ReleaseId { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("notes")]
    public string? Notes { get; set; }

    [JsonPropertyName("size")]
    public long? Size { get; set; }

    [JsonPropertyName("sha256")]
    public string? Sha256 { get; set; }

    [JsonPropertyName("download_token")]
    public string? DownloadToken { get; set; }
}

public class ServiceNotification
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("body")]
    public string Body { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "info";

    [JsonPropertyName("data")]
    public NotificationData? Data { get; set; }

    // Set by NotificationManager when received
    public bool Read { get; set; }
    public string ReceivedAt { get; set; } = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
}
