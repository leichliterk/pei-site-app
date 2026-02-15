using System.Text.Json.Serialization;

namespace PeiSiteService.Models;

[JsonConverter(typeof(JsonStringEnumConverter<ConnectionStatus>))]
public enum ConnectionStatus
{
    connecting,
    connected,
    disconnected,
    error
}
