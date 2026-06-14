using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace PFBeacon;

internal static class PluginServices
{
    internal static IDalamudPluginInterface PluginInterface => Plugin.PluginInterface;
    internal static IPartyFinderGui PartyFinderGui => Plugin.PartyFinderGui;
    internal static IDataManager DataManager => Plugin.DataManager;
    internal static IPluginLog Log => Plugin.Log;
    internal static IClientState ClientState => Plugin.ClientState;
    internal static IObjectTable ObjectTable => Plugin.ObjectTable;
    internal static IFramework Framework => Plugin.Framework;
    internal static ICommandManager CommandManager => Plugin.CommandManager;
}
