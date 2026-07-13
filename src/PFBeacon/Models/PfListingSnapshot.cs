using System.Text.Json.Serialization;

namespace PFBeacon.Models;

public sealed record PfListingSnapshot
{
    public required string CompositeKey { get; init; }
    public required ulong ListingId { get; init; }
    public required string DataCenter { get; init; }

    /// <summary>
    /// Duty/content-finder row ID. Never populate from Dalamud IPartyFinderListing.ContentId.
    /// </summary>
    public required uint ContentId { get; init; }
    public required int ContentLevel { get; init; }

    public required string ContentName { get; init; }
    public required string ContentCategory { get; init; }

    public required bool IsMinimumItemLevel { get; init; }
    public required bool IsNoEcho { get; init; }
    public required bool IsPrivate { get; init; }

    /// <summary>
    /// Actual Party Finder listing capacity. Limited PF listings may advertise fewer slots
    /// than the underlying 8-player duty.
    /// </summary>
    public required int MaxPlayers { get; init; }

    /// <summary>
    /// Underlying duty capacity, used only for local filtering. This is intentionally not
    /// sent to the bot service to keep the public API contract compact.
    /// </summary>
    [JsonIgnore]
    public int DutyMaxPlayers { get; init; }

    public required IReadOnlyList<SlotSummary> OpenSlots { get; init; }
    public required IReadOnlyList<SlotSummary> FilledSlots { get; init; }

    public required DateTimeOffset ObservedAtUtc { get; init; }
    public required string ContentHash { get; init; }
}
