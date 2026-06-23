using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using PFBeacon.Services;

namespace PFBeacon.UI;

internal sealed class ConfigWindow : Window, IDisposable
{
    private readonly Configuration configuration;
    private readonly BotApiClient botApiClient;
    private string connectionStatus = string.Empty;
    private bool testingConnection;

    public ConfigWindow(Configuration configuration, BotApiClient botApiClient)
        : base("PFBeacon")
    {
        this.configuration = configuration;
        this.botApiClient = botApiClient;

        Size = new Vector2(560, 520);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public void Dispose()
    {
    }

    public override void Draw()
    {
        DrawSetupInstructions();
        ImGui.Separator();
        ImGui.Spacing();

        DrawTokenSettings();
        ImGui.Separator();
        ImGui.Spacing();

        DrawContributionSettings();
        ImGui.Separator();
        ImGui.Spacing();

        DrawGlobalAlertSettings();
    }

    private static void DrawSetupInstructions()
    {
        ImGui.TextWrapped("PFBeacon sends sanitized Party Finder observations to the official PFBeacon service. It never sends PF descriptions, recruiter names, player names, worlds, or Discord routing information.");
        ImGui.Spacing();
        ImGui.BulletText("Run /pf register in Discord.");
        ImGui.BulletText("Paste the returned API token below.");
        ImGui.BulletText("Contributing observations only sees listings when you open or refresh Party Finder.");
        ImGui.BulletText("Global in-game alerts poll PFBeacon's sanitized server feed instead of querying FFXIV Party Finder.");
        ImGui.Spacing();
        ImGui.TextWrapped($"Service: {BotApiClient.OfficialApiBaseUrl}");
    }

    private void DrawTokenSettings()
    {
        ImGui.TextUnformatted("Token:");
        var token = configuration.UserApiToken;
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputText("##api-token", ref token, 512, ImGuiInputTextFlags.Password))
        {
            configuration.UserApiToken = token;
            configuration.Save();
        }

        if (ImGui.Button("Clear token"))
        {
            configuration.UserApiToken = string.Empty;
            configuration.Save();
            connectionStatus = string.Empty;
        }

        ImGui.SameLine();
        var disableTestButton = testingConnection || string.IsNullOrWhiteSpace(configuration.UserApiToken);
        if (disableTestButton)
            ImGui.BeginDisabled();

        if (ImGui.Button("Test connection"))
            _ = TestConnectionAsync();

        if (disableTestButton)
            ImGui.EndDisabled();

        if (!string.IsNullOrWhiteSpace(connectionStatus))
            ImGui.TextWrapped(connectionStatus);
    }

    private void DrawContributionSettings()
    {
        ImGui.TextUnformatted("Contribution");
        var enabled = configuration.Enabled;
        if (ImGui.Checkbox("Contribute sanitized PF observations", ref enabled))
        {
            configuration.Enabled = enabled;
            configuration.Save();
        }

        ImGui.TextWrapped("When enabled, this client helps Discord alerts by submitting matching MINE listings that your game client actually observes. Category filters below apply to both contribution and global feed alerts.");
        DrawCategorySettings("##contribution-categories");
    }

    private void DrawGlobalAlertSettings()
    {
        ImGui.TextUnformatted("In-game global feed alerts");
        var alertsEnabled = configuration.GlobalChatAlertsEnabled;
        if (ImGui.Checkbox("Show local chat alerts from the global PFBeacon feed", ref alertsEnabled))
        {
            configuration.GlobalChatAlertsEnabled = alertsEnabled;
            configuration.Save();
        }

        ImGui.TextWrapped("This makes one batched, authenticated request every few minutes for all selected data centers. Known MSQ-spoiler duties are redacted server-side before the plugin receives them.");

        var pollInterval = configuration.GlobalAlertPollIntervalSeconds;
        ImGui.SetNextItemWidth(120);
        if (ImGui.InputInt("Poll interval seconds", ref pollInterval, 30, 60))
        {
            configuration.GlobalAlertPollIntervalSeconds = pollInterval;
            configuration.Save();
        }

        ImGui.TextWrapped("Minimum 120 seconds. PFBeacon adds random jitter and backs off on rate limits/errors to reduce server load.");

        ImGui.Spacing();
        ImGui.TextUnformatted("Interested data centers:");
        DrawDataCenterToggles();
    }

    private void DrawCategorySettings(string id)
    {
        ImGui.PushID(id);

        var changed = false;
        var includeRaid = configuration.IncludeRaid;
        var includeExtreme = configuration.IncludeExtreme;
        var includeSavage = configuration.IncludeSavage;
        var includeUltimate = configuration.IncludeUltimate;
        var includeUnreal = configuration.IncludeUnreal;

        changed |= ImGui.Checkbox("Raid", ref includeRaid);
        ImGui.SameLine();
        changed |= ImGui.Checkbox("Extreme", ref includeExtreme);
        ImGui.SameLine();
        changed |= ImGui.Checkbox("Savage", ref includeSavage);
        ImGui.SameLine();
        changed |= ImGui.Checkbox("Ultimate", ref includeUltimate);
        ImGui.SameLine();
        changed |= ImGui.Checkbox("Unreal", ref includeUnreal);

        if (changed)
        {
            configuration.IncludeRaid = includeRaid;
            configuration.IncludeExtreme = includeExtreme;
            configuration.IncludeSavage = includeSavage;
            configuration.IncludeUltimate = includeUltimate;
            configuration.IncludeUnreal = includeUnreal;
            configuration.Save();
        }

        ImGui.PopID();
    }

    private void DrawDataCenterToggles()
    {
        var selected = configuration.GlobalAlertDataCenters.ToHashSet(StringComparer.Ordinal);
        var changed = false;

        if (ImGui.BeginTable("##pfbeacon-dc-table", 3))
        {
            foreach (var dataCenter in Configuration.KnownDataCenters)
            {
                ImGui.TableNextColumn();
                var enabled = selected.Contains(dataCenter);
                if (ImGui.Checkbox(dataCenter, ref enabled))
                {
                    changed = true;
                    if (enabled)
                        selected.Add(dataCenter);
                    else
                        selected.Remove(dataCenter);
                }
            }

            ImGui.EndTable();
        }

        if (changed)
        {
            configuration.GlobalAlertDataCenters = Configuration.KnownDataCenters
                .Where(selected.Contains)
                .Take(4)
                .ToList();
            configuration.Save();
        }

        if (configuration.GlobalAlertDataCenters.Count >= 4)
            ImGui.TextWrapped("Up to 4 data centers can be selected per poll to keep requests bounded.");
    }

    private async Task TestConnectionAsync()
    {
        testingConnection = true;
        connectionStatus = "Testing connection...";

        try
        {
            var result = await botApiClient.TestConnectionAsync().ConfigureAwait(false);
            connectionStatus = result.Message;
        }
        catch (Exception ex)
        {
            connectionStatus = $"Connection failed: {ex.Message}";
        }
        finally
        {
            testingConnection = false;
        }
    }
}
