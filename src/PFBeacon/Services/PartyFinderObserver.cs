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
        disposed = true;
    }

    private void OnReceiveListing(IPartyFinderListing listing, IPartyFinderListingEventArgs args)
    {
        try
        {
            if (!configuration.Enabled)
                return;

            if (configuration.DebugLogging)
                LogSanitizedRawListing(listing, args);

            var snapshot = mapper.TryMap(listing);
            if (snapshot is null)
                return;

            if (configuration.DebugLogging)
                LogSanitizedObservation(snapshot, "observed");

            if (!filter.IsMatch(snapshot))
                return;

            LogSanitizedObservation(snapshot, "matched");

            if (configuration.FeasibilityLoggingOnly)
                return;

            var listingEvent = listingCache.Observe(snapshot);
            if (listingEvent is null)
            {
                if (configuration.DebugLogging)
                    PluginServices.Log.Debug("PFBeacon suppressed duplicate refresh for {CompositeKey}", snapshot.CompositeKey);
                return;
            }

            outboundEventQueue.Enqueue(listingEvent);
        }
        catch (Exception ex)
        {
            PluginServices.Log.Error(ex, "Failed to process Party Finder listing");
        }
    }

    private static void LogSanitizedRawListing(IPartyFinderListing listing, IPartyFinderListingEventArgs args)
    {
        var dutyRowId = listing.Duty.IsValid ? listing.Duty.RowId : (uint)listing.RawDuty;
        var dutyName = listing.Duty.IsValid ? listing.Duty.Value.Name.ToString() : "<invalid-duty>";
        var acceptingJobCounts = string.Join(",", listing.Slots.Select(slot => slot.Accepting.Count));

        var hasMinimumIl = (listing.DutyFinderSettings & DutyFinderSettingsFlags.MinimumIL) == DutyFinderSettingsFlags.MinimumIL;
        var hasSilenceEcho = (listing.DutyFinderSettings & DutyFinderSettingsFlags.SilenceEcho) == DutyFinderSettingsFlags.SilenceEcho;

        PluginServices.Log.Information(
            "PFBeacon feasibility raw: listingId={ListingId} batch={BatchNumber} visible={Visible} rawDuty={RawDuty} dutyRow={DutyRowId} dutyValid={DutyValid} dutyName={DutyName} pfCategory={PfCategory} dutyType={DutyType} dutyFinderSettings={DutyFinderSettings} hasMinimumIl={HasMinimumIl} hasSilenceEcho={HasSilenceEcho} slotsAvailable={SlotsAvailable} slotsFilled={SlotsFilled} parties={Parties} numericMinIl={NumericMinIl} slotCount={SlotCount} acceptingJobCounts=[{AcceptingJobCounts}] jobsPresentCount={JobsPresentCount} rawJobsPresentCount={RawJobsPresentCount}",
            listing.Id,
            args.BatchNumber,
            args.Visible,
            listing.RawDuty,
            dutyRowId,
            listing.Duty.IsValid,
            dutyName,
            listing.Category,
            listing.DutyType,
            listing.DutyFinderSettings,
            hasMinimumIl,
            hasSilenceEcho,
            listing.SlotsAvailable,
            listing.SlotsFilled,
            listing.Parties,
            listing.MinimumItemLevel,
            listing.Slots.Count,
            acceptingJobCounts,
            listing.JobsPresent.Count,
            listing.RawJobsPresent.Count);
    }

    private static void LogSanitizedObservation(PfListingSnapshot snapshot, string state)
    {
        PluginServices.Log.Information(
            "PFBeacon {State}: key={CompositeKey} listingId={ListingId} mappedDataCenter={DataCenter} duty={ContentId} content={ContentName} category={Category} mine={Mine} noEcho={NoEcho} maxPlayers={MaxPlayers} openSlots={OpenSlots} filledSlots={FilledSlots} openSummary={OpenSummary} filledSummary={FilledSummary} hash={ContentHash}",
            state,
            snapshot.CompositeKey,
            snapshot.ListingId,
            snapshot.DataCenter,
            snapshot.ContentId,
            snapshot.ContentName,
            snapshot.ContentCategory,
            snapshot.IsMinimumItemLevel,
            snapshot.IsNoEcho,
            snapshot.MaxPlayers,
            snapshot.OpenSlots.Count,
            snapshot.FilledSlots.Count,
            FormatSlots(snapshot.OpenSlots),
            FormatSlots(snapshot.FilledSlots),
            snapshot.ContentHash);
    }

    private static string FormatSlots(IReadOnlyList<SlotSummary> slots)
    {
        if (slots.Count == 0)
            return "None";

        return string.Join(", ", slots.Select(slot =>
            slot.Job is null
                ? $"{slot.Count}x {slot.Role}"
                : $"{slot.Count}x {slot.Role} ({slot.Job})"));
    }
}
