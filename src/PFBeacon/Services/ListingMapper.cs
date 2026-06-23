using Dalamud.Game.Gui.PartyFinder.Types;
using PFBeacon.Models;
using PFBeacon.Util;

namespace PFBeacon.Services;

internal sealed class ListingMapper
{
    private static readonly StringComparer Ordinal = StringComparer.Ordinal;

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
            ContentLevel = GetContentLevel(listing),
            ContentName = contentName,
            ContentCategory = category,
            IsMinimumItemLevel = HasDutyFinderSetting(listing, DutyFinderSettingsFlags.MinimumIL),
            IsNoEcho = HasDutyFinderSetting(listing, DutyFinderSettingsFlags.SilenceEcho),
            IsPrivate = HasSearchAreaFlag(listing, SearchAreaFlags.Private),
            MaxPlayers = maxPlayers,
            OpenSlots = BuildOpenSlotSummaries(listing, maxPlayers),
            FilledSlots = BuildFilledSlotSummaries(listing, maxPlayers),
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

    private static int GetContentLevel(IPartyFinderListing listing)
    {
        if (listing.Duty.IsValid && listing.Duty.Value.ClassJobLevelRequired > 0)
            return listing.Duty.Value.ClassJobLevelRequired;

        return 0;
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

    private static bool HasSearchAreaFlag(IPartyFinderListing listing, SearchAreaFlags flag)
    {
        return (listing.SearchArea & flag) == flag;
    }

    private static string ResolveDataCenter()
    {
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
        // TODO: replace/validate this heuristic with structured sheet mapping.
        if (contentName.Contains("Ultimate", StringComparison.OrdinalIgnoreCase))
            return "Ultimate";

        if (contentName.Contains("Unreal", StringComparison.OrdinalIgnoreCase))
            return "Unreal";

        if (contentName.Contains("Savage", StringComparison.OrdinalIgnoreCase))
            return "Savage";

        if (contentName.Contains("Extreme", StringComparison.OrdinalIgnoreCase)
            || contentName.Contains("Minstrel", StringComparison.OrdinalIgnoreCase))
            return "Extreme";

        if (partyFinderCategory == DutyCategory.Raids)
            return "Raid";

        if (partyFinderCategory == DutyCategory.HighEndDuty)
            return "HighEndDuty";

        return "Unknown";
    }

    private static IReadOnlyList<SlotSummary> BuildOpenSlotSummaries(IPartyFinderListing listing, int maxPlayers)
    {
        // PartyFinderSlot order is the PF slot layout, not "filled slots first". Derive openings by
        // consuming slots that can explain the currently-present jobs, then summarize the remainder.
        var candidates = BuildSlotCandidates(listing, maxPlayers);
        var filledJobs = GetPresentJobAbbrevs(listing).ToArray();
        var filledSlotCount = GetFilledSlotCount(listing, filledJobs.Length, maxPlayers);
        var consumedSlots = new bool[candidates.Count];

        foreach (var slotIndex in MatchFilledJobsToSlots(candidates, filledJobs))
            consumedSlots[slotIndex] = true;

        ConsumeFallbackFilledSlots(candidates, consumedSlots, filledSlotCount - consumedSlots.Count(consumed => consumed));

        var openSlotCount = Math.Max(0, candidates.Count - filledSlotCount);
        var summaries = candidates
            .Where((_, index) => !consumedSlots[index])
            .Take(openSlotCount)
            .Select(candidate => BuildOpenSlotSummary(candidate.AcceptedJobs))
            .ToList();

        while (summaries.Count < openSlotCount)
            summaries.Add(BuildUnknownSlotSummary());

        return CollapseSlots(summaries);
    }

    private static IReadOnlyList<SlotSummary> BuildFilledSlotSummaries(IPartyFinderListing listing, int maxPlayers)
    {
        var filledJobs = GetPresentJobAbbrevs(listing).ToArray();
        var filledSlotCount = GetFilledSlotCount(listing, filledJobs.Length, maxPlayers);
        var summaries = new List<SlotSummary>();

        foreach (var abbrev in filledJobs)
        {
            summaries.Add(new SlotSummary
            {
                Role = RoleFromJobAbbrev(abbrev),
                Job = abbrev,
                Count = 1,
            });
        }

        var unknownFilledCount = filledSlotCount - summaries.Count;
        if (unknownFilledCount > 0)
        {
            summaries.Add(new SlotSummary
            {
                Role = "Unknown",
                Job = null,
                Count = unknownFilledCount,
            });
        }

        return CollapseSlots(summaries);
    }

    private static IReadOnlyList<SlotCandidate> BuildSlotCandidates(IPartyFinderListing listing, int maxPlayers)
    {
        var slotLimit = Math.Max(0, Math.Min(maxPlayers, listing.Slots.Count));

        return listing.Slots
            .Take(slotLimit)
            .Select((slot, index) => new SlotCandidate(
                index,
                slot.Accepting
                    .Select(MapJobFlagToAbbrev)
                    .Where(job => job is not null)
                    .Select(job => job!)
                    .Select(job => job.ToUpperInvariant())
                    .Distinct(Ordinal)
                    .ToArray()))
            .ToArray();
    }

    private static IEnumerable<string> GetPresentJobAbbrevs(IPartyFinderListing listing)
    {
        foreach (var jobRef in listing.JobsPresent)
        {
            if (!jobRef.IsValid)
                continue;

            var abbrev = jobRef.Value.Abbreviation.ToString();
            if (string.IsNullOrWhiteSpace(abbrev))
                continue;

            abbrev = abbrev.Trim().ToUpperInvariant();
            if (RoleFromJobAbbrev(abbrev) == "Unknown")
                continue;

            yield return abbrev;
        }
    }

    private static int GetFilledSlotCount(IPartyFinderListing listing, int knownFilledJobCount, int maxPlayers)
    {
        var slotCapacity = Math.Max(0, Math.Min(maxPlayers, listing.Slots.Count));
        var reportedFilledSlots = Math.Max(0, (int)listing.SlotsFilled);
        var filledSlotCount = Math.Max(knownFilledJobCount, reportedFilledSlots);

        return Math.Min(filledSlotCount, slotCapacity);
    }

    private static IReadOnlyList<int> MatchFilledJobsToSlots(
        IReadOnlyList<SlotCandidate> candidates,
        IReadOnlyList<string> filledJobs)
    {
        if (candidates.Count == 0 || filledJobs.Count == 0)
            return Array.Empty<int>();

        var jobs = filledJobs
            .Select(job => job.ToUpperInvariant())
            .OrderBy(job => candidates.Count(candidate => candidate.Accepts(job)))
            .ThenBy(job => job, Ordinal)
            .ToArray();

        var used = new bool[candidates.Count];
        var current = new List<int>();
        var best = Array.Empty<int>();
        var bestMatched = -1;
        var bestCost = int.MaxValue;

        Search(0, 0);
        return best;

        void Search(int jobIndex, int cost)
        {
            if (jobIndex == jobs.Length)
            {
                if (current.Count > bestMatched || (current.Count == bestMatched && cost < bestCost))
                {
                    bestMatched = current.Count;
                    bestCost = cost;
                    best = current.ToArray();
                }

                return;
            }

            if (current.Count + jobs.Length - jobIndex < bestMatched)
                return;

            var job = jobs[jobIndex];
            var matchingCandidates = candidates
                .Where((candidate, index) => !used[index] && candidate.Accepts(job))
                .OrderBy(candidate => SlotFillCost(candidate))
                .ThenBy(candidate => candidate.Index)
                .ToArray();

            foreach (var candidate in matchingCandidates)
            {
                used[candidate.Index] = true;
                current.Add(candidate.Index);
                Search(jobIndex + 1, cost + SlotFillCost(candidate));
                current.RemoveAt(current.Count - 1);
                used[candidate.Index] = false;
            }

            Search(jobIndex + 1, cost);
        }
    }

    private static int SlotFillCost(SlotCandidate candidate)
    {
        if (candidate.AcceptedJobs.Count == 0)
            return 10_000;

        var acceptedRoleCount = candidate.AcceptedJobs
            .Select(RoleFromJobAbbrev)
            .Distinct(Ordinal)
            .Count();

        return candidate.AcceptedJobs.Count * 100 + acceptedRoleCount;
    }

    private static void ConsumeFallbackFilledSlots(
        IReadOnlyList<SlotCandidate> candidates,
        bool[] consumedSlots,
        int count)
    {
        if (count <= 0)
            return;

        foreach (var candidate in candidates
                     .Where(candidate => !consumedSlots[candidate.Index])
                     .OrderBy(candidate => candidate.AcceptedJobs.Count == 0 ? 0 : 1)
                     .ThenByDescending(candidate => candidate.AcceptedJobs.Count)
                     .ThenBy(candidate => candidate.Index)
                     .Take(count))
        {
            consumedSlots[candidate.Index] = true;
        }
    }

    private static SlotSummary BuildOpenSlotSummary(IReadOnlyList<string> acceptedJobs)
    {
        if (acceptedJobs.Count == 0)
            return BuildUnknownSlotSummary();

        if (acceptedJobs.Count == 1)
        {
            return new SlotSummary
            {
                Role = RoleFromJobAbbrev(acceptedJobs[0]),
                Job = acceptedJobs[0],
                Count = 1,
            };
        }

        var roles = acceptedJobs.Select(RoleFromJobAbbrev).Distinct(Ordinal).ToArray();
        var acceptedRoles = BuildAcceptedRoleGroups(roles);
        return new SlotSummary
        {
            Role = GeneralizeAcceptedRoles(roles),
            Job = null,
            Count = 1,
            AcceptedRoles = acceptedRoles.Count > 1 ? acceptedRoles : null,
        };
    }

    private static SlotSummary BuildUnknownSlotSummary()
    {
        return new SlotSummary
        {
            Role = "Unknown",
            Job = null,
            Count = 1,
        };
    }

    private static IReadOnlyList<SlotSummary> CollapseSlots(IEnumerable<SlotSummary> slots)
    {
        return slots
            .GroupBy(slot => new { slot.Role, slot.Job, AcceptedRoles = AcceptedRolesKey(slot.AcceptedRoles) })
            .Select(group => new SlotSummary
            {
                Role = group.Key.Role,
                Job = group.Key.Job,
                Count = group.Sum(slot => slot.Count),
                AcceptedRoles = ParseAcceptedRolesKey(group.Key.AcceptedRoles),
            })
            .OrderBy(slot => slot.Role, Ordinal)
            .ThenBy(slot => slot.Job ?? string.Empty, Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<string> BuildAcceptedRoleGroups(IEnumerable<string> roles)
    {
        return roles
            .Select(CoarsenAcceptedRole)
            .Where(role => role is not "Unknown")
            .Distinct(Ordinal)
            .OrderBy(AcceptedRoleOrder)
            .ToArray();
    }

    private static string CoarsenAcceptedRole(string role)
    {
        return IsDpsRole(role) ? "DPS" : role;
    }

    private static int AcceptedRoleOrder(string role)
    {
        return role switch
        {
            "Tank" => 10,
            "Healer" => 20,
            "DPS" => 30,
            _ => 99,
        };
    }

    private static string AcceptedRolesKey(IReadOnlyList<string>? acceptedRoles)
    {
        if (acceptedRoles is null || acceptedRoles.Count == 0)
            return string.Empty;

        return string.Join(",", acceptedRoles.OrderBy(AcceptedRoleOrder).ThenBy(role => role, Ordinal));
    }

    private static IReadOnlyList<string>? ParseAcceptedRolesKey(string acceptedRoles)
    {
        if (string.IsNullOrWhiteSpace(acceptedRoles))
            return null;

        return acceptedRoles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
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

    private sealed record SlotCandidate(int Index, IReadOnlyList<string> AcceptedJobs)
    {
        public bool Accepts(string job)
        {
            return AcceptedJobs.Any(acceptedJob => Ordinal.Equals(acceptedJob, job));
        }
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
