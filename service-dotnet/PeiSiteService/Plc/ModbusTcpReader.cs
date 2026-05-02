using FluentModbus;

namespace PeiSiteService.Plc;

/// <summary>
/// Reads tags from a Modbus TCP device (Host Engineering, etc.).
///
/// Address notation — standard 5-digit Modbus:
///   00001–09999  Coils              (FC01, 1-bit read/write)
///   10001–19999  Discrete Inputs    (FC02, 1-bit read-only)
///   30001–39999  Input Registers    (FC04, 16-bit read-only)
///   40001–49999  Holding Registers  (FC03, 16-bit read/write) ← most common
///
/// DataType values:
///   UINT16   1 register  unsigned 16-bit
///   INT16    1 register  signed 16-bit
///   UINT32   2 registers unsigned 32-bit (high word first)
///   INT32    2 registers signed 32-bit
///   FLOAT32  2 registers IEEE 754 float  (high word first, big-endian)
///   BOOL     1 coil or lowest bit of 1 register
/// </summary>
public sealed class ModbusTcpReader : IPlcTagReader
{
    private readonly string _ipAddress;
    private readonly int _port;
    private readonly byte _unitId;
    private readonly IReadOnlyList<TagDefinition> _tags;
    private ModbusTcpClient? _client;
    private bool _isConnected;
    private bool _disposed;

    public ModbusTcpReader(string ipAddress, int port, byte unitId, IReadOnlyList<TagDefinition> tags)
    {
        _ipAddress = ipAddress;
        _port      = port;
        _unitId    = unitId;
        _tags      = tags;
    }

    // -------------------------------------------------------------------------
    // IPlcTagReader
    // -------------------------------------------------------------------------

    public async Task<PlcSnapshot> ReadAllAsync(CancellationToken ct)
    {
        if (_tags.Count == 0)
            return new PlcSnapshot(_ipAddress, 0, DateTimeOffset.UtcNow, true, Array.Empty<TagSnapshot>());

        try
        {
            // Offload blocking Modbus I/O to thread pool
            var results = await Task.Run(() => ReadAll(ct), ct);
            bool connected = results.Any(r => !r.Error) || results.Length == 0;
            return new PlcSnapshot(_ipAddress, 0, DateTimeOffset.UtcNow, connected, results);
        }
        catch (Exception ex)
        {
            // Connection-level failure — drop the client so next call reconnects
            DropClient();
            var errors = _tags.Select(t =>
                new TagSnapshot(t.Name, t.DataType, null, t.DisplayName, t.Unit, true, ex.Message))
                .ToArray();
            return new PlcSnapshot(_ipAddress, 0, DateTimeOffset.UtcNow, false, errors);
        }
    }

    // -------------------------------------------------------------------------
    // Synchronous read loop (runs on thread pool via Task.Run)
    // -------------------------------------------------------------------------

    private TagSnapshot[] ReadAll(CancellationToken ct)
    {
        var client = GetOrCreateClient();
        var results = new TagSnapshot[_tags.Count];

        for (int i = 0; i < _tags.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            results[i] = ReadTag(_tags[i], client);
        }

        return results;
    }

    private TagSnapshot ReadTag(TagDefinition def, ModbusTcpClient client)
    {
        try
        {
            var (fc, address, registerCount) = ParseAddress(def.Name, def.DataType);
            object value = ReadValue(client, fc, address, registerCount, def.DataType);

            // Apply scaling multiplier (used for BCD_INT_16 implied decimal places)
            if (def.Multiplier != 1.0 && value is not bool)
                value = Convert.ToDouble(value) * def.Multiplier;

            return new TagSnapshot(def.Name, def.DataType, value, def.DisplayName, def.Unit, false, null);
        }
        catch (Exception ex)
        {
            // Tag-level error — drop client so next poll reconnects
            DropClient();
            return new TagSnapshot(def.Name, def.DataType, null, def.DisplayName, def.Unit, true, ex.Message);
        }
    }

    private object ReadValue(ModbusTcpClient client, byte fc, ushort address, ushort count, string dataType)
    {
        if (fc == 1)
        {
            // Coil (BOOL)
            Span<byte> raw = client.ReadCoils(_unitId, address, 1);
            return (raw[0] & 0x01) != 0;
        }

        if (fc == 2)
        {
            // Discrete Input (BOOL)
            Span<byte> raw = client.ReadDiscreteInputs(_unitId, address, 1);
            return (raw[0] & 0x01) != 0;
        }

        // Registers (FC03 or FC04)
        // ReadHoldingRegisters/ReadInputRegisters<byte> count is in bytes, not registers —
        // each 16-bit register = 2 bytes, so multiply register count by 2.
        Span<byte> data = fc == 4
            ? client.ReadInputRegisters<byte>(_unitId, address, (ushort)(count * 2))
            : client.ReadHoldingRegisters<byte>(_unitId, address, (ushort)(count * 2));

        return dataType.ToUpperInvariant() switch
        {
            "UINT16"     => ToUInt16(data),
            "INT16"      => ToInt16(data),
            "UINT32"     => ToUInt32(data),
            "INT32"      => ToInt32(data),
            "FLOAT32"    => ToFloat32(data),
            "BOOL"       => (ToUInt16(data) & 0x0001) != 0,
            "BCD_INT_16" => ToBcdInt16(data),
            _            => (object)ToUInt16(data)
        };
    }

    // -------------------------------------------------------------------------
    // Address parsing
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns (functionCode, zeroBasedAddress, registerCount).
    /// Supports standard 5-digit Modbus notation:
    ///   0xxxx → FC01 coils          (subtract 1)
    ///   1xxxx → FC02 discrete input (subtract 10001)
    ///   3xxxx → FC04 input register (subtract 30001)
    ///   4xxxx → FC03 holding reg    (subtract 40001)
    /// A plain number ≤ 65535 with no prefix is treated as a holding register.
    /// </summary>
    private static (byte fc, ushort address, ushort count) ParseAddress(string name, string dataType)
    {
        if (!int.TryParse(name.Trim(), out int raw))
            throw new FormatException($"Cannot parse Modbus address '{name}'. Use standard notation e.g. 40001.");

        byte fc;
        int addr;

        if (raw >= 40001 && raw <= 49999)      { fc = 3; addr = raw - 40001; }
        else if (raw >= 30001 && raw <= 39999) { fc = 4; addr = raw - 30001; }
        else if (raw >= 10001 && raw <= 19999) { fc = 2; addr = raw - 10001; }
        else if (raw >= 1     && raw <= 9999)  { fc = 1; addr = raw - 1;     }
        else if (raw == 0)                     { fc = 3; addr = 0;           }
        else                                   { fc = 3; addr = raw;         } // raw 0-based

        bool is32Bit = dataType.ToUpperInvariant() is "UINT32" or "INT32" or "FLOAT32";
        ushort count = is32Bit ? (ushort)2 : (ushort)1;

        return (fc, (ushort)addr, count);
    }

    // -------------------------------------------------------------------------
    // Big-endian byte decoding (Modbus standard)
    // -------------------------------------------------------------------------

    private static ushort ToUInt16(Span<byte> d)
        => (ushort)((d[0] << 8) | d[1]);

    private static short ToInt16(Span<byte> d)
        => (short)((d[0] << 8) | d[1]);

    private static uint ToUInt32(Span<byte> d)
        => ((uint)((d[0] << 8) | d[1]) << 16) | (uint)((d[2] << 8) | d[3]);

    private static int ToInt32(Span<byte> d)
        => (int)ToUInt32(d);

    /// <summary>
    /// Converts 4 bytes (two big-endian registers, high word first) to float.
    /// This matches the standard Modbus big-endian float encoding used by
    /// most vendors. Host Engineering PLCs that store native BCD data should
    /// use BCD_INT_16 instead of FLOAT32.
    /// </summary>
    private static float ToFloat32(Span<byte> d)
    {
        // Modbus wire order: [HH, HL, LH, LL]
        // BitConverter.ToSingle on little-endian needs: [LL, LH, HL, HH]
        Span<byte> le = stackalloc byte[4] { d[3], d[2], d[1], d[0] };
        return BitConverter.ToSingle(le);
    }

    /// <summary>
    /// Decodes a 16-bit BCD register. Each 4-bit nibble represents one decimal
    /// digit (0–9). Sign convention: high nibble = 0xF means negative value,
    /// with the remaining three nibbles giving the magnitude.
    ///
    /// Examples:
    ///   0x0150 → 150
    ///   0x0015 → 15
    ///   0xF015 → -15
    ///   0xF150 → -150
    /// </summary>
    private static object ToBcdInt16(Span<byte> d)
    {
        int n3 = (d[0] >> 4) & 0xF;  // most significant nibble
        int n2 =  d[0]       & 0xF;
        int n1 = (d[1] >> 4) & 0xF;
        int n0 =  d[1]       & 0xF;  // least significant nibble

        bool negative = n3 == 0xF;
        int magnitude = (negative ? 0 : n3 * 1000) + n2 * 100 + n1 * 10 + n0;
        return negative ? -magnitude : magnitude;
    }

    // -------------------------------------------------------------------------
    // Connection management
    // -------------------------------------------------------------------------

    private ModbusTcpClient GetOrCreateClient()
    {
        if (_isConnected && _client != null)
            return _client;

        DropClient();
        var client = new ModbusTcpClient();

        // FluentModbus Connect(string, ModbusEndianness) always uses port 502.
        // For a custom port, resolve the host and use the IPEndPoint overload.
        if (_port == 502)
        {
            client.Connect(_ipAddress, ModbusEndianness.BigEndian);
        }
        else
        {
            var ip = System.Net.IPAddress.TryParse(_ipAddress, out var parsed)
                ? parsed
                : System.Net.Dns.GetHostAddresses(_ipAddress)[0];
            client.Connect(new System.Net.IPEndPoint(ip, _port), ModbusEndianness.BigEndian);
        }

        _client = client;
        _isConnected = true;
        return _client;
    }

    private void DropClient()
    {
        _isConnected = false;
        var old = _client;
        _client = null;
        try { old?.Disconnect(); } catch { }
        try { old?.Dispose();    } catch { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DropClient();
    }
}
