namespace PFBeacon.Models;

public sealed record SlotSummary
{
    public required string Role { get; init; }
    public string? Job { get; init; }
    public required int Count { get; init; }
}
