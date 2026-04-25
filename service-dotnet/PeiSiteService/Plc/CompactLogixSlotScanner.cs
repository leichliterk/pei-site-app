using libplctag;
using libplctag.DataTypes;

namespace PeiSiteService.Plc;

/// <summary>
/// Probes chassis slots 0-7 to find the first slot that responds with a CPU.
/// Used when PlcSettings.Slot = -1 (auto-discover).
/// </summary>
public static class CompactLogixSlotScanner
{
    public static async Task<int> FindFirstSlotAsync(string ipAddress, CancellationToken ct)
    {
        for (int slot = 0; slot <= 7; slot++)
        {
            if (await SlotRespondsAsync(ipAddress, slot, ct))
                return slot;
        }
        return -1;
    }

    private static async Task<bool> SlotRespondsAsync(string ipAddress, int slot, CancellationToken ct)
    {
        try
        {
            // Read a well-known system tag that exists on every AB controller
            using var tag = new Tag<DintPlcMapper, int>
            {
                Name = "@CPU",
                Gateway = ipAddress,
                PlcType = PlcType.ControlLogix,
                Protocol = Protocol.ab_eip,
                Path = $"1,{slot}",
                Timeout = TimeSpan.FromSeconds(2)
            };

            await tag.InitializeAsync(ct);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
