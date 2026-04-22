using libplctag;
using libplctag.DataTypes;

namespace PeiSiteService.Plc;

/// <summary>
/// Reads a configured list of tags from a single CompactLogix/ControlLogix PLC.
/// All tags are read concurrently via Task.WhenAll.
/// </summary>
public class CompactLogixTagReader
{
    private readonly string _ipAddress;
    private readonly int _slot;
    private readonly IReadOnlyList<TagDefinition> _tags;

    public CompactLogixTagReader(string ipAddress, int slot, IReadOnlyList<TagDefinition> tags)
    {
        _ipAddress = ipAddress;
        _slot = slot;
        _tags = tags;
    }

    public async Task<PlcSnapshot> ReadAllAsync(CancellationToken ct)
    {
        if (_tags.Count == 0)
        {
            return new PlcSnapshot(_ipAddress, _slot, DateTimeOffset.UtcNow, true, Array.Empty<TagSnapshot>());
        }

        var tasks = _tags.Select(t => ReadTagAsync(t, ct)).ToList();
        TagSnapshot[] results;
        bool connected = true;

        try
        {
            results = await Task.WhenAll(tasks);
            // If any tag errored, still mark connected — individual tag errors are expected
            // Only mark disconnected if we got a timeout/connection error pattern
            connected = results.Any(r => !r.Error) || results.Length == 0;
        }
        catch (Exception ex)
        {
            // Catastrophic failure — build error snapshots for all tags
            connected = false;
            results = _tags.Select(t => new TagSnapshot(
                t.Name, t.DataType, null,
                t.DisplayName, t.Unit,
                true, ex.Message)).ToArray();
        }

        return new PlcSnapshot(_ipAddress, _slot, DateTimeOffset.UtcNow, connected, results);
    }

    private async Task<TagSnapshot> ReadTagAsync(TagDefinition def, CancellationToken ct)
    {
        try
        {
            object? value = def.DataType.ToUpperInvariant() switch
            {
                "REAL"   => await ReadTypedAsync<RealPlcMapper, float>(def.Name, ct),
                "BOOL"   => await ReadTypedAsync<BoolPlcMapper, bool>(def.Name, ct),
                "DINT"   => await ReadTypedAsync<DintPlcMapper, int>(def.Name, ct),
                "STRING" => await ReadStringAsync(def.Name, ct),
                _        => throw new NotSupportedException($"Unsupported data type: {def.DataType}")
            };

            return new TagSnapshot(def.Name, def.DataType, value, def.DisplayName, def.Unit, false, null);
        }
        catch (Exception ex)
        {
            return new TagSnapshot(def.Name, def.DataType, null, def.DisplayName, def.Unit, true, ex.Message);
        }
    }

    private async Task<T> ReadTypedAsync<TMapper, T>(string tagName, CancellationToken ct)
        where TMapper : IPlcMapper<T>, new()
    {
        using var tag = new Tag<TMapper, T>
        {
            Name = tagName,
            Gateway = _ipAddress,
            PlcType = PlcType.ControlLogix,
            Protocol = Protocol.ab_eip,
            Path = $"1,{_slot}",
            Timeout = TimeSpan.FromSeconds(5)
        };

        await tag.InitializeAsync(ct);
        await tag.ReadAsync(ct);
        return tag.Value;
    }

    private async Task<string> ReadStringAsync(string tagName, CancellationToken ct)
    {
        using var tag = new Tag<StringPlcMapper, string>
        {
            Name = tagName,
            Gateway = _ipAddress,
            PlcType = PlcType.ControlLogix,
            Protocol = Protocol.ab_eip,
            Path = $"1,{_slot}",
            Timeout = TimeSpan.FromSeconds(5)
        };

        await tag.InitializeAsync(ct);
        await tag.ReadAsync(ct);
        return tag.Value ?? "";
    }
}
