namespace PeiSiteService.Plc;

/// <summary>
/// Singleton holding the latest PLC snapshot so the HTTP API can serve it
/// without requiring the WPF client to have a WebSocket connection.
/// </summary>
public class PlcSnapshotState
{
    private volatile PlcSnapshot? _latest;

    public PlcSnapshot? Latest => _latest;

    public void Update(PlcSnapshot snapshot) => _latest = snapshot;
}
