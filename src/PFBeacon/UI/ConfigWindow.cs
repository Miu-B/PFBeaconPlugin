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
        : base("PFBeacon Configuration")
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

        var enabled = configuration.Enabled;
        if (ImGui.Checkbox("Enabled", ref enabled))
        {
            configuration.Enabled = enabled;
            configuration.Save();
        }

        var loggingOnly = configuration.FeasibilityLoggingOnly;
        if (ImGui.Checkbox("Feasibility logging only (no network sends)", ref loggingOnly))
        {
            configuration.FeasibilityLoggingOnly = loggingOnly;
            configuration.Save();
        }

        ImGui.Spacing();
        DrawConnectionSettings();
        ImGui.Spacing();
        DrawFilters();
        ImGui.Spacing();
        DrawTiming();
        ImGui.Spacing();

        var debug = configuration.DebugLogging;
        if (ImGui.Checkbox("Debug logging", ref debug))
        {
            configuration.DebugLogging = debug;
            configuration.Save();
        }
    }

    private static void DrawSetupInstructions()
    {
        ImGui.TextWrapped("Setup:");
        ImGui.BulletText("Run /pf register in Discord.");
        ImGui.BulletText("Paste the returned API token below.");
        ImGui.BulletText("Set the bot API base URL, for example https://api.example.com.");
        ImGui.BulletText("Submitted sanitized observations may update all Discord guilds subscribed to this bot service.");
        ImGui.BulletText("Enable the plugin after Phase 0 feasibility checks are complete.");
    }

    private void DrawConnectionSettings()
    {
        ImGui.TextUnformatted("Connection");

        var url = configuration.BotApiUrl;
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputText("Bot API URL", ref url, 512))
        {
            configuration.BotApiUrl = url;
            configuration.Save();
        }

        var token = configuration.UserApiToken;
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputText("User API Token", ref token, 512, ImGuiInputTextFlags.Password))
        {
            configuration.UserApiToken = token;
            configuration.Save();
        }

        if (ImGui.Button("Clear token"))
        {
            configuration.UserApiToken = string.Empty;
            configuration.Save();
        }

        ImGui.SameLine();
        var disableTestButton = testingConnection;
        if (disableTestButton)
            ImGui.BeginDisabled();

        if (ImGui.Button("Test connection"))
        {
            _ = TestConnectionAsync();
        }

        if (disableTestButton)
            ImGui.EndDisabled();

        if (!string.IsNullOrWhiteSpace(connectionStatus))
            ImGui.TextWrapped(connectionStatus);

        var dcOverride = configuration.DataCenterOverride;
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputText("Data center override", ref dcOverride, 64))
        {
            configuration.DataCenterOverride = dcOverride;
            configuration.Save();
        }
    }

    private void DrawFilters()
    {
        ImGui.TextUnformatted("Filters");

        var requireMinIl = configuration.RequireMinimumItemLevel;
        if (ImGui.Checkbox("Require Minimum Item Level", ref requireMinIl))
        {
            configuration.RequireMinimumItemLevel = requireMinIl;
            configuration.Save();
        }

        var requireNoEcho = configuration.RequireNoEcho;
        if (ImGui.Checkbox("Require No Echo / Silence Echo", ref requireNoEcho))
        {
            configuration.RequireNoEcho = requireNoEcho;
            configuration.Save();
        }

        var extreme = configuration.IncludeExtreme;
        if (ImGui.Checkbox("Extreme", ref extreme))
        {
            configuration.IncludeExtreme = extreme;
            configuration.Save();
        }

        ImGui.SameLine();
        var savage = configuration.IncludeSavage;
        if (ImGui.Checkbox("Savage", ref savage))
        {
            configuration.IncludeSavage = savage;
            configuration.Save();
        }

        ImGui.SameLine();
        var ultimate = configuration.IncludeUltimate;
        if (ImGui.Checkbox("Ultimate", ref ultimate))
        {
            configuration.IncludeUltimate = ultimate;
            configuration.Save();
        }

        ImGui.SameLine();
        var unreal = configuration.IncludeUnreal;
        if (ImGui.Checkbox("Unreal", ref unreal))
        {
            configuration.IncludeUnreal = unreal;
            configuration.Save();
        }
    }

    private void DrawTiming()
    {
        ImGui.TextUnformatted("Timing");

        var debounce = configuration.UpdateDebounceSeconds;
        if (ImGui.InputInt("Update debounce seconds", ref debounce))
        {
            configuration.UpdateDebounceSeconds = debounce;
            configuration.Save();
        }

        var refresh = configuration.RefreshSendMinIntervalSeconds;
        if (ImGui.InputInt("Refresh send min interval seconds", ref refresh))
        {
            configuration.RefreshSendMinIntervalSeconds = refresh;
            configuration.Save();
        }

        var prune = configuration.LocalCachePruneSeconds;
        if (ImGui.InputInt("Local cache prune seconds", ref prune))
        {
            configuration.LocalCachePruneSeconds = prune;
            configuration.Save();
        }
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
