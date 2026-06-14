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

    public required string ContentName { get; init; }
    public required string ContentCategory { get; init; }

    public required bool IsMinimumItemLevel { get; init; }
    public required bool IsNoEcho { get; init; }
    public required int MaxPlayers { get; init; }

    public required IReadOnlyList<SlotSummary> OpenSlots { get; init; }
    public required IReadOnlyList<SlotSummary> FilledSlots { get; init; }

    public required DateTimeOffset ObservedAtUtc { get; init; }
    public required string ContentHash { get; init; }
}
