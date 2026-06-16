using System.Text.Json.Serialization;

namespace PFBeacon.Models;

public sealed record SlotSummary
{
    public required string Role { get; init; }
    public string? Job { get; init; }
    public required int Count { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? AcceptedRoles { get; init; }
}
