namespace PFBeacon.Models;

public enum PfListingEventType
{
    ListingSeen,
    ListingUpdated,
    ListingRefresh,
}

public sealed record PfListingEvent
{
    public required PfListingEventType EventType { get; init; }
    public required PfListingSnapshot Listing { get; init; }
}
