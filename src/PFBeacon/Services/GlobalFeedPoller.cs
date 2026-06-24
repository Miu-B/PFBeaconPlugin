using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using PFBeacon.Models;

namespace PFBeacon.Services;

internal sealed class GlobalFeedPoller : IDisposable
{
    private const int FeedPageLimit = 20;
    private const int MaxImmediatePages = 3;
    private const ushort BrandColor = 576;
    private const ushort NewColor = 45;
    private const ushort UpdatedColor = 500;

    private readonly Configuration configuration;
    private readonly BotApiClient botApiClient;
    private readonly CancellationTokenSource cts = new();
    private readonly Task worker;
    private string? cursor;
    private string? lastSettingsKey;
    private string? lastToken;
    private string? rejectedToken;
    private int consecutiveFailures;
    private bool disposed;

    public GlobalFeedPoller(Configuration configuration, BotApiClient botApiClient)
    {
        this.configuration = configuration;
        this.botApiClient = botApiClient;
        worker = Task.Run(WorkerLoopAsync);
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        cts.Cancel();

        try
        {
            worker.Wait(TimeSpan.FromSeconds(2));
        }
        catch (Exception ex)
        {
            PluginServices.Log.Debug(ex, "PFBeacon global feed poller did not stop cleanly");
        }

        cts.Dispose();
    }

    private async Task WorkerLoopAsync()
    {
        while (!cts.IsCancellationRequested)
        {
            var delay = TimeSpan.FromSeconds(30);

            try
            {
                if (!TryBuildPollRequest(out var dataCenters, out var categories, out var settingsKey))
                {
                    ResetCursor();
                    await DelayAsync(delay).ConfigureAwait(false);
                    continue;
                }

                if (!string.Equals(lastSettingsKey, settingsKey, StringComparison.Ordinal)
                    || !string.Equals(lastToken, configuration.UserApiToken, StringComparison.Ordinal))
                {
                    ResetCursor();
                    lastSettingsKey = settingsKey;
                    lastToken = configuration.UserApiToken;
                }

                if (rejectedToken is not null)
                {
                    if (string.Equals(configuration.UserApiToken, rejectedToken, StringComparison.Ordinal))
                    {
                        await DelayAsync(TimeSpan.FromMinutes(5)).ConfigureAwait(false);
                        continue;
                    }

                    rejectedToken = null;
                }

                delay = await PollFeedAsync(dataCenters, categories).ConfigureAwait(false);
                consecutiveFailures = 0;
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
                break;
            }
            catch (BotApiSendException ex) when (ex.Kind == BotApiSendErrorKind.NotConfigured)
            {
                ResetCursor();
                delay = TimeSpan.FromMinutes(1);
            }
            catch (BotApiSendException ex) when (ex.Kind == BotApiSendErrorKind.InvalidToken)
            {
                rejectedToken = configuration.UserApiToken;
                ResetCursor();
                PluginServices.Log.Error(ex, "PFBeacon token was rejected while polling the global feed; feed alerts paused until token changes");
                delay = TimeSpan.FromMinutes(5);
            }
            catch (BotApiSendException ex) when (ex.Kind == BotApiSendErrorKind.RateLimited)
            {
                delay = AddSmallJitter(ex.RetryAfter ?? TimeSpan.FromMinutes(3));
            }
            catch (BotApiSendException ex) when (ex.Kind == BotApiSendErrorKind.Rejected)
            {
                ResetCursor();
                PluginServices.Log.Warning(ex, "PFBeacon global feed request was rejected by the bot service");
                delay = TimeSpan.FromMinutes(5);
            }
            catch (Exception ex)
            {
                consecutiveFailures++;
                delay = BackoffDelay(consecutiveFailures);
                PluginServices.Log.Warning(ex, "PFBeacon global feed poll failed; retrying in {DelaySeconds}s", delay.TotalSeconds);
            }

            await DelayAsync(delay).ConfigureAwait(false);
        }
    }

    private async Task<TimeSpan> PollFeedAsync(IReadOnlyList<string> dataCenters, IReadOnlyList<string> categories)
    {
        var pages = 0;
        var hasMore = false;

        do
        {
            var response = await botApiClient.GetListingFeedAsync(
                dataCenters,
                categories,
                cursor,
                FeedPageLimit,
                cts.Token).ConfigureAwait(false);

            cursor = response.Cursor;
            hasMore = response.HasMore;
            pages++;

            foreach (var item in response.Items)
                Notify(item);
        }
        while (hasMore && pages < MaxImmediatePages && !cts.IsCancellationRequested);

        return hasMore ? TimeSpan.FromSeconds(15) : PollDelay();
    }

    private bool TryBuildPollRequest(out IReadOnlyList<string> dataCenters, out IReadOnlyList<string> categories, out string settingsKey)
    {
        dataCenters = Array.Empty<string>();
        categories = Array.Empty<string>();
        settingsKey = string.Empty;

        if (!configuration.GlobalChatAlertsEnabled)
            return false;

        if (string.IsNullOrWhiteSpace(configuration.UserApiToken))
            return false;

        if (!PluginServices.ClientState.IsLoggedIn)
            return false;

        dataCenters = configuration.GlobalAlertDataCenters
            .Where(dataCenter => Configuration.KnownDataCenters.Contains(dataCenter, StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .Take(4)
            .ToArray();

        if (dataCenters.Count == 0)
            return false;

        categories = configuration.GetIncludedContentCategories();
        if (categories.Count == 0)
            return false;

        settingsKey = $"{string.Join(',', dataCenters)}|{string.Join(',', categories)}";
        return true;
    }

    private static void Notify(ListingFeedItem item)
    {
        if (string.Equals(item.ChangeType, "revived", StringComparison.Ordinal))
            return;

        var message = BuildChatMessage(item);
        if (message is null)
            return;

        PluginServices.Framework.RunOnFrameworkThread(() =>
        {
            try
            {
                PluginServices.ChatGui.Print(new XivChatEntry
                {
                    Message = message,
                    Type = XivChatType.SystemMessage,
                });
            }
            catch (Exception ex)
            {
                PluginServices.Log.Warning(ex, "Failed to print PFBeacon global feed alert to chat");
            }
        });
    }

    private static SeString? BuildChatMessage(ListingFeedItem item)
    {
        var dataCenter = SanitizeInline(item.DataCenter, 32);
        var title = SanitizeInline(item.DisplayTitle, 180);
        if (string.IsNullOrWhiteSpace(dataCenter) || string.IsNullOrWhiteSpace(title))
            return null;

        var (change, changeColor) = item.ChangeType switch
        {
            "new" => ("New", NewColor),
            "updated" => ("Upd", UpdatedColor),
            _ => ("Upd", UpdatedColor),
        };
        var privateText = item.IsPrivate ? "[🔒]" : string.Empty;
        var filled = Math.Clamp(item.FilledCount, 0, Math.Max(1, item.MaxPlayers));
        var max = Math.Clamp(item.MaxPlayers, 1, 8);
        var openSlots = SanitizeInline(item.OpenSlotsText, 160);

        var builder = new SeStringBuilder()
            .AddUiForeground("[PFBeacon]", BrandColor)
            .AddUiForeground($"[{change}]", changeColor)
            .AddText($"[{dataCenter}]");

        if (!string.IsNullOrWhiteSpace(privateText))
            builder.AddText(privateText);

        builder.AddText(" ");
        AddPartyFinderTitle(builder, item, title);
        builder.AddText($" - {filled}/{max} filled");

        if (!string.IsNullOrWhiteSpace(openSlots))
            builder.AddText($" - Need: {openSlots}");

        return builder.Build();
    }

    private static void AddPartyFinderTitle(SeStringBuilder builder, ListingFeedItem item, string title)
    {
        if (item.ListingId > uint.MaxValue)
        {
            builder.AddText(title);
            return;
        }

        builder
            .AddPartyFinderLink((uint)item.ListingId, isCrossWorld: true)
            .AddText(title)
            .Add(RawPayload.LinkTerminator);
    }

    private static string SanitizeInline(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var sanitized = value
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Replace('\t', ' ');

        while (sanitized.Contains("  ", StringComparison.Ordinal))
            sanitized = sanitized.Replace("  ", " ", StringComparison.Ordinal);

        sanitized = sanitized.Trim();
        return sanitized.Length <= maxLength ? sanitized : $"{sanitized[..Math.Max(0, maxLength - 1)]}…";
    }

    private TimeSpan PollDelay()
    {
        var baseSeconds = Math.Clamp(configuration.GlobalAlertPollIntervalSeconds, 120, 900);
        var jitterRange = Math.Min(30, Math.Max(5, baseSeconds / 6));
        var jitter = Random.Shared.Next(-jitterRange, jitterRange + 1);
        return TimeSpan.FromSeconds(Math.Max(60, baseSeconds + jitter));
    }

    private static TimeSpan AddSmallJitter(TimeSpan delay)
    {
        var seconds = Math.Clamp((int)Math.Ceiling(delay.TotalSeconds), 30, 900);
        return TimeSpan.FromSeconds(seconds + Random.Shared.Next(0, 16));
    }

    private static TimeSpan BackoffDelay(int failures)
    {
        var seconds = Math.Min(900, 30 * Math.Pow(2, Math.Min(failures, 5)));
        return TimeSpan.FromSeconds(seconds + Random.Shared.Next(0, 16));
    }

    private void ResetCursor()
    {
        cursor = null;
        lastSettingsKey = null;
        lastToken = null;
        consecutiveFailures = 0;
    }

    private async Task DelayAsync(TimeSpan delay)
    {
        try
        {
            await Task.Delay(delay, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
        }
    }
}
