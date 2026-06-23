using Dalamud.Configuration;

namespace PFBeacon;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public static readonly string[] KnownDataCenters =
    {
        "Aether",
        "Primal",
        "Crystal",
        "Dynamis",
        "Chaos",
        "Light",
        "Elemental",
        "Gaia",
        "Mana",
        "Meteor",
        "Materia",
    };

    public int Version { get; set; } = 2;

    public bool Enabled { get; set; }
    public string UserApiToken { get; set; } = string.Empty;
    public string ClientInstanceId { get; set; } = Guid.NewGuid().ToString("N");

    public bool GlobalChatAlertsEnabled { get; set; }
    public List<string> GlobalAlertDataCenters { get; set; } = new() { "Light", "Chaos" };
    public int GlobalAlertPollIntervalSeconds { get; set; } = 180;

    public bool RequireMinimumItemLevel { get; set; } = true;
    public bool RequireNoEcho { get; set; } = true;

    public bool IncludeRaid { get; set; } = true;
    public bool IncludeExtreme { get; set; } = true;
    public bool IncludeSavage { get; set; } = true;
    public bool IncludeUltimate { get; set; } = true;
    public bool IncludeUnreal { get; set; } = true;

    public int UpdateDebounceSeconds { get; set; } = 3;
    public int SnapshotDebounceSeconds { get; set; } = 2;
    public int RefreshSendMinIntervalSeconds { get; set; } = 10;
    public int LocalCachePruneSeconds { get; set; } = 300;

    public void Normalize()
    {
        if (string.IsNullOrWhiteSpace(ClientInstanceId))
            ClientInstanceId = Guid.NewGuid().ToString("N");

        UserApiToken = UserApiToken.Trim();
        GlobalAlertDataCenters ??= new List<string> { "Light", "Chaos" };

        GlobalAlertDataCenters = GlobalAlertDataCenters
            .Where(dataCenter => KnownDataCenters.Contains(dataCenter, StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .Take(4)
            .ToList();

        UpdateDebounceSeconds = Math.Clamp(UpdateDebounceSeconds, 1, 60);
        SnapshotDebounceSeconds = Math.Clamp(SnapshotDebounceSeconds, 1, 30);
        RefreshSendMinIntervalSeconds = Math.Clamp(RefreshSendMinIntervalSeconds, 5, 300);
        LocalCachePruneSeconds = Math.Clamp(LocalCachePruneSeconds, 60, 3600);
        GlobalAlertPollIntervalSeconds = Math.Clamp(GlobalAlertPollIntervalSeconds, 120, 900);
    }

    public IReadOnlyList<string> GetIncludedContentCategories()
    {
        var categories = new List<string>();
        if (IncludeRaid)
            categories.Add("Raid");
        if (IncludeExtreme)
            categories.Add("Extreme");
        if (IncludeSavage)
            categories.Add("Savage");
        if (IncludeUltimate)
            categories.Add("Ultimate");
        if (IncludeUnreal)
            categories.Add("Unreal");

        return categories;
    }

    public void Save()
    {
        Normalize();
        PluginServices.PluginInterface.SavePluginConfig(this);
    }
}
