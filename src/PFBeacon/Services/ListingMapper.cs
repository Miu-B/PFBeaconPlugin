using Dalamud.Game.Gui.PartyFinder.Types;
using PFBeacon.Models;
using PFBeacon.Util;

namespace PFBeacon.Services;

internal sealed class ListingMapper
{
    private static readonly StringComparer Ordinal = StringComparer.Ordinal;

    private readonly Configuration configuration;

    public ListingMapper(Configuration configuration)
    {
        this.configuration = configuration;
    }

    public PfListingSnapshot? TryMap(IPartyFinderListing listing)
    {
        // Privacy guardrail: do not read listing.ContentId, listing.Name, listing.Description,
        // listing.World, listing.HomeWorld, or listing.CurrentWorld here.
        var dutyRowId = GetDutyRowId(listing);
        if (dutyRowId == 0)
            return null;

        var contentName = GetDutyName(listing);
        if (string.IsNullOrWhiteSpace(contentName))
            return null;

        var dataCenter = ResolveDataCenter();
        if (string.IsNullOrWhiteSpace(dataCenter))
            dataCenter = "Unknown";

        var category = DetectContentCategory(contentName, listing.Category);
        var maxPlayers = GetMaxPlayers(listing);
        var listingId = listing.Id;

        var snapshot = new PfListingSnapshot
        {
            CompositeKey = $"{dataCenter}:{listingId}",
            ListingId = listingId,
            DataCenter = dataCenter,
            ContentId = dutyRowId,
            ContentName = contentName,
            ContentCategory = category,
            IsMinimumItemLevel = HasDutyFinderSetting(listing, DutyFinderSettingsFlags.MinimumIL),
            IsNoEcho = HasDutyFinderSetting(listing, DutyFinderSettingsFlags.SilenceEcho),
            MaxPlayers = maxPlayers,
            OpenSlots = BuildOpenSlotSummaries(listing),
            FilledSlots = BuildFilledSlotSummaries(listing),
            ObservedAtUtc = DateTimeOffset.UtcNow,
            ContentHash = string.Empty,
        };

        return snapshot with { ContentHash = Hashing.ComputeContentHash(snapshot) };
    }

    private static uint GetDutyRowId(IPartyFinderListing listing)
    {
        if (listing.Duty.IsValid)
            return listing.Duty.RowId;

        return listing.RawDuty;
    }

    private static string GetDutyName(IPartyFinderListing listing)
    {
        if (!listing.Duty.IsValid)
            return string.Empty;

        return listing.Duty.Value.Name.ToString();
    }

    private static int GetMaxPlayers(IPartyFinderListing listing)
    {
        if (listing.Parties > 0)
            return listing.Parties * 8;

        if (listing.Duty.IsValid && listing.Duty.Value.QueueMaxPlayers > 0)
            return listing.Duty.Value.QueueMaxPlayers;

        return 8;
    }

    private static bool HasDutyFinderSetting(IPartyFinderListing listing, DutyFinderSettingsFlags flag)
    {
        return (listing.DutyFinderSettings & flag) == flag;
    }

    private string ResolveDataCenter()
    {
        if (!string.IsNullOrWhiteSpace(configuration.DataCenterOverride))
            return configuration.DataCenterOverride.Trim();

        try
        {
            var worldRef = PluginServices.ObjectTable.LocalPlayer?.CurrentWorld;
            if (worldRef is { IsValid: true })
            {
                var dataCenter = worldRef.Value.Value.DataCenter;
                if (dataCenter.IsValid)
                    return dataCenter.Value.Name.ToString();
            }
        }
        catch (Exception ex)
        {
            PluginServices.Log.Debug(ex, "Failed to resolve local data center");
        }

        return string.Empty;
    }

    private static string DetectContentCategory(string contentName, DutyCategory partyFinderCategory)
    {
        // Feasibility spike must replace/validate this with structured sheet mapping.
        if (contentName.Contains("Ultimate", StringComparison.OrdinalIgnoreCase))
            return "Ultimate";

        if (contentName.Contains("Unreal", StringComparison.OrdinalIgnoreCase))
            return "Unreal";

        if (contentName.Contains("Savage", StringComparison.OrdinalIgnoreCase))
            return "Savage";

        if (contentName.Contains("Extreme", StringComparison.OrdinalIgnoreCase)
            || contentName.Contains("Minstrel", StringComparison.OrdinalIgnoreCase))
            return "Extreme";

        if (partyFinderCategory == DutyCategory.HighEndDuty)
            return "HighEndDuty";

        return "Unknown";
    }

    private static IReadOnlyList<SlotSummary> BuildOpenSlotSummaries(IPartyFinderListing listing)
    {
        var summaries = new List<SlotSummary>();

        foreach (var slot in listing.Slots.Skip(Math.Min((int)listing.SlotsFilled, listing.Slots.Count)))
        {
            var acceptedJobs = slot.Accepting
                .Select(MapJobFlagToAbbrev)
                .Where(job => job is not null)
                .Select(job => job!)
                .Distinct(Ordinal)
                .ToArray();

            if (acceptedJobs.Length == 0)
                continue;

            if (acceptedJobs.Length == 1)
            {
                summaries.Add(new SlotSummary
                {
                    Role = RoleFromJobAbbrev(acceptedJobs[0]),
                    Job = acceptedJobs[0],
                    Count = 1,
                });
                continue;
            }

            var roles = acceptedJobs.Select(RoleFromJobAbbrev).Distinct(Ordinal).ToArray();
            summaries.Add(new SlotSummary
            {
                Role = GeneralizeAcceptedRoles(roles),
                Job = null,
                Count = 1,
            });
        }

        return CollapseSlots(summaries);
    }

    private static IReadOnlyList<SlotSummary> BuildFilledSlotSummaries(IPartyFinderListing listing)
    {
        var summaries = new List<SlotSummary>();

        foreach (var jobRef in listing.JobsPresent)
        {
            if (!jobRef.IsValid)
                continue;

            var abbrev = jobRef.Value.Abbreviation.ToString();
            if (string.IsNullOrWhiteSpace(abbrev))
                continue;

            var role = RoleFromJobAbbrev(abbrev);
            if (role == "Unknown")
                continue;

            summaries.Add(new SlotSummary
            {
                Role = role,
                Job = abbrev,
                Count = 1,
            });
        }

        return CollapseSlots(summaries);
    }

    private static IReadOnlyList<SlotSummary> CollapseSlots(IEnumerable<SlotSummary> slots)
    {
        return slots
            .GroupBy(slot => new { slot.Role, slot.Job })
            .Select(group => new SlotSummary
            {
                Role = group.Key.Role,
                Job = group.Key.Job,
                Count = group.Sum(slot => slot.Count),
            })
            .OrderBy(slot => slot.Role, Ordinal)
            .ThenBy(slot => slot.Job ?? string.Empty, Ordinal)
            .ToArray();
    }

    private static string GeneralizeAcceptedRoles(IReadOnlyCollection<string> roles)
    {
        if (roles.Count == 0)
            return "Unknown";

        if (roles.Count == 1)
            return roles.First();

        if (roles.All(IsDpsRole))
            return "DPS";

        return "Any";
    }

    private static bool IsDpsRole(string role)
    {
        return role is "Melee" or "Ranged" or "Caster" or "DPS";
    }

    private static string RoleFromJobAbbrev(string job)
    {
        return job.ToUpperInvariant() switch
        {
            "GLD" or "PLD" or "MRD" or "WAR" or "DRK" or "GNB" => "Tank",
            "CNJ" or "WHM" or "SCH" or "AST" or "SGE" => "Healer",
            "PGL" or "MNK" or "LNC" or "DRG" or "ROG" or "NIN" or "SAM" or "RPR" or "VPR" => "Melee",
            "ARC" or "BRD" or "MCH" or "DNC" => "Ranged",
            "THM" or "BLM" or "ACN" or "SMN" or "RDM" or "PCT" or "BLU" => "Caster",
            _ => "Unknown",
        };
    }

    private static string? MapJobFlagToAbbrev(JobFlags flag)
    {
        return flag switch
        {
            JobFlags.Gladiator => "GLD",
            JobFlags.Paladin => "PLD",
            JobFlags.Marauder => "MRD",
            JobFlags.Warrior => "WAR",
            JobFlags.DarkKnight => "DRK",
            JobFlags.Gunbreaker => "GNB",
            JobFlags.Conjurer => "CNJ",
            JobFlags.WhiteMage => "WHM",
            JobFlags.Scholar => "SCH",
            JobFlags.Astrologian => "AST",
            JobFlags.Sage => "SGE",
            JobFlags.Pugilist => "PGL",
            JobFlags.Monk => "MNK",
            JobFlags.Lancer => "LNC",
            JobFlags.Dragoon => "DRG",
            JobFlags.Rogue => "ROG",
            JobFlags.Ninja => "NIN",
            JobFlags.Samurai => "SAM",
            JobFlags.Reaper => "RPR",
            JobFlags.Viper => "VPR",
            JobFlags.Archer => "ARC",
            JobFlags.Bard => "BRD",
            JobFlags.Machinist => "MCH",
            JobFlags.Dancer => "DNC",
            JobFlags.Thaumaturge => "THM",
            JobFlags.BlackMage => "BLM",
            JobFlags.Arcanist => "ACN",
            JobFlags.Summoner => "SMN",
            JobFlags.RedMage => "RDM",
            JobFlags.Pictomancer => "PCT",
            JobFlags.BlueMage => "BLU",
            _ => null,
        };
    }
}
