using Dalamud.Configuration;

namespace PFBeacon;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public bool Enabled { get; set; }
    public string UserApiToken { get; set; } = string.Empty;
    public string ClientInstanceId { get; set; } = Guid.NewGuid().ToString("N");

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

        UpdateDebounceSeconds = Math.Clamp(UpdateDebounceSeconds, 1, 60);
        SnapshotDebounceSeconds = Math.Clamp(SnapshotDebounceSeconds, 1, 30);
        RefreshSendMinIntervalSeconds = Math.Clamp(RefreshSendMinIntervalSeconds, 5, 300);
        LocalCachePruneSeconds = Math.Clamp(LocalCachePruneSeconds, 60, 3600);
    }

    public void Save()
    {
        Normalize();
        PluginServices.PluginInterface.SavePluginConfig(this);
    }
}
