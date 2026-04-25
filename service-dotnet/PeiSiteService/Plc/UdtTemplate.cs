using System.Text;
using libplctag;
using libplctag.DataTypes;

namespace PeiSiteService.Plc;

/// <summary>
/// Represents a resolved UDT/template structure read from the PLC via @udt/{handle}.
/// </summary>
public record UdtTemplate(string TemplateName, IReadOnlyList<UdtMember> Members);

public record UdtMember(string Name, ushort TypeCode);

/// <summary>
/// IPlcMapper that decodes the binary payload returned by @udt/{handle}.
///
/// Correct AB CIP template object layout (all little-endian):
///   [0-3]    UINT32 — object definition size in 32-bit words
///   [4-7]    UINT32 — structure size in bytes (when instantiated)
///   [8-9]    UINT16 — member count
///   [10-11]  UINT16 — structure handle (same as the type code)
///   [12+]    Member records, 8 bytes each:
///              [+0-1]  UINT16 — array element count / flags
///              [+2-3]  UINT16 — member type code
///              [+4-7]  UINT32 — byte offset within structure
///   After member records: null-terminated ASCII strings
///              First:  template/UDT name
///              Then:   one name per member, in member order
/// </summary>
public class UdtTemplatePlcMapper : IPlcMapper<UdtTemplate?>
{
    public PlcType PlcType { get; set; } = libplctag.PlcType.ControlLogix;
    // ElementSize=1 tells libplctag to treat the response as raw bytes so
    // the full PLC payload is available in Decode() via tag.GetSize().
    public int? ElementSize => 1;
    public int[] ArrayDimensions { get; set; } = Array.Empty<int>();
    public int? GetElementCount() => null;

    private const int HeaderSize = 12;   // 4 + 4 + 2 + 2
    private const int RecordSize = 8;    // per member record

    public UdtTemplate? Decode(Tag tag)
    {
        int size = tag.GetSize();
        if (size < HeaderSize) return null;

        // Member count is at byte offset 8
        ushort memberCount = tag.GetUInt16(8);
        if (memberCount == 0 || memberCount > 512) return null;

        int recordsEnd = HeaderSize + memberCount * RecordSize;
        if (recordsEnd > size) return null;

        // Parse member type codes — at offset +2 within each 8-byte record
        var memberTypeCodes = new ushort[memberCount];
        for (int i = 0; i < memberCount; i++)
        {
            int recordOffset = HeaderSize + i * RecordSize;
            memberTypeCodes[i] = tag.GetUInt16(recordOffset + 2);
        }

        // Parse null-terminated ASCII name strings after the member records
        // First string = template name, then one per member
        var names = new List<string>(memberCount + 1);
        int strOffset = recordsEnd;
        while (strOffset < size && names.Count <= memberCount)
        {
            int start = strOffset;
            while (strOffset < size && tag.GetUInt8(strOffset) != 0)
                strOffset++;

            int len = strOffset - start;
            if (len > 0)
            {
                var bytes = new byte[len];
                for (int i = 0; i < len; i++)
                    bytes[i] = tag.GetUInt8(start + i);
                names.Add(Encoding.ASCII.GetString(bytes));
            }
            else
            {
                names.Add(""); // empty string between nulls
            }
            strOffset++; // skip null terminator
        }

        // First string is the template/UDT name
        string templateName = names.Count > 0 && !string.IsNullOrWhiteSpace(names[0])
            ? names[0] : "Unknown";

        var members = new List<UdtMember>(memberCount);
        for (int i = 0; i < memberCount; i++)
        {
            string memberName = (i + 1) < names.Count ? names[i + 1] : $"member{i}";
            members.Add(new UdtMember(memberName, memberTypeCodes[i]));
        }

        return new UdtTemplate(templateName, members);
    }

    public void Encode(Tag tag, UdtTemplate? value) => throw new NotSupportedException();
}
