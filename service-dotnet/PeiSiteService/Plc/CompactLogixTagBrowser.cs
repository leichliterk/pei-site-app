using libplctag;
using libplctag.DataTypes;
using PeiSiteService.Services;

namespace PeiSiteService.Plc;

/// <summary>
/// Queries the PLC tag database using the @tags / Program:{name}.@tags system attribute.
/// UDT instances are expanded recursively by reading @udt/{handle} templates.
/// Returns a flat list of all leaf (atomic) tags with their full dotted paths,
/// plus UDT container rows (IsUdtContainer=true) for grouping in the UI.
/// </summary>
public class CompactLogixTagBrowser
{
    private readonly string _ipAddress;
    private readonly int _slot;
    private readonly FileLogger? _logger;

    // Cache of template handle → template (null = read failed or unsupported)
    private readonly Dictionary<ushort, UdtTemplate?> _templateCache = new();

    private const int MaxDepth = 8;

    public CompactLogixTagBrowser(string ipAddress, int slot, FileLogger? logger = null)
    {
        _ipAddress = ipAddress;
        _slot = slot;
        _logger = logger;
    }

    public async Task<IReadOnlyList<DiscoveredTag>> BrowseAsync(CancellationToken ct)
    {
        _templateCache.Clear();
        var results = new List<DiscoveredTag>();

        var controllerTags = await ReadRawTagListAsync("@tags", null, ct);
        _logger?.Log($"[TagBrowser] @tags returned {controllerTags.Count} entries ({controllerTags.Count(t => IsUdtTypeCode(t.typeCode))} UDT containers)");

        foreach (var (name, typeCode, program) in controllerTags)
            await ExpandTagAsync(name, typeCode, program, results, depth: 0, ct);

        // Program-scope: program containers are type 0x02
        var programNames = controllerTags
            .Where(t => (t.typeCode & 0xFF) == 0x02)
            .Select(t => t.name)
            .ToList();

        foreach (var prog in programNames)
        {
            try
            {
                var progTags = await ReadRawTagListAsync($"Program:{prog}.@tags", prog, ct);
                _logger?.Log($"[TagBrowser] Program:{prog} returned {progTags.Count} entries");
                foreach (var (name, typeCode, program) in progTags)
                    await ExpandTagAsync(name, typeCode, program, results, depth: 0, ct);
            }
            catch { }
        }

        return results;
    }

    /// <summary>
    /// Recursively expands a single tag. Atomic tags are added as leaves.
    /// UDT tags emit a container row then recurse into their members.
    /// </summary>
    private async Task ExpandTagAsync(
        string fullPath, ushort typeCode, string? program,
        List<DiscoveredTag> results, int depth, CancellationToken ct)
    {
        if (depth > MaxDepth) return;

        string dataType = MapTypeCode(typeCode);

        if (!IsUdtTypeCode(typeCode))
        {
            // Atomic leaf — add directly
            if (dataType != "PROGRAM")
                results.Add(new DiscoveredTag(fullPath, dataType, program, IsUdtContainer: false));
            return;
        }

        // UDT — read template, emit container row, recurse into members
        ushort handle = (ushort)(typeCode & 0x0FFF);
        var template = await ReadTemplateAsync(handle, ct);

        string containerType = template?.TemplateName ?? $"0x{typeCode:X4}";
        results.Add(new DiscoveredTag(fullPath, containerType, program, IsUdtContainer: true));

        if (template == null) return;

        foreach (var member in template.Members)
        {
            if (IsInternalMemberName(member.Name)) continue;

            string memberPath = $"{fullPath}.{member.Name}";
            await ExpandTagAsync(memberPath, member.TypeCode, program, results, depth + 1, ct);
        }
    }

    private async Task<List<(string name, ushort typeCode, string? program)>> ReadRawTagListAsync(
        string tagName, string? program, CancellationToken ct)
    {
        var result = new List<(string, ushort, string?)>();
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
                    if (info.Name.StartsWith('@')) continue;
                    result.Add((info.Name, info.Type, program));
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.Log($"[TagBrowser] ReadRawTagListAsync({tagName}) failed: {ex.GetType().Name}: {ex.Message}");
        }
        return result;
    }

    private async Task<UdtTemplate?> ReadTemplateAsync(ushort handle, CancellationToken ct)
    {
        if (_templateCache.TryGetValue(handle, out var cached)) return cached;

        try
        {
            using var tag = new Tag<UdtTemplatePlcMapper, UdtTemplate?>
            {
                Name = $"@udt/{handle}",
                Gateway = _ipAddress,
                PlcType = PlcType.ControlLogix,
                Protocol = Protocol.ab_eip,
                Path = $"1,{_slot}",
                Timeout = TimeSpan.FromSeconds(5)
            };

            await tag.InitializeAsync(ct);
            await tag.ReadAsync(ct);

            var template = tag.Value;
            if (template != null)
                _logger?.Log($"[TagBrowser] UDT handle {handle}: '{template.TemplateName}' with {template.Members.Count} members");
            else
                _logger?.Log($"[TagBrowser] UDT handle {handle}: Decode returned null");

            _templateCache[handle] = template;
            return template;
        }
        catch (Exception ex)
        {
            _logger?.Log($"[TagBrowser] UDT handle {handle}: read failed — {ex.GetType().Name}: {ex.Message}");
            _templateCache[handle] = null;
            return null;
        }
    }

    private static bool IsUdtTypeCode(ushort typeCode) =>
        (typeCode & 0xFF) switch
        {
            0xCA or 0xC1 or 0xC4 or 0xD0 or 0xC3 or 0xC2 or 0xC5 => false,
            0x02 => false,
            _ => true
        };

    private static bool IsInternalMemberName(string name) =>
        string.IsNullOrWhiteSpace(name) ||
        name.StartsWith("ZZZZZ", StringComparison.OrdinalIgnoreCase) ||
        char.IsDigit(name[0]) ||
        name.Contains('@');

    private static string MapTypeCode(ushort typeCode) =>
        (typeCode & 0xFF) switch
        {
            0xCA => "REAL",
            0xC1 => "BOOL",
            0xC4 => "DINT",
            0xD0 => "STRING",
            0xC3 => "INT",
            0xC2 => "SINT",
            0xC5 => "LINT",
            0x02 => "PROGRAM",
            _ => $"0x{typeCode:X4}"
        };
}
