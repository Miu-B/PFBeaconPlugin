using PFBeacon.Models;

namespace PFBeacon.Services;

internal sealed class ListingFilter
{
    private readonly Configuration configuration;

    public ListingFilter(Configuration configuration)
    {
        this.configuration = configuration;
    }

    public bool IsMatch(PfListingSnapshot snapshot)
    {
        if (!configuration.Enabled)
            return false;

        if (string.IsNullOrWhiteSpace(snapshot.CompositeKey) || snapshot.ContentId == 0)
            return false;

        if (snapshot.MaxPlayers != 8)
            return false;

        if (configuration.RequireMinimumItemLevel && !snapshot.IsMinimumItemLevel)
            return false;

        if (configuration.RequireNoEcho && !snapshot.IsNoEcho)
            return false;

        return snapshot.ContentCategory switch
        {
            "Raid" => configuration.IncludeRaid,
            "Extreme" => configuration.IncludeExtreme,
            "Savage" => configuration.IncludeSavage,
            "Ultimate" => configuration.IncludeUltimate,
            "Unreal" => configuration.IncludeUnreal,
            _ => false,
        };
    }
}
