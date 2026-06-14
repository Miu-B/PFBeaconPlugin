using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using PFBeacon.Models;

namespace PFBeacon.Services;

internal enum BotApiSendErrorKind
{
    NotConfigured,
    InvalidToken,
    RateLimited,
    Rejected,
    Transient,
}

internal sealed class BotApiSendException : Exception
{
    public BotApiSendException(BotApiSendErrorKind kind, string message, TimeSpan? retryAfter = null)
        : base(message)
    {
        Kind = kind;
        RetryAfter = retryAfter;
    }

    public BotApiSendErrorKind Kind { get; }
    public TimeSpan? RetryAfter { get; }
}

internal sealed class BotApiClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    private readonly Configuration configuration;
    private readonly HttpClient httpClient;

    public BotApiClient(Configuration configuration)
    {
        this.configuration = configuration;
        httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10),
        };
    }

    public async Task<(bool Ok, string Message)> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        if (!HasMinimumConfiguration())
            return (false, "Bot API URL and token are required.");

        using var request = new HttpRequestMessage(HttpMethod.Get, BuildUri("/api/v1/plugin/me"));
        AddAuthorization(request);

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.IsSuccessStatusCode)
            return (true, "Connection OK.");

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            return (false, "Token was rejected by the bot service.");

        if ((int)response.StatusCode == 429)
            return (false, "Rate limited by the bot service. Try again later.");

        return (false, $"Bot service returned {(int)response.StatusCode}.");
    }

    public async Task SendEventAsync(PfListingEvent listingEvent, CancellationToken cancellationToken = default)
    {
        if (!HasMinimumConfiguration())
            throw new BotApiSendException(BotApiSendErrorKind.NotConfigured, "Bot API URL and token are required.");

        var payload = BuildEventPayload(listingEvent);
        var json = JsonSerializer.Serialize(payload, JsonOptions);

        using var request = new HttpRequestMessage(HttpMethod.Post, BuildUri("/api/v1/listings/events"));
        AddAuthorization(request);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.IsSuccessStatusCode)
            return;

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new BotApiSendException(BotApiSendErrorKind.InvalidToken, "PFBeacon token was rejected by the bot service.");

        if ((int)response.StatusCode == 429)
            throw new BotApiSendException(
                BotApiSendErrorKind.RateLimited,
                "PFBeacon bot service rate limit exceeded.",
                ParseRetryAfter(response));

        if ((int)response.StatusCode >= 500)
            throw new BotApiSendException(BotApiSendErrorKind.Transient, $"PFBeacon bot service failed with {(int)response.StatusCode}.");

        throw new BotApiSendException(BotApiSendErrorKind.Rejected, $"PFBeacon bot service rejected the payload with {(int)response.StatusCode}.");
    }

    public void Dispose()
    {
        httpClient.Dispose();
    }

    private bool HasMinimumConfiguration()
    {
        return !string.IsNullOrWhiteSpace(configuration.BotApiUrl)
               && Uri.TryCreate(configuration.BotApiUrl, UriKind.Absolute, out var uri)
               && (uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback))
               && !string.IsNullOrWhiteSpace(configuration.UserApiToken);
    }

    private Uri BuildUri(string path)
    {
        var baseUrl = configuration.BotApiUrl.Trim().TrimEnd('/');
        return new Uri($"{baseUrl}{path}");
    }

    private void AddAuthorization(HttpRequestMessage request)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", configuration.UserApiToken);
    }

    private object BuildEventPayload(PfListingEvent listingEvent)
    {
        return new
        {
            EventType = EventTypeName(listingEvent.EventType),
            SchemaVersion = 1,
            Client = new
            {
                PluginVersion = typeof(BotApiClient).Assembly.GetName().Version?.ToString() ?? "0.1.0",
                DalamudApiLevel = "current",
                configuration.ClientInstanceId,
            },
            Listing = listingEvent.Listing,
        };
    }

    private static string EventTypeName(PfListingEventType eventType)
    {
        return eventType switch
        {
            PfListingEventType.ListingSeen => "listing_seen",
            PfListingEventType.ListingUpdated => "listing_updated",
            PfListingEventType.ListingRefresh => "listing_refresh",
            _ => "listing_seen",
        };
    }

    private static TimeSpan? ParseRetryAfter(HttpResponseMessage response)
    {
        if (response.Headers.RetryAfter?.Delta is { } delta)
            return delta;

        if (response.Headers.RetryAfter?.Date is { } date)
        {
            var delay = date - DateTimeOffset.UtcNow;
            return delay > TimeSpan.Zero ? delay : TimeSpan.FromSeconds(1);
        }

        return null;
    }
}
