namespace PeiSiteService.Plc;

/// <summary>
/// Common interface for all PLC tag readers (CompactLogix, Modbus TCP, …).
/// PlcPollingService uses this so it is decoupled from any specific protocol.
/// </summary>
public interface IPlcTagReader : IDisposable
{
    Task<PlcSnapshot> ReadAllAsync(CancellationToken ct);
}
