using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using MSBT.Windows;
using System.Collections.Generic;
using System;

namespace MSBT;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IGameInteropProvider GameInteropProvider { get; private set; } = null!;
    [PluginService] internal static ISigScanner SigScanner { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static IPlayerState PlayerState { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static ITargetManager TargetManager { get; private set; } = null!;

    private const string CommandName = "/msbt";

    public Configuration Configuration { get; init; }
    public readonly WindowSystem WindowSystem = new("MSBT");
    public ConfigWindow ConfigWindow { get; init; }
    public FontManager FontManager { get; init; }

    public bool IsEditMode = false;

    public readonly List<CustomSCTNode> CustomTexts = new();

    public CombatParser Parser { get; private set; }
    public Renderer Renderer { get; private set; }

    public IpcManager IpcManager { get; private set; }

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Configuration.Initialize(PluginInterface);

        FontManager = new FontManager(Log, PluginInterface.UiBuilder.FontAtlas, PluginInterface, Configuration);

        ConfigWindow = new ConfigWindow(this);
        WindowSystem.AddWindow(ConfigWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand) { HelpMessage = "Open MSBT Settings." });

        Parser = new CombatParser(this);
        Renderer = new Renderer(this);

        IpcManager = new IpcManager(this, PluginInterface);

        PluginInterface.UiBuilder.Draw += DrawUI;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;
    }

    private void DrawUI()
    {
        Renderer.Draw();
    }

    public void Dispose()
    {
        WindowSystem.RemoveAllWindows();
        ConfigWindow.Dispose();

        CommandManager.RemoveHandler(CommandName);

        FontManager?.Dispose();
        Parser?.Dispose();
        IpcManager?.Dispose();
    }

    private void OnCommand(string command, string args) => ConfigWindow.IsOpen = !ConfigWindow.IsOpen;
    public void ToggleConfigUi() => ConfigWindow.IsOpen = !ConfigWindow.IsOpen;

    public void SpawnTestText(bool isCrit, DisplayChannel ch, bool isHeal = false, bool isAlert = false)
    {
        Renderer.SpawnTestText(isCrit, ch, isHeal, isAlert);
    }
}
