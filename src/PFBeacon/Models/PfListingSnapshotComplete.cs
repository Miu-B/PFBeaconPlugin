namespace PFBeacon.Models;

public sealed record PfListingSnapshotComplete
{
    public required string DataCenter { get; init; }
    public required IReadOnlyList<string> ActiveCompositeKeys { get; init; }
    public required DateTimeOffset ObservedAtUtc { get; init; }
}
