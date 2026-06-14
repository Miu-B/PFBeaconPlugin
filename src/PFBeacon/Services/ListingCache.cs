using PFBeacon.Models;

namespace PFBeacon.Services;

internal sealed class ListingCache
{
    private readonly Configuration configuration;
    private readonly Dictionary<string, CacheEntry> entries = new(StringComparer.Ordinal);
    private readonly object gate = new();

    public ListingCache(Configuration configuration)
    {
        this.configuration = configuration;
    }

    public PfListingEvent? Observe(PfListingSnapshot snapshot)
    {
        var now = DateTimeOffset.UtcNow;

        lock (gate)
        {
            PruneLocked(now);

            if (!entries.TryGetValue(snapshot.CompositeKey, out var entry))
            {
                entries[snapshot.CompositeKey] = new CacheEntry(snapshot.ContentHash, now, now);
                return new PfListingEvent
                {
                    EventType = PfListingEventType.ListingSeen,
                    Listing = snapshot,
                };
            }

            entry.LastSeenAt = now;

            if (!string.Equals(entry.ContentHash, snapshot.ContentHash, StringComparison.Ordinal))
            {
                entry.ContentHash = snapshot.ContentHash;
                entry.LastSentAt = now;
                return new PfListingEvent
                {
                    EventType = PfListingEventType.ListingUpdated,
                    Listing = snapshot,
                };
            }

            if ((now - entry.LastSentAt).TotalSeconds >= configuration.RefreshSendMinIntervalSeconds)
            {
                entry.LastSentAt = now;
                return new PfListingEvent
                {
                    EventType = PfListingEventType.ListingRefresh,
                    Listing = snapshot,
                };
            }

            return null;
        }
    }

    private void PruneLocked(DateTimeOffset now)
    {
        var maxAge = TimeSpan.FromSeconds(configuration.LocalCachePruneSeconds);
        var staleKeys = entries
            .Where(pair => now - pair.Value.LastSeenAt > maxAge)
            .Select(pair => pair.Key)
            .ToArray();

        foreach (var key in staleKeys)
            entries.Remove(key);
    }

    private sealed class CacheEntry
    {
        public CacheEntry(string contentHash, DateTimeOffset lastSeenAt, DateTimeOffset lastSentAt)
        {
            ContentHash = contentHash;
            LastSeenAt = lastSeenAt;
            LastSentAt = lastSentAt;
        }

        public string ContentHash { get; set; }
        public DateTimeOffset LastSeenAt { get; set; }
        public DateTimeOffset LastSentAt { get; set; }
    }
}
