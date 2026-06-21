using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PFBeacon.Models;

namespace PFBeacon.Util;

internal static class Hashing
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    public static string ComputeContentHash(PfListingSnapshot snapshot)
    {
        var stable = new
        {
            snapshot.CompositeKey,
            snapshot.ContentId,
            snapshot.ContentLevel,
            snapshot.ContentName,
            snapshot.ContentCategory,
            snapshot.IsMinimumItemLevel,
            snapshot.IsNoEcho,
            snapshot.IsPrivate,
            snapshot.MaxPlayers,
            OpenSlots = NormalizeSlots(snapshot.OpenSlots),
            FilledSlots = NormalizeSlots(snapshot.FilledSlots),
        };

        var json = JsonSerializer.Serialize(stable, JsonOptions);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return $"sha256:{Convert.ToHexString(bytes).ToLowerInvariant()}";
    }

    private static IReadOnlyList<SlotSummary> NormalizeSlots(IEnumerable<SlotSummary> slots)
    {
        return slots
            .OrderBy(slot => slot.Role, StringComparer.Ordinal)
            .ThenBy(slot => slot.Job ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(slot => slot.AcceptedRoles is null ? string.Empty : string.Join(',', slot.AcceptedRoles))
            .ThenBy(slot => slot.Count)
            .ToArray();
    }
}
