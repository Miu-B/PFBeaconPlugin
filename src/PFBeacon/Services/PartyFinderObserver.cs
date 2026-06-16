using Dalamud.Game.Gui.PartyFinder.Types;
using PFBeacon.Models;

namespace PFBeacon.Services;

internal sealed class PartyFinderObserver : IDisposable
{
    private readonly Configuration configuration;
    private readonly ListingMapper mapper;
    private readonly ListingFilter filter;
    private readonly ListingCache listingCache;
    private readonly OutboundEventQueue outboundEventQueue;
    private readonly object snapshotGate = new();
    private readonly Dictionary<string, HashSet<string>> snapshotKeysByDataCenter = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource snapshotCts = new();
    private int snapshotGeneration;
    private bool disposed;

    public PartyFinderObserver(
        Configuration configuration,
        ListingMapper mapper,
        ListingFilter filter,
        ListingCache listingCache,
        OutboundEventQueue outboundEventQueue)
    {
        this.configuration = configuration;
        this.mapper = mapper;
        this.filter = filter;
        this.listingCache = listingCache;
        this.outboundEventQueue = outboundEventQueue;
    }

    public void Start()
    {
        PluginServices.PartyFinderGui.ReceiveListing += OnReceiveListing;
        PluginServices.Log.Information("PFBeacon Party Finder observer subscribed");
    }

    public void Dispose()
    {
        if (disposed)
            return;

        PluginServices.PartyFinderGui.ReceiveListing -= OnReceiveListing;
        snapshotCts.Cancel();
        snapshotCts.Dispose();
        disposed = true;
    }

    private void OnReceiveListing(IPartyFinderListing listing, IPartyFinderListingEventArgs args)
    {
        try
        {
            if (!configuration.Enabled)
                return;

            var snapshot = mapper.TryMap(listing);
            if (snapshot is null)
                return;

            var isMatch = args.Visible && filter.IsMatch(snapshot);
            if (isMatch)
                RecordSnapshotObservation(snapshot.DataCenter, snapshot.CompositeKey);

            if (!isMatch)
                return;

            var listingEvent = listingCache.Observe(snapshot);
            if (listingEvent is null)
                return;

            outboundEventQueue.Enqueue(listingEvent);
        }
        catch (Exception ex)
        {
            PluginServices.Log.Error(ex, "Failed to process Party Finder listing");
        }
    }

    private void RecordSnapshotObservation(string dataCenter, string activeCompositeKey)
    {
        if (string.IsNullOrWhiteSpace(dataCenter) || string.IsNullOrWhiteSpace(activeCompositeKey))
            return;

        int generation;
        lock (snapshotGate)
        {
            if (!snapshotKeysByDataCenter.TryGetValue(dataCenter, out var activeKeys))
            {
                activeKeys = new HashSet<string>(StringComparer.Ordinal);
                snapshotKeysByDataCenter[dataCenter] = activeKeys;
            }

            activeKeys.Add(activeCompositeKey);

            generation = ++snapshotGeneration;
        }

        _ = FlushSnapshotAfterDelayAsync(generation);
    }

    private async Task FlushSnapshotAfterDelayAsync(int generation)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(configuration.SnapshotDebounceSeconds), snapshotCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        Dictionary<string, string[]> snapshots;
        lock (snapshotGate)
        {
            if (generation != snapshotGeneration || snapshotKeysByDataCenter.Count == 0)
                return;

            snapshots = snapshotKeysByDataCenter.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.OrderBy(key => key, StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);
            snapshotKeysByDataCenter.Clear();
        }

        foreach (var (dataCenter, activeKeys) in snapshots)
            EnqueueCompleteSnapshot(dataCenter, activeKeys);
    }

    private void EnqueueCompleteSnapshot(string dataCenter, IReadOnlyList<string> activeKeys)
    {
        outboundEventQueue.EnqueueSnapshot(new PfListingSnapshotComplete
        {
            DataCenter = dataCenter,
            ActiveCompositeKeys = activeKeys,
            ObservedAtUtc = DateTimeOffset.UtcNow,
        });
    }
}
