using System.Text.Json.Serialization;

namespace PeiSiteService.Models;

public class FtpFileAck
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("filename")]
    public string Filename { get; set; } = "";

    [JsonPropertyName("file_id")]
    public string? FileId { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("records_inserted")]
    public int? RecordsInserted { get; set; }
}
