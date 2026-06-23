namespace PFBeacon.Models;

internal sealed class ListingFeedResponse
{
    public bool Ok { get; init; }
    public int SchemaVersion { get; init; }
    public string Cursor { get; init; } = string.Empty;
    public bool HasMore { get; init; }
    public List<ListingFeedItem> Items { get; init; } = new();
}

internal sealed class ListingFeedItem
{
    public string Sequence { get; init; } = string.Empty;
    public string ChangeType { get; init; } = string.Empty;
    public string CompositeKey { get; init; } = string.Empty;
    public ulong ListingId { get; init; }
    public string DataCenter { get; init; } = string.Empty;
    public string ContentCategory { get; init; } = string.Empty;
    public int? ContentLevel { get; init; }
    public string DisplayTitle { get; init; } = string.Empty;
    public bool IsSpoilerRedacted { get; init; }
    public bool IsPrivate { get; init; }
    public int FilledCount { get; init; }
    public int MaxPlayers { get; init; }
    public List<SlotSummary> OpenSlots { get; init; } = new();
    public string OpenSlotsText { get; init; } = string.Empty;
    public DateTimeOffset UpdatedAtUtc { get; init; }
    public DateTimeOffset LastSeenAtUtc { get; init; }
}
