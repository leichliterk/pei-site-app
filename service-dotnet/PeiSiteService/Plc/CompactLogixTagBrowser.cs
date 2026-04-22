using libplctag;
using libplctag.DataTypes;

namespace PeiSiteService.Plc;

/// <summary>
/// Queries the PLC tag database using the @tags / Program:{name}.@tags system attribute.
/// Returns a flat list of controller-scope and program-scope tags.
/// </summary>
public class CompactLogixTagBrowser
{
    private readonly string _ipAddress;
    private readonly int _slot;

    public CompactLogixTagBrowser(string ipAddress, int slot)
    {
        _ipAddress = ipAddress;
        _slot = slot;
    }

    public async Task<IReadOnlyList<DiscoveredTag>> BrowseAsync(CancellationToken ct)
    {
        var results = new List<DiscoveredTag>();

        // Controller-scope tags
        var controllerTags = await ReadTagListAsync("@tags", null, ct);
        results.AddRange(controllerTags);

        // Program-scope tags — first get program names from controller tags
        var programNames = controllerTags
            .Where(t => t.DataType == "PROGRAM")
            .Select(t => t.Name)
            .ToList();

        foreach (var program in programNames)
        {
            try
            {
                var programTags = await ReadTagListAsync($"Program:{program}.@tags", program, ct);
                results.AddRange(programTags);
            }
            catch
            {
                // Skip unreadable program scopes
            }
        }

        return results;
    }

    private async Task<List<DiscoveredTag>> ReadTagListAsync(string tagName, string? program, CancellationToken ct)
    {
        var tags = new List<DiscoveredTag>();

        try
        {
            using var tag = new Tag<TagInfoPlcMapper, TagInfo[]>
            {
                Name = tagName,
                Gateway = _ipAddress,
                PlcType = PlcType.ControlLogix,
                Protocol = Protocol.ab_eip,
                Path = $"1,{_slot}",
                Timeout = TimeSpan.FromSeconds(10)
            };

            await tag.InitializeAsync(ct);
            await tag.ReadAsync(ct);

            if (tag.Value != null)
            {
                foreach (var info in tag.Value)
                {
                    if (string.IsNullOrEmpty(info.Name)) continue;
                    // Skip internal/system tags starting with @
                    if (info.Name.StartsWith('@')) continue;

                    tags.Add(new DiscoveredTag(
                        info.Name,
                        MapTypeCode(info.Type),
                        program
                    ));
                }
            }
        }
        catch
        {
            // Return empty on error
        }

        return tags;
    }

    private static string MapTypeCode(ushort typeCode)
    {
        // AB type codes — common ones
        return (typeCode & 0xFF) switch
        {
            0xCA => "REAL",
            0xC1 => "BOOL",
            0xC4 => "DINT",
            0xD0 => "STRING",
            0xC3 => "INT",
            0xC2 => "SINT",
            0xC5 => "LINT",
            0x02 => "PROGRAM", // program scope marker
            _ => $"0x{typeCode:X4}"
        };
    }
}
