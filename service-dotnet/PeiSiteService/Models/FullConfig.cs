using System.Text.Json;
using System.Text.Json.Serialization;
using PeiSiteService.Plc;

namespace PeiSiteService.Models;

/// <summary>
/// Reads siteId as either a JSON string or number so existing config.json files
/// written with the old int type are migrated transparently.
/// </summary>
internal class SiteIdConverter : JsonConverter<string>
{
    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString() ?? "",
            JsonTokenType.Number => reader.TryGetInt64(out var n) ? n.ToString() : reader.GetDecimal().ToString(),
            _ => ""
        };

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value);
}

public class FullConfig
{
    public string ApiUrl { get; set; } = "";
    public string ApiKey { get; set; } = "";
    [JsonConverter(typeof(SiteIdConverter))]
    public string SiteId { get; set; } = "";
    public int TenantId { get; set; }
    public ServiceLogLevel LogLevel { get; set; } = ServiceLogLevel.Info;
    public bool FtpEnabled { get; set; }
    public List<FtpServerConfig> FtpServers { get; set; } = new();

    public PlcSettings PlcSettings { get; set; } = new();

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
            Name = s.Name,
            FtpHost = s.FtpHost,
            FtpPath = s.FtpPath,
            FtpPollInterval = s.FtpPollInterval,
            Username = s.Username,
            Password = s.Password,
            ForceFullUploadOnNextPoll = s.ForceFullUploadOnNextPoll
        }).ToList()
    };
}
