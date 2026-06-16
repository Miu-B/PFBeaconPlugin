using PFBeacon.Models;

namespace PFBeacon.Services;

internal sealed class OutboundEventQueue : IDisposable
{
    private const int MaxQueuedEvents = 100;
    private static readonly TimeSpan MaxRetryAge = TimeSpan.FromMinutes(5);

    private readonly Configuration configuration;
    private readonly BotApiClient botApiClient;
    private readonly Queue<QueuedEvent> queue = new();
    private readonly Dictionary<string, QueuedEvent> debouncedEvents = new(StringComparer.Ordinal);
    private readonly HashSet<string> scheduledDebounceKeys = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim signal = new(0);
    private readonly CancellationTokenSource cts = new();
    private readonly object gate = new();
    private readonly Task worker;
    private string? rejectedToken;
    private bool disposed;

    public OutboundEventQueue(Configuration configuration, BotApiClient botApiClient)
    {
        this.configuration = configuration;
        this.botApiClient = botApiClient;
        worker = Task.Run(WorkerLoopAsync);
    }

    public void Enqueue(PfListingEvent listingEvent)
    {
        if (disposed)
            return;

        if (listingEvent.EventType == PfListingEventType.ListingRefresh)
        {
            EnqueueImmediate(QueuedEvent.FromEvent(listingEvent, DateTimeOffset.UtcNow));
            return;
        }

        EnqueueDebounced(listingEvent);
    }

    public void EnqueueSnapshot(PfListingSnapshotComplete snapshot)
    {
        if (disposed)
            return;

        EnqueueImmediate(QueuedEvent.FromSnapshot(snapshot, DateTimeOffset.UtcNow));
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        cts.Cancel();
        signal.Release();

        try
        {
            worker.Wait(TimeSpan.FromSeconds(2));
        }
        catch (Exception ex)
        {
            PluginServices.Log.Debug(ex, "PFBeacon outbound queue did not stop cleanly");
        }

        signal.Dispose();
        cts.Dispose();
    }

    private void EnqueueDebounced(PfListingEvent listingEvent)
    {
        var key = listingEvent.Listing.CompositeKey;
        var shouldSchedule = false;

        lock (gate)
        {
            if (debouncedEvents.TryGetValue(key, out var existing)
                && existing.Event?.EventType == PfListingEventType.ListingSeen)
            {
                listingEvent = listingEvent with { EventType = PfListingEventType.ListingSeen };
            }

            debouncedEvents[key] = QueuedEvent.FromEvent(listingEvent, DateTimeOffset.UtcNow);

            if (scheduledDebounceKeys.Add(key))
                shouldSchedule = true;
        }

        if (shouldSchedule)
            _ = FlushDebouncedAfterDelayAsync(key);
    }

    private async Task FlushDebouncedAfterDelayAsync(string compositeKey)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(configuration.UpdateDebounceSeconds), cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        QueuedEvent? queuedEvent;
        lock (gate)
        {
            debouncedEvents.Remove(compositeKey, out queuedEvent);
            scheduledDebounceKeys.Remove(compositeKey);
        }

        if (queuedEvent is not null)
            EnqueueImmediate(queuedEvent);
    }

    private void EnqueueImmediate(QueuedEvent queuedEvent)
    {
        lock (gate)
        {
            if (queue.Count >= MaxQueuedEvents)
            {
                if (!DropOldestRefreshLocked() && queuedEvent.IsRefresh)
                    return;

                if (queue.Count >= MaxQueuedEvents)
                    DropOldestLocked();
            }

            queue.Enqueue(queuedEvent);
            signal.Release();
        }
    }

    private async Task WorkerLoopAsync()
    {
        while (!cts.IsCancellationRequested)
        {
            try
            {
                await signal.WaitAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            QueuedEvent? queuedEvent;
            lock (gate)
            {
                queuedEvent = queue.Count > 0 ? queue.Dequeue() : null;
            }

            if (queuedEvent is null)
                continue;

            await SendWithRetryAsync(queuedEvent, cts.Token).ConfigureAwait(false);
        }
    }

    private async Task SendWithRetryAsync(QueuedEvent queuedEvent, CancellationToken cancellationToken)
    {
        var attempt = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            if (DateTimeOffset.UtcNow - queuedEvent.EnqueuedAt > MaxRetryAge)
            {
                PluginServices.Log.Warning(
                    "PFBeacon dropped {OutboundType} for {OutboundKey}: retry age exceeded",
                    queuedEvent.OutboundType,
                    queuedEvent.OutboundKey);
                return;
            }

            if (rejectedToken is not null)
            {
                if (string.Equals(configuration.UserApiToken, rejectedToken, StringComparison.Ordinal))
                    return;

                rejectedToken = null;
            }

            try
            {
                if (queuedEvent.Event is { } listingEvent)
                    await botApiClient.SendEventAsync(listingEvent, cancellationToken).ConfigureAwait(false);
                else if (queuedEvent.Snapshot is { } snapshot)
                    await botApiClient.SendSnapshotAsync(snapshot, cancellationToken).ConfigureAwait(false);
                else
                    return;

                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (BotApiSendException ex) when (ex.Kind == BotApiSendErrorKind.NotConfigured)
            {
                PluginServices.Log.Warning("PFBeacon outbound send skipped: bot API URL/token is not configured");
                return;
            }
            catch (BotApiSendException ex) when (ex.Kind == BotApiSendErrorKind.InvalidToken)
            {
                rejectedToken = configuration.UserApiToken;
                PluginServices.Log.Error(ex, "PFBeacon token was rejected by the bot service; sending paused until token changes");
                return;
            }
            catch (BotApiSendException ex) when (ex.Kind == BotApiSendErrorKind.Rejected)
            {
                PluginServices.Log.Warning(
                    ex,
                    "PFBeacon bot rejected {OutboundType} for {OutboundKey}",
                    queuedEvent.OutboundType,
                    queuedEvent.OutboundKey);
                return;
            }
            catch (BotApiSendException ex) when (ex.Kind == BotApiSendErrorKind.RateLimited)
            {
                var delay = ex.RetryAfter ?? TimeSpan.FromSeconds(30);
                await DelayBeforeRetryAsync(delay, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                attempt++;
                var delay = TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, Math.Min(attempt, 5))));

                PluginServices.Log.Warning(
                    ex,
                    "PFBeacon send failed for {OutboundKey}; retrying in {DelaySeconds}s",
                    queuedEvent.OutboundKey,
                    delay.TotalSeconds);

                await DelayBeforeRetryAsync(delay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static async Task DelayBeforeRetryAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private bool DropOldestRefreshLocked()
    {
        if (!queue.Any(item => item.IsRefresh))
            return false;

        var rebuilt = new Queue<QueuedEvent>(queue.Count);
        var dropped = false;

        while (queue.Count > 0)
        {
            var item = queue.Dequeue();
            if (!dropped && item.IsRefresh)
            {
                dropped = true;
                continue;
            }

            rebuilt.Enqueue(item);
        }

        while (rebuilt.Count > 0)
            queue.Enqueue(rebuilt.Dequeue());

        return true;
    }

    private void DropOldestLocked()
    {
        if (queue.Count == 0)
            return;

        var dropped = queue.Dequeue();
        PluginServices.Log.Warning(
            "PFBeacon outbound queue full; dropped {OutboundType} for {OutboundKey}",
            dropped.OutboundType,
            dropped.OutboundKey);
    }

    private sealed record QueuedEvent(PfListingEvent? Event, PfListingSnapshotComplete? Snapshot, DateTimeOffset EnqueuedAt)
    {
        public static QueuedEvent FromEvent(PfListingEvent listingEvent, DateTimeOffset enqueuedAt)
            => new(listingEvent, null, enqueuedAt);

        public static QueuedEvent FromSnapshot(PfListingSnapshotComplete snapshot, DateTimeOffset enqueuedAt)
            => new(null, snapshot, enqueuedAt);

        public bool IsRefresh => Event?.EventType == PfListingEventType.ListingRefresh;

        public string OutboundType => Event?.EventType.ToString() ?? "SnapshotComplete";

        public string OutboundKey => Event?.Listing.CompositeKey ?? Snapshot?.DataCenter ?? "unknown";
    }
}
