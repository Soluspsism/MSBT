using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace MSBT;

internal sealed class Service
{
    [PluginService] public static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] public static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] public static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] public static IPluginLog Log { get; private set; } = null!;
    [PluginService] public static IGameInteropProvider GameInteropProvider { get; private set; } = null!;
    [PluginService] public static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] public static IDataManager DataManager { get; private set; } = null!;
    [PluginService] public static IFramework Framework { get; private set; } = null!;
    [PluginService] public static ITargetManager TargetManager { get; private set; } = null!;

    public static void Clear()
    {
        PluginInterface = null!;
        TextureProvider = null!;
        CommandManager = null!;
        Log = null!;
        GameInteropProvider = null!;
        ObjectTable = null!;
        DataManager = null!;
        Framework = null!;
        TargetManager = null!;
    }
}
