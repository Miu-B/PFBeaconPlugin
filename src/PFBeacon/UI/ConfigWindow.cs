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

        Size = new Vector2(500, 260);
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

        DrawSettings();
    }

    private static void DrawSetupInstructions()
    {
        ImGui.TextWrapped("PFBeacon sends sanitized Party Finder observations to the official PFBeacon service. It never sends PF descriptions, recruiter names, player names, worlds, or Discord routing information.");
        ImGui.Spacing();
        ImGui.BulletText("Run /pf register in Discord.");
        ImGui.BulletText("Paste the returned API token below.");
        ImGui.BulletText("PFBeacon only sees listings when you open or refresh the Party Finder window.");
        ImGui.BulletText("Enable contribution when you want this client to help update PFBeacon alerts.");
        ImGui.Spacing();
        ImGui.TextWrapped($"Service: {BotApiClient.OfficialApiBaseUrl}");
    }

    private void DrawSettings()
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

        ImGui.Spacing();
        var enabled = configuration.Enabled;
        if (ImGui.Checkbox("Contribute sanitized PF observations", ref enabled))
        {
            configuration.Enabled = enabled;
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
