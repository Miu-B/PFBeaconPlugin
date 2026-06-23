using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using PFBeacon.Services;
using PFBeacon.UI;

namespace PFBeacon;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static IPartyFinderGui PartyFinderGui { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;

    private const string CommandName = "/pfbeacon";

    private readonly WindowSystem windowSystem = new("PFBeacon");
    private readonly BotApiClient botApiClient;
    private readonly OutboundEventQueue outboundEventQueue;
    private readonly PartyFinderObserver partyFinderObserver;
    private readonly ConfigWindow configWindow;
    private GlobalFeedPoller? globalFeedPoller;

    public Plugin()
    {
        Configuration = PluginServices.PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Configuration.Normalize();
        Configuration.Save();

        botApiClient = new BotApiClient(Configuration);
        outboundEventQueue = new OutboundEventQueue(Configuration, botApiClient);
        var mapper = new ListingMapper();
        var filter = new ListingFilter(Configuration);
        var listingCache = new ListingCache(Configuration);
        partyFinderObserver = new PartyFinderObserver(Configuration, mapper, filter, listingCache, outboundEventQueue);
        configWindow = new ConfigWindow(Configuration, botApiClient);

        windowSystem.AddWindow(configWindow);

        PluginServices.CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open PFBeacon configuration. Optional: status",
        });

        PluginServices.PluginInterface.UiBuilder.Draw += DrawUi;
        PluginServices.PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;

        partyFinderObserver.Start();
        SyncGlobalFeedPoller();
        PluginServices.Log.Information("PFBeacon loaded");
    }

    public Configuration Configuration { get; }

    public void Dispose()
    {
        PluginServices.PluginInterface.UiBuilder.Draw -= DrawUi;
        PluginServices.PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        PluginServices.CommandManager.RemoveHandler(CommandName);

        windowSystem.RemoveAllWindows();
        configWindow.Dispose();
        globalFeedPoller?.Dispose();
        globalFeedPoller = null;
        partyFinderObserver.Dispose();
        outboundEventQueue.Dispose();
        botApiClient.Dispose();
    }

    private void OnCommand(string command, string args)
    {
        var trimmed = args.Trim();
        if (trimmed.Equals("status", StringComparison.OrdinalIgnoreCase))
        {
            PluginServices.Log.Information(
                "PFBeacon status: Enabled={Enabled}, GlobalAlerts={GlobalAlerts}, HasToken={HasToken}, Service={Service}",
                Configuration.Enabled,
                Configuration.GlobalChatAlertsEnabled,
                !string.IsNullOrWhiteSpace(Configuration.UserApiToken),
                BotApiClient.OfficialApiBaseUrl);
            return;
        }

        ToggleConfigUi();
    }

    private void ToggleConfigUi()
    {
        configWindow.Toggle();
    }

    private void DrawUi()
    {
        SyncGlobalFeedPoller();
        windowSystem.Draw();
    }

    private void SyncGlobalFeedPoller()
    {
        if (Configuration.GlobalChatAlertsEnabled)
        {
            globalFeedPoller ??= new GlobalFeedPoller(Configuration, botApiClient);
            return;
        }

        if (globalFeedPoller is null)
            return;

        globalFeedPoller.Dispose();
        globalFeedPoller = null;
    }
}
